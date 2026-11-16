using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Mica {
 // Self-test checks for the account, settings sync, dialogs, island prompts and links from other apps.
 sealed partial class BrowserWindow {
  static IEnumerable<T> Under<T>(Visual root) where T:Visual {return root==null?Enumerable.Empty<T>():root.GetVisualDescendants().OfType<T>();}
  async Task WaitUntil(Func<bool> done,int ms) {for(int waited=0;waited<ms&&!done();waited+=100)await Task.Delay(100);}
  // A click on the frosted window behind a dialog, as the pointer would make it.
  static void ClickBackdrop(Popup dialog) {
   var glass=Under<Grid>(dialog.Child).FirstOrDefault(g=>(g.Tag as string)=="dialog-backdrop");if(glass==null)return;
   var pointer=new Pointer(Pointer.GetNextFreeId(),PointerType.Mouse,true);
   glass.RaiseEvent(new PointerPressedEventArgs(glass,pointer,glass,new Point(5,5),0,new PointerPointProperties(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed),KeyModifiers.None));
  }

  async Task DialogAndPromptChecks() {
   // A dialog sits on the frosted window; a click outside the card closes it.
   ShowAbout();await Task.Delay(300);var about=openDialog;
   bool frosted=about!=null&&Under<Grid>(about.Child).Any(g=>(g.Tag as string)=="dialog-backdrop");
   if(about!=null)ClickBackdrop(about);await Task.Delay(200);
   Check("a dialog frosts the window behind it and closes on a click outside",frosted&&openDialog==null&&about!=null&&!about.IsOpen,"frosted="+frosted+" open="+(openDialog!=null));

   // Island prompts: one you only just saw stays; after 2 s untouched, a new one takes its place, and the one it
   // replaced comes back afterwards, unanswered until then.
   var answers=new List<string>();
   AskSite(Glyphs.Lock,"first.test wants to","use a feature of this device","Allow","Block",()=>true,delegate(bool? a) {answers.Add("first:"+a);});
   await Task.Delay(300);
   AskSite(Glyphs.Lock,"second.test wants to","use a feature of this device","Allow","Block",()=>true,delegate(bool? a) {answers.Add("second:"+a);});
   await Task.Delay(600);
   bool stayed=(sitePromptText??"").StartsWith("first.test",StringComparison.Ordinal);
   await WaitUntil(()=>(sitePromptText??"").StartsWith("second.test",StringComparison.Ordinal),4000);
   bool replaced=(sitePromptText??"").StartsWith("second.test",StringComparison.Ordinal)&&answers.Count==0;
   await Task.Delay(800);AnswerSitePrompt(false);await Task.Delay(300);
   bool back=(sitePromptText??"").StartsWith("first.test",StringComparison.Ordinal);
   await Task.Delay(800);AnswerSitePrompt(true);await Task.Delay(300);
   Check("a new island prompt waits while the shown one is fresh, then replaces one left alone for 2 s",stayed&&replaced,"stayed="+stayed+" replaced="+replaced+" answers="+string.Join(",",answers));
   Check("the replaced prompt comes back afterwards and is answered only then",back&&sitePrompt==null&&string.Join(",",answers)=="second:False,first:True",string.Join(",",answers));

   // A link from another app: a second start hands it over through the profile's socket; a new tab opens and
   // Mica comes to the front.
   string page=Path.Combine(profile,"launched.html");File.WriteAllText(page,"<title>Launched</title>");
   int tabsBefore=state.Tabs.Count,frontBefore=broughtToFront;
   bool handed=launchSocket!=null&&Launch.Forward(profile,new[]{page});
   await WaitUntil(()=>broughtToFront>frontBefore,15000); // Slow from a disk image on an Intel runner.
   var opened=Active;
   Check("a link from another app opens in a new tab and brings Mica to the front",handed&&broughtToFront>frontBefore&&state.Tabs.Count==tabsBefore+1&&opened!=null&&(opened.Entry.Url??"").StartsWith("file:",StringComparison.Ordinal)&&opened.Entry.Url.EndsWith("launched.html",StringComparison.Ordinal),
    "handed="+handed+" front="+broughtToFront+" tabs="+tabsBefore+"->"+state.Tabs.Count+" url="+(opened==null?"":opened.Entry.Url));
   if(opened!=null&&state.Tabs.Count==tabsBefore+1)CloseTab(opened.Entry.Id);
  }

  // Sign-in from the account card and a settings round trip, against the real sync Worker on loopback with Discord
  // mocked (server/worker/test/desktop-fixture.mjs). The test plays the browser side: Discord sends it back to the
  // callback, and it types the code Mica shows into the confirm page. A second device is played over HTTP: it
  // signs in to the same Discord account and writes settings as the Windows build would, Windows-only keys included.
  async Task SyncChecks() {
   string script=null;
   for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir!=null;dir=dir.Parent) {
    string candidate=Path.Combine(dir.FullName,"server","worker","test","desktop-fixture.mjs");
    if(File.Exists(candidate)){script=candidate;break;}
   }
   if(script==null){Skip("sign-in and settings sync","server/worker/test/desktop-fixture.mjs isn't next to this build");return;}
   if(!Directory.Exists(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(script)),"node_modules","miniflare"))){Skip("sign-in and settings sync","run npm install in server/worker first");return;}
   string dirPath=Path.Combine(profile,"sync-test");Directory.CreateDirectory(dirPath);string endpoint=Path.Combine(dirPath,"endpoint.txt");
   Process fixture=null;
   try {
    try {
     var start=new ProcessStartInfo("node") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
     start.ArgumentList.Add("--no-warnings");start.ArgumentList.Add(script);start.ArgumentList.Add(endpoint);
     fixture=Process.Start(start);
    } catch(Exception e) {Skip("sign-in and settings sync","node isn't available: "+e.Message);return;}
    await WaitUntil(()=>File.Exists(endpoint)&&new FileInfo(endpoint).Length>0||fixture.HasExited,30000);
    if(!File.Exists(endpoint)){Check("sync fixture starts",false,fixture.HasExited?fixture.StandardError.ReadToEnd():"timed out");return;}
    string api=File.ReadAllText(endpoint).Trim();syncTestApi=api;
    var http=new HttpClient(new HttpClientHandler {AllowAutoRedirect=false}) {Timeout=TimeSpan.FromSeconds(15)};
    Func<string,string,Task<string>> fixtureHttp=async(url,form)=>{
     var response=form==null?await http.GetAsync(url):await http.PostAsync(url,new StringContent(form,Encoding.UTF8,"application/x-www-form-urlencoded"));
     return await response.Content.ReadAsStringAsync();
    };
    Func<Uri,Task<string>> passDiscord=async url=>{
     string state=url.Query.TrimStart('?').Split('&').Single(s=>s.StartsWith("state=",StringComparison.Ordinal)).Substring(6);
     if(url.Host!="discord.com"||!url.Query.Contains("client_id=desktop-test"))throw new InvalidOperationException("unexpected authorization address "+url);
     string page=await fixtureHttp(api+"/v1/auth/discord/callback?state="+state+"&code=fixture",null);
     var form=Regex.Match(page,"name=\"form\" value=\"([A-Za-z0-9_-]{43})\"");if(!form.Success)throw new InvalidOperationException("callback didn't ask for the code: "+page);
     return form.Groups[1].Value;
    };

    // The account card, signed out: Sign in with Discord.
    var steps=new List<string>();bool cardStayed=false;
    openSignInPage=async url=>{
     try {
      string form=await passDiscord(url);
      if(signInCodeCard==null)throw new InvalidOperationException("Mica showed no sign-in code");
      ClickBackdrop(signInCodeCard);await Task.Delay(150);cardStayed=signInCodeCard!=null&&signInCodeCard.IsOpen;
      string code=Under<TextBox>(signInCodeCard.Child).Single(t=>(t.Tag as string)=="signin-code").Text;
      string done=await fixtureHttp(api+"/v1/auth/discord/confirm","form="+form+"&code="+Uri.EscapeDataString(code));
      steps.Add(done.Contains("Sign-in complete")?"confirmed":"confirm page: "+done);
     } catch(Exception e) {steps.Add("browser side: "+e.Message);}
    };
    ShowAccount(avatarButton);await Task.Delay(300);
    var signIn=openMenu==null?null:Under<Button>(openMenu.Child).FirstOrDefault(b=>(b.Tag as string)=="account-signin");
    bool offered=signIn!=null&&Under<TextBlock>(signIn).Any(t=>t.Text=="Sign in with Discord");
    Check("the signed-out account card offers Sign in with Discord",offered,null);
    if(signIn==null)return;
    signIn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    await WaitUntil(()=>ReadAccount()!=null||steps.Any(s=>s!="confirmed"),30000);
    var account=ReadAccount();
    string mode=OperatingSystem.IsWindows()?"n/a":File.GetUnixFileMode(AccountPath).ToString();
    Check("Sign in with Discord on the account card signs in through the device code",account!=null&&account.id=="223456789012345678"&&steps.SequenceEqual(new[]{"confirmed"})&&signInCodeCard==null,string.Join("; ",steps)+" account="+(account!=null));
    Check("the sign-in code card stays when you click outside it",cardStayed,null);
    Check("the session is kept in a file only you can read",OperatingSystem.IsWindows()||mode==(UnixFileMode.UserRead|UnixFileMode.UserWrite).ToString(),mode);
    if(account==null)return;
    ShowAccount(avatarButton);await Task.Delay(300);
    bool shown=openMenu!=null&&Under<TextBlock>(openMenu.Child).Any(t=>t.Text=="Sync fixture")&&Under<TextBlock>(openMenu.Child).Any(t=>t.Text=="Sign out");
    if(openMenu!=null)openMenu.IsOpen=false;
    Check("the card shows the Discord account and Sign out",shown,null);

    // This device changes how Mica looks; it reaches the account a moment later.
    await WaitUntil(()=>!SettingsPending,5000); // The pull after sign-in.
    SetDark(true);state.TextScale=1.3;ApplyTextScale();state.FrameColor="#336699";ApplyFrame();state.ReaderSize=22;state.ReaderSerif=false;state.HomeGlass=40;Save();
    await WaitUntil(()=>!SettingsPending,15000);
    Func<string,Task<Dictionary<string,object>>> cloud=async token=>{
     var request=new HttpRequestMessage(HttpMethod.Get,api+"/v1/settings");request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
     var reply=syncJson.DeserializeObject(await (await http.SendAsync(request)).Content.ReadAsStringAsync()) as Dictionary<string,object>;
     return reply==null?null:reply["settings"] as Dictionary<string,object>;
    };
    var up=await cloud(account.token);
    Check("this device's settings reach the account, all ten keys",!SettingsPending&&up!=null&&up.Count==10&&(bool)up["dark"]&&Convert.ToDouble(up["textScale"])==1.3&&(string)up["frameColor"]=="#336699"&&Convert.ToInt32(up["readerSize"])==22&&Convert.ToInt32(up["homeGlass"])==40,up==null?"none":syncJson.Serialize(up));

    // The other device: signs in to the same Discord account and writes newer settings, as Windows would.
    string verifier=RandomVerifier();
    var started=syncJson.Deserialize<LoginStart>(await (await http.PostAsync(api+"/v1/auth/discord/start",new StringContent(syncJson.Serialize(new {challenge=Challenge(verifier),confirm=true}),Encoding.UTF8,"application/json"))).Content.ReadAsStringAsync());
    string otherForm=await passDiscord(new Uri(started.authorizationUrl));
    await fixtureHttp(api+"/v1/auth/discord/confirm","form="+otherForm+"&code="+Uri.EscapeDataString(started.confirmationCode));
    var polled=syncJson.Deserialize<LoginPoll>(await (await http.PostAsync(api+"/v1/auth/discord/poll",new StringContent(syncJson.Serialize(new {requestId=started.requestId,verifier=verifier}),Encoding.UTF8,"application/json"))).Content.ReadAsStringAsync());
    string other=polled==null?null:polled.token;
    Check("a second device signs in to the same account",other!=null&&polled.user.id==account.id,polled==null?"none":polled.status);
    if(other==null)return;
    var windows=new Dictionary<string,object>{{"dark",false},{"ambientYoutube",false},{"strictTracking",true},{"textScale",1.15},{"treeTabs",true},{"readerSize",20},{"readerSerif",true},{"frameColor",""},{"homeGlass",-1},{"closeEffect","melt"}};
    var put=new HttpRequestMessage(HttpMethod.Put,api+"/v1/settings") {Content=new StringContent(syncJson.Serialize(new {settings=windows,updatedAt=NowMs()}),Encoding.UTF8,"application/json")};
    put.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",other);
    var putReply=await http.SendAsync(put);string putBody=await putReply.Content.ReadAsStringAsync();
    PullSettings();await WaitUntil(()=>!state.Dark&&state.ReaderSerif,15000);
    Check("a pull takes the other device's newer settings",putReply.IsSuccessStatusCode&&!state.Dark&&Math.Abs(TextScale-1.15)<0.001&&state.ReaderSerif&&state.ReaderSize==20&&state.FrameColor==null&&state.HomeGlass==null,
     "put="+(int)putReply.StatusCode+" "+putBody+" local updated="+LoadSettingsSync().Updated+" dark="+state.Dark+" text="+state.TextScale+" serif="+state.ReaderSerif+" reader="+state.ReaderSize+" frame="+state.FrameColor+" glass="+state.HomeGlass);
    var saved=StateStore.Load(statePath);
    Check("synced settings are saved, the Windows-only ones kept as they came",saved.ReaderSerif&&!saved.Dark&&saved.TreeTabs&&saved.CloseEffect=="melt"&&saved.StrictTracking,"tree="+saved.TreeTabs+" effect="+saved.CloseEffect+" strict="+saved.StrictTracking);

    // A later change here goes back without wiping what this shell doesn't use.
    state.ReaderSize=24;Save();
    await WaitUntil(()=>!SettingsPending,15000);
    up=await cloud(other);
    Check("a change here goes back with the Windows-only settings untouched",!SettingsPending&&up!=null&&Convert.ToInt32(up["readerSize"])==24&&(bool)up["treeTabs"]&&(string)up["closeEffect"]=="melt"&&(bool)up["strictTracking"],up==null?"none":syncJson.Serialize(up));

    // Values Mica would never write: the service refuses them, and if a server sent them anyway only valid ones apply.
    var bad=new Dictionary<string,object>{{"dark",true},{"textScale",9},{"frameColor","red;}"},{"closeEffect","boom!"},{"readerSize",500},{"homeGlass",40}};
    var badPut=new HttpRequestMessage(HttpMethod.Put,api+"/v1/settings") {Content=new StringContent(syncJson.Serialize(new {settings=bad,updatedAt=NowMs()+60000}),Encoding.UTF8,"application/json")};
    badPut.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",other);
    var refused=await http.SendAsync(badPut);string refusedBody=await refused.Content.ReadAsStringAsync();
    Check("the sync service refuses values Mica would never write",(int)refused.StatusCode==400&&refusedBody.Contains("invalid_settings"),refusedBody);
    ReceiveSettings(new RemoteSettings {settings=bad,updatedAt=NowMs()+60000},null);await Task.Delay(200);
    Check("out-of-range synced values are ignored, valid ones still apply",state.Dark&&state.HomeGlass==40&&Math.Abs(TextScale-1.15)<0.001&&state.FrameColor==null&&state.CloseEffect=="melt"&&state.ReaderSize==24,
     "dark="+state.Dark+" glass="+state.HomeGlass+" text="+state.TextScale+" frame="+state.FrameColor+" effect="+state.CloseEffect+" reader="+state.ReaderSize);

    SignOut();
    Check("Sign out forgets the session on this device",ReadAccount()==null&&!File.Exists(AccountPath),null);
    http.Dispose();
   } catch(Exception e) {Check("sign-in and settings sync ran",false,e.ToString());}
   finally {
    syncTestApi=null;openSignInPage=null;
    if(fixture!=null){try {if(!fixture.HasExited)fixture.Kill(true);} catch(Exception) {}fixture.Dispose();}
   }
  }
 }
}
