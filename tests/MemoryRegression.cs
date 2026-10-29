using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Mica {
 partial class BrowserApp {
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  static string ForegroundOwner() {uint pid;GetWindowThreadProcessId(GetForegroundWindow(),out pid);try {var p=System.Diagnostics.Process.GetProcessById((int)pid);return p.ProcessName+" "+pid+(pid==(uint)System.Diagnostics.Process.GetCurrentProcess().Id?" (this test)":"");}catch(Exception){return pid.ToString();}}
  async Task RunMemoryTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"memory");Directory.CreateDirectory(dir);
   try {
    var ids=new System.Collections.Generic.List<string>();
    for(int i=0;i<3;i++){Navigate(Navigation.Tetris,i>0);await WaitForPage();await Task.Delay(500);ids.Add(state.ActiveId);}
    log.Add("three tabs open: "+MemorySummary());
    // A tab still loading is never frozen, so the check waits for all three to settle first.
    try {await WaitFor(delegate {return ids.All(id=>{NavigationProgress p;return !(navigations.TryGetValue(id,out p)&&p.IsLoading);});},8000);}catch(Exception){}await Task.Delay(AsideHideMs+200);
    BalanceMemory(true);await Task.Delay(2500);
    Func<string,CoreWebView2> core=id=>LiveCore(views[id]);
    log.Add("freeze reasons: "+string.Join(" | ",ids.Select(id=>{var t=state.Tabs.First(x=>x.Id==id);NavigationProgress p;return "can "+CanFreeze(t,views[id])+" audible "+RecentlyAudible(id,views[id])+" mode "+SiteMemoryMode(t)+" loading "+(navigations.TryGetValue(id,out p)&&p.IsLoading)+" visible "+views[id].IsVisible+" pinned "+t.Pinned;})));
    string states=string.Join(", ",ids.Select(id=>core(id).IsSuspended+"/"+core(id).MemoryUsageTargetLevel));log.Add("suspended/level per tab: "+states+" · "+MemorySummary());
    RegressionCheck("background tabs freeze and use less memory",delegate {
     foreach(var id in ids.Take(2))Require(core(id).IsSuspended&&core(id).MemoryUsageTargetLevel==CoreWebView2MemoryUsageTargetLevel.Low,states);
     Require(!core(ids[2]).IsSuspended&&core(ids[2]).MemoryUsageTargetLevel==CoreWebView2MemoryUsageTargetLevel.Normal,"active tab: "+states);
    });
    // The frozen tabs share their site's renderer with the shown one, so that process is left alone.
    var sharedPid=(int)(await environment.GetProcessExtendedInfosAsync()).First(p=>p.ProcessInfo.Kind==CoreWebView2ProcessKind.Renderer&&p.AssociatedFrameInfos.Any(f=>f.FrameId==core(ids[2]).FrameId)).ProcessInfo.ProcessId;
    Func<int,long> resident=delegate(int pid) {using(var p=System.Diagnostics.Process.GetProcessById(pid)){p.Refresh();return p.WorkingSet64;}};
    long sharedBefore=resident(sharedPid);await CompressIdleMemory(false);long sharedAfter=resident(sharedPid);
    RegressionCheck("a renderer that also shows the current tab isn't trimmed",delegate {Require(sharedAfter>sharedBefore/2,"renderer went from "+sharedBefore/1048576+" to "+sharedAfter/1048576+" MB");});
    string beforeAll=MemorySummary();await CompressIdleMemory(true);string afterAll=MemorySummary();log.Add("compress everything: "+beforeAll+" -> "+afterAll+", "+lastCompressed+" processes");
    RegressionCheck("compressing a minimized window trims the engine",delegate {Require(lastCompressed>=3&&double.Parse(afterAll.Split(' ')[0])<double.Parse(beforeAll.Split(' ')[0])*0.7,beforeAll+" -> "+afterAll+", "+lastCompressed);});
    await Task.Delay(600);
    // A window left behind another app for a minute: the shown tab goes to low memory, helpers are trimmed once.
    selfCompressed=false;inactiveSince=DateTime.UtcNow.AddMinutes(-2);BalanceMemory(false);await Task.Delay(1500);
    var awayLevel=core(ids[2]).MemoryUsageTargetLevel;int awayTrimmed=lastCompressed;
    RegressionCheck("a window in the background lowers the shown tab and trims helpers",delegate {Require(awayLevel==CoreWebView2MemoryUsageTargetLevel.Low&&awayTrimmed>=2,awayLevel+", trimmed "+awayTrimmed);});
    inactiveSince=null;selfCompressed=false;BalanceMemory(false);await Task.Delay(300);
    RegressionCheck("coming back restores the shown tab",delegate {Require(core(ids[2]).MemoryUsageTargetLevel==CoreWebView2MemoryUsageTargetLevel.Normal,core(ids[2]).MemoryUsageTargetLevel.ToString());});
    // Another site gets its own renderer; once its tab is frozen, that process runs in Efficiency mode.
    Navigate("https://example.com/",true);await WaitFor(delegate {return CurrentView()!=null&&CurrentView().CoreWebView2!=null&&CurrentView().CoreWebView2.DocumentTitle=="Example Domain";},20000);
    string other=state.ActiveId;uint otherFrame=core(other).FrameId;await Activate(ids[2]);await Task.Delay(AsideHideMs+300);BalanceMemory(true);await Task.Delay(2500);await CompressIdleMemory(false);
    var infos=await environment.GetProcessExtendedInfosAsync();var otherInfo=infos.FirstOrDefault(p=>p.ProcessInfo.Kind==CoreWebView2ProcessKind.Renderer&&p.AssociatedFrameInfos.Any(f=>f.FrameId==otherFrame));
    int rendererPid=otherInfo==null?0:otherInfo.ProcessInfo.ProcessId;bool on;
    Func<string> priority=delegate {try {using(var p=System.Diagnostics.Process.GetProcessById(rendererPid))return p.PriorityClass.ToString();}catch(Exception e){return e.GetType().Name;}};
    string frozenPriority=priority();
    RegressionCheck("a renderer holding only frozen tabs runs in Efficiency mode",delegate {Require(rendererPid!=0&&core(other).IsSuspended,"rendererPid "+rendererPid+", frozen "+core(other).IsSuspended);Require(efficiencyMode.TryGetValue(rendererPid,out on)&&on&&frozenPriority=="Idle","mode "+(efficiencyMode.ContainsKey(rendererPid)?efficiencyMode[rendererPid].ToString():"none")+", priority "+frozenPriority);});
    await Activate(other);await Task.Delay(500);string wokenPriority=priority();
    RegressionCheck("opening its tab puts the renderer back at full speed",delegate {Require(efficiencyMode.TryGetValue(rendererPid,out on)&&!on&&wokenPriority=="Normal","priority "+wokenPriority);});
    await Activate(ids[2]);await Task.Delay(300);
    await Activate(ids[0]);await Task.Delay(800);
    string alive=await core(ids[0]).ExecuteScriptAsync("document.title");
    RegressionCheck("a frozen tab comes back instantly, without reloading",delegate {Require(!core(ids[0]).IsSuspended&&core(ids[0]).MemoryUsageTargetLevel==CoreWebView2MemoryUsageTargetLevel.Normal&&alive=="\"Tetris\"",core(ids[0]).IsSuspended+" "+alive);});
    audibleAt[ids[1]]=DateTime.UtcNow;await core(ids[1]).ExecuteScriptAsync("1").ContinueWith(t=>{});BalanceMemory(true);await Task.Delay(500);
    RegressionCheck("a hidden tab that plays audio stays fully awake, so the next video can start",delegate {Require(!core(ids[1]).IsSuspended&&core(ids[1]).MemoryUsageTargetLevel==CoreWebView2MemoryUsageTargetLevel.Normal,core(ids[1]).IsSuspended+"/"+core(ids[1]).MemoryUsageTargetLevel);});
    // Resting: a snapshot covers the reload, then goes away.
    await Activate(ids[2]);await Task.Delay(400);await Activate(ids[0]);await Task.Delay(400);
    RegressionCheck("leaving a tab keeps a snapshot of it",delegate {Require(snapshots.ContainsKey(ids[2]),"no snapshot");});
    DisposeView(ids[2]);lastSnapshotShown=null;var started=DateTime.UtcNow;var reopen=Activate(ids[2]);
    lastSnapshotShown=null;await Task.Delay(1);
    await reopen;bool covered=lastSnapshotShown==ids[2];await WaitForPage();double ms=(DateTime.UtcNow-started).TotalMilliseconds;await Task.Delay(400);
    log.Add(string.Format("rested tab reopened in {0:0} ms (snapshot shown at once: {1})",ms,covered));
    RegressionCheck("a rested tab shows its snapshot right away, then the live page",delegate {Require(covered,"no snapshot while loading");Require(snapshotOverlay==null,"snapshot stayed");});
    // The island in the corner.
    UpdateMemory();await Task.Delay(800);Status("Finding your way…");await Task.Delay(300);
    RegressionCheck("routine status doesn't open the island",delegate {Require(island!=null&&island.IsOpen&&islandShowing==null,"showing "+islandShowing);});
    Status("1 background tab(s) put to rest. They reload when reopened.");await Task.Delay(500);
    RegressionCheck("a notice widens the island, which keeps showing memory",delegate {Require(islandShowing!=null&&islandMemory.Text.Contains("MB"),islandShowing+" / "+islandMemory.Text);Require(((System.Windows.Controls.Grid)Find<System.Windows.Controls.Border>("ContentFrame").Child).RowDefinitions[2].Height.Value==0,"status bar still takes space");});
    {var card=islandCard;card.UpdateLayout();var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth+24),(int)Math.Ceiling(card.ActualHeight+30),96,96,System.Windows.Media.PixelFormats.Pbgra32);image.Render(card);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(dir,"island.png")))png.Save(file);}
    CollapseIsland();await Task.Delay(400);
    RegressionCheck("the island settles back to memory only",delegate {Require(islandShowing==null&&islandMessage.MaxWidth<1,islandMessage.MaxWidth.ToString());});
    RegressionCheck("tabs rest after 15 minutes, 5 when memory is low",delegate {Require(TabLifecycle.RestAfter(false)==TimeSpan.FromMinutes(15)&&TabLifecycle.RestAfter(true)==TimeSpan.FromMinutes(5),"timing");});
    // Where Mica's own memory goes: the .NET heap, the rest of the process (WPF, drawing, the engine's host side).
    {
     long heapBefore=GC.GetTotalMemory(false),heap=GC.GetTotalMemory(true);
     using(var self=System.Diagnostics.Process.GetCurrentProcess()) {
      self.Refresh();
      log.Add(string.Format("mica process: private {0} MB, working set {1} MB, .NET heap {2:0.0} MB (before collecting {3:0.0} MB), gen0/1/2 collections {4}/{5}/{6}, threads {7}, handles {8}",self.PrivateMemorySize64>>20,self.WorkingSet64>>20,heap/1048576.0,heapBefore/1048576.0,GC.CollectionCount(0),GC.CollectionCount(1),GC.CollectionCount(2),self.Threads.Count,self.HandleCount));
     }
     log.Add("snapshots "+snapshots.Count+" ("+(snapshots.Values.Sum(b=>(long)b.Length)>>10)+" KB), history "+state.History.Count+", tabs "+state.Tabs.Count+", views "+views.Count);
    }
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
