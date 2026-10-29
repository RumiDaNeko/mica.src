using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace Mica {
 partial class BrowserApp {
  // Frame times during Mica's own animations, with busy pages open: once as shipped, once with the
  // network watching (the ads-and-trackers count) turned off, to see what it costs.
  // Step marks inside Activate, collected only while the probe runs.
  static Stopwatch markClock;static List<string> marks;
  [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetWindowRect")] static extern bool ProbeWindowRect(IntPtr hwnd,out ProbeRect rect);
  struct ProbeRect {public int Left,Top,Right,Bottom;}
  int PageWindowLeft() {var view=CurrentView();ProbeRect r;return view!=null&&ProbeWindowRect(((System.Windows.Interop.HwndHost)view).Handle,out r)?r.Left:int.MinValue;}
  static void Mark(string step){if(marks==null)return;marks.Add(step+" "+markClock.Elapsed.TotalMilliseconds.ToString("0.0"));markClock.Restart();}
  async Task RunPerfProbe() {
   var log=regressionLog;string dir=Path.Combine(profile,"perf");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Width=1920;window.Height=1032;window.Left=-6000;window.Top=-6000;
   // Like a real profile: dark, the Ember background, 28 tabs (the extra ones resting).
   asyncSaveForTest=true;state.Dark=true;state.HomeBackground="Ember";state.HomeGlass=100;ApplyTheme();ApplyPersonalization();
   for(int i=0;i<22;i++){var extra=state.AddTab("https://example.org/resting-"+i);extra.Title="Resting page "+i;extra.LastUsed=DateTime.UtcNow.AddHours(-1);}RenderTabs();
   try {
    var sites=new[]{"https://www.youtube.com/","https://www.cnn.com/","https://www.reddit.com/","https://en.wikipedia.org/wiki/Web_browser","https://www.theverge.com/","https://www.bbc.com/news"};
    var tabs=new List<PageEntry>();
    foreach(var url in sites){Navigate(url,true);await Task.Delay(300);tabs.Add(Active);await WaitForPage(30000).ContinueWith(t=>{});await Task.Delay(1500);}
    await Task.Delay(4000);
    // What a tab switch and Spotlight do on the UI thread, step by step.
    Func<string,int,Action,string> time=delegate(string name,int n,Action run) {var w=Stopwatch.StartNew();for(int i=0;i<n;i++)run();return name+" "+(w.Elapsed.TotalMilliseconds/n).ToString("0.0")+" ms";};
    log.Add("steps: "+string.Join(", ",new[]{
     time("RenderTabs",10,RenderTabs),time("Save",10,Save),time("UpdateNavigation",10,UpdateNavigation),time("BalanceMemory",10,delegate {BalanceMemory(false);}),
     time("MarkRestingTabs",10,delegate {MarkRestingTabs(false);}),time("RefreshPersonalization",5,RefreshPersonalization),
     time("RefreshSearch",10,delegate {RefreshSearch();}),time("RefreshTabRow",10,delegate {RefreshTabRow(state.ActiveId);}),time("window.Title",10,delegate {window.Title=window.Title+" ";}),time("Spotlight.Search",10,delegate {Spotlight.Search(state,"");})}));
    {var w=Stopwatch.StartNew();var shot=SnapshotTab(state.ActiveId,CurrentView());long sync=w.ElapsedMilliseconds;await shot;log.Add("SnapshotTab: "+sync+" ms on the UI thread, "+w.ElapsedMilliseconds+" ms in all");}
    File.WriteAllLines(Path.Combine(dir,"result.txt"),log);
    for(int k=0;k<3;k++){marks=new List<string>();markClock=Stopwatch.StartNew();var w=Stopwatch.StartNew();OpenSpotlight(false);long open=w.ElapsedMilliseconds;log.Add("OpenSpotlight "+open+" ms; "+string.Join(", ",marks));marks=null;await Task.Delay(400);log.Add("page window left while open: "+PageWindowLeft()+", visible "+(CurrentView()!=null&&CurrentView().IsVisible));w.Restart();CloseSpotlight();log.Add("CloseSpotlight "+w.ElapsedMilliseconds+" ms");await Task.Delay(400);log.Add("page window left after: "+PageWindowLeft());}
    for(int k=1;k<4;k++){marks=new List<string>();markClock=Stopwatch.StartNew();var w=Stopwatch.StartNew();var switching=Activate(tabs[k].Id);long sync=w.ElapsedMilliseconds;await switching;log.Add("Activate: "+sync+" ms sync, "+w.ElapsedMilliseconds+" ms in all; "+string.Join(", ",marks));marks=null;await Task.Delay(600);}
    // What each page-window operation costs: on the UI thread, then the two frames after it.
    {
     Func<Task<double>> nextFrames=async delegate {var w=Stopwatch.StartNew();var done=new TaskCompletionSource<bool>();int n=0;EventHandler h=null;h=delegate {if(++n>=2){CompositionTarget.Rendering-=h;done.TrySetResult(true);}};CompositionTarget.Rendering+=h;await done.Task;return w.Elapsed.TotalMilliseconds;};
     Func<string,Action,Task> op=async delegate(string name,Action run) {await Task.Delay(500);var w=Stopwatch.StartNew();run();double sync=w.Elapsed.TotalMilliseconds;double frames=await nextFrames();log.Add("op "+name+": "+sync.ToString("0.0")+" ms sync, next 2 frames "+frames.ToString("0")+" ms");};
     var a=CurrentView();var host=Find<System.Windows.Controls.Grid>("WebHost");
     Microsoft.Web.WebView2.Wpf.WebView2 b=views.Where(p=>p.Value!=a&&LiveCore(p.Value)!=null).Select(p=>p.Value).FirstOrDefault();
     for(int round=0;round<2;round++) {
      await op("hide",delegate {a.Visibility=Visibility.Collapsed;});await op("show",delegate {a.Visibility=Visibility.Visible;});
      if(b!=null){await op("swap",delegate {b.Visibility=Visibility.Visible;a.Visibility=Visibility.Collapsed;});await op("swap back",delegate {a.Visibility=Visibility.Visible;b.Visibility=Visibility.Collapsed;});}
      await op("narrow",delegate {host.HorizontalAlignment=HorizontalAlignment.Right;host.Width=host.ActualWidth-174;});await op("widen",delegate {host.Width=double.NaN;host.HorizontalAlignment=HorizontalAlignment.Stretch;});
      await op("park",delegate {ParkWebHost(true);});await op("unpark",delegate {ParkWebHost(false);});
      await op("focus page",delegate {a.Focus();});await op("focus app",delegate {Find<System.Windows.Controls.Button>("MenuButton").Focus();});
     }
     File.WriteAllLines(Path.Combine(dir,"result.txt"),log);
    }
    // Layout cost of one sidebar-animation step, measured directly (less noisy than frame times on a busy PC):
    // the width is stepped by hand and each layout pass timed, with parts taken out one at a time.
    {
     var grid=Find<System.Windows.Controls.Grid>("AppLayout");var page=CurrentView();var toolbar=Find<FrameworkElement>("ToolbarBar");var tabList=Find<FrameworkElement>("ExpandedTabScroller");var rail=Find<FrameworkElement>("CompactRail");
     Func<string,double> step=delegate(string name) {
      window.UpdateLayout();var times=new List<double>();
      for(int k=0;k<40;k++){double width=238-(174.0*(k%20)/19);grid.SetValue(MotionProperties.SidebarWidthProperty,width);var w=Stopwatch.StartNew();window.UpdateLayout();times.Add(w.Elapsed.TotalMilliseconds);}
      grid.SetValue(MotionProperties.SidebarWidthProperty,238.0);window.UpdateLayout();times.Sort();double median=times[times.Count/2];
      log.Add("layout step ("+name+"): median "+median.ToString("0.00")+" ms, worst "+times.Last().ToString("0.00")+" ms");return median;
     };
     step("as is");
     {
      // As the real slide sets things up: sidebar panels at fixed widths, the page pinned at the right.
      var sidebarGrid=Find<System.Windows.Controls.Grid>("Sidebar");var host=Find<System.Windows.Controls.Grid>("WebHost");
      foreach(var child in sidebarGrid.Children.OfType<FrameworkElement>())if(child!=Find<FrameworkElement>("BrandBar")){child.Width=child==rail?64:238-9-15;child.HorizontalAlignment=HorizontalAlignment.Left;}
      host.HorizontalAlignment=HorizontalAlignment.Right;host.Width=host.ActualWidth;
      step("as animated, before");
      sidebarGrid.Width=238-sidebarGrid.Margin.Left-sidebarGrid.Margin.Right;sidebarGrid.HorizontalAlignment=HorizontalAlignment.Left;toolbar.Width=toolbar.ActualWidth;toolbar.HorizontalAlignment=HorizontalAlignment.Right;
      step("as animated");
      sidebarGrid.Width=double.NaN;sidebarGrid.ClearValue(FrameworkElement.HorizontalAlignmentProperty);toolbar.Width=double.NaN;toolbar.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
      tabList.Visibility=Visibility.Collapsed;step("as animated, no tab list");tabList.Visibility=Visibility.Visible;
      toolbar.Visibility=Visibility.Collapsed;step("as animated, no toolbar");toolbar.Visibility=Visibility.Visible;
      foreach(var child in sidebarGrid.Children.OfType<FrameworkElement>()){child.Width=double.NaN;child.ClearValue(FrameworkElement.HorizontalAlignmentProperty);}
      host.Width=double.NaN;host.HorizontalAlignment=HorizontalAlignment.Stretch;
     }
     if(page!=null){page.Visibility=Visibility.Collapsed;step("no page");page.Visibility=Visibility.Visible;}
     // Every part of the window, taken out (collapsed) in turn; each one's own name, or its type and place.
     var parts=new List<FrameworkElement>();
     foreach(var root in new[]{Find<System.Windows.Controls.Grid>("Root"),(System.Windows.Controls.Panel)((System.Windows.Controls.Border)Find<FrameworkElement>("ContentFrame")).Child,Find<System.Windows.Controls.Grid>("Sidebar")})
      if(root!=null)foreach(var child in root.Children.OfType<FrameworkElement>())if(child.Visibility==Visibility.Visible&&child!=grid)parts.Add(child);
     foreach(var part in parts) {
      string name=string.IsNullOrEmpty(part.Name)?part.GetType().Name+"#"+System.Windows.Controls.Panel.GetZIndex(part)+"@"+(part.Parent as FrameworkElement).Name:part.Name;
      part.Visibility=Visibility.Collapsed;step("no "+name);part.Visibility=Visibility.Visible;
     }
     File.WriteAllLines(Path.Combine(dir,"result.txt"),log);
    }
    for(int round=0;round<1;round++) {
     string label=round==0?"as shipped":"network watching off";
     foreach(var scenario in new[]{"sidebar","sidebar-no-page","sidebar-no-toolbar","sidebar-no-tabs","menu","tabs","spotlight","titles","idle"}) {
      // Experiments: the same sidebar animation with one part taken out, to see what each costs.
      var page=CurrentView();var toolbar=Find<FrameworkElement>("ToolbarBar");var tabList=Find<FrameworkElement>("ExpandedTabScroller");
      if(scenario=="sidebar-no-page"&&page!=null)page.Visibility=Visibility.Collapsed;
      if(scenario=="sidebar-no-toolbar")toolbar.Visibility=Visibility.Hidden;
      if(scenario=="sidebar-no-tabs")tabList.Visibility=Visibility.Hidden;
      var gaps=new List<double>();TimeSpan last=TimeSpan.Zero;
      EventHandler tick=delegate(object sender,EventArgs e) {var t=((RenderingEventArgs)e).RenderingTime;if(last!=TimeSpan.Zero&&t!=last)gaps.Add((t-last).TotalMilliseconds);last=t;};
      // A little animation keeps frames coming even when nothing else moves, so stalls show up as gaps.
      var spinner=new RotateTransform();var probe=new System.Windows.Shapes.Rectangle {Width=2,Height=2,Fill=Brushes.Transparent,RenderTransform=spinner,IsHitTestVisible=false};Find<System.Windows.Controls.Grid>("Root").Children.Add(probe);
      spinner.BeginAnimation(RotateTransform.AngleProperty,new System.Windows.Media.Animation.DoubleAnimation(0,360,TimeSpan.FromSeconds(1)) {RepeatBehavior=System.Windows.Media.Animation.RepeatBehavior.Forever});
      // What ran long on the UI thread meanwhile: each dispatcher job's method, total and worst time.
      var slow=new Dictionary<string,Tuple<int,double,double>>();var running=new Dictionary<System.Windows.Threading.DispatcherOperation,Stopwatch>();
      var methodField=typeof(System.Windows.Threading.DispatcherOperation).GetField("_method",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
      System.Windows.Threading.DispatcherHookEventHandler started=delegate(object s,System.Windows.Threading.DispatcherHookEventArgs h) {if(h.Operation!=null)running[h.Operation]=Stopwatch.StartNew();};
      System.Windows.Threading.DispatcherHookEventHandler finished=delegate(object s,System.Windows.Threading.DispatcherHookEventArgs h) {
       Stopwatch w;if(h.Operation==null||!running.TryGetValue(h.Operation,out w))return;running.Remove(h.Operation);double ms=w.Elapsed.TotalMilliseconds;if(ms<4)return;
       var method=methodField==null?null:methodField.GetValue(h.Operation) as Delegate;string name=h.Operation.Priority+" "+(method==null?"?":method.Method.DeclaringType.Name+"."+method.Method.Name);
       Tuple<int,double,double> seen;slow.TryGetValue(name,out seen);slow[name]=Tuple.Create((seen==null?0:seen.Item1)+1,(seen==null?0:seen.Item2)+ms,Math.Max(seen==null?0:seen.Item3,ms));
      };
      window.Dispatcher.Hooks.OperationStarted+=started;window.Dispatcher.Hooks.OperationCompleted+=finished;
      CompositionTarget.Rendering+=tick;var clock=Stopwatch.StartNew();
      for(int i=0;i<3;i++) {
       if(scenario.StartsWith("sidebar")){ToggleSidebar();await Task.Delay(450);ToggleSidebar();await Task.Delay(450);}
       else if(scenario=="menu"){ShowMenu();await Task.Delay(350);CloseMenus();await Task.Delay(250);}
       else if(scenario=="tabs"){SelectTab(tabs[(i*2+1)%tabs.Count].Id);await Task.Delay(450);SelectTab(tabs[(i*2+2)%tabs.Count].Id);await Task.Delay(450);}
       else if(scenario.StartsWith("spotlight")){OpenSpotlight(false);await Task.Delay(350);CloseSpotlight();await Task.Delay(350);}
       else if(scenario=="titles"){var core=LiveCore(CurrentView());if(core!=null)await core.ExecuteScriptAsync("clearInterval(window.__t);let n=0;window.__t=setInterval(()=>document.title='('+(++n)+') busy page',150)");ToggleSidebar();await Task.Delay(450);ToggleSidebar();await Task.Delay(450);if(core!=null)await core.ExecuteScriptAsync("clearInterval(window.__t)");}
       else await Task.Delay(900);
      }
      window.Dispatcher.Hooks.OperationStarted-=started;window.Dispatcher.Hooks.OperationCompleted-=finished;
      log.Add("  slow UI work in "+scenario+": "+string.Join("; ",slow.OrderByDescending(p=>p.Value.Item2).Take(6).Select(p=>p.Key+" ×"+p.Value.Item1+" "+p.Value.Item2.ToString("0")+" ms (worst "+p.Value.Item3.ToString("0")+")")));
      if(page!=null)page.Visibility=Visibility.Visible;toolbar.Visibility=Visibility.Visible;tabList.Visibility=Visibility.Visible;
      CompositionTarget.Rendering-=tick;spinner.BeginAnimation(RotateTransform.AngleProperty,null);Find<System.Windows.Controls.Grid>("Root").Children.Remove(probe);
      gaps.Sort();double p95=gaps.Count==0?0:gaps[(int)(gaps.Count*0.95)],max=gaps.Count==0?0:gaps.Last();int janky=gaps.Count(g=>g>34);
      File.WriteAllLines(Path.Combine(dir,"result.txt"),log);log.Add(label+" · "+scenario+": "+gaps.Count+" frames in "+clock.ElapsedMilliseconds+" ms, p95 "+p95.ToString("0")+" ms, worst "+max.ToString("0")+" ms, "+janky+" over 34 ms");
     }
     if(round==0)foreach(var view in views.Values){var core=LiveCore(view);if(core!=null)try {await core.CallDevToolsProtocolMethodAsync("Network.disable","{}");}catch(Exception){}}
    }
   }catch(Exception e){log.Add("ERROR "+e);}
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
