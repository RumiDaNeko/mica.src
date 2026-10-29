using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // Extensions can open pages without any Mica view asking (chrome.tabs.create,
 // windows.create, uninstall "goodbye" pages). The engine would show those in
 // its own Edge-style window. Mica watches DevTools target events, and any page
 // target it did not create itself is closed and reopened inside Mica.
 partial class BrowserApp {
  static readonly HashSet<string> ownTargets=new HashSet<string>();
  static CoreWebView2 targetWatcher;
  static EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> targetHandler;
  static readonly HashSet<string> adoptingTargets=new HashSet<string>();
  async void RegisterOwnTarget(CoreWebView2 core) {
   try {
    var info=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(await core.CallDevToolsProtocolMethodAsync("Target.getTargetInfo","{}"));
    var target=info["targetInfo"] as Dictionary<string,object>;if(target!=null)ownTargets.Add((string)target["targetId"]);
   }catch(Exception){}
   // The hidden extension host is never frozen, so it is the best place to listen;
   // a suspended tab stops delivering DevTools events and pages slip out in Edge windows.
   WatchEngineWindows();
   EnsureTargetWatcher(core,extensionView!=null&&LiveCore(extensionView)==core);
  }
  void EnsureTargetWatcher(CoreWebView2 core,bool prefer=false) {
   if(targetWatcher==core||targetWatcher!=null&&!prefer&&!WatcherAsleep())return;
   var old=targetWatcher;EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> handler=OnTargetCreated;
   try {
    targetWatcher=core;
    core.GetDevToolsProtocolEventReceiver("Target.targetCreated").DevToolsProtocolEventReceived+=handler;
    core.CallDevToolsProtocolMethodAsync("Target.setDiscoverTargets","{\"discover\":true}");
   }catch(Exception){targetWatcher=old;return;}
   if(old!=null)StopTargetWatcher(old);targetHandler=handler;
   ListenToExtensionWorkers(core);
  }
  static bool WatcherAsleep() {try {return targetWatcher.IsSuspended;}catch(Exception){return true;}}
  static void StopTargetWatcher(CoreWebView2 core) {
   try {if(targetHandler!=null)core.GetDevToolsProtocolEventReceiver("Target.targetCreated").DevToolsProtocolEventReceived-=targetHandler;}catch(Exception){}
   try {if(groupsHandler!=null)core.GetDevToolsProtocolEventReceiver("Runtime.bindingCalled").DevToolsProtocolEventReceived-=groupsHandler;}catch(Exception){}
   groupsHandler=null;workerSessions.Clear();
  }

  // The tab groups AI extensions make (Claude's tools keep their tabs in one) show in Mica's tab list. The engine
  // can't group tabs, so the extension's shim keeps them (ClaudeSidePanelPatch.TabsShim) and reports them from its
  // worker through a DevTools binding; Mica listens on the worker from the same view that watches targets.
  static readonly Dictionary<string,string> workerSessions=new Dictionary<string,string>();
  static EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> groupsHandler;
  async void ListenToExtensionWorkers(CoreWebView2 core) {
   try {
    var all=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(await core.CallDevToolsProtocolMethodAsync("Target.getTargets","{}"));
    foreach(Dictionary<string,object> info in (System.Collections.ArrayList)all["targetInfos"])if((string)info["type"]=="service_worker")ListenToExtensionWorker((string)info["targetId"],(string)info["url"]);
   }catch(Exception){}
  }
  async void ListenToExtensionWorker(string target,string url) {
   var core=targetWatcher;
   if(core==null||privateMode||!AiExtensions.Any(ai=>(url??"").StartsWith("chrome-extension://"+ai.Id+"/",StringComparison.Ordinal))||workerSessions.ContainsKey(target))return;
   workerSessions[target]=null;
   try {
    if(groupsHandler==null){groupsHandler=OnGroupsReported;core.GetDevToolsProtocolEventReceiver("Runtime.bindingCalled").DevToolsProtocolEventReceived+=groupsHandler;}
    var ser=new JavaScriptSerializer();
    string session=(string)ser.Deserialize<Dictionary<string,object>>(await core.CallDevToolsProtocolMethodAsync("Target.attachToTarget",ser.Serialize(new {targetId=target,flatten=true})))["sessionId"];
    if(targetWatcher!=core)return;
    workerSessions[target]=session;
    await core.CallDevToolsProtocolMethodForSessionAsync(session,"Runtime.addBinding","{\"name\":\"__micaGroups\"}");
    await core.CallDevToolsProtocolMethodForSessionAsync(session,"Runtime.evaluate","{\"expression\":\"self.__micaGroupsNow&&self.__micaGroupsNow()\"}");
   }catch(Exception){workerSessions.Remove(target);}
  }
  void OnGroupsReported(object sender,CoreWebView2DevToolsProtocolEventReceivedEventArgs e) {
   try {
    var call=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(e.ParameterObjectAsJson);
    if((string)call["name"]!="__micaGroups"||!workerSessions.ContainsValue(e.SessionId))return;
    string payload=(string)call["payload"];
    window.Dispatcher.BeginInvoke(new Action(async delegate {foreach(var browser in Program.Browsers.Where(b=>!b.closing&&!b.privateMode).ToList())await browser.ShowExtensionGroups(payload);}));
   }catch(Exception){}
  }
  // Chrome's group colours, as Mica's.
  static readonly Dictionary<string,string> chromeGroupColors=new Dictionary<string,string> {{"blue","#4A7FD9"},{"red","#D9534F"},{"yellow","#E0A526"},{"green","#3E9B5F"},{"pink","#D45C9B"},{"purple","#8B62D9"},{"cyan","#2E9FB5"},{"orange","#E07B35"},{"grey","#8A8A93"}};
  internal static string ExtensionGroupId(string extension,object group) {return StateStore.ExtensionGroupPrefix+extension.Substring(0,Math.Min(8,extension.Length))+"-"+group;}
  // Puts this window's tabs into the extension's groups (and out of the ones they left), keeping the group's
  // title, colour and folding as the extension set them.
  async Task ShowExtensionGroups(string payload) {
   var ser=new JavaScriptSerializer();var report=ser.Deserialize<Dictionary<string,object>>(payload);
   string extension=(string)report["extension"],prefix=ExtensionGroupId(extension,"");
   var byTarget=new Dictionary<string,PageEntry>();
   foreach(var pair in views.ToList()) {
    var core=LiveCore(pair.Value);var tab=state.Tabs.FirstOrDefault(t=>t.Id==pair.Key);if(core==null||tab==null)continue;
    try {var info=ser.Deserialize<Dictionary<string,object>>(await core.CallDevToolsProtocolMethodAsync("Target.getTargetInfo","{}"))["targetInfo"] as Dictionary<string,object>;byTarget[(string)info["targetId"]]=tab;}catch(Exception){}
   }
   if(closing)return;
   var placed=new Dictionary<string,string>();
   foreach(Dictionary<string,object> group in (System.Collections.ArrayList)report["groups"]) {
    var members=((System.Collections.ArrayList)group["targets"]).OfType<string>().Where(byTarget.ContainsKey).Select(t=>byTarget[t]).Where(t=>!t.Pinned).ToList();
    if(members.Count==0)continue;
    string id=ExtensionGroupId(extension,group["id"]);var mica=state.FindGroup(id);
    if(mica==null){mica=new TabGroup {Id=id};state.Groups.Add(mica);}
    object value;string color;
    mica.Name=group.TryGetValue("title",out value)?value as string??"":"";
    mica.Color=group.TryGetValue("color",out value)&&chromeGroupColors.TryGetValue(value as string??"",out color)?color:chromeGroupColors["grey"];
    mica.Collapsed=group.TryGetValue("collapsed",out value)&&value is bool&&(bool)value;
    foreach(var tab in members)placed[tab.Id]=id;
   }
   // Tabs out of sight (resting, or in another window) keep the group they had.
   foreach(var tab in state.Tabs) {
    string want;bool known=byTarget.ContainsValue(tab);
    if(placed.TryGetValue(tab.Id,out want))tab.Group=want;
    else if(known&&tab.Group!=null&&tab.Group.StartsWith(prefix,StringComparison.Ordinal))tab.Group=null;
   }
   state.NormalizeGroups();RenderTabs();Save();
  }
  // The watcher's view can go away (closed, put to rest or frozen); hand over to a view that stays awake.
  void ReplaceTargetWatcher(WebView2 leaving) {
   if(targetWatcher==null||leaving==null||LiveCore(leaving)!=null&&LiveCore(leaving)!=targetWatcher)return;
   var old=targetWatcher;targetWatcher=null;
   StopTargetWatcher(old);targetHandler=null;
   Func<WebView2,bool> awake=v=>v!=null&&v!=leaving&&LiveCore(v)!=null&&!LiveCore(v).IsSuspended;
   var browsers=Program.Browsers.Where(b=>!b.closing).ToList();
   foreach(var browser in browsers)if(awake(browser.extensionView)){browser.EnsureTargetWatcher(LiveCore(browser.extensionView));return;}
   foreach(var browser in browsers){WebView2 active;if(browser.views.TryGetValue(browser.state.ActiveId??"",out active)&&awake(active)){browser.EnsureTargetWatcher(LiveCore(active));return;}}
   foreach(var browser in browsers){var next=browser.views.Values.FirstOrDefault(awake);if(next!=null){browser.EnsureTargetWatcher(LiveCore(next));return;}}
  }
  void OnTargetCreated(object sender,CoreWebView2DevToolsProtocolEventReceivedEventArgs e) {
   try {
    var target=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(e.ParameterObjectAsJson)["targetInfo"] as Dictionary<string,object>;
    if(target!=null&&(string)target["type"]=="page")AdoptTarget((string)target["targetId"]);
    else if(target!=null&&(string)target["type"]=="service_worker")ListenToExtensionWorker((string)target["targetId"],(string)target["url"]);
   }catch(Exception){}
  }
  // The engine's control closes the window it sits in when its page asks to close (a page's window.close(), an
  // extension's chrome.tabs.remove or windows.remove). Views inside Mica's window may only close themselves.
  static void KeepWindowOpen(WebView2 view) {
   try {
    const System.Reflection.BindingFlags inside=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
    // The control keeps its engine wiring in a helper (WebView2Base), which handles the request.
    var helper=typeof(WebView2).GetFields(inside).Select(f=>f.GetValue(view)).FirstOrDefault(v=>v!=null&&v.GetType().Name=="WebView2Base");
    var closer=helper==null?null:helper.GetType().GetMethod("CoreWebView2_WindowCloseRequested",inside);
    if(closer!=null)view.CoreWebView2.WindowCloseRequested-=(EventHandler<object>)Delegate.CreateDelegate(typeof(EventHandler<object>),helper,closer);
   }catch(Exception){}
  }
  static bool AnyBrowserOpen() {return Program.Browsers.Any(b=>!b.closing);}
  async void AdoptTarget(string id) {
   try {
    if(ownTargets.Contains(id)||!adoptingTargets.Add(id))return;
    // Give Mica's own new views time to register, and the page time to get its URL.
    string url=null;
    for(int i=0;i<16&&AnyBrowserOpen();i++) {
     await Task.Delay(250);if(ownTargets.Contains(id)){adoptingTargets.Remove(id);return;}
     url=await TargetUrl(id);if(url==null){adoptingTargets.Remove(id);return;}
     if(i>=4&&url.Length>0&&url!="about:blank"||IsExtensionTabRequest(url))break;
    }
    if(!AnyBrowserOpen()||ownTargets.Contains(id)||string.IsNullOrEmpty(url)||url.StartsWith("devtools:",StringComparison.OrdinalIgnoreCase)||url.StartsWith("http://127.0.0.1:"+devToolsPort+"/",StringComparison.OrdinalIgnoreCase)){adoptingTargets.Remove(id);return;}
    bool web=Navigation.IsWeb(url),extensionPage=url.StartsWith("chrome-extension://",StringComparison.OrdinalIgnoreCase);
    if(!web&&!extensionPage){adoptingTargets.Remove(id);return;}
    await targetWatcher.CallDevToolsProtocolMethodAsync("Target.closeTarget","{\"targetId\":\""+id+"\"}");
    var host=Program.Browsers.FirstOrDefault(b=>!b.closing&&!b.privateMode&&b.window.IsActive)??Program.Browsers.FirstOrDefault(b=>!b.closing&&!b.privateMode)??Program.Browsers.FirstOrDefault(b=>!b.closing);
    if(host==null)return;
    if(IsExtensionTabRequest(url))host.OpenExtensionTab(url);else if(web)host.Navigate(url,true);else host.OpenExtensionPage(url);
    host.Status("Opened a page from an extension inside Mica.");
   }catch(Exception){adoptingTargets.Remove(id);}
  }
  // Safety net: if the engine ever shows a window of its own (the target event was missed,
  // or the listening view died), sweep its pages into Mica. Mica's own views are child
  // windows, so a visible top-level engine window that isn't a small widget means a page escaped.
  static System.Windows.Threading.DispatcherTimer engineWindowTimer;
  internal static int engineSweeps;
  void WatchEngineWindows() {
   if(engineWindowTimer!=null)return;
   engineWindowTimer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(800)};
   engineWindowTimer.Tick+=async delegate {
    var app=Program.Browsers.FirstOrDefault(b=>!b.closing);if(app==null)return;
    try {
     if(targetWatcher==null||WatcherDead()){targetWatcher=null;workerSessions.Clear();groupsHandler=null;foreach(var b in Program.Browsers.Where(x=>!x.closing)){var live=new[]{b.extensionView}.Concat(b.views.Values).FirstOrDefault(v=>v!=null&&LiveCore(v)!=null);if(live!=null){b.EnsureTargetWatcher(LiveCore(live));break;}}}
     if(targetWatcher==null||!EngineHasOwnWindow(targetWatcher.BrowserProcessId))return;
     engineSweeps++;
     var all=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(await targetWatcher.CallDevToolsProtocolMethodAsync("Target.getTargets","{}"));
     foreach(Dictionary<string,object> info in (System.Collections.ArrayList)all["targetInfos"])if((string)info["type"]=="page")app.AdoptTarget((string)info["targetId"]);
    }catch(Exception){}
   };
   engineWindowTimer.Start();
  }
  static bool WatcherDead() {try {var unused=targetWatcher.BrowserProcessId;return false;}catch(Exception){return true;}}
  static bool EngineHasOwnWindow(uint pid) {
   bool found=false;
   EnumWindows(delegate(IntPtr h,IntPtr d){uint owner;GetWindowThreadProcessId(h,out owner);RECT r;if(owner==pid&&IsWindowVisible(h)&&GetWindowRect(h,out r)&&r.Right-r.Left>=320&&r.Bottom-r.Top>=240&&(GetWindowLongPtr(h,-16).ToInt64()&0x00C00000)==0x00C00000){found=true;return false;}return true;},IntPtr.Zero);
   return found;
  }
  [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct RECT {public int Left,Top,Right,Bottom;}
  delegate bool EnumWindowsProc(IntPtr hwnd,IntPtr data);
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback,IntPtr data);
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
  async Task<string> TargetUrl(string id) {
   var all=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(await targetWatcher.CallDevToolsProtocolMethodAsync("Target.getTargets","{}"));
   foreach(Dictionary<string,object> info in (System.Collections.ArrayList)all["targetInfos"])if((string)info["targetId"]==id)return (string)info["url"]??"";
   return null;
  }
 }
}
