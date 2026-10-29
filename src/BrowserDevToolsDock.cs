using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // The engine's real DevTools, docked to the right of the page. Docking needs the engine's debugging
 // port, which any program on this PC could connect to, so it's opt-in: by default F12 opens the
 // engine's own DevTools window, which needs no port. When docking is on, the port is a fresh random
 // one each launch, and only the DevTools front end's own origin may open its socket from a page.
 partial class BrowserApp {
  static readonly Dictionary<string,int> devToolsPorts=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
  // Every window of a profile shares one engine, which must start with the same arguments, so the
  // choice is fixed per profile at the first window's start. 0 means no port.
  static int DevToolsPort(string userData,bool docked) {
   lock(devToolsPorts) {
    int port;if(devToolsPorts.TryGetValue(userData,out port))return port;
    try {File.Delete(Path.Combine(userData,"mica-devtools-port"));}catch(Exception){} // Older builds kept the port across launches.
    port=0;if(docked){var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();}
    devToolsPorts[userData]=port;return port;
   }
  }
  // Docked DevTools talks to the engine over a local port, and anything served from that port controls the
  // whole browser. Only the DevTools panel itself may load it: every page view (frames, fetches and all) is refused.
  void KeepPagesOffDevTools(CoreWebView2 core) {
   int port=devToolsPort;if(port==0)return;
   foreach(var host in new[]{"127.0.0.1","localhost","[::1]"})core.AddWebResourceRequestedFilter("http://"+host+":"+port+"/*",CoreWebView2WebResourceContext.All);
   core.WebResourceRequested+=delegate(object sender,CoreWebView2WebResourceRequestedEventArgs e) {
    Uri uri;if(!Uri.TryCreate(e.Request.Uri,UriKind.Absolute,out uri)||uri.Port!=port||!uri.IsLoopback)return;
    try {e.Response=core.Environment.CreateWebResourceResponse(null,403,"Forbidden","");}catch(Exception){}
   };
   core.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e) {Uri uri;if(Uri.TryCreate(e.Uri,UriKind.Absolute,out uri)&&uri.IsLoopback&&uri.Port==port)e.Cancel=true;};
   core.FrameNavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e) {Uri uri;if(Uri.TryCreate(e.Uri,UriKind.Absolute,out uri)&&uri.IsLoopback&&uri.Port==port)e.Cancel=true;};
  }
  static string DevToolsArguments(int port) {return port==0?"":"--remote-debugging-address=127.0.0.1 --remote-debugging-port="+port+" --remote-allow-origins=http://127.0.0.1:"+port;}
  bool DockedDevToolsWanted {get {return state.DockedDevTools||smoke&&Environment.GetCommandLineArgs().Contains("--devtools-test");}}
  TextBlock dockHint;
  void ToggleDockedDevTools() {
   state.DockedDevTools=!state.DockedDevTools;Save();if(dockHint!=null)dockHint.Text=state.DockedDevTools?"On":"Off";
   bool now=devToolsPort!=0;
   if(state.DockedDevTools==now){Status(now?"DevTools docks inside Mica.":"F12 opens DevTools in its own window.");return;}
   SwitchEngineLive();
  }
  // The port is fixed when the engine starts, so switching restarts just the engine: every window of this
  // profile lets go of its pages, the old engine exits, and the open tabs load again in a fresh one.
  // Mica itself, its windows and the tab list stay as they are.
  bool switchingEngine;
  internal async void SwitchEngineLive() {
   var windows=Program.Browsers.Where(b=>!b.closing&&!b.privateMode&&string.Equals(b.EngineFolder,EngineFolder,StringComparison.OrdinalIgnoreCase)).ToList();
   if(windows.Any(b=>b.switchingEngine))return;
   if(windows.Any(b=>b.liveDownloads.Count>0||b.pendingDownloadPrompts.Count>0)){state.DockedDevTools=!state.DockedDevTools;Save();if(dockHint!=null)dockHint.Text=state.DockedDevTools?"On":"Off";Status("Finish or cancel your downloads first; switching reloads the engine.");return;}
   foreach(var b in windows)b.switchingEngine=true;
   try {
    var engines=windows.SelectMany(b=>b.EngineProcessIds()).Distinct().ToList();
    targetWatcher=null;ownTargets.Clear();
    foreach(var b in windows)b.ReleaseEngine();
    lock(devToolsPorts)devToolsPorts.Remove(EngineFolder);
    // A new engine can't share the profile with the old one while it still runs under other arguments.
    await Task.Run(delegate {
     var deadline=DateTime.UtcNow.AddSeconds(10);
     foreach(int id in engines)try {using(var engine=System.Diagnostics.Process.GetProcessById(id)){var left=deadline-DateTime.UtcNow;if(left>TimeSpan.Zero)engine.WaitForExit((int)left.TotalMilliseconds);}}catch(Exception){}
    });
    foreach(var b in windows) {
     if(b.closing||b.state.ActiveId==null)continue;
     b.state.DockedDevTools=state.DockedDevTools;if(b.dockHint!=null)b.dockHint.Text=state.DockedDevTools?"On":"Off";
     await b.Activate(b.state.ActiveId);
    }
    Status(state.DockedDevTools?"DevTools now docks inside Mica. Press F12. A local debugging port is open while this is on.":"F12 opens DevTools in its own window; the debugging port is closed.");
   }catch(Exception e){Status("Couldn't switch the engine: "+e.Message);}
   finally {foreach(var b in windows)b.switchingEngine=false;}
  }
  // Lets go of everything that runs in the engine: tabs, DevTools, pop-ups, extension pages.
  void ReleaseEngine() {
   if(devToolsOpen){devToolsOpen=false;if(devToolsDetached)CloseDevToolsWindow();LayoutDevTools();}
   DisposeDevTools();devToolsTarget=null;
   ClosePopups();CloseExtensionAction();CloseBlockerControl();
   foreach(var tab in views.Keys.ToList())DisposeView(tab);
   if(extensionView!=null){var view=extensionView;extensionView=null;Find<Grid>("Root").Children.Remove(view);try {view.Dispose();}catch(Exception){}}
   extensionProfileTask=null;environment=null;environmentTask=null;
  }
  int devToolsPort;
  bool devToolsOpen;
  WebView2 devToolsView;
  string devToolsTarget;
  double devToolsWidth=460;
  int devToolsAttach;
  // Detached: the same inspector in its own Mica window, beside the browser.
  bool devToolsDetached;Window devToolsWindow;Grid devToolsWindowHost;TextBlock devToolsWindowTarget,devToolsWindowEmpty;
  Grid DevToolsHostPanel {get {return devToolsDetached&&devToolsWindowHost!=null?devToolsWindowHost:Find<Grid>("DevToolsHost");}}
  TextBlock DevToolsEmptyText {get {return devToolsDetached&&devToolsWindowEmpty!=null?devToolsWindowEmpty:Find<TextBlock>("DevToolsEmpty");}}
  TextBlock DevToolsTargetText {get {return devToolsDetached&&devToolsWindowTarget!=null?devToolsWindowTarget:Find<TextBlock>("DevToolsTarget");}}
  void InitializeDevToolsDock() {
   InitializeSiteCard();
   foreach(var host in new FrameworkElement[]{Find<Grid>("WebHost"),Find<Border>("ContentFrame"),Find<Grid>("DevToolsHost")})host.SizeChanged+=delegate {QueuePageCorners();};
   Find<Button>("DevToolsClose").Click+=delegate {ToggleDevTools();};
   Find<Button>("DevToolsDetach").Click+=delegate {SetDevToolsDetached(true);};
   window.Closed+=delegate {if(devToolsWindow!=null)devToolsWindow.Close();};
   Find<Button>("DevToolsReload").Click+=delegate {devToolsTarget=null;AttachDevTools();};
   Find<Grid>("WebHost").IsVisibleChanged+=delegate {if(devToolsOpen)AttachDevTools();};
   Find<GridSplitter>("DevToolsSplitter").DragCompleted+=delegate {var columns=Find<Grid>("PageSurface").ColumnDefinitions;if(columns[2].ActualWidth>0)devToolsWidth=columns[2].ActualWidth;};
  }
  void ToggleDevTools() {
   if(devToolsPort==0&&!devToolsOpen) {
    var page=Find<Grid>("WebHost").IsVisible?CurrentView():null;
    if(page!=null&&page.CoreWebView2!=null)page.CoreWebView2.OpenDevToolsWindow();else Status("Open a web page to inspect it.");
    return;
   }
   devToolsOpen=!devToolsOpen;LayoutDevTools(true);
   if(devToolsOpen){if(devToolsDetached)OpenDevToolsWindow();AttachDevTools();Status(devToolsDetached?"DevTools opened in its own window. Press F12 to close.":"DevTools docked. Press F12 to close.");}
   else {
    devToolsTarget=null;
    if(devToolsDetached){CloseDevToolsWindow();}
    else if(devToolsView!=null&&devToolsView.CoreWebView2!=null)devToolsView.CoreWebView2.Navigate("about:blank");
    var page=CurrentView();if(page!=null){window.Activate();page.Focus();}
   }
  }
  // Moving between dock and window starts a fresh inspector in the new place; it reconnects to the same page.
  void SetDevToolsDetached(bool detached) {
   if(detached==devToolsDetached)return;
   DisposeDevTools();devToolsTarget=null;devToolsDetached=detached;
   if(detached){LayoutDevTools(true);if(devToolsOpen)OpenDevToolsWindow();}
   else {CloseDevToolsWindow();window.Activate();LayoutDevTools(true);}
   if(devToolsOpen)AttachDevTools();
  }
  void OpenDevToolsWindow() {
   if(devToolsWindow!=null){devToolsWindow.Activate();return;}
   var w=new Window {Title="DevTools — Mica",Width=720,Height=Math.Min(800,SystemParameters.WorkArea.Height-40),WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=true,FontFamily=window.FontFamily,FontSize=12,Resources=window.Resources,WindowStartupLocation=WindowStartupLocation.Manual,Icon=window.Icon};
   w.SetResourceReference(Window.BackgroundProperty,"Canvas");w.SetResourceReference(Window.ForegroundProperty,"Ink");
   System.Windows.Shell.WindowChrome.SetWindowChrome(w,new System.Windows.Shell.WindowChrome {CaptionHeight=40,ResizeBorderThickness=new Thickness(5),CornerRadius=new CornerRadius(10),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});
   // Beside Mica when there's room, otherwise over its right side.
   var area=SystemParameters.WorkArea;double right=window.Left+window.ActualWidth;
   if(FullscreenBounds.KeepOffscreen){w.Left=window.Left;w.Top=window.Top;}
   else if(right+w.Width<=area.Right){w.Left=right;w.Top=Math.Max(area.Top,window.Top);}
   else {w.Left=Math.Max(area.Left,Math.Min(right-w.Width-24,area.Right-w.Width));w.Top=Math.Max(area.Top,Math.Min(window.Top+40,area.Bottom-w.Height));}
   var frame=new Border {BorderThickness=new Thickness(1)};frame.SetResourceReference(Border.BorderBrushProperty,"Line");w.Content=frame;
   var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition {Height=new GridLength(40)});layout.RowDefinitions.Add(new RowDefinition());frame.Child=layout;
   var bar=new Border {BorderThickness=new Thickness(0,0,0,1)};bar.SetResourceReference(Border.BorderBrushProperty,"Line");bar.SetResourceReference(Border.BackgroundProperty,"Canvas");layout.Children.Add(bar);
   var barGrid=new Grid {Margin=new Thickness(14,0,6,0)};bar.Child=barGrid;
   var label=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};barGrid.Children.Add(label);
   var icon=DialogText("\uE943",12,"Accent");icon.FontFamily=new System.Windows.Media.FontFamily("Segoe MDL2 Assets");icon.Margin=new Thickness(0,0,8,0);icon.VerticalAlignment=VerticalAlignment.Center;label.Children.Add(icon);
   var name=DialogText("DevTools",12,"Ink");name.FontWeight=FontWeights.SemiBold;name.VerticalAlignment=VerticalAlignment.Center;label.Children.Add(name);
   devToolsWindowTarget=DialogText("",12,"Muted");devToolsWindowTarget.Margin=new Thickness(10,0,0,0);devToolsWindowTarget.VerticalAlignment=VerticalAlignment.Center;devToolsWindowTarget.TextTrimming=TextTrimming.CharacterEllipsis;devToolsWindowTarget.TextWrapping=TextWrapping.NoWrap;devToolsWindowTarget.MaxWidth=360;label.Children.Add(devToolsWindowTarget);
   var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};barGrid.Children.Add(buttons);
   Func<string,string,Action,Button> caption=delegate(string glyph,string tip,Action click) {
    var b=new Button {Style=(Style)window.FindResource("CaptionButton"),Content=glyph,ToolTip=tip};System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b,true);b.Click+=delegate {click();};buttons.Children.Add(b);return b;
   };
   caption("\uE72C","Reconnect DevTools",delegate {devToolsTarget=null;AttachDevTools();});
   caption("\uE73F","Dock to Mica",delegate {SetDevToolsDetached(false);}).Tag="devtools-dock";
   caption("\uE8BB","Close DevTools (F12)",delegate {if(devToolsOpen)ToggleDevTools();});
   var body=new Grid();Grid.SetRow(body,1);layout.Children.Add(body);
   devToolsWindowEmpty=DialogText("Open a web page to inspect it.",12,"Muted");devToolsWindowEmpty.HorizontalAlignment=HorizontalAlignment.Center;devToolsWindowEmpty.VerticalAlignment=VerticalAlignment.Center;body.Children.Add(devToolsWindowEmpty);
   devToolsWindowHost=new Grid();body.Children.Add(devToolsWindowHost);
   w.KeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e) {if(e.Key==System.Windows.Input.Key.F12){e.Handled=true;if(devToolsOpen)ToggleDevTools();}};
   w.Closed+=delegate {
    if(devToolsWindow!=w)return;
    devToolsWindow=null;DisposeDevTools();devToolsWindowHost=null;devToolsWindowTarget=null;devToolsWindowEmpty=null;devToolsTarget=null;
    if(devToolsOpen&&devToolsDetached&&!closing){devToolsOpen=false;LayoutDevTools();} // Closed with the window's own ×.
   };
   devToolsWindow=w;w.Show();
  }
  void CloseDevToolsWindow() {var w=devToolsWindow;if(w==null)return;devToolsWindow=null;DisposeDevTools();devToolsWindowHost=null;devToolsWindowTarget=null;devToolsWindowEmpty=null;w.Close();}
  // Fullscreen hides the dock; leaving fullscreen brings it back.
  // F12 slides the dock open and closed by width (the inspector is a native window, so
  // it can't be moved with a transform); fullscreen changes it instantly.
  int devToolsMotion;
  void LayoutDevTools(bool animate=false) {
   bool show=devToolsOpen&&!devToolsDetached&&!fullscreenApplied;var surface=Find<Grid>("PageSurface");var columns=surface.ColumnDefinitions;
   double available=Math.Max(0,surface.ActualWidth);double width=Math.Max(300,Math.Min(devToolsWidth,available-320));
   var splitter=Find<GridSplitter>("DevToolsSplitter");var dock=Find<Grid>("DevToolsDock");int motion=++devToolsMotion;
   surface.BeginAnimation(MotionProperties.DevToolsWidthProperty,null);surface.SetValue(MotionProperties.DevToolsWidthProperty,columns[2].ActualWidth);
   columns[2].MinWidth=0;
   if(show) {
    columns[1].Width=new GridLength(6);splitter.Visibility=Visibility.Visible;dock.Visibility=Visibility.Visible;
    if(!animate){surface.SetValue(MotionProperties.DevToolsWidthProperty,width);columns[2].MinWidth=280;return;}
    Tween(surface,MotionProperties.DevToolsWidthProperty,width,220,delegate {if(motion==devToolsMotion&&devToolsOpen)columns[2].MinWidth=280;});
   } else {
    Action hide=delegate {if(motion!=devToolsMotion)return;columns[1].Width=new GridLength(0);splitter.Visibility=Visibility.Collapsed;dock.Visibility=Visibility.Collapsed;};
    if(!animate||columns[2].ActualWidth<1){surface.SetValue(MotionProperties.DevToolsWidthProperty,0.0);hide();return;}
    Tween(surface,MotionProperties.DevToolsWidthProperty,0,180,hide);
   }
  }
  async void AttachDevTools() {
   int attempt=++devToolsAttach;
   var page=Find<Grid>("WebHost").IsVisible?CurrentView():null;
   bool hasPage=page!=null&&page.CoreWebView2!=null;
   DevToolsEmptyText.Text=hasPage?"Connecting…":"Open a web page to inspect it.";
   if(devToolsView!=null)devToolsView.Visibility=hasPage?Visibility.Visible:Visibility.Hidden;
   DevToolsTargetText.Text=hasPage&&Active!=null?OriginLabel(Active.Url):"";
   if(!hasPage||!devToolsOpen)return;
   try {
    var info=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(await page.CoreWebView2.CallDevToolsProtocolMethodAsync("Target.getTargetInfo","{}"));
    string target=(string)((Dictionary<string,object>)info["targetInfo"])["targetId"];
    if(attempt!=devToolsAttach||!devToolsOpen)return;
    if(target==devToolsTarget&&devToolsView!=null)return;
    if(devToolsView==null) {
     var view=new WebView2 {DefaultBackgroundColor=System.Drawing.Color.Transparent};var host=DevToolsHostPanel;host.Children.Add(view);
     {var devEngine=await EnsureEnvironment();await view.EnsureCoreWebView2Async(devEngine,ViewOptions(devEngine));}
     if(closing||host!=DevToolsHostPanel){host.Children.Remove(view);view.Dispose();return;}
     devToolsView=view;var core=view.CoreWebView2;RegisterOwnTarget(core);view.SizeChanged+=delegate {QueuePageCorners();};view.IsVisibleChanged+=delegate {QueuePageCorners();};QueuePageCorners();
     string origin="http://127.0.0.1:"+devToolsPort+"/";
     core.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e) {
      if(e.Uri=="about:blank"||e.Uri.StartsWith(origin,StringComparison.OrdinalIgnoreCase)||e.Uri.StartsWith("devtools://",StringComparison.OrdinalIgnoreCase))return;
      e.Cancel=true;if(Navigation.IsWeb(e.Uri))Navigate(e.Uri,true);
     };
     core.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e) {e.Handled=true;string uri=e.Uri;if(Navigation.IsWeb(uri))window.Dispatcher.BeginInvoke(new Action(delegate {if(!closing)Navigate(uri,true);}));};
     core.ProcessFailed+=delegate {devToolsTarget=null;};
     if(attempt!=devToolsAttach||!devToolsOpen)return;
    }
    devToolsTarget=target;
    devToolsView.Visibility=Visibility.Visible;
    devToolsView.CoreWebView2.Navigate("http://127.0.0.1:"+devToolsPort+"/devtools/devtools_app.html?ws=127.0.0.1:"+devToolsPort+"/devtools/page/"+target);
   }catch(Exception e) {
    if(attempt==devToolsAttach){DevToolsEmptyText.Text="DevTools couldn't connect: "+e.Message;if(devToolsView!=null)devToolsView.Visibility=Visibility.Hidden;devToolsTarget=null;}
   }
  }
  void DisposeDevTools() {if(devToolsView!=null){ReplaceTargetWatcher(devToolsView);var parent=devToolsView.Parent as Panel;if(parent!=null)parent.Children.Remove(devToolsView);devToolsView.Dispose();devToolsView=null;}}
 }
}
