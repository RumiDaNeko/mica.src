using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Mica {
 // Send tab to device: every Mica signed in to the same account names itself to the sync server; a tab
 // you send waits there for that device, which picks it up within a couple of minutes (or at launch) and
 // adds it to its tabs. Only the address and title travel.
 partial class BrowserApp {
  sealed class DeviceInfo {public string id {get;set;} public string name {get;set;} public long seen {get;set;}}
  sealed class SentTab {public string id {get;set;} public string from {get;set;} public string url {get;set;} public string title {get;set;} public long created {get;set;}}
  sealed class ThisDevice {public string Id {get;set;} public string Name {get;set;}}
  bool SendTabEnabled {get {return !privateMode&&(!smoke||vaultTestApi!=null);}}
  string DevicePath {get {return Path.Combine(MainRoot,"device.json");}}
  ThisDevice thisDevice;
  ThisDevice Device() {
   if(thisDevice!=null)return thisDevice;
   try {if(File.Exists(DevicePath))thisDevice=VaultCrypto.Json.Deserialize<ThisDevice>(File.ReadAllText(DevicePath));}catch(Exception){}
   if(thisDevice==null||thisDevice.Id==null||thisDevice.Id.Length<16) {
    var bytes=new byte[18];using(var rng=System.Security.Cryptography.RandomNumberGenerator.Create())rng.GetBytes(bytes);
    thisDevice=new ThisDevice {Id=Convert.ToBase64String(bytes).Replace('+','-').Replace('/','_').TrimEnd('='),Name=Environment.MachineName};
    try {File.WriteAllText(DevicePath,VaultCrypto.Json.Serialize(thisDevice));}catch(Exception){}
   }
   if(string.IsNullOrWhiteSpace(thisDevice.Name))thisDevice.Name=Environment.MachineName;
   return thisDevice;
  }
  // null when signed out; throws WebException on a server error.
  string SyncCall(string method,string route,object body) {
   var account=ReadVaultAccount();if(account==null||string.IsNullOrEmpty(account.token))return null;
   var request=(HttpWebRequest)WebRequest.Create(VaultApi+route);request.Method=method;request.AllowAutoRedirect=false;request.Timeout=10000;request.ReadWriteTimeout=10000;request.Accept="application/json";
   request.Headers[HttpRequestHeader.Authorization]="Bearer "+account.token;
   if(body!=null){var bytes=Encoding.UTF8.GetBytes(VaultCrypto.Json.Serialize(body));request.ContentType="application/json";request.ContentLength=bytes.Length;using(var stream=request.GetRequestStream())stream.Write(bytes,0,bytes.Length);}
   using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8))return reader.ReadToEnd();
  }
  static string SyncError(Exception e) {
   var web=e as WebException;var response=web==null?null:web.Response as HttpWebResponse;
   if(response!=null&&response.StatusCode==HttpStatusCode.NotFound)return "the sync server doesn't support sending tabs yet";
   if(response!=null&&(int)response.StatusCode==401)return "you're signed out; sign in again in Passwords & sync";
   return e.Message;
  }
  // Names this device and returns the account's devices.
  List<DeviceInfo> RegisterDevice() {
   var me=Device();string reply=SyncCall("POST","/v1/devices",new {id=me.Id,name=me.Name});if(reply==null)return null;
   var parsed=VaultCrypto.Json.Deserialize<Dictionary<string,List<DeviceInfo>>>(reply);List<DeviceInfo> list;return parsed!=null&&parsed.TryGetValue("devices",out list)?list:new List<DeviceInfo>();
  }

  static DispatcherTimer sentTabsTimer;static bool sentTabsChecking;
  void StartSentTabs() {
   if(!SendTabEnabled||sentTabsTimer!=null)return;
   sentTabsTimer=new DispatcherTimer {Interval=TimeSpan.FromMinutes(2)};sentTabsTimer.Tick+=delegate {CheckSentTabs();};sentTabsTimer.Start();
   // Coming back to Mica is when a tab you just sent from your phone is wanted: look then too, at most every 20 s.
   window.Activated+=delegate {if(DateTime.UtcNow-lastSentTabsCheck>TimeSpan.FromSeconds(20))CheckSentTabs();};
   CheckSentTabs();
  }
  static DateTime lastSentTabsCheck;
  // A device reinstalled or reset signs in under a new id with the same name; only its newest entry is shown.
  static List<DeviceInfo> Newest(IEnumerable<DeviceInfo> devices) {return devices.GroupBy(d=>(d.name??"").Trim()).Select(g=>g.OrderByDescending(d=>d.seen).First()).OrderByDescending(d=>d.seen).ToList();}
  internal async void CheckSentTabs() {
   if(!SendTabEnabled||sentTabsChecking||closing)return;sentTabsChecking=true;lastSentTabsCheck=DateTime.UtcNow;
   try {
    var me=Device();
    var arrived=await Task.Run(delegate {
     if(RegisterDevice()==null)return null;
     string reply=SyncCall("GET","/v1/tabs?device="+Uri.EscapeDataString(me.Id),null);if(reply==null)return null;
     var parsed=VaultCrypto.Json.Deserialize<Dictionary<string,List<SentTab>>>(reply);List<SentTab> tabs;
     return parsed!=null&&parsed.TryGetValue("tabs",out tabs)?tabs:null;
    });
    if(arrived==null||arrived.Count==0||closing)return;
    // The newest window takes them, as resting tabs: nothing loads until you open one.
    var target=Program.Browsers.Where(b=>!b.closing&&!b.privateMode&&b.MainRoot==MainRoot&&b.ProfileId==ProfileId).LastOrDefault()??this;
    var added=new List<SentTab>();
    foreach(var sent in arrived) {
     if(!Navigation.IsWeb(sent.url))continue;
     if(!target.state.Tabs.Any(t=>t.Url==sent.url))target.state.Tabs.Add(new PageEntry {Id=Guid.NewGuid().ToString("N"),Url=sent.url,Title=string.IsNullOrWhiteSpace(sent.title)?new Uri(sent.url).Host:sent.title,Space=target.state.Space,LastUsed=DateTime.UtcNow});
     added.Add(sent);
    }
    foreach(var sent in arrived){var id=sent.id;try {await Task.Run(()=>SyncCall("DELETE","/v1/tabs/"+id,null));}catch(Exception){}}
    if(added.Count>0){target.RenderTabs();target.MarkRestingTabs(false);target.Save();var from=added.Select(a=>a.from).Distinct().ToList();target.Status((added.Count==1?"“"+added[0].title+"” arrived":added.Count+" tabs arrived")+" from "+string.Join(", ",from)+". They're at the end of your tabs.");}
   }catch(Exception){}
   finally {sentTabsChecking=false;}
  }

  Popup sendTabPopup;
  internal async void ShowSendTab() {
   var tab=Active;if(tab==null||!Navigation.IsWeb(tab.Url)){Status("Open a website to send it.");return;}
   if(privateMode){Status("Private tabs can't be sent.");return;}
   if(ReadVaultAccount()==null){Status("Sign in to Passwords & sync first; your other devices sign in with the same account.");ShowVault();return;}
   CloseMenus();if(sendTabPopup==null){sendTabPopup=CreatePopup("SendTabPopup",320);sendTabPopup.Placement=PlacementMode.Bottom;sendTabPopup.VerticalOffset=6;}
   var body=PopupBody(sendTabPopup);
   var head=DialogText("Send to a device",15,"Ink");head.FontWeight=FontWeights.SemiBold;head.Margin=new Thickness(14,12,14,2);body.Children.Add(head);
   var note=DialogText("Finding your devices…",11,"Muted");note.TextWrapping=TextWrapping.Wrap;note.Margin=new Thickness(14,0,14,8);body.Children.Add(note);
   var anchor=Find<Button>("AddressButton");sendTabPopup.PlacementTarget=anchor;sendTabPopup.HorizontalOffset=(anchor.ActualWidth-320)/2;sendTabPopup.IsOpen=true;
   List<DeviceInfo> devices;
   try {devices=await Task.Run(()=>RegisterDevice());}catch(Exception e){note.Text="Couldn't reach your devices: "+SyncError(e)+".";return;}
   if(devices==null){note.Text="You're signed out. Sign in again in Passwords & sync.";return;}
   var me=Device();var others=Newest(devices.Where(d=>d.id!=me.Id&&(d.name??"").Trim()!=(me.Name??"").Trim()));
   note.Text=others.Count==0?"No other device yet. Sign in to Passwords & sync in Mica on your other PC, and it shows up here.":"“"+tab.Title+"” goes to the device you pick; it arrives within a couple of minutes.";
   foreach(var device in others) {
    var d=device;var seen=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(d.seen).ToLocalTime();
    MenuCommand(body,sendTabPopup,"",d.name,seen.Date==DateTime.Today?"today":seen.ToString("d MMM"),async delegate {
     string url=tab.Url,title=tab.Title;
     try {await Task.Run(()=>SyncCall("POST","/v1/tabs",new {to=d.id,from=me.Name,url=url,title=title.Length>300?title.Substring(0,300):title}));Status("Sent to "+d.name+".");}
     catch(Exception e){Status("Couldn't send it: "+SyncError(e)+".");}
    }).Tag="send-to:"+d.id;
   }
  }
 }
}
