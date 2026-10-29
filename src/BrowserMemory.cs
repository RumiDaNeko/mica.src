using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // Keeps the engine light. Background tabs first drop to low memory and freeze (instant to
 // bring back, no reload); after two minutes they rest completely (SleepIdleTabs).
 partial class BrowserApp {
  // No spare renderer waiting for a tab that may never open; tabs of one site share a renderer. No pages
  // rendered unseen ahead of a click (prerendering), one page kept for Back instead of six, and a frozen
  // tab's renderer gives back what it can at once.
  const string EngineMemoryArguments=" --disable-features=SpareRendererForSitePerProcess,Prerender2,IntensiveWakeUpThrottling --enable-features=BackForwardCache:cache_size/1/foreground_cache_size/1,MemoryPurgeOnFreeze --process-per-site";
  static readonly TimeSpan FreezeAfter=TimeSpan.FromSeconds(15);
  readonly Dictionary<string,DateTime> hiddenSince=new Dictionary<string,DateTime>();

  bool CanFreeze(PageEntry tab,WebView2 view) {
   NavigationProgress progress;
   return !tab.Pinned&&SiteMemoryMode(tab)!="never"&&!RecentlyAudible(tab.Id,view)&&!HasDownloads(view)&&!(navigations.TryGetValue(tab.Id,out progress)&&progress.IsLoading);
  }
  // Called on every tab switch, window state change and the 15-second timer.
  // A window left in the background for a minute (you're in another app) is treated almost like a minimized one.
  static readonly TimeSpan AwayAfter=TimeSpan.FromMinutes(1);
  DateTime? inactiveSince;
  bool Away {get {return window.WindowState==WindowState.Minimized||inactiveSince.HasValue&&DateTime.UtcNow-inactiveSince.Value>=AwayAfter;}}
  void BalanceMemory(bool force) {
   bool minimized=window.WindowState==WindowState.Minimized,away=Away;var now=DateTime.UtcNow;
   foreach(var tab in state.Tabs.ToArray()) {
    WebView2 view;if(!views.TryGetValue(tab.Id,out view))continue;var core=LiveCore(view);if(core==null)continue;
    bool active=tab.Id==state.ActiveId||InSplit(tab.Id);
    try {
     if(active){hiddenSince.Remove(tab.Id);core.MemoryUsageTargetLevel=minimized||away&&!RecentlyAudible(tab.Id,view)?CoreWebView2MemoryUsageTargetLevel.Low:CoreWebView2MemoryUsageTargetLevel.Normal;continue;}
     DateTime since;if(!hiddenSince.TryGetValue(tab.Id,out since)){since=now;hiddenSince[tab.Id]=now;}
     // Music in a hidden tab keeps playing at full strength: a page trimmed to low memory lets YouTube's
     // countdown to the next video stall once the current one ends and the tab goes quiet.
     if(!CanFreeze(tab,view)){if(core.IsSuspended)core.Resume();core.MemoryUsageTargetLevel=CoreWebView2MemoryUsageTargetLevel.Normal;continue;}
     if(!force&&now-since<FreezeAfter)continue;
     core.MemoryUsageTargetLevel=CoreWebView2MemoryUsageTargetLevel.Low;
     if(!core.IsSuspended&&!view.IsVisible){if(core==targetWatcher)ReplaceTargetWatcher(view);FreezeTab(tab.Id,core);}
    }catch(Exception){} // The engine may be restarting; the next tick tries again.
   }
   foreach(var gone in hiddenSince.Keys.Where(id=>!views.ContainsKey(id)).ToList())hiddenSince.Remove(gone);typedInput.RemoveWhere(id=>!views.ContainsKey(id));
   var compress=CompressIdleMemory(false,away);
  }
  // Before a tab freezes, note whether you've typed into it, so the memory budget never drops unsaved text.
  const string TypedInputScript="(()=>{for(const e of document.querySelectorAll('input,textarea')){if(/^(hidden|checkbox|radio|submit|button|reset|image|file|range|color)$/.test(e.type))continue;if(e.value!==e.defaultValue)return true;}const a=document.activeElement;return !!(a&&a.isContentEditable);})()";
  readonly HashSet<string> typedInput=new HashSet<string>();
  async void FreezeTab(string id,CoreWebView2 core) {
   try {if(await core.ExecuteScriptAsync(TypedInputScript)=="true")typedInput.Add(id);else typedInput.Remove(id);}catch(Exception){}
   try {if(id!=state.ActiveId)await core.TrySuspendAsync();}catch(Exception){}
  }

  // Above this much private memory the background tabs you used longest ago rest (they reload, from a
  // snapshot, when opened) until Mica is back under it. A tab you left in the last 45 seconds is spared,
  // so flicking between a few tabs never reloads them.
  // Room to use up to 3 GB when pages need it (a 1080p video, many tabs); only a computer short on memory gets less.
  const long MemoryBudget=3072L<<20,LowMemoryBudget=1024L<<20;
  static readonly TimeSpan BudgetSpare=TimeSpan.FromSeconds(45);
  bool SomethingPlaying() {return state.Tabs.Any(t=>{WebView2 v;CoreWebView2 c;try {return views.TryGetValue(t.Id,out v)&&(c=LiveCore(v))!=null&&c.IsDocumentPlayingAudio;}catch(Exception){return false;}});}
  bool budgeting;internal int budgetRested;
  async Task KeepUnderBudget() {
   if(budgeting||closing||environment==null)return;budgeting=true;int rested=0;
   try {
    long budget=LowOnMemory()?LowMemoryBudget:MemoryBudget;
    // While something plays, nothing is rested unless Windows itself is short on memory: a rest can hitch the video.
    if(!LowOnMemory()&&SomethingPlaying())return;
    for(int round=0;round<4;round++) {
     var ids=EngineProcessIds();long used=await Task.Run(()=>PrivateBytes(ids));if(used<=budget||closing)break;
     var now=DateTime.UtcNow;var tab=state.Tabs.Where(t=>BudgetCanRest(t,now)).OrderBy(t=>t.Pinned).ThenByDescending(t=>RestScore(t,now)).FirstOrDefault();if(tab==null)break;
     DisposeView(tab.Id);rested++;await Task.Delay(2500);
    }
   }catch(Exception){}
   finally {budgeting=false;}
   if(rested>0){budgetRested+=rested;RenderTabs();UpdateMemory();}
  }
  bool BudgetCanRest(PageEntry tab,DateTime now) {
   WebView2 view;DateTime since;NavigationProgress progress;
   if(tab.Id==state.ActiveId||InSplit(tab.Id)||!views.TryGetValue(tab.Id,out view)||typedInput.Contains(tab.Id)||SiteMemoryMode(tab)=="never")return false;
   if(!hiddenSince.TryGetValue(tab.Id,out since)||now-since<BudgetSpare)return false;
   return !RecentlyAudible(tab.Id,view)&&!HasDownloads(view)&&!(navigations.TryGetValue(tab.Id,out progress)&&progress.IsLoading);
  }
  static long PrivateBytes(List<int> engine) {
   long total;using(var own=System.Diagnostics.Process.GetCurrentProcess())total=own.PrivateMemorySize64;
   foreach(int id in engine)try {using(var process=System.Diagnostics.Process.GetProcessById(id))total+=process.PrivateMemorySize64;}catch(ArgumentException){}catch(InvalidOperationException){}
   return total;
  }
  // Pages a process isn't touching go back to Windows, which keeps them compressed in memory; they
  // return on first use, without a reload. Frozen tabs' renderers are trimmed on every balance, the
  // whole engine once Mica is minimized. While the window is away but showing, the engine's helpers and
  // Mica itself are trimmed once; the graphics process and the shown page's renderer keep their memory,
  // so coming back doesn't stutter.
  [System.Runtime.InteropServices.DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr process);
  bool compressing,selfCompressed;internal int lastCompressed=-1;
  async Task CompressIdleMemory(bool everything,bool away=false) {
   if(compressing||closing||environment==null)return;compressing=true;
   try {
    // Main frames of tabs that are awake (shown, playing, loading): their processes stay as they are.
    var awake=new HashSet<uint>();var running=new HashSet<uint>();
    foreach(var tab in state.Tabs) {
     WebView2 view;if(!views.TryGetValue(tab.Id,out view))continue;var core=LiveCore(view);if(core==null)continue;
     try {bool busy=everything?RecentlyAudible(tab.Id,view):!core.IsSuspended;if(busy)awake.Add(core.FrameId);if(!core.IsSuspended)running.Add(core.FrameId);}catch(Exception){}
    }
    var efficient=new Dictionary<int,bool>();
    // While anything plays, the audio and network helpers are left alone so sound never skips.
    bool playing=SomethingPlaying();
    // Trimming makes Windows page memory back in a moment later, which hitches a playing video; it waits until playback stops.
    if(playing&&!everything)return;
    var ids=new List<int>();
    var infos=await environment.GetProcessExtendedInfosAsync();
    // Renderers whose frames can't be read this time are left alone rather than trimmed blind.
    foreach(var renderer in RendererTopFrames(infos)??new List<KeyValuePair<int,List<uint>>>()) {
     bool serving=renderer.Value.Any(awake.Contains),live=renderer.Value.Any(running.Contains);
     if(!serving)ids.Add(renderer.Key);
     efficient[renderer.Key]=!live;
    }
    foreach(var info in infos) {
     var kind=info.ProcessInfo.Kind;if(kind==CoreWebView2ProcessKind.Renderer||playing&&kind==CoreWebView2ProcessKind.Utility)continue;
     if(everything||away&&!selfCompressed&&kind!=CoreWebView2ProcessKind.Gpu)ids.Add(info.ProcessInfo.ProcessId);
    }
    bool self=(everything||away)&&!selfCompressed;if(self)selfCompressed=true;
    lastCompressed=await Task.Run(delegate {
     int trimmed=0;
     foreach(var pair in efficient)SetEfficiency(pair.Key,pair.Value);
     foreach(int id in ids)try {using(var process=System.Diagnostics.Process.GetProcessById(id))if(EmptyWorkingSet(process.Handle))trimmed++;}catch(Exception){}
     if(self){System.Runtime.GCSettings.LargeObjectHeapCompactionMode=System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;GC.Collect();GC.WaitForPendingFinalizers();
      using(var own=System.Diagnostics.Process.GetCurrentProcess())if(EmptyWorkingSet(own.Handle))trimmed++;}
     return trimmed;
    });
   }catch(Exception){}
   finally {compressing=false;}
  }

  // Renderers that only hold frozen tabs run in Windows' Efficiency mode (as in Task Manager): EcoQoS, low CPU
  // priority and low memory priority, so the tab you're on gets the machine. Waking a tab in them undoes it.
  [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct PowerThrottling {public uint Version,ControlMask,StateMask;}
  [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr process,int infoClass,ref PowerThrottling info,int size);
  [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr process,int infoClass,ref uint info,int size);
  internal readonly Dictionary<int,bool> efficiencyMode=new Dictionary<int,bool>();
  // A tab coming forward gets its renderer back at full speed before the page draws.
  async void WakeRenderer(string id) {
   WebView2 view;if(environment==null||!views.TryGetValue(id,out view))return;var core=LiveCore(view);if(core==null)return;
   try {
    uint frame=core.FrameId;
    var renderers=RendererTopFrames(await environment.GetProcessExtendedInfosAsync());if(renderers==null)return;
    foreach(var renderer in renderers)
     if(renderer.Value.Contains(frame)){int pid=renderer.Key;await Task.Run(delegate {SetEfficiency(pid,false);});}
   }catch(Exception){}
  }
  void SetEfficiency(int pid,bool on) {
   bool current;lock(efficiencyMode)if(efficiencyMode.TryGetValue(pid,out current)&&current==on)return;
   try {
    using(var process=System.Diagnostics.Process.GetProcessById(pid)) {
     var throttle=new PowerThrottling {Version=1,ControlMask=1,StateMask=on?1u:0u};SetProcessInformation(process.Handle,4,ref throttle,12);
     uint memory=on?2u:5u;SetProcessInformation(process.Handle,0,ref memory,4);
     process.PriorityClass=on?System.Diagnostics.ProcessPriorityClass.Idle:System.Diagnostics.ProcessPriorityClass.Normal;
    }
    lock(efficiencyMode)efficiencyMode[pid]=on;
   }catch(Exception){}
  }

  [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
  struct MemoryStatus {public uint Length,Load;public ulong TotalPhys,AvailPhys,TotalPage,AvailPage,TotalVirtual,AvailVirtual,AvailExtended;}
  [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
  internal static Func<bool> LowOnMemory=delegate {var s=new MemoryStatus {Length=(uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MemoryStatus))};return GlobalMemoryStatusEx(ref s)&&s.Load>=85;};

  // A picture of each tab as it was left, shown while a rested tab reloads so it feels instant.
  // Kept as the engine's JPEG (~100 KB) and decoded only when shown; decoded, each would hold several MB.
  readonly Dictionary<string,byte[]> snapshots=new Dictionary<string,byte[]>();
  readonly List<string> snapshotOrder=new List<string>();
  System.Windows.Controls.Image snapshotOverlay;internal string lastSnapshotShown;
  async Task SnapshotTab(string id,WebView2 view) {
   var core=LiveCore(view);if(core==null||!Navigation.IsWeb(core.Source)||view.ActualWidth<50)return;
   try {
    var stream=new System.IO.MemoryStream();var capture=core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Jpeg,stream);
    if(await Task.WhenAny(capture,Task.Delay(150))!=capture)return; // Never hold up a tab switch for it.
    await capture;
    snapshots[id]=stream.ToArray();snapshotOrder.Remove(id);snapshotOrder.Add(id);
    while(snapshotOrder.Count>24){snapshots.Remove(snapshotOrder[0]);snapshotOrder.RemoveAt(0);}
   }catch(Exception){}
  }
  void ShowSnapshotWhileLoading(string id,WebView2 view) {
   byte[] jpeg;if(!snapshots.TryGetValue(id,out jpeg))return;
   var image=new System.Windows.Media.Imaging.BitmapImage();
   try {image.BeginInit();image.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;image.StreamSource=new System.IO.MemoryStream(jpeg);image.EndInit();image.Freeze();}catch(Exception){return;}
   HideSnapshot();var host=Find<System.Windows.Controls.Grid>("WebHost");
   snapshotOverlay=new System.Windows.Controls.Image {Source=image,Stretch=System.Windows.Media.Stretch.UniformToFill,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false,Tag=id};
   host.Children.Add(snapshotOverlay);lastSnapshotShown=id;
   EventHandler<CoreWebView2NavigationCompletedEventArgs> done=null;
   done=delegate {view.NavigationCompleted-=done;if(snapshotOverlay!=null&&(string)snapshotOverlay.Tag==id)HideSnapshot();};
   view.NavigationCompleted+=done;
   // A page that never finishes loading still gets uncovered.
   var timer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(8)};timer.Tick+=delegate {timer.Stop();if(snapshotOverlay!=null&&(string)snapshotOverlay.Tag==id)HideSnapshot();};timer.Start();
  }
  void HideSnapshot() {
   if(snapshotOverlay==null)return;var overlay=snapshotOverlay;snapshotOverlay=null;
   var fade=new System.Windows.Media.Animation.DoubleAnimation(0,TimeSpan.FromMilliseconds(160));
   fade.Completed+=delegate {var parent=overlay.Parent as System.Windows.Controls.Panel;if(parent!=null)parent.Children.Remove(overlay);};
   overlay.BeginAnimation(UIElement.OpacityProperty,fade);
  }
 }
}
