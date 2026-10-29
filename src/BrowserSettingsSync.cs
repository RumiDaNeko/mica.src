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
  static SettingsSyncState settingsSync;
  static DispatcherTimer settingsPushTimer;
  static bool settingsPulling,settingsPulledAtLaunch;
  void PullSettingsAtLaunch() {if(!privateMode)LoadSettingsSync();if(settingsPulledAtLaunch||!SettingsSyncEnabled)return;settingsPulledAtLaunch=true;PullSettings();}
  string SettingsSyncPath {get {return Path.Combine(profile,"settings-sync.json");}}
  bool SettingsSyncEnabled {get {return !privateMode&&(!smoke||vaultTestApi!=null);}}
  Dictionary<string,object> SettingsSnapshot() {
   return new Dictionary<string,object> {{"dark",state.Dark},{"ambientYoutube",state.AmbientYoutube},{"strictTracking",state.StrictTracking}};
  }
  static long NowMs() {return (long)(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;}
  SettingsSyncState LoadSettingsSync() {
   if(settingsSync!=null)return settingsSync;
   try {if(File.Exists(SettingsSyncPath))settingsSync=VaultCrypto.Json.Deserialize<SettingsSyncState>(File.ReadAllText(SettingsSyncPath));}catch(Exception){}
   if(settingsSync==null)settingsSync=new SettingsSyncState();
   // First run: what is here now counts as "old", so an existing cloud copy wins.
   if(settingsSync.Seen==null)settingsSync.Seen=VaultCrypto.Json.Serialize(SettingsSnapshot());
   return settingsSync;
  }
  void SaveSettingsSync() {try {File.WriteAllText(SettingsSyncPath,VaultCrypto.Json.Serialize(settingsSync));}catch(Exception){}}
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
  async void PushSettings() {
   if(!SettingsSyncEnabled||!SettingsPending)return;
   var sync=LoadSettingsSync();var settings=SettingsSnapshot();long updated=sync.Updated;string json=sync.Seen;
   try {
    string reply=await Task.Run(()=>SettingsRequest("PUT",new {settings=settings,updatedAt=updated},10000));
    if(reply!=null)ReceiveSettings(VaultCrypto.Json.Deserialize<RemoteSettings>(reply),json);
   }catch(Exception){} // Offline: it stays pending and goes out on the next change or on close.
  }
  // On close there is no "later": send synchronously, but never hold the window for long.
  void PushSettingsOnClose() {
   if(!SettingsSyncEnabled||!SettingsPending)return;
   var sync=LoadSettingsSync();
   try {if(SettingsRequest("PUT",new {settings=SettingsSnapshot(),updatedAt=sync.Updated},3000)!=null){sync.Synced=sync.Seen;SaveSettingsSync();}}catch(Exception){}
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
