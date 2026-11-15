using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Mica {
 // Preferences follow the Mica account without unlocking the vault: they are not
 // secret, so they sync as plain JSON (/v1/settings). Every change is pushed a moment
 // later and again when Mica closes; launch and sign-in pull. The newest change wins.
 partial class BrowserApp {
  sealed class SettingsSyncState {public long Updated {get;set;} public string Seen {get;set;} public string Synced {get;set;}}
  sealed class RemoteSettings {public Dictionary<string,object> settings {get;set;} public long updatedAt {get;set;}}
  // Per profile: two profiles open in one Mica are two accounts' worth of settings.
  static readonly Dictionary<string,SettingsSyncState> settingsSyncs=new Dictionary<string,SettingsSyncState>(StringComparer.OrdinalIgnoreCase);
  static readonly HashSet<string> settingsPullingProfiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase),settingsPulledProfiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  DispatcherTimer settingsPushTimer;
  bool settingsPulling {get {return settingsPullingProfiles.Contains(profile);} set {if(value)settingsPullingProfiles.Add(profile);else settingsPullingProfiles.Remove(profile);}}
  void PullSettingsAtLaunch() {if(!privateMode)LoadSettingsSync();if(!SettingsSyncEnabled||!settingsPulledProfiles.Add(profile))return;PullSettings();}
  string SettingsSyncPath {get {return Path.Combine(profile,"settings-sync.json");}}
  bool SettingsSyncEnabled {get {return !privateMode&&(!smoke||vaultTestApi!=null);}}
  Dictionary<string,object> SettingsSnapshot() {
   // Only how Mica looks and behaves; nothing that names a site, a file or an address. Null travels as "".
   return new Dictionary<string,object> {{"dark",state.Dark},{"ambientYoutube",state.AmbientYoutube},{"strictTracking",state.StrictTracking},
    {"textScale",UiScale},{"treeTabs",state.TreeTabs},{"readerSize",state.ReaderSize},{"readerSerif",state.ReaderSerif},
    {"frameColor",state.FrameColor??""},{"homeGlass",state.HomeGlass??-1},{"closeEffect",state.CloseEffect??""}};
  }
  static bool SyncedNumber(object value,out double number) {number=0;if(!(value is int||value is long||value is decimal||value is double))return false;number=Convert.ToDouble(value,System.Globalization.CultureInfo.InvariantCulture);return true;}
  // Takes the plain look-and-feel values from a cloud copy, each only when it is one Mica could have written.
  static bool TakeSyncedLook(BrowserState into,Dictionary<string,object> from) {
   object value;double number;bool changed=false;
   if(from.TryGetValue("textScale",out value)&&SyncedNumber(value,out number)&&(number==1||number==1.15||number==1.3)&&Math.Abs((into.TextScale<=0?1:into.TextScale)-number)>0.001){into.TextScale=number;changed=true;}
   if(from.TryGetValue("treeTabs",out value)&&value is bool&&into.TreeTabs!=(bool)value){into.TreeTabs=(bool)value;changed=true;}
   if(from.TryGetValue("readerSize",out value)&&SyncedNumber(value,out number)&&(number==0||number>=13&&number<=28)&&number==Math.Floor(number)&&into.ReaderSize!=(int)number){into.ReaderSize=(int)number;changed=true;}
   if(from.TryGetValue("readerSerif",out value)&&value is bool&&into.ReaderSerif!=(bool)value){into.ReaderSerif=(bool)value;changed=true;}
   if(from.TryGetValue("frameColor",out value)&&value is string&&((string)value==""||System.Text.RegularExpressions.Regex.IsMatch((string)value,"^#[0-9A-Fa-f]{6}$"))){string color=(string)value==""?null:(string)value;if(into.FrameColor!=color){into.FrameColor=color;changed=true;}}
   if(from.TryGetValue("homeGlass",out value)&&SyncedNumber(value,out number)&&number>=-1&&number<=100&&number==Math.Floor(number)){int? glass=number<0?(int?)null:(int)number;if(into.HomeGlass!=glass){into.HomeGlass=glass;changed=true;}}
   if(from.TryGetValue("closeEffect",out value)&&value is string&&((string)value==""||CloseEffects.Any(e=>e[0]==(string)value&&e[0]!="stars"))){string effect=(string)value==""?null:(string)value;if(into.CloseEffect!=effect){into.CloseEffect=effect;changed=true;}}
   return changed;
  }
  static long NowMs() {return (long)(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;}
  SettingsSyncState LoadSettingsSync() {
   SettingsSyncState sync;if(settingsSyncs.TryGetValue(profile,out sync))return sync;
   try {if(File.Exists(SettingsSyncPath))sync=VaultCrypto.Json.Deserialize<SettingsSyncState>(File.ReadAllText(SettingsSyncPath));}catch(Exception){}
   if(sync==null)sync=new SettingsSyncState();
   // First run: what is here now counts as "old", so an existing cloud copy wins.
   if(sync.Seen==null)sync.Seen=VaultCrypto.Json.Serialize(SettingsSnapshot());
   else {
    // Settings added in a newer Mica count as already seen, so upgrading doesn't look like a change made here.
    var now=SettingsSnapshot();Dictionary<string,object> seen=null;try {seen=VaultCrypto.Json.Deserialize<Dictionary<string,object>>(sync.Seen);}catch(Exception){}
    if(seen!=null&&now.Keys.Any(k=>!seen.ContainsKey(k))){
     bool same=sync.Seen==sync.Synced;foreach(var key in now.Keys.ToList())if(seen.ContainsKey(key))now[key]=seen[key];
     sync.Seen=VaultCrypto.Json.Serialize(now);if(same)sync.Synced=sync.Seen;
    }
   }
   settingsSyncs[profile]=sync;return sync;
  }
  void SaveSettingsSync() {try {File.WriteAllText(SettingsSyncPath,VaultCrypto.Json.Serialize(LoadSettingsSync()));}catch(Exception){}}
  // Called from Save(): notices a changed preference and schedules a push.
  void NoteSettingsChange() {
   // Tracked even while signed out, so the first sync after sign-in knows which side is newer.
   if(privateMode||settingsPulling)return;
   var sync=LoadSettingsSync();string json=VaultCrypto.Json.Serialize(SettingsSnapshot());
   if(json==sync.Seen)return;
   sync.Seen=json;sync.Updated=NowMs();SaveSettingsSync();
   if(!SettingsSyncEnabled)return;
   if(settingsPushTimer==null){settingsPushTimer=new DispatcherTimer(DispatcherPriority.Background,window.Dispatcher) {Interval=TimeSpan.FromSeconds(1.5)};settingsPushTimer.Tick+=delegate {settingsPushTimer.Stop();PushSettings();};}
   settingsPushTimer.Stop();settingsPushTimer.Start();
  }
  bool SettingsPending {get {var sync=LoadSettingsSync();return sync.Updated>0&&sync.Seen!=sync.Synced;}}
  string SettingsRequest(string method,object body,int timeout) {
   var account=ReadVaultAccount();if(account==null||string.IsNullOrEmpty(account.token))return null;
   var request=(HttpWebRequest)WebRequest.Create(VaultApi+"/v1/settings");request.Method=method;request.AllowAutoRedirect=false;request.Timeout=timeout;request.ReadWriteTimeout=timeout;request.Accept="application/json";
   request.Headers[HttpRequestHeader.Authorization]="Bearer "+account.token;
   if(body!=null){var bytes=Encoding.UTF8.GetBytes(VaultCrypto.Json.Serialize(body));request.ContentType="application/json";request.ContentLength=bytes.Length;using(var stream=request.GetRequestStream())stream.Write(bytes,0,bytes.Length);}
   using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8))return reader.ReadToEnd();
  }
  // A sync service from before the look-and-feel settings refuses them; it still gets the three it knows.
  static readonly string[] FirstSettingKeys={"dark","ambientYoutube","strictTracking"};
  string PutSettings(Dictionary<string,object> settings,long updated,int timeout) {
   try {return SettingsRequest("PUT",new {settings=settings,updatedAt=updated},timeout);}
   catch(WebException e) {
    if(ApiError(e)!="invalid_settings")throw;
    return SettingsRequest("PUT",new {settings=settings.Where(p=>FirstSettingKeys.Contains(p.Key)).ToDictionary(p=>p.Key,p=>p.Value),updatedAt=updated},timeout);
   }
  }
  async void PushSettings() {
   if(!SettingsSyncEnabled||!SettingsPending)return;
   var sync=LoadSettingsSync();var settings=SettingsSnapshot();long updated=sync.Updated;string json=sync.Seen;
   try {
    string reply=await Task.Run(()=>PutSettings(settings,updated,10000));
    if(reply!=null)ReceiveSettings(VaultCrypto.Json.Deserialize<RemoteSettings>(reply),json);
   }catch(Exception){} // Offline: it stays pending and goes out on the next change or on close.
  }
  // On close there is no "later": send synchronously, but never hold the window for long.
  void PushSettingsOnClose() {
   if(!SettingsSyncEnabled||!SettingsPending)return;
   var sync=LoadSettingsSync();
   try {if(PutSettings(SettingsSnapshot(),sync.Updated,3000)!=null){sync.Synced=sync.Seen;SaveSettingsSync();}}catch(Exception){}
  }
  async void PullSettings() {
   if(!SettingsSyncEnabled)return;
   try {
    string reply=await Task.Run(()=>SettingsRequest("GET",null,10000));if(reply==null||closing)return;
    var remote=VaultCrypto.Json.Deserialize<RemoteSettings>(reply);var sync=LoadSettingsSync();
    if(remote.settings==null||remote.updatedAt<sync.Updated){if(SettingsPending||remote.settings==null){sync.Synced=null;if(sync.Updated==0)sync.Updated=NowMs();SaveSettingsSync();PushSettings();}return;}
    ReceiveSettings(remote,null);
   }catch(Exception){}
  }
  // Applies a cloud copy that is newer than ours; otherwise just records that ours is synced.
  void ReceiveSettings(RemoteSettings remote,string pushed) {
   if(remote==null||remote.settings==null||closing)return;var sync=LoadSettingsSync();
   if(remote.updatedAt<=sync.Updated){if(pushed!=null&&remote.updatedAt==sync.Updated){sync.Synced=pushed;SaveSettingsSync();}return;}
   settingsPulling=true;
   try {
    object value;
    foreach(var browser in Program.Browsers.Where(b=>!b.closing&&!b.privateMode&&b.profile==profile)) {
     bool themeChanged=false;
     if(remote.settings.TryGetValue("dark",out value)&&value is bool&&browser.state.Dark!=(bool)value){browser.state.Dark=(bool)value;themeChanged=true;}
     // Settings travel in plain JSON, so the server could change them: stronger protection syncs freely,
     // weaker protection is only turned on here once this device says so (below).
     if(remote.settings.TryGetValue("strictTracking",out value)&&value is bool&&(bool)value&&browser.state.StrictTracking!=(bool)value){browser.state.StrictTracking=(bool)value;browser.state.TrackingLevel=(bool)value?3:2;var view=browser.views.Values.Select(LiveCore).FirstOrDefault(c=>c!=null);if(view!=null)browser.ApplyTrackingPrevention(view);}
     if(themeChanged)browser.ApplyTheme();
     if(TakeSyncedLook(browser.state,remote.settings)){browser.ApplyTextScale();browser.ApplyFrame();browser.UpdateHomeGlass();browser.RenderTabs();}
    }
    var expected=SettingsSnapshot();
    if(remote.settings.TryGetValue("strictTracking",out value)&&value is bool&&!(bool)value&&state.StrictTracking){expected["strictTracking"]=false;window.Dispatcher.BeginInvoke(new Action(delegate {
     ShowChoiceBubble(Find<System.Windows.Controls.Button>("AddressButton"),"","From your other device","Strict tracking protection off","Your other device uses balanced tracking protection. Use it here too? Strict blocks more trackers.",null,new[]{new[]{"Keep strict","skip",""},new[]{"Use balanced","off","primary"}},()=>!closing,delegate(string choice) {
      if(choice!="off")return;
      foreach(var browser in Program.Browsers.Where(b=>!b.closing&&!b.privateMode&&b.profile==profile)){browser.state.StrictTracking=false;browser.state.TrackingLevel=2;var view=browser.views.Values.Select(LiveCore).FirstOrDefault(c=>c!=null);if(view!=null)browser.ApplyTrackingPrevention(view);browser.Save();}
     });
    }));}
    if(remote.settings.TryGetValue("ambientYoutube",out value)&&value is bool&&state.AmbientYoutube!=(bool)value){bool wanted=(bool)value;expected["ambientYoutube"]=wanted;window.Dispatcher.BeginInvoke(new Action(async delegate {try {
     // Turning it off syncs freely; turning it on would install an extension, which this device asks about first.
     if(!wanted)await SetAmbientExtension(false);
     else ShowChoiceBubble(Find<System.Windows.Controls.Button>("AddressButton"),"\uE706","From your other device","YouTube ambient","Turning it on adds its extension from the Chrome Web Store on this PC too.",null,new[]{new[]{"Not here","skip",""},new[]{"Turn on","on","primary"}},()=>!closing,async delegate(string choice) {try {if(choice=="on")await SetAmbientExtension(true);}catch(Exception e){Status(ExtensionError(e));}});
    }catch(Exception){}}));}
    sync.Updated=remote.updatedAt;sync.Seen=VaultCrypto.Json.Serialize(expected);sync.Synced=sync.Seen;SaveSettingsSync();
    foreach(var browser in Program.Browsers.Where(b=>!b.closing&&!b.privateMode&&b.profile==profile))browser.Save();
    Status("Settings synced from your Mica account.");
   }finally {settingsPulling=false;}
  }
 }
}
