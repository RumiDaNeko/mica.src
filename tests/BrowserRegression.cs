using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace Mica {
 partial class BrowserApp {
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
  [StructLayout(LayoutKind.Sequential)] struct TestRect {public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)] struct TestMonitor {public int Size;public TestRect Monitor,Work;public uint Flags;}
  [StructLayout(LayoutKind.Sequential)] struct TestPoint {public int X,Y;}
  [StructLayout(LayoutKind.Sequential)] struct TestMinMax {public TestPoint Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}
  [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
  [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor,ref TestMonitor info);
  void CheckFooterOnScreen(string menuName,string themeName) {
   var info=new TestMonitor {Size=Marshal.SizeOf(typeof(TestMonitor))};Require(GetMonitorInfo(MonitorFromWindow(new WindowInteropHelper(window).Handle,2),ref info),"Monitor lookup failed");
   foreach(string name in new[]{menuName,themeName}) {var button=Find<Button>(name);var top=button.PointToScreen(new Point(0,0));var bottom=button.PointToScreen(new Point(button.ActualWidth,button.ActualHeight));regressionLog.Add(name+": "+top+" .. "+bottom+"; work "+info.Work.Left+","+info.Work.Top+" .. "+info.Work.Right+","+info.Work.Bottom+"; root "+Find<Grid>("Root").ActualHeight);Require(button.IsVisible&&top.Y>=info.Work.Top&&bottom.Y<=info.Work.Bottom&&top.X>=info.Work.Left&&bottom.X<=info.Work.Right,"Footer falls outside monitor work area: "+name);}
  }
  readonly List<string> regressionLog=new List<string>();
  int regressionFailures;
  void RegressionCheck(string name,Action assertion) {try {assertion();regressionLog.Add("PASS "+name);}catch(Exception e){regressionFailures++;regressionLog.Add("FAIL "+name+": "+e.Message);}}
  void Require(bool condition,string error) {if(!condition)throw new Exception(error);}
  int HitTest(FrameworkElement element,double x,double y) {var p=element.PointToScreen(new Point(x,y));long packed=((long)(short)p.Y<<16)|((long)(short)p.X&65535);return SendMessage(new WindowInteropHelper(window).Handle,0x84,IntPtr.Zero,new IntPtr(packed)).ToInt32();}
  async Task WaitFor(Func<bool> done,int milliseconds) {for(int i=0;i<milliseconds/50;i++){if(done())return;await Task.Delay(50);}throw new Exception("Timed out waiting for browser state");}
  async Task RunSitesTest() {
   var log=new List<string>();int failures=0;
   foreach(var url in new[]{"https://example.com/","https://github.com/","https://mail.google.com/"}) {
    try {
     Navigate(url,false);await WaitFor(delegate {return CurrentView()!=null;},15000);
     await Task.Delay(500);
     await WaitFor(delegate {NavigationProgress p;return navigations.TryGetValue(state.ActiveId,out p)&&!p.IsLoading;},25000);
     await Task.Delay(1500);
     var view=CurrentView();Require(view!=null&&Find<Border>("ErrorPanel").Visibility!=Visibility.Visible,"Error panel: "+Find<TextBlock>("ErrorText").Text);
     string title=view.CoreWebView2.DocumentTitle;Require(!string.IsNullOrWhiteSpace(title),"Document title empty");
     string text=await view.CoreWebView2.ExecuteScriptAsync("document.body ? document.body.innerText.length : 0");Require(text!="0","Document body empty");
     log.Add("PASS "+url+" -> "+view.CoreWebView2.Source+" | "+title);
     if(url.Contains("mail.google.com"))using(var output=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","gmail-checked.png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,output);
    }catch(Exception e){failures++;log.Add("FAIL "+url+" | "+e.Message);}
   }
   log.Add(failures==0?"PASS":"FAILURES: "+failures);File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","sites-result.txt"),log);Application.Current.Shutdown(failures==0?0:1);
  }
  void CapturePopup(System.Windows.Controls.Primitives.Popup popup,string name) {
   CaptureElement((FrameworkElement)popup.Child,name);
  }
  void CaptureElement(FrameworkElement element,string name) {
   element.UpdateLayout();if(element.ActualWidth<1||element.ActualHeight<1)return; // Not laid out yet: a missing picture, not a failed check.
   var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);image.Render(element);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(var output=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts",name)))encoder.Save(output);
  }
  async Task RequestFixtureContextMenu() {
   var core=CurrentView().CoreWebView2;
   await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent","{\"type\":\"mousePressed\",\"x\":80,\"y\":40,\"button\":\"right\",\"clickCount\":1}");
   await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent","{\"type\":\"mouseReleased\",\"x\":80,\"y\":40,\"button\":\"right\",\"clickCount\":1}");
   await WaitFor(delegate {return openContextMenu!=null&&openContextMenu.IsOpen;},5000);await Task.Delay(240);
  }
  async Task RunPinnedTest() {
   var log=new List<string>();int failures=0;
   try {
    string pinnedId=state.ActiveId;Active.Pinned=true;RenderTabs();int count=state.Tabs.Count;
    Require(HandleKey(System.Windows.Input.Key.W,System.Windows.Input.ModifierKeys.Control),"Ctrl W not consumed");
    Require(state.ActiveId==pinnedId&&state.Tabs.Count==count&&Active.Pinned,"Ctrl W closed the sole pinned tab");
    log.Add("PASS Ctrl W preserves the sole pinned tab and active selection");
    var normal=state.AddTab("mica://home");await Activate(normal.Id);
    HandleKey(System.Windows.Input.Key.W,System.Windows.Input.ModifierKeys.Control);
    Require(state.Tabs.TrueForAll(t=>t.Id!=normal.Id)&&state.ActiveId==pinnedId,"Ctrl W did not close the normal tab");
    log.Add("PASS Ctrl W still closes normal tabs");
    Active.Pinned=false;RenderTabs();HandleKey(System.Windows.Input.Key.W,System.Windows.Input.ModifierKeys.Control);
    Require(state.Tabs.TrueForAll(t=>t.Id!=pinnedId)&&state.Tabs.Count>0,"Unpinned tab could not be closed");
    log.Add("PASS unpinning restores Ctrl W close behavior and last-tab home fallback");
   }catch(Exception e){failures++;log.Add("FAIL "+e.Message);}
   log.Add(failures==0?"PASS":"FAILURES: "+failures);File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","pinned-result.txt"),log);Application.Current.Shutdown(failures==0?0:1);
  }
  [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] static extern bool GetCursorPos(out TestPoint point);
  [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
  async Task ClickMenuTrigger(Button button) {
   var point=button.PointToScreen(new Point(button.ActualWidth/2,button.ActualHeight/2));
   SetCursorPos((int)point.X,(int)point.Y);await Task.Delay(80);
   mouse_event(2,0,0,0,UIntPtr.Zero);await Task.Delay(100);mouse_event(4,0,0,0,UIntPtr.Zero);await Task.Delay(300);
  }
  async Task RunRegressionTest() {
   try {
    await Task.Delay(300);window.UpdateLayout();
    RegressionCheck("WPF shell uses GPU rendering",delegate {Require(System.Windows.Media.RenderOptions.ProcessRenderMode==RenderMode.Default,"Shell is forced to software rendering");});
    RegressionCheck("taskbar window identity matches installed shortcuts",delegate {Require(WindowsIntegration.WindowIdentity(new WindowInteropHelper(window).Handle)==DesktopShell.AppId,"Window identity missing");});
    RegressionCheck("Jump List exposes normal and private new-window tasks",delegate {var tasks=BuildJumpItems();bool normal=false,priv=false;foreach(var item in tasks){var task=item as System.Windows.Shell.JumpTask;if(task!=null){if(task.Arguments=="--new-window")normal=true;if(task.Arguments=="--private")priv=true;}}Require(normal&&priv,"New window tasks missing");});
    RegressionCheck("selected tab highlight spans title and close region without scaling",delegate {
     var rows=Active.Pinned?Find<StackPanel>("PinnedTabs"):Find<StackPanel>("TabItems");Grid row=null;Button select=null;
     foreach(var item in rows.Children){var candidate=item as Grid;if(candidate==null)continue;var b=candidate.Children[0] as Button;if(b!=null&&(string)b.Tag==state.ActiveId){row=candidate;select=b;break;}}
     Require(row!=null&&Grid.GetColumnSpan(select)==2,"Selection excludes close button");
     Require(Math.Abs(select.ActualWidth-row.ActualWidth)<1,"Highlight does not fill the row");
     AnimateButton(select,1.025);var surface=select.Template.FindName("Surface",select) as Border;
     Require(surface!=null&&surface.RenderTransform.Value.IsIdentity,"Tab highlight scales outside its bounds");
     var close=(Button)row.Children[1];Require(close.ActualHeight<row.ActualHeight,"Close hover covers the entire row height");
    });
    CaptureElement(Find<Grid>("Sidebar"),"mica-tab-highlight-light.png");state.Dark=true;ApplyTheme();await Task.Delay(250);CaptureElement(Find<Grid>("Sidebar"),"mica-tab-highlight-dark.png");state.Dark=false;ApplyTheme();

    RegressionCheck("legacy shortcut and ambiguous space buttons removed",delegate {Require(window.FindName("QuickGitHub")==null&&window.FindName("QuickYouTube")==null&&window.FindName("QuickGmail")==null,"Fixed website shortcut cluster remains");Require(window.FindName("PersonalSpace")==null&&window.FindName("WorkSpace")==null,"Ambiguous bottom space buttons remain");});
    RegressionCheck("Spaces opens a dropdown without switching",delegate {string before=state.Space;Find<Button>("SpaceButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var popup=window.FindName("SpacesPopup") as System.Windows.Controls.Primitives.Popup;Require(popup!=null&&popup.IsOpen,"Spaces dropdown missing");Require(state.Space==before,"Opening Spaces changed workspace");popup.IsOpen=false;});
    TestPoint previousPointer;GetCursorPos(out previousPointer);window.Activate();
    try {
     foreach(bool compact in new[]{false,true}) {
      if(sidebarHidden!=compact){ToggleSidebar();await Task.Delay(350);}
      foreach(bool space in new[]{true,false}) {
       CloseMenus();var popup=space?spacesPopup:menuPopup;var trigger=Find<Button>(space?(compact?"MiniSpace":"SpaceButton"):(compact?"MiniMenu":"MenuButton"));int opened=0;
       EventHandler countOpen=delegate {opened++;};popup.Opened+=countOpen;
       await ClickMenuTrigger(trigger);bool first=popup.IsOpen;
       await ClickMenuTrigger(trigger);bool second=!popup.IsOpen&&opened==1;
       await ClickMenuTrigger(trigger);bool third=popup.IsOpen&&opened==2;
       RegressionCheck((compact?"compact ":"expanded ")+(space?"Space":"Menu")+" mouse clicks toggle without reopening",delegate {Require(first&&second&&third,"Mouse toggle or Opened count incorrect: "+opened);});
       popup.Opened-=countOpen;CloseMenus();
      }
     }
    }finally {SetCursorPos(previousPointer.X,previousPointer.Y);}
    if(sidebarHidden){ToggleSidebar();await Task.Delay(350);}
    ShowSpaces();await Task.Delay(240);CapturePopup(spacesPopup,"mica-spaces-menu.png");
    var spacesBody=(StackPanel)((ScrollViewer)((Border)spacesPopup.Child).Child).Content;
    foreach(var child in spacesBody.Children){var button=child as Button;if(button!=null&&System.Windows.Automation.AutomationProperties.GetName(button)=="Work"){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));break;}}
    RegressionCheck("Spaces selection switches workspace",delegate {Require(state.Space=="Work"&&!spacesPopup.IsOpen,"Work selection did not switch and close");});SwitchSpace("Personal");
    ShowMenu();await Task.Delay(240);CapturePopup(menuPopup,"mica-browser-menu.png");
    RegressionCheck("browser menu includes labeled extensions command",delegate {bool found=false;foreach(var child in System.Linq.Enumerable.SelectMany(submenus.Values,s=>System.Linq.Enumerable.Cast<UIElement>(s.Children))){var button=child as Button;if(button!=null&&System.Windows.Automation.AutomationProperties.GetName(button)=="Extensions")found=true;}Require(found,"Extension manager missing from menu");});CloseMenus();await Task.Delay(200);
    RegressionCheck("only the sidebar toggle remains",delegate {Require(window.FindName("ToolbarSidebarButton")==null,"Duplicate toolbar toggle still exists");Require(Find<Button>("ToggleSidebar").IsVisible,"Sidebar toggle missing");});
    var animationWatch=System.Diagnostics.Stopwatch.StartNew();ToggleSidebar();await Task.Delay(45);window.UpdateLayout();
    regressionLog.Add("Sidebar frame: "+Find<Grid>("AppLayout").ColumnDefinitions[0].ActualWidth+", elapsed "+animationWatch.ElapsedMilliseconds+"ms, value "+Find<Grid>("AppLayout").GetValue(MotionProperties.SidebarWidthProperty));
    RegressionCheck("sidebar moves instead of jumping when animations enabled",delegate {if(SystemParameters.ClientAreaAnimation)Require(Find<Grid>("AppLayout").ColumnDefinitions[0].ActualWidth>64&&Find<Grid>("AppLayout").ColumnDefinitions[0].ActualWidth<238,"Sidebar jumped directly to endpoint");});
    ToggleSidebar();await Task.Delay(280);window.UpdateLayout();
    ToggleSidebar();await Task.Delay(280);
    RegressionCheck("collapsed menu and search share the sidebar center",delegate {
     window.UpdateLayout();var toggle=Find<Button>("ToggleSidebar");var search=Find<Button>("MiniSearch");var layout=Find<Grid>("AppLayout");
     double menuCenter=toggle.TranslatePoint(new Point(toggle.ActualWidth/2,0),layout).X;
     double searchCenter=search.TranslatePoint(new Point(search.ActualWidth/2,0),layout).X;
     Require(Math.Abs(menuCenter-searchCenter)<0.5&&Math.Abs(menuCenter-layout.ColumnDefinitions[0].ActualWidth/2)<0.5,"Compact buttons are off center");
    });CaptureElement(Find<Grid>("Sidebar"),"mica-centered-sidebar.png");
    RegressionCheck("compact rail exposes Spotlight search",delegate {var b=window.FindName("MiniSearch") as Button;Require(b!=null&&b.IsVisible&&b.IsEnabled,"Compact search icon missing");b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Require(IsSpotlight,"Mini search did not open Spotlight");CloseSpotlight();});
    await Task.Delay(180);
    string originalTab=state.ActiveId;var extraTabs=new HashSet<string>();for(int i=0;i<30;i++)extraTabs.Add(state.AddTab("mica://home").Id);RenderTabs();await Task.Delay(280);window.UpdateLayout();
    RegressionCheck("many compact tabs retain full icon width",delegate {var scroll=Find<ScrollViewer>("MiniTabScroller");Require(scroll.ExtentHeight>scroll.ViewportHeight,"Fixture did not overflow");Require(scroll.ViewportWidth>=36,"Scrollbar clipped favicon buttons");scroll.ScrollToBottom();});
    state.Tabs.RemoveAll(t=>extraTabs.Contains(t.Id));await Activate(originalTab);ToggleSidebar();await Task.Delay(280);
    RegressionCheck("sidebar rapid reversal ends fully open",delegate {Require(!sidebarHidden&&Math.Abs(Find<Grid>("AppLayout").ColumnDefinitions[0].ActualWidth-238)<1&&Find<Grid>("Sidebar").IsVisible,"Interrupted sidebar animation failed to restore");});
    OpenSpotlight(false);await Task.Delay(80);CloseSpotlight();await Task.Delay(40);OpenSpotlight(false);await Task.Delay(260);
    RegressionCheck("Spotlight reopen survives old close completion",delegate {Require(IsSpotlight&&Find<Grid>("SpotlightOverlay").Opacity>0.99&&!Find<Grid>("AppLayout").IsHitTestVisible,"Stale close hid reopened Spotlight");});
    CloseSpotlight();await WaitFor(delegate {return !IsSpotlight&&Find<Grid>("AppLayout").IsHitTestVisible;},3000);
    RegressionCheck("Spotlight close restores interaction",delegate {Require(!IsSpotlight&&Find<Grid>("AppLayout").IsHitTestVisible,"Spotlight close left browser disabled");});
    RegressionCheck("toolbar provides native drag region",delegate {Require(HitTest(Find<Grid>("Toolbar"),190,5)==2,"Blank toolbar is not HTCAPTION");});
    RegressionCheck("window buttons remain clickable and compact",delegate {var b=Find<Button>("MaxButton");Require(b.ActualWidth<=30&&b.FontSize<=11,"Window controls are oversized");Require(HitTest(b,b.ActualWidth/2,b.ActualHeight/2)==1,"Maximize button is not HTCLIENT");});
    var windowHandle=new WindowInteropHelper(window).Handle;SendMessage(windowHandle,0x231,IntPtr.Zero,IntPtr.Zero);await Task.Delay(200);
    RegressionCheck("native resize still enforces minimum dimensions",delegate {IntPtr memory=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TestMinMax)));try {Marshal.StructureToPtr(new TestMinMax(),memory,false);SendMessage(windowHandle,0x24,IntPtr.Zero,memory);var limits=(TestMinMax)Marshal.PtrToStructure(memory,typeof(TestMinMax));var scale=PresentationSource.FromVisual(window).CompositionTarget.TransformToDevice;Require(limits.MinTrackSize.X>=window.MinWidth*scale.M11&&limits.MinTrackSize.Y>=window.MinHeight*scale.M22,"Native minimum tracking size bypassed: "+limits.MinTrackSize.X+" x "+limits.MinTrackSize.Y);}finally {Marshal.FreeHGlobal(memory);}});
    RegressionCheck("native resize starts chrome feedback",delegate {Require(Find<Border>("ResizeGlow").Opacity>0.7,"Resize feedback did not appear");});
    SendMessage(windowHandle,0x232,IntPtr.Zero,IntPtr.Zero);await Task.Delay(280);
    RegressionCheck("native resize feedback settles",delegate {Require(Find<Border>("ResizeGlow").Opacity<0.01,"Resize left animation active");});
    RegressionCheck("native frame retains caption and resize styles for DWM transitions",delegate {Require(WindowBounds.HasNativeFrame(windowHandle),"Native frame styles missing before maximize");});
    Maximize();await Task.Delay(240);RegressionCheck("native maximize works",delegate {Require(window.WindowState==WindowState.Maximized&&WindowBounds.HasNativeFrame(windowHandle),"Maximize lost native frame styles");});
    RegressionCheck("maximized footer stays inside monitor work area",delegate {CheckFooterOnScreen("MenuButton","ThemeButton");});
    ToggleSidebar();await Task.Delay(280);RegressionCheck("maximized compact footer stays inside monitor work area",delegate {CheckFooterOnScreen("MiniMenu","MiniTheme");});ToggleSidebar();await Task.Delay(280);
    string beforePins=state.ActiveId;var pinnedFixture=new HashSet<string>();for(int i=0;i<30;i++){var tab=state.AddTab("mica://home");tab.Pinned=true;pinnedFixture.Add(tab.Id);}RenderTabs();await Task.Delay(280);window.UpdateLayout();
    RegressionCheck("many pinned tabs cannot displace footer",delegate {CheckFooterOnScreen("MenuButton","ThemeButton");});state.Tabs.RemoveAll(t=>pinnedFixture.Contains(t.Id));await Activate(beforePins);
    Capture("mica-maximized-before.png");Maximize();await Task.Delay(240);
    ToggleSidebar();await Task.Delay(280);window.UpdateLayout();
    RegressionCheck("sidebar can reopen using visible mouse control",delegate {var b=window.FindName("ToggleSidebar") as Button;Require(b!=null&&b.IsVisible&&b.IsEnabled,"No visible sidebar restore button");b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Require(!sidebarHidden&&Find<Grid>("Sidebar").IsVisible,"Sidebar did not reopen");});
    if(sidebarHidden)ToggleSidebar();
    using(var server=new RegressionServer()) {
     string aborted=server.Url+"stopped";Navigate(aborted,false);
     await WaitFor(delegate {return CurrentView()!=null;},15000);
     string outcome="none";int status=0;
     CurrentView().CoreWebView2.NavigationCompleted+=delegate(object sender,CoreWebView2NavigationCompletedEventArgs e){outcome=e.WebErrorStatus.ToString();status=e.HttpStatusCode;};
     await WaitFor(delegate {return outcome!="none";},10000);await Task.Delay(200);
     regressionLog.Add("Stopped document event: "+outcome+", HTTP "+status);
     RegressionCheck("HTTP 200 window.stop retains real page",delegate {Require(outcome=="ConnectionAborted"&&status==200,"Fixture did not produce requested abort");Require(Find<Border>("ErrorPanel").Visibility!=Visibility.Visible&&Find<Grid>("WebHost").Visibility==Visibility.Visible,"Usable document hidden behind error");});
     Navigate(server.Url+"slow",false);await Task.Delay(150);
     RegressionCheck("refresh glyph rotates during navigation",delegate {var glyph=window.FindName("ReloadGlyph") as TextBlock;var rotation=glyph==null?null:glyph.RenderTransform as System.Windows.Media.RotateTransform;Require(rotation!=null&&(!SystemParameters.ClientAreaAnimation||rotation.HasAnimatedProperties),"Reload glyph does not animate while loading");});
     var restingHome=state.AddTab("mica://home");string loadingId=null;foreach(var tab in state.Tabs)if(tab.Url==server.Url+"slow")loadingId=tab.Id;await Activate(restingHome.Id);await Task.Delay(240);
     RegressionCheck("refresh motion belongs only to the active tab",delegate {var glyph=window.FindName("ReloadGlyph") as TextBlock;Require(glyph!=null&&!((System.Windows.Media.RotateTransform)glyph.RenderTransform).HasAnimatedProperties,"Background loading keeps the active home spinner running");});
     await Activate(loadingId);Navigate(server.Url+"ok",false);
     await WaitFor(delegate {return CurrentView()!=null&&CurrentView().CoreWebView2.DocumentTitle=="Regression OK"&&Find<ProgressBar>("Loading").Visibility!=Visibility.Visible;},10000);
     await Task.Delay(600);
     RegressionCheck("rapid navigation leaves latest page visible",delegate {Require(Find<Border>("ErrorPanel").Visibility!=Visibility.Visible&&Active.Url==server.Url+"ok","Old navigation replaced new page state");});
     RegressionCheck("refresh animation stops after navigation",delegate {var glyph=window.FindName("ReloadGlyph") as TextBlock;Require(glyph!=null&&!((System.Windows.Media.RotateTransform)glyph.RenderTransform).HasAnimatedProperties,"Reload animation remains active after completion");});
     var context=TabMenu(Active);context.PlacementTarget=Find<Button>("MenuButton");context.IsOpen=true;await Task.Delay(240);CaptureElement(context,"mica-tab-context.png");
     RegressionCheck("tab context menu uses rounded custom surface",delegate {Require(context.Template.FindName("MenuSurface",context) is Border,"Stock context menu template remains");});
     var spaceMenu=System.Linq.Enumerable.First(System.Linq.Enumerable.OfType<MenuItem>(context.Items),m=>(string)m.Header=="Move to space");spaceMenu.IsSubmenuOpen=true;await Task.Delay(240);CapturePopup((System.Windows.Controls.Primitives.Popup)spaceMenu.Template.FindName("PART_Popup",spaceMenu),"mica-context-submenu.png");
     RegressionCheck("space submenu preserves selected and disabled states",delegate {var selected=(MenuItem)spaceMenu.Items[state.Space=="Personal"?0:1];Require(selected.IsChecked&&!selected.IsEnabled,"Current space choice incorrect");});spaceMenu.IsSubmenuOpen=false;
     bool pinnedBefore=Active.Pinned;((MenuItem)context.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
     RegressionCheck("redesigned tab menu still pins tabs",delegate {Require(Active.Pinned!=pinnedBefore&&!context.IsOpen,"Pin command failed or menu remained open");});Active.Pinned=pinnedBefore;RenderTabs();
     state.Dark=true;ApplyTheme();context=TabMenu(Active);context.PlacementTarget=Find<Button>("MenuButton");context.IsOpen=true;await Task.Delay(240);CaptureElement(context,"mica-tab-context-dark.png");CloseContextMenu();state.Dark=false;ApplyTheme();
     await RequestFixtureContextMenu();CaptureElement(openContextMenu,"mica-web-context.png");
     var webMenu=openContextMenu;MenuItem webReload=null;foreach(var item in webMenu.Items){var command=item as MenuItem;if(command!=null&&(string)command.Tag=="reload")webReload=command;}
     RegressionCheck("web page context menu preserves engine commands",delegate {Require(webReload!=null&&webReload.IsEnabled,"Native reload command missing");Require(webMenu.Template.FindName("MenuSurface",webMenu) is Border,"Website context menu was not restyled");});
     if(webReload!=null){ulong previousNavigation=navigations[state.ActiveId].CurrentId;webReload.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await WaitFor(delegate {return navigations[state.ActiveId].CurrentId!=previousNavigation&&!navigations[state.ActiveId].IsLoading;},10000);RegressionCheck("web context reload invokes the native command",delegate {Require(!webMenu.IsOpen,"Native command left menu open");});}
     await RequestFixtureContextMenu();CloseContextMenu();await Task.Delay(100);RegressionCheck("dismissed web context menu releases its deferral",delegate {Require(openContextMenu==null,"Context menu stayed active after dismissal");});
     await RequestFixtureContextMenu();CloseTab(state.ActiveId);await Task.Delay(220);RegressionCheck("closing a tab releases its pending native menu",delegate {Require(openContextMenu==null&&finishContextRequest==null,"Tab disposal left a native menu request pending");});Navigate(server.Url+"ok",false);await WaitFor(delegate {return CurrentView()!=null&&CurrentView().CoreWebView2.DocumentTitle=="Regression OK"&&!navigations[state.ActiveId].IsLoading;},10000);
     OpenSpotlight(false);Find<ListBox>("SearchResults").ItemsSource=new List<SearchHit> {new SearchHit {Title="Fixture",Url=server.Url+"ok?spotlight",Kind="URL"}};Find<ListBox>("SearchResults").SelectedIndex=0;int beforeSelection=state.Tabs.Count;
     ExecuteSearch();ExecuteSearch();
     RegressionCheck("Spotlight rapid double activation opens only one tab",delegate {Require(state.Tabs.Count==beforeSelection+1,"Repeated selection during closing created duplicate tabs");});
     await WaitFor(delegate {return CurrentView()!=null&&CurrentView().CoreWebView2.DocumentTitle=="Regression OK";},10000);await Task.Delay(250);
     await WaitFor(delegate {return HasTabFavicon(state.ActiveId);},8000);
     RegressionCheck("web favicon appears as an image",delegate {Require(CreateTabIcon(Active,16) is Image,"Real favicon did not replace fallback");});
     string tabToSelect=state.ActiveId;var another=state.AddTab("mica://home");await Activate(another.Id);
     SleepIdleTabs(true);RegressionCheck("sleeping tab retains favicon",delegate {Require(!views.ContainsKey(tabToSelect)&&HasTabFavicon(tabToSelect),"Sleeping tab lost its icon");});
     ToggleSidebar();await Task.Delay(280);
     RegressionCheck("compact favicon tab selects existing tab",delegate {var tabs=window.FindName("MiniTabs") as StackPanel;Require(tabs!=null&&tabs.IsVisible,"Compact tabs missing");Button target=null;foreach(var item in tabs.Children){var b=item as Button;if(b!=null&&(string)b.Tag==tabToSelect)target=b;}Require(target!=null,"Existing tab absent from rail");Require(target.Content is Image||target.Content is Grid&&((Grid)target.Content).Children[0] is Image,"Mini tab did not display favicon");target.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Require(state.ActiveId==tabToSelect,"Mini tab did not activate existing tab");});
     ToggleSidebar();await Task.Delay(280);
     ToggleSidebar();await Task.Delay(280);window.UpdateLayout();Capture("mica-fixed-focus.png");ToggleSidebar();await Task.Delay(280);
     Capture("mica-fixed-chrome.png");
     RegressionCheck("no debugging port is open unless docked DevTools is turned on",delegate {Require(devToolsPort==0&&!state.DockedDevTools,"port "+devToolsPort);});
    }
   }catch(Exception e){regressionFailures++;regressionLog.Add("FAIL regression runner: "+e);}
   regressionLog.Add(regressionFailures==0?"PASS":"FAILURES: "+regressionFailures);
   File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","regression-result.txt"),regressionLog);
   Application.Current.Shutdown(regressionFailures==0?0:1);
  }
  async Task RunUpdateCloseTest() {
   Active.Title="Update window one";Save();
   var second=Program.OpenWindow(new string[0],true,profile);
   await Task.Delay(300);second.Active.Title="Update window two";second.Save();
   File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","update-close-ready.txt"),statePath);
  }
 }
 sealed class RegressionServer:IDisposable {
  readonly TcpListener listener;bool disposed;public readonly string Url;
  public RegressionServer(){listener=new TcpListener(IPAddress.Loopback,0);listener.Start();Url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/";Accept();}
  async void Accept(){while(!disposed)try{var client=await listener.AcceptTcpClientAsync();Serve(client);}catch(ObjectDisposedException){break;}catch(SocketException){if(disposed)break;throw;}}
  async void Serve(TcpClient client){using(client)try {var stream=client.GetStream();var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);var line=await reader.ReadLineAsync();string path=line==null?"/":line.Split(' ')[1];while(!string.IsNullOrEmpty(await reader.ReadLineAsync())){}if(path=="/favicon.png"){byte[] icon=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAGUlEQVR4nGOoTlzxnxLMMGrAqAGjBgwXAwB/B4MfB6S/WQAAAABJRU5ErkJggg==");byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: "+icon.Length+"\r\nConnection: close\r\n\r\n");await stream.WriteAsync(response,0,response.Length);await stream.WriteAsync(icon,0,icon.Length);return;}if(path=="/slow")await Task.Delay(2500);string html=path=="/stopped"?"<!doctype html><title>Stopped but usable</title><h1>Usable page</h1><script>setTimeout(function(){window.stop()},250)</script><img src='/slow'>":"<!doctype html><title>Regression OK</title><link rel='icon' href='/favicon.png'><h1>Regression OK</h1>";byte[] body=Encoding.UTF8.GetBytes(html);byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n\r\n");await stream.WriteAsync(header,0,header.Length);await stream.WriteAsync(body,0,body.Length);}catch(IOException){}catch(ObjectDisposedException){}}
  public void Dispose(){disposed=true;listener.Stop();}
 }
}
