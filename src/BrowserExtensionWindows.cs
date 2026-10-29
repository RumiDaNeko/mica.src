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
  }
  static bool WatcherAsleep() {try {return targetWatcher.IsSuspended;}catch(Exception){return true;}}
  static void StopTargetWatcher(CoreWebView2 core) {try {if(targetHandler!=null)core.GetDevToolsProtocolEventReceiver("Target.targetCreated").DevToolsProtocolEventReceived-=targetHandler;}catch(Exception){}}
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
     if(targetWatcher==null||WatcherDead()){targetWatcher=null;foreach(var b in Program.Browsers.Where(x=>!x.closing)){var live=new[]{b.extensionView}.Concat(b.views.Values).FirstOrDefault(v=>v!=null&&LiveCore(v)!=null);if(live!=null){b.EnsureTargetWatcher(LiveCore(live));break;}}}
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
