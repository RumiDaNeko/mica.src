using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Mica {
 // The Mica account (Discord) and settings sync, as on Windows (BrowserSignIn.cs, BrowserSettingsSync.cs).
 // Sign-in is bound to this device: the sync service gives Mica a short code, and after Discord the browser page
 // asks for it, so someone who sends you a sign-in link they started can't finish it with your account.
 // The session token is kept per profile in an owner-only file (Windows keeps it under DPAPI, which Mac and Linux
 // don't have).
 sealed class SyncAccount {public string token {get;set;} public string id {get;set;} public string username {get;set;} public string avatar {get;set;} public string banner {get;set;} public int? accent_color {get;set;}}
 sealed class SyncApiException : Exception {
  public readonly int Status;public readonly string Code;
  public SyncApiException(int status,string code) : base("HTTP "+status+(code==null?"":" "+code)) {Status=status;Code=code;}
 }
 sealed partial class BrowserWindow {
  static readonly HttpClient syncHttp=new HttpClient(new HttpClientHandler {AllowAutoRedirect=false,UseCookies=false}) {Timeout=Timeout.InfiniteTimeSpan};
  static readonly JavaScriptSerializer syncJson=new JavaScriptSerializer();
  // The self-test points Mica at a local copy of the sync service (server/worker/test/desktop-fixture.mjs).
  internal string syncTestApi;
  string SyncApi {get {return syncTestApi??"https://the-api.lessnoise.online";}}
  bool SyncEnabled {get {return !Program.Private&&(Program.SelfTestResult==null||syncTestApi!=null);}}

  // ── Requests: JSON in and out; a failed one throws with the service's {"error":"…"} code.
  async Task<string> SyncRequest(string route,string method,object body,string token,int timeoutMs) {
   using(var cancel=new CancellationTokenSource(timeoutMs))
   using(var request=new HttpRequestMessage(new HttpMethod(method),SyncApi+route)) {
    request.Headers.Accept.ParseAdd("application/json");
    if(token!=null)request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
    if(body!=null)request.Content=new StringContent(syncJson.Serialize(body),Encoding.UTF8,"application/json");
    using(var response=await syncHttp.SendAsync(request,cancel.Token).ConfigureAwait(false)) {
     string text=await response.Content.ReadAsStringAsync(cancel.Token).ConfigureAwait(false);
     if(response.IsSuccessStatusCode)return text;
     string code=null;try {var error=syncJson.DeserializeObject(text) as Dictionary<string,object>;object value;if(error!=null&&error.TryGetValue("error",out value))code=value as string;} catch(Exception) {}
     throw new SyncApiException((int)response.StatusCode,code);
    }
   }
  }
  static string Base64Url(byte[] data) {return Convert.ToBase64String(data).TrimEnd('=').Replace('+','-').Replace('/','_');}
  static string RandomVerifier() {return Base64Url(RandomNumberGenerator.GetBytes(32));}
  static string Challenge(string verifier) {return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));}

  // ── The session, one per profile: readable by this user only.
  string AccountPath {get {return Path.Combine(profile,"sync-account.json");}}
  internal SyncAccount ReadAccount() {
   if(Program.Private)return null;
   try {if(File.Exists(AccountPath)){var account=syncJson.Deserialize<SyncAccount>(File.ReadAllText(AccountPath));if(account!=null&&!string.IsNullOrEmpty(account.token))return account;}} catch(Exception) {}
   return null;
  }
  void WriteAccount(SyncAccount account) {
   string temp=AccountPath+".tmp";
   var options=new FileStreamOptions {Mode=FileMode.Create,Access=FileAccess.Write};
   if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
   using(var stream=new FileStream(temp,options))using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))writer.Write(syncJson.Serialize(account));
   if(!OperatingSystem.IsWindows())File.SetUnixFileMode(temp,UnixFileMode.UserRead|UnixFileMode.UserWrite);
   File.Move(temp,AccountPath,true);
  }

  // ── Sign-in (BrowserSignIn.cs SignInWithDiscord).
  sealed class LoginStart {public string requestId {get;set;} public string authorizationUrl {get;set;} public string confirmationCode {get;set;} public string expiresAt {get;set;}}
  sealed class LoginUser {public string id {get;set;} public string username {get;set;} public string avatar {get;set;} public string banner {get;set;} public int? accent_color {get;set;}}
  sealed class LoginPoll {public string status {get;set;} public string token {get;set;} public LoginUser user {get;set;}}
  static readonly Regex SignInCode=new Regex("^[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}$");
  // Opens the Discord page in the system browser; the self-test replaces it to play the browser side.
  internal Func<Uri,Task> openSignInPage;
  internal bool signingIn;
  internal async void SignInFromAccountCard() {
   if(Program.Private||signingIn)return;
   signingIn=true;
   try {await SignInWithDiscord();}
   catch(Exception e) {if(IsVisible)Status(e is InvalidOperationException?e.Message:"Couldn't reach the sync service. Try again.");}
   finally {signingIn=false;}
  }
  async Task SignInWithDiscord() {
   string verifier=RandomVerifier();LoginStart start;
   try {start=syncJson.Deserialize<LoginStart>(await SyncRequest("/v1/auth/discord/start","POST",new {challenge=Challenge(verifier),confirm=true},null,15000));}
   catch(SyncApiException e) {if(e.Code=="confirmation_required")throw new InvalidOperationException("Update Mica to sign in.");throw;}
   Uri url;
   if(start==null||string.IsNullOrEmpty(start.requestId)||!Uri.TryCreate(start.authorizationUrl,UriKind.Absolute,out url)||url.Scheme!="https"||url.Host!="discord.com"||!url.IsDefaultPort||!string.IsNullOrEmpty(url.UserInfo)||(url.AbsolutePath!="/oauth2/authorize"&&url.AbsolutePath!="/api/oauth2/authorize"))throw new InvalidOperationException("The service returned an unsafe sign-in address.");
   if(start.confirmationCode==null||!SignInCode.IsMatch(start.confirmationCode))throw new InvalidOperationException("The service didn't send a sign-in code, so Mica won't continue.");
   DateTime until;if(!DateTime.TryParse(start.expiresAt,null,System.Globalization.DateTimeStyles.AdjustToUniversal|System.Globalization.DateTimeStyles.AssumeUniversal,out until)||until>DateTime.UtcNow.AddMinutes(30))until=DateTime.UtcNow.AddMinutes(10);
   bool cancelled=false;Action closeCard=ShowSignInCode(start.confirmationCode,delegate {cancelled=true;});
   try {
    Status("Complete Discord sign-in in your browser, then enter the code Mica shows.");
    if(openSignInPage!=null){var opened=openSignInPage(url);}else OpenInSystemBrowser(url.AbsoluteUri);
    while(DateTime.UtcNow<until) {
     await Task.Delay(2000);if(cancelled){Status("Sign-in cancelled.");return;}
     LoginPoll poll;
     try {poll=syncJson.Deserialize<LoginPoll>(await SyncRequest("/v1/auth/discord/poll","POST",new {requestId=start.requestId,verifier=verifier},null,15000));}
     catch(SyncApiException retry) {
      if(retry.Status==429)continue;
      if(retry.Code=="authorization_failed"){Status("Sign-in was cancelled in the browser. Try again.");return;}
      throw;
     }
     if(cancelled)return;
     if(poll==null)throw new InvalidOperationException("Invalid sign-in response.");
     if(poll.status!="ready")continue;
     if(poll.user==null||string.IsNullOrEmpty(poll.user.id)||string.IsNullOrEmpty(poll.token))throw new InvalidOperationException("Invalid sign-in response.");
     closeCard();CompleteSignIn(poll);return;
    }
    Status("Sign-in timed out. Please try again.");
   } finally {closeCard();}
  }
  void CompleteSignIn(LoginPoll poll) {
   WriteAccount(new SyncAccount {token=poll.token,id=poll.user.id,username=poll.user.username,avatar=poll.user.avatar,banner=poll.user.banner,accent_color=poll.user.accent_color});
   Status("Signed in. Your settings now sync with your other devices.");PullSettings();
  }
  // The address goes to the system's browser as one argument, never through a shell.
  static void OpenInSystemBrowser(string url) {
   var start=new System.Diagnostics.ProcessStartInfo {UseShellExecute=OperatingSystem.IsWindows()};
   if(OperatingSystem.IsWindows())start.FileName=url;
   else {start.FileName=OperatingSystem.IsMacOS()?"open":"xdg-open";start.ArgumentList.Add(url);}
   System.Diagnostics.Process.Start(start);
  }
  // Signing out forgets the session here and ends it at the service (best effort, offline included).
  async void SignOut() {
   var account=ReadAccount();if(account==null)return;
   try {File.Delete(AccountPath);} catch(Exception) {}
   Status("Signed out. Settings stay on this device.");
   try {await SyncRequest("/v1/auth/logout","POST",null,account.token,5000);} catch(Exception) {}
  }

  // The card with the code. It stays when you click back into Mica, since Discord is open in another app.
  internal Popup signInCodeCard;
  Action ShowSignInCode(string code,Action cancel) {
   StackPanel body,actions;var card=MicaDialog("Sign-in code",460,out body,out actions);signInCodeCard=card;
   stayOnOutsideClick.Add(card);
   body.Children.Add(DialogText("After you approve Mica in Discord, the browser asks for this code. Type it there.",13,"Muted"));
   // A read-only box rather than plain text, so the code can be selected as well as copied.
   var shown=new TextBox {Text=code,IsReadOnly=true,FontSize=34,FontFamily=new FontFamily("Menlo, Consolas, DejaVu Sans Mono, monospace"),FontWeight=FontWeight.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,
    Margin=new Thickness(0,22,0,18),Tag="signin-code",Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0)};
   Res(shown,TextBox.ForegroundProperty,"Ink");body.Children.Add(shown);
   body.Children.Add(DialogText("Never share this code. If a page or person asks you for it, you didn't start this sign-in.",11,"Muted"));
   var copy=DialogButton("Copy",false);copy.Margin=new Thickness(0,0,8,0);copy.Tag="signin-copy";actions.Children.Add(copy);
   copy.Click+=async delegate {
    try {var board=Clipboard;if(board==null)return;await board.SetTextAsync(code);} catch(Exception) {return;}
    copy.Content=Localizer.Translate("Copied");DispatcherTimer.RunOnce(delegate {copy.Content=Localizer.Translate("Copy");},TimeSpan.FromSeconds(2));
   };
   var stop=DialogButton("Cancel sign-in",true);stop.Tag="signin-cancel";actions.Children.Add(stop);
   bool closedByUs=false;stop.Click+=delegate {cancel();card.IsOpen=false;};
   card.Closed+=delegate {if(signInCodeCard==card)signInCodeCard=null;if(!closedByUs)cancel();};
   card.IsOpen=true;
   return delegate {if(closedByUs)return;closedByUs=true;card.IsOpen=false;};
  }

  // ── Settings sync (BrowserSettingsSync.cs): preferences follow the account as plain JSON at /v1/settings. Every
  // change is pushed a moment later and again when Mica closes; launch and sign-in pull. The newest change wins.
  // The service keeps one copy for all devices, so the Windows-only settings (YouTube ambient, strict tracking,
  // tree tabs, the close effect) travel through here untouched: kept in the session, sent back as they came.
  sealed class SettingsSyncState {public long Updated {get;set;} public string Seen {get;set;} public string Synced {get;set;}}
  sealed class RemoteSettings {public Dictionary<string,object> settings {get;set;} public long updatedAt {get;set;}}
  SettingsSyncState settingsSync;bool settingsPulling;DispatcherTimer settingsPushTimer;
  string SettingsSyncPath {get {return Path.Combine(profile,"settings-sync.json");}}
  // The ten keys Windows syncs, read the way Windows writes them; null travels as "" (or -1 for the glass).
  Dictionary<string,object> SettingsSnapshot() {
   return new Dictionary<string,object> {{"dark",state.Dark},{"ambientYoutube",state.AmbientYoutube},{"strictTracking",state.StrictTracking},
    {"textScale",TextScale},{"treeTabs",state.TreeTabs},{"readerSize",state.ReaderSize},{"readerSerif",state.ReaderSerif},
    {"frameColor",state.FrameColor??""},{"homeGlass",state.HomeGlass??-1},{"closeEffect",state.CloseEffect??""}};
  }
  static readonly string[] SyncedCloseEffects={"fade","drop","shatter","tv","swirl","melt","dissolve","off"};
  static bool SyncedNumber(object value,out double number) {number=0;if(!(value is int||value is long||value is decimal||value is double))return false;number=Convert.ToDouble(value,System.Globalization.CultureInfo.InvariantCulture);return true;}
  // Takes the values from a cloud copy, each only when it is one Mica could have written (TakeSyncedLook).
  static bool TakeSyncedSettings(BrowserState into,Dictionary<string,object> from) {
   object value;double number;bool changed=false;
   if(from.TryGetValue("dark",out value)&&value is bool&&into.Dark!=(bool)value){into.Dark=(bool)value;changed=true;}
   if(from.TryGetValue("textScale",out value)&&SyncedNumber(value,out number)&&(number==1||number==1.15||number==1.3)&&Math.Abs((into.TextScale<=0?1:into.TextScale)-number)>0.001){into.TextScale=number;changed=true;}
   if(from.TryGetValue("readerSize",out value)&&SyncedNumber(value,out number)&&(number==0||number>=13&&number<=28)&&number==Math.Floor(number)&&into.ReaderSize!=(int)number){into.ReaderSize=(int)number;changed=true;}
   if(from.TryGetValue("readerSerif",out value)&&value is bool&&into.ReaderSerif!=(bool)value){into.ReaderSerif=(bool)value;changed=true;}
   if(from.TryGetValue("frameColor",out value)&&value is string&&((string)value==""||Regex.IsMatch((string)value,"^#[0-9A-Fa-f]{6}$"))){string color=(string)value==""?null:(string)value;if(into.FrameColor!=color){into.FrameColor=color;changed=true;}}
   if(from.TryGetValue("homeGlass",out value)&&SyncedNumber(value,out number)&&number>=-1&&number<=100&&number==Math.Floor(number)){int? glass=number<0?(int?)null:(int)number;if(into.HomeGlass!=glass){into.HomeGlass=glass;changed=true;}}
   // Not used on this shell; kept so they go back to the service as they came.
   if(from.TryGetValue("ambientYoutube",out value)&&value is bool&&into.AmbientYoutube!=(bool)value){into.AmbientYoutube=(bool)value;changed=true;}
   if(from.TryGetValue("strictTracking",out value)&&value is bool&&into.StrictTracking!=(bool)value){into.StrictTracking=(bool)value;into.TrackingLevel=(bool)value?3:2;changed=true;}
   if(from.TryGetValue("treeTabs",out value)&&value is bool&&into.TreeTabs!=(bool)value){into.TreeTabs=(bool)value;changed=true;}
   if(from.TryGetValue("closeEffect",out value)&&value is string&&((string)value==""||SyncedCloseEffects.Contains((string)value))){string effect=(string)value==""?null:(string)value;if(into.CloseEffect!=effect){into.CloseEffect=effect;changed=true;}}
   return changed;
  }
  // Read as plain values (bool, int, long, decimal, string), the way the Windows build reads them; System.Text.Json
  // would hand out JsonElements, which no check below would take.
  static RemoteSettings ParseRemote(string json) {
   var reply=syncJson.DeserializeObject(json) as Dictionary<string,object>;if(reply==null)return null;
   object settings,updated;reply.TryGetValue("settings",out settings);reply.TryGetValue("updatedAt",out updated);
   return new RemoteSettings {settings=settings as Dictionary<string,object>,updatedAt=updated is int||updated is long?Convert.ToInt64(updated):0};
  }
  static long NowMs() {return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();}
  SettingsSyncState LoadSettingsSync() {
   if(settingsSync!=null)return settingsSync;
   try {if(File.Exists(SettingsSyncPath))settingsSync=syncJson.Deserialize<SettingsSyncState>(File.ReadAllText(SettingsSyncPath));} catch(Exception) {}
   if(settingsSync==null)settingsSync=new SettingsSyncState();
   // First run: what is here now counts as "old", so an existing cloud copy wins.
   if(settingsSync.Seen==null)settingsSync.Seen=syncJson.Serialize(SettingsSnapshot());
   return settingsSync;
  }
  void SaveSettingsSync() {try {File.WriteAllText(SettingsSyncPath,syncJson.Serialize(LoadSettingsSync()));} catch(Exception) {}}
  // Called from Save(): notices a changed preference and schedules a push. Tracked while signed out too, so the
  // first sync after sign-in knows which side is newer.
  void NoteSettingsChange() {
   if(Program.Private||settingsPulling)return;
   var sync=LoadSettingsSync();string json=syncJson.Serialize(SettingsSnapshot());
   if(json==sync.Seen)return;
   sync.Seen=json;sync.Updated=NowMs();SaveSettingsSync();
   if(!SyncEnabled||ReadAccount()==null)return;
   if(settingsPushTimer==null){settingsPushTimer=new DispatcherTimer(DispatcherPriority.Background) {Interval=TimeSpan.FromSeconds(1.5)};settingsPushTimer.Tick+=delegate {settingsPushTimer.Stop();PushSettings();};}
   settingsPushTimer.Stop();settingsPushTimer.Start();
  }
  internal bool SettingsPending {get {var sync=LoadSettingsSync();return sync.Updated>0&&sync.Seen!=sync.Synced;}}
  // A sync service from before the look-and-feel settings refuses them; it still gets the three it knows.
  static readonly string[] FirstSettingKeys={"dark","ambientYoutube","strictTracking"};
  async Task<string> PutSettings(string token,Dictionary<string,object> settings,long updated,int timeout) {
   try {return await SyncRequest("/v1/settings","PUT",new {settings=settings,updatedAt=updated},token,timeout).ConfigureAwait(false);}
   catch(SyncApiException e) {
    if(e.Code!="invalid_settings")throw;
    return await SyncRequest("/v1/settings","PUT",new {settings=settings.Where(p=>FirstSettingKeys.Contains(p.Key)).ToDictionary(p=>p.Key,p=>p.Value),updatedAt=updated},token,timeout).ConfigureAwait(false);
   }
  }
  async void PushSettings() {
   var account=ReadAccount();if(!SyncEnabled||account==null||!SettingsPending)return;
   var sync=LoadSettingsSync();var settings=SettingsSnapshot();long updated=sync.Updated;string json=sync.Seen;
   try {ReceiveSettings(ParseRemote(await PutSettings(account.token,settings,updated,10000)),json);}
   catch(Exception) {} // Offline: it stays pending and goes out on the next change or on close.
  }
  // On close there is no "later": sent while the window goes, but never held up for more than 3 seconds.
  void PushSettingsOnClose() {
   var account=ReadAccount();if(!SyncEnabled||account==null||!SettingsPending)return;
   var sync=LoadSettingsSync();var settings=SettingsSnapshot();
   try {if(Task.Run(()=>PutSettings(account.token,settings,sync.Updated,3000)).Wait(3200)){sync.Synced=sync.Seen;SaveSettingsSync();}} catch(Exception) {}
  }
  internal async void PullSettings() {
   var account=ReadAccount();if(!SyncEnabled||account==null)return;
   try {
    var remote=ParseRemote(await SyncRequest("/v1/settings","GET",null,account.token,10000));var sync=LoadSettingsSync();
    if(remote==null)return;
    if(remote.settings==null||remote.updatedAt<sync.Updated){if(SettingsPending||remote.settings==null){sync.Synced=null;if(sync.Updated==0)sync.Updated=NowMs();SaveSettingsSync();PushSettings();}return;}
    ReceiveSettings(remote,null);
   } catch(Exception) {}
  }
  // Applies a cloud copy that is newer than ours; otherwise just records that ours is synced.
  void ReceiveSettings(RemoteSettings remote,string pushed) {
   if(remote==null||remote.settings==null)return;var sync=LoadSettingsSync();
   if(remote.updatedAt<=sync.Updated){if(pushed!=null&&remote.updatedAt==sync.Updated){sync.Synced=pushed;SaveSettingsSync();}return;}
   settingsPulling=true;
   try {
    bool dark=state.Dark;double scale=TextScale;string frame=state.FrameColor;int? glass=state.HomeGlass;
    TakeSyncedSettings(state,remote.settings);
    if(state.Dark!=dark)SetDark(state.Dark);
    if(Math.Abs(TextScale-scale)>0.001)ApplyTextScale();
    if(state.FrameColor!=frame)ApplyFrame();
    if(state.HomeGlass!=glass)RefreshHomePages();
    sync.Updated=remote.updatedAt;sync.Seen=syncJson.Serialize(SettingsSnapshot());sync.Synced=sync.Seen;SaveSettingsSync();
    Save();
    Status("Settings synced from your Mica account.");
   } finally {settingsPulling=false;}
  }
  void StartSettingsSync() {
   LoadSettingsSync();
   Opened+=delegate {Dispatcher.UIThread.Post(PullSettings,DispatcherPriority.ApplicationIdle);};
   Closing+=delegate {PushSettingsOnClose();};
  }

  // ── The Discord picture for the account card, from Discord's public CDN, cached in the profile.
  static string AvatarUrl(SyncAccount account) {
   if(account.avatar!=null&&Regex.IsMatch(account.avatar,"^(a_)?[0-9a-f]{32}$")&&Regex.IsMatch(account.id??"","^[0-9]{1,20}$"))return "https://cdn.discordapp.com/avatars/"+account.id+"/"+account.avatar+".png?size=128";
   ulong id;return "https://cdn.discordapp.com/embed/avatars/"+(ulong.TryParse(account.id,out id)?(id>>22)%6:0)+".png";
  }
  async Task<Avalonia.Media.Imaging.Bitmap> LoadAvatar(SyncAccount account) {
   if(Program.SelfTestResult!=null)return null; // The self-test stays off the network here.
   string url=AvatarUrl(account),folder=Path.Combine(profile,"Avatars");
   string path=Path.Combine(folder,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant()+".png");
   try {
    if(!File.Exists(path)) {
     Directory.CreateDirectory(folder);
     byte[] data;using(var cancel=new CancellationTokenSource(10000))data=await syncHttp.GetByteArrayAsync(url,cancel.Token);
     if(data.Length==0||data.Length>2*1024*1024)return null;
     File.WriteAllBytes(path,data);
     foreach(var old in Directory.GetFiles(folder,"*.png"))if(old!=path)try {File.Delete(old);} catch(IOException) {}
    }
    using(var stream=File.OpenRead(path))return Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream,124);
   } catch(Exception) {try {if(File.Exists(path))File.Delete(path);} catch(IOException) {}return null;}
  }
 }
}
