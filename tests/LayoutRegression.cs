using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.Generic;
using Microsoft.Web.WebView2.Core;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mica {
 partial class BrowserApp {
  void SaveWindowImage(string path) {
   var root=Find<Grid>("Root");var bitmap=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(window);
   var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);
  }
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out TestRect rect);
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd,out TestRect rect);
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd,ref TestPoint point);
  void SaveScreenImage(string path,List<string> log) {
   var hwnd=new System.Windows.Interop.WindowInteropHelper(window).Handle;TestRect r,c;GetWindowRect(hwnd,out r);GetClientRect(hwnd,out c);var origin=new TestPoint();ClientToScreen(hwnd,ref origin);
   var rootOrigin=Find<Grid>("Root").PointToScreen(new Point(0,0));
   log.Add("window "+r.Left+","+r.Top+" .. "+r.Right+","+r.Bottom+" client "+c.Right+"x"+c.Bottom+" @"+origin.X+","+origin.Y+" root@"+rootOrigin+" state "+window.WindowState);
   using(var bmp=new System.Drawing.Bitmap(r.Right-r.Left+40,r.Bottom-r.Top+40))using(var g=System.Drawing.Graphics.FromImage(bmp)){g.CopyFromScreen(r.Left-20,r.Top-20,0,0,bmp.Size);bmp.Save(path);}
  }
  string Describe(FrameworkElement e) {var p=e.TranslatePoint(new Point(0,0),Find<Grid>("Root"));return e.Name+" @"+p.X.ToString("0.#")+","+p.Y.ToString("0.#")+" "+e.ActualWidth.ToString("0.#")+"x"+e.ActualHeight.ToString("0.#");}
  async Task RunLayoutTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"layout");Directory.CreateDirectory(dir);
   for(int i=0;i<40;i++)state.AddTab("mica://home");RenderTabs();
   window.Topmost=true;window.Activate();
   foreach(var windowState in new[]{WindowState.Normal,WindowState.Maximized})
   foreach(bool compact in new[]{false,true}) {
    window.WindowState=windowState;if(windowState==WindowState.Normal){window.Width=1100;window.Height=760;window.Left=60;window.Top=40;}await Task.Delay(500);
    if(sidebarHidden!=compact)ToggleSidebar();await Task.Delay(600);window.UpdateLayout();
    string tag=(compact?"compact":"expanded")+"-"+windowState;var scroller=Find<ScrollViewer>(compact?"MiniTabScroller":"ExpandedTabScroller");
    log.Add(tag+" "+Describe(scroller)+" viewport "+scroller.ViewportHeight+" extent "+scroller.ExtentHeight+" scrollable "+scroller.ScrollableHeight);
    log.Add(tag+" "+Describe(Find<Grid>("Sidebar"))+" | "+Describe(Find<Grid>("CompactRail"))+" | "+Describe(Find<Border>("ContentFrame"))+" | root "+Describe(Find<Grid>("Root"))+" | toggle "+Describe(Find<Button>("ToggleSidebar"))+" | minisearch "+Describe(Find<Button>("MiniSearch")));
    RegressionCheck(tag+" tab list scrolls",delegate {Require(scroller.ScrollableHeight>0,"Scroller is not constrained (extent "+scroller.ExtentHeight+", viewport "+scroller.ViewportHeight+")");});
    RegressionCheck(tag+" mouse wheel over a tab scrolls",delegate {
     scroller.ScrollToTop();window.UpdateLayout();var target=(UIElement)Find<StackPanel>(compact?"MiniTabs":"TabItems").Children[5];
     target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,0,-120){RoutedEvent=UIElement.MouseWheelEvent});window.UpdateLayout();
     Require(scroller.VerticalOffset>0,"Wheel did not scroll the tab list");
    });
    SaveWindowImage(Path.Combine(dir,tag+".png"));
    SaveScreenImage(Path.Combine(dir,tag+"-screen.png"),log);
   }
   window.WindowState=WindowState.Normal;window.Width=1100;window.Height=760;window.Left=60;window.Top=40;await Task.Delay(500);
   var hwnd=new System.Windows.Interop.WindowInteropHelper(window).Handle;TestRect before,during,after;GetWindowRect(hwnd,out before);
   var monitor=new TestMonitor {Size=System.Runtime.InteropServices.Marshal.SizeOf(typeof(TestMonitor))};GetMonitorInfo(MonitorFromWindow(hwnd,2),ref monitor);
   HandleKey(Key.F11,ModifierKeys.None);await Task.Delay(600);GetWindowRect(hwnd,out during);
   RegressionCheck("F11 covers the whole monitor without shell chrome",delegate {
    Require(during.Left==monitor.Monitor.Left&&during.Top==monitor.Monitor.Top&&during.Right==monitor.Monitor.Right&&during.Bottom==monitor.Monitor.Bottom,"Window "+during.Left+","+during.Top+" .. "+during.Right+","+during.Bottom+" is not the monitor");
    Require(!Find<Grid>("Sidebar").IsVisible&&Find<Border>("ContentFrame").TranslatePoint(new Point(0,0),Find<Grid>("Root"))==new Point(0,0),"Shell chrome still visible");
   });
   SaveScreenImage(Path.Combine(dir,"f11-screen.png"),log);
   HandleKey(Key.F11,ModifierKeys.None);await Task.Delay(600);GetWindowRect(hwnd,out after);
   RegressionCheck("F11 again restores the window",delegate {Require(after.Left==before.Left&&after.Top==before.Top&&after.Right==before.Right&&after.Bottom==before.Bottom&&Find<Grid>("Sidebar").IsVisible,"Restored to "+after.Left+","+after.Top+" .. "+after.Right+","+after.Bottom);});
   window.WindowState=WindowState.Maximized;await Task.Delay(500);
   Navigate("https://example.com/",false);await WaitFor(delegate {NavigationProgress p;return CurrentView()!=null&&navigations.TryGetValue(state.ActiveId,out p)&&!p.IsLoading&&CurrentView().CoreWebView2!=null;},25000);
   for(int i=0;i<60;i++){await Task.Delay(250);try {if(await CurrentView().CoreWebView2.ExecuteScriptAsync("location.host+document.readyState")=="\"example.comcomplete\"")break;}catch(Exception){}}await Task.Delay(300); // Navigate returns before the new page replaces the old one.
   var core=CurrentView().CoreWebView2;await core.ExecuteScriptAsync("var b=document.createElement('button');b.id='fs';b.textContent='fs';b.style='position:fixed;left:0;top:0;width:200px;height:200px;z-index:9';b.onclick=function(){window.fsClicks=(window.fsClicks||0)+1;document.documentElement.requestFullscreen().then(()=>window.fsOk=1,e=>window.fsErr=String(e));};document.body.appendChild(b);");
   foreach(var type in new[]{"mousePressed","mouseReleased"})log.Add("fullscreen click: "+await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent","{\"type\":\""+type+"\",\"x\":50,\"y\":50,\"button\":\"left\",\"clickCount\":1}"));
   await Task.Delay(800);GetWindowRect(hwnd,out during);log.Add("contains fullscreen element: "+CurrentView().CoreWebView2.ContainsFullScreenElement+" dom "+await CurrentView().CoreWebView2.ExecuteScriptAsync("String(!!document.fullscreenElement)"));log.Add("fs probe: "+await CurrentView().CoreWebView2.ExecuteScriptAsync("JSON.stringify({clicks:window.fsClicks,ok:window.fsOk,err:window.fsErr,button:!!document.getElementById(\u0027fs\u0027),at:document.elementFromPoint(50,50)&&document.elementFromPoint(50,50).id,focus:document.hasFocus(),url:location.href})"));
   RegressionCheck("page fullscreen (video) covers the monitor",delegate {Require(IsFullscreen&&during.Left==monitor.Monitor.Left&&during.Bottom==monitor.Monitor.Bottom&&!Find<Grid>("Sidebar").IsVisible,"Page fullscreen not applied: "+during.Left+","+during.Top+" .. "+during.Right+","+during.Bottom);});
   SaveScreenImage(Path.Combine(dir,"page-fullscreen-screen.png"),log);
   await CurrentView().CoreWebView2.ExecuteScriptAsync("document.exitFullscreen()");await Task.Delay(800);
   RegressionCheck("leaving page fullscreen restores maximized window",delegate {Require(!IsFullscreen&&window.WindowState==WindowState.Maximized&&Find<Grid>("Sidebar").IsVisible,"State after exit: "+window.WindowState+" fullscreen "+IsFullscreen);});
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
namespace Mica {
 partial class BrowserApp {
  // Renders off-screen: nothing is shown on the user's monitor.
  async Task RunAvatarTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"avatar");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;window.Width=1100;window.Height=760;window.ShowInTaskbar=false;
   WriteVaultAccount(new VaultAccount {token="fixture",id="80351110224678912",username="fixture"});RenderAvatar();
   try {await WaitFor(delegate {return Find<Button>("AvatarButton").Content is System.Windows.Shapes.Ellipse;},15000);}catch(Exception){}
   RegressionCheck("signed-in avatar shows in sidebar",delegate {Require(Find<Button>("AvatarButton").Content is System.Windows.Shapes.Ellipse,"Avatar image did not load: "+avatarError);});
   // The avatar opens the account card: name, profile, a Passwords button and the profiles to switch to.
   Find<Button>("AvatarButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));if(accountPopup!=null)accountPopup.StaysOpen=true; // The off-screen test window loses focus, which would light-dismiss it.
   try {await WaitFor(delegate {var p=accountPopup==null?null:VaultElements<Border>(accountPopup.Child).FirstOrDefault(e=>(e.Tag as string)=="account-picture");return p!=null&&p.Child is System.Windows.Shapes.Ellipse;},10000);}catch(Exception){}await Task.Delay(300);
   Func<string,FrameworkElement> accountPart=delegate(string tag) {return accountPopup==null?null:VaultElements<FrameworkElement>(accountPopup.Child).FirstOrDefault(e=>(e.Tag as string)==tag);};
   RegressionCheck("the avatar opens the account card, not the vault",delegate {
    Require(accountPopup!=null&&accountPopup.IsOpen,"no account menu");Require(vaultWindow==null,"vault opened");
    Require(((TextBlock)accountPart("account-name")).Text=="fixture","name "+((TextBlock)accountPart("account-name")).Text);
    Require(accountPart("account-passwords")!=null&&accountPart("account-profile:main")!=null&&accountPart("account-add")!=null,"parts missing");
   });
   // Clicking text (a Run, not a Visual) in a bubble walked the tree with VisualTreeHelper and crashed Mica (Save as).
   RegressionCheck("walking up from clicked text doesn't crash",delegate {var run=new System.Windows.Documents.Run("Save as");var block=new TextBlock(run);Require(TreeParent(run)==block,"parent "+TreeParent(run));});
   RegressionCheck("the account card shows the picture",delegate {var p=accountPart("account-picture") as Border;var e=p==null?null:p.Child as System.Windows.Shapes.Ellipse;Require(e!=null&&e.ActualWidth>40&&e.Fill is ImageBrush,"picture "+(p==null?"missing":p.Child==null?"empty":p.Child.GetType().Name)+" "+avatarError);});
   if(accountPopup!=null&&accountPopup.IsOpen){((FrameworkElement)accountPopup.Child).UpdateLayout();CaptureElement((FrameworkElement)accountPopup.Child,"account-menu.png");}
   ((Button)accountPart("account-passwords")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Task.Delay(800);
   RegressionCheck("Passwords on the card opens the vault",delegate {Require(!accountPopup.IsOpen&&vaultWindow!=null,"vault "+(vaultWindow!=null));});
   foreach(Window open in Application.Current.Windows.Cast<Window>().Where(w=>w!=window).ToList())open.Close();await Task.Delay(300);
   var probe=new StackPanel {Margin=new Thickness(260,120,0,0),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Background=Brush("Panel")};
   probe.Children.Add(new CheckBox {Content="Enable Windows Hello on this device",Margin=new Thickness(12)});probe.Children.Add(new CheckBox {Content="Include backgrounds",IsChecked=true,Margin=new Thickness(12)});probe.Children.Add(new CheckBox {Content="Disabled option",IsEnabled=false,Margin=new Thickness(12)});
   Find<Grid>("Root").Children.Add(probe);await Task.Delay(300);window.UpdateLayout();SaveWindowImage(Path.Combine(dir,"expanded.png"));
   ToggleSidebar();await Task.Delay(600);window.UpdateLayout();SaveWindowImage(Path.Combine(dir,"compact.png"));
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
namespace Mica {
 partial class BrowserApp {
  Window NewestDialog(Window except) {Window found=null;foreach(Window w in Application.Current.Windows)if(w!=window&&w!=except&&w.IsVisible)found=w;return found;}
  void SaveElement(FrameworkElement element,string path) {var bitmap=new RenderTargetBitmap((int)Math.Max(1,element.ActualWidth+40),(int)Math.Max(1,element.ActualHeight+40),96,96,PixelFormats.Pbgra32);bitmap.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);}
  void SaveAnyWindow(Window target,string path) {
   var element=(FrameworkElement)target.Content;element.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Max(1,element.ActualWidth),(int)Math.Max(1,element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(element);
   var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);
  }
  async Task<Window> WaitDialog(Window except,int ms) {Window found=null;try{await WaitFor(delegate {found=NewestDialog(except);return found!=null;},ms);}catch(Exception){}return found;}
  async Task ClickPage(CoreWebView2 core,string script) {
   await core.ExecuteScriptAsync("var b=document.getElementById('mica-test')||document.body.appendChild(document.createElement('button'));b.id='mica-test';b.style='position:fixed;left:0;top:0;width:120px;height:120px;z-index:99';b.onclick=function(){"+script+"};");
   foreach(var type in new[]{"mousePressed","mouseReleased"})await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent","{\"type\":\""+type+"\",\"x\":40,\"y\":40,\"button\":\"left\",\"clickCount\":1}");
  }
  // Off-screen: the window and every dialog it owns stay at -6000,-6000.
  async Task RunPopupsTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"popups");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;window.Width=1100;window.Height=760;
   Navigate("https://example.com/",false);await WaitFor(delegate {NavigationProgress p;return CurrentView()!=null&&CurrentView().CoreWebView2!=null&&navigations.TryGetValue(state.ActiveId,out p)&&!p.IsLoading;},25000);
   var core=CurrentView().CoreWebView2;Window dialog;
   int resizes=0;SizeChangedEventHandler count=delegate {resizes++;};CurrentView().SizeChanged+=count;
   ToggleSidebar();await Task.Delay(700);int collapse=resizes;resizes=0;ToggleSidebar();await Task.Delay(700);int expand=resizes;CurrentView().SizeChanged-=count;
   log.Add("webview resizes per sidebar toggle: collapse "+collapse+", expand "+expand);
   RegressionCheck("sidebar toggle resizes the page once",delegate {Require(collapse<=1&&expand<=1,"collapse "+collapse+" expand "+expand);Require(Math.Abs(CurrentView().ActualWidth-Find<Border>("ContentFrame").ActualWidth)<4,"Page width not restored: "+CurrentView().ActualWidth);});
   core.ExecuteScriptAsync("setTimeout(function(){window.__r=confirm('Delete this draft?');},50)");dialog=await WaitDialog(null,8000);
   RegressionCheck("confirm() uses a Mica dialog",delegate {Require(dialog!=null&&dialog.Title=="This page says"&&VaultElements<TextBlock>(dialog).Any(b=>b.Text=="example.com"),"No Mica confirm dialog");});
   if(dialog!=null){await Task.Delay(600);SaveAnyWindow(dialog,Path.Combine(dir,"confirm.png"));dialog.DialogResult=true;}
   await Task.Delay(400);var result=await core.ExecuteScriptAsync("String(window.__r)");RegressionCheck("confirm() returns the choice",delegate {Require(result=="\"true\"","confirm returned "+result);});
   await ClickPage(core,"navigator.geolocation.getCurrentPosition(function(){},function(){})");try{await WaitFor(delegate {return siteBubble!=null&&siteBubble.IsOpen;},8000);}catch(Exception){}
   RegressionCheck("location request shows Mica permission bubble",delegate {Require(siteBubble!=null&&siteBubble.IsOpen,"No permission bubble");});
   if(siteBubble!=null){await Task.Delay(500);SaveElement((FrameworkElement)siteBubble.Child,Path.Combine(dir,"permission.png"));((Action)siteBubble.Tag)();}
   await Task.Delay(300);await ClickPage(core,"location.href='discord:test'");try{await WaitFor(delegate {return siteBubble!=null&&siteBubble.IsOpen;},8000);}catch(Exception){}
   RegressionCheck("app link asks before opening",delegate {Require(siteBubble!=null&&siteBubble.IsOpen,"No app bubble");});
   if(siteBubble!=null){await Task.Delay(500);SaveElement((FrameworkElement)siteBubble.Child,Path.Combine(dir,"app-link.png"));((Action)siteBubble.Tag)();}
   await Task.Delay(300);
   await ClickPage(core,"window.__p=window.open('https://example.org/','signin','width=480,height=560')");
   await WaitFor(delegate {return popupWindows.Count>0;},10000).ContinueWith(t=>{});
   await Task.Delay(2500);
   RegressionCheck("sized window.open opens a Mica popup",delegate {Require(popupWindows.Count==1,"Popup count "+popupWindows.Count);});
   var opener=await core.ExecuteScriptAsync("String(!!(window.__p&&!window.__p.closed))");
   RegressionCheck("popup stays attached to its opener",delegate {Require(opener=="\"true\"","opener handle "+opener);});
   if(popupWindows.Count>0){var popup=popupWindows[0];log.Add("popup at "+popup.Left+","+popup.Top+" "+popup.Title);SaveAnyWindow(popup,Path.Combine(dir,"popup.png"));}
   await core.ExecuteScriptAsync("window.__p&&window.__p.close()");await Task.Delay(800);
   RegressionCheck("window.close() closes the popup",delegate {Require(popupWindows.Count==0,"Popup still open");});
   ShowSitePermissions();await Task.Delay(1500);
   if(sitePermissionsWindow!=null) {
    var content=sitePermissionsWindow.Content as DependencyObject;var segments=VaultElements<Grid>(content).Where(g=>g.Tag is Func<int>).ToList();
    var firstCard=VaultElements<Border>(content).FirstOrDefault();
    if(segments.Count>0) {
     var segment=segments[0];var buttons=VaultElements<Button>(segment).ToList();int before=((Func<int>)segment.Tag)();
     var pill=segment.Children.OfType<Border>().First();double startX=((TranslateTransform)pill.RenderTransform).X;
     buttons[2].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Task.Delay(40);
     double midX=((TranslateTransform)pill.RenderTransform).X;await Task.Delay(500);
     double endX=((TranslateTransform)pill.RenderTransform).X;double blockX=buttons[2].TranslatePoint(new Point(0,0),segment).X;
     RegressionCheck("changing a permission keeps the dialog in place",delegate {Require(VaultElements<Border>(sitePermissionsWindow.Content as DependencyObject).FirstOrDefault()==firstCard,"the dialog was rebuilt");Require(((Func<int>)segment.Tag)()==2,"choice "+((Func<int>)segment.Tag)());});
     RegressionCheck("the highlight slides to the new choice",delegate {Require(midX>startX+0.5&&midX<blockX-0.5&&Math.Abs(endX-blockX)<0.6,"x "+startX+" -> "+midX+" -> "+endX+" (target "+blockX+")");});
     SaveAnyWindow(sitePermissionsWindow,Path.Combine(dir,"site-permissions-block.png"));
     buttons[before<0?0:before].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Task.Delay(500);
    } else log.Add("no permission rows to try");
   }
   if(sitePermissionsWindow!=null){sitePermissionsWindow.Left=-6000;SaveAnyWindow(sitePermissionsWindow,Path.Combine(dir,"site-permissions.png"));sitePermissionsWindow.Close();}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
namespace Mica {
 partial class BrowserApp {
  [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="SetWindowPos")] static extern bool ResizeTestSetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
  async Task<double> MeasureResize(IntPtr hwnd,int steps) {
   SendMessage(hwnd,0x231,IntPtr.Zero,IntPtr.Zero);var clock=System.Diagnostics.Stopwatch.StartNew();
   for(int i=0;i<steps;i++) {
    int width=1000+(i%2==0?i*6:(steps-i)*6);
    ResizeTestSetWindowPos(hwnd,IntPtr.Zero,-6000,-6000,width,760,0x0004|0x0010);
    await window.Dispatcher.InvokeAsync(delegate {},System.Windows.Threading.DispatcherPriority.Background);
   }
   double result=clock.Elapsed.TotalMilliseconds/steps;SendMessage(hwnd,0x232,IntPtr.Zero,IntPtr.Zero);await Task.Delay(200);return result;
  }
  async Task RunResizeTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"resize");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;window.Width=1100;window.Height=760;await Task.Delay(500);
   var hwnd=new System.Windows.Interop.WindowInteropHelper(window).Handle;
   log.Add("render mode "+System.Windows.Media.RenderOptions.ProcessRenderMode);
   await MeasureResize(hwnd,10);
   log.Add("home page: "+(await MeasureResize(hwnd,80)).ToString("0.0")+" ms/frame");
   Navigate("https://example.com/",false);await WaitFor(delegate {NavigationProgress p;return CurrentView()!=null&&CurrentView().CoreWebView2!=null&&navigations.TryGetValue(state.ActiveId,out p)&&!p.IsLoading;},25000);await Task.Delay(500);
   await MeasureResize(hwnd,10);
   log.Add("web page: "+(await MeasureResize(hwnd,80)).ToString("0.0")+" ms/frame");
   Find<Grid>("WebHost").Visibility=Visibility.Hidden;await Task.Delay(300);log.Add("web page hidden: "+(await MeasureResize(hwnd,80)).ToString("0.0")+" ms/frame");Find<Grid>("WebHost").Visibility=Visibility.Visible;await Task.Delay(300);
   log.Add("web page again: "+(await MeasureResize(hwnd,80)).ToString("0.0")+" ms/frame");
   ToggleSidebar();await Task.Delay(600);
   log.Add("web page, compact sidebar: "+(await MeasureResize(hwnd,80)).ToString("0.0")+" ms/frame");
   window.UpdateLayout();RegressionCheck("page snaps to full size after resize",delegate {Require(Math.Abs(CurrentView().ActualWidth-((FrameworkElement)Find<Grid>("WebHost").Parent).ActualWidth)<1,"Page width "+CurrentView().ActualWidth);});
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}

namespace Mica {
 partial class BrowserApp {
  // Off-screen render of the new-tab page in both themes.
  async Task RunHomeTest() {
   string dir=Path.Combine(profile,"home");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;window.Width=1280;window.Height=820;
   foreach(var site in new[]{"https://github.com/","https://www.youtube.com/","https://mail.google.com/","https://www.wikipedia.org/","https://discord.com/","https://www.reddit.com/","https://news.ycombinator.com/","https://www.figma.com/"})state.Visit(site,new Uri(site).Host);
   state.RecordClosed(new PageEntry {Url="https://developer.mozilla.org/en-US/docs/Web",Title="MDN Web Docs"});state.RecordClosed(new PageEntry {Url="https://www.youtube.com/watch?v=x",Title="Lo-fi beats to code to"});
   RenderHome();await Task.Delay(600);window.UpdateLayout();SaveWindowImage(Path.Combine(dir,"light.png"));
   state.Dark=true;ApplyTheme();await Task.Delay(600);window.UpdateLayout();SaveWindowImage(Path.Combine(dir,"dark.png"));
   window.Close();
  }
 }
}
namespace Mica {
 partial class BrowserApp {
  // Off-screen: downloads two files from a real HTTPS page and records what
  // Windows/WebView2 stamp on them (Mark of the Web) and how risky types fare.
  async Task RunMotwTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"motw");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;
   Navigate("https://example.com/",false);await WaitFor(delegate {NavigationProgress p;return CurrentView()!=null&&CurrentView().CoreWebView2!=null&&navigations.TryGetValue(state.ActiveId,out p)&&!p.IsLoading;},25000);
   var core=CurrentView().CoreWebView2;
   log.Add("SmartScreen reputation checking setting: "+core.Settings.IsReputationCheckingRequired);
   foreach(var name in new[]{"notes.txt","setup.bat"}) {
    int before=downloads.Count;
    await ClickPage(core,"var a=document.createElement('a');a.href=URL.createObjectURL(new Blob(['echo mica test'],{type:'application/octet-stream'}));a.download='"+name+"';document.body.appendChild(a);a.click();");
    if(Environment.GetEnvironmentVariable("MICA_TEST_DOWNLOAD_PROMPT")=="1") {
     try {await WaitFor(delegate {return siteBubble!=null&&siteBubble.IsOpen;},10000);}catch(Exception){}
     if(siteBubble!=null){await Task.Delay(500);SaveElement((FrameworkElement)siteBubble.Child,Path.Combine(dir,name+"-card.png"));var save=VaultElements<Button>(siteBubble.Child).FirstOrDefault(b=>b.Tag as string=="save");log.Add(name+": card shown");if(save!=null)save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
     else log.Add(name+": NO CARD");
    }
    try {await WaitFor(delegate {return downloads.Count>before&&downloads[0].Status!="Downloading";},20000);}catch(Exception){}
    var entry=downloads.Count>before?downloads[0]:null;
    if(entry==null){log.Add(name+": no download started");continue;}
    string zone="none";try {var stream=entry.FilePath+":Zone.Identifier";if(File.Exists(entry.FilePath)){var text=ReadAlternateStream(entry.FilePath);if(text!=null)zone=text.Replace("\r\n"," ").Trim();}}catch(Exception e){zone="error "+e.Message;}
    log.Add(name+": status "+entry.Status+" | "+entry.Detail+" | exists "+File.Exists(entry.FilePath)+" | Zone.Identifier: "+zone);
   }
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
  [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)] static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
  static string ReadAlternateStream(string path) {
   using(var handle=CreateFileW(path+":Zone.Identifier",0x80000000,1,IntPtr.Zero,3,0,IntPtr.Zero)) {if(handle.IsInvalid)return null;using(var stream=new FileStream(handle,FileAccess.Read))using(var reader=new StreamReader(stream))return reader.ReadToEnd();}
  }
 }
}
namespace Mica {
 partial class BrowserApp {
  [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,System.Text.StringBuilder text,int max);
  List<string> EngineWindows(uint pid) {var found=new List<string>();EnumWindows(delegate(IntPtr h,IntPtr d){uint owner;GetWindowThreadProcessId(h,out owner);if(owner==pid&&IsWindowVisible(h)){var t=new System.Text.StringBuilder(256);GetWindowText(h,t,256);found.Add(h+" '"+t+"'");}return true;},IntPtr.Zero);return found;}
  // Off-screen experiment: does an extension's chrome.tabs.create reach Mica?
  async Task RunExtensionWindowTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"extwin");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;
   Navigate("https://example.com/",false);await WaitFor(delegate {NavigationProgress p;return CurrentView()!=null&&CurrentView().CoreWebView2!=null&&navigations.TryGetValue(state.ActiveId,out p)&&!p.IsLoading;},25000);
   var core=CurrentView().CoreWebView2;uint pid=core.BrowserProcessId;
   core.GetDevToolsProtocolEventReceiver("Target.targetCreated").DevToolsProtocolEventReceived+=delegate(object s,CoreWebView2DevToolsProtocolEventReceivedEventArgs e){log.Add("targetCreated "+e.ParameterObjectAsJson);};
   log.Add("discover: "+await core.CallDevToolsProtocolMethodAsync("Target.setDiscoverTargets","{\"discover\":true}"));
   log.Add("windows before: "+string.Join(", ",EngineWindows(pid)));
   string ext=Path.Combine(dir,"opener");Directory.CreateDirectory(ext);
   File.WriteAllText(Path.Combine(ext,"manifest.json"),"{\"manifest_version\":3,\"name\":\"Mica window probe\",\"version\":\"1.0\",\"background\":{\"service_worker\":\"bg.js\"},\"permissions\":[\"tabs\"]}");
   File.WriteAllText(Path.Combine(ext,"page.html"),"<!doctype html><title>Probe options</title><h1>Options</h1>");File.WriteAllText(Path.Combine(ext,"bg.js"),"chrome.runtime.onInstalled.addListener(()=>{chrome.tabs.create({url:'https://example.org/'});setTimeout(()=>chrome.tabs.create({url:chrome.runtime.getURL('page.html')}),1500);});");
   var browserProfile=await EnsureExtensionProfile();var installed=await browserProfile.AddBrowserExtensionAsync(ext);log.Add("installed "+installed.Id);
   await Task.Delay(9000);
   var targets=await core.CallDevToolsProtocolMethodAsync("Target.getTargets","{}");
   RegressionCheck("extension-opened website becomes a Mica tab",delegate {Require(state.Tabs.Any(t=>t.Url.StartsWith("https://example.org")),"No Mica tab for example.org");});
   RegressionCheck("extension page opens in a Mica popup",delegate {Require(popupWindows.Count==1,"Mica popups: "+popupWindows.Count);});
   int engineOrg=System.Text.RegularExpressions.Regex.Matches(targets,@"example\.org").Count;
   log.Add("page.html targets: "+System.Text.RegularExpressions.Regex.Matches(targets,"page.html").Count+" status: "+Find<TextBlock>("StatusText").Text);
   log.Add("engine page targets for example.org: "+engineOrg+" (1 = only Mica's own tab)");
   if(popupWindows.Count>0){await Task.Delay(500);SaveAnyWindow(popupWindows[0],Path.Combine(dir,"extension-page.png"));}
   try{await installed.RemoveAsync();}catch(Exception e){log.Add("remove: "+e.Message);}
   // Goodbye pages: freeze every background tab first, as the memory saver does after 15 s.
   foreach(var w in popupWindows.ToArray())w.Close();
   Navigate("https://example.com/?front",true);
   await Task.Delay(1500);BalanceMemory(true);await Task.Delay(2500);
   log.Add("frozen tabs: "+views.Values.Count(v=>LiveCore(v)!=null&&LiveCore(v).IsSuspended)+" watcher asleep: "+(targetWatcher==null?"none":WatcherAsleep().ToString()));
   string bye=Path.Combine(dir,"goodbye");Directory.CreateDirectory(bye);
   File.WriteAllText(Path.Combine(bye,"manifest.json"),"{\"manifest_version\":3,\"name\":\"Mica goodbye probe\",\"version\":\"1.0\",\"background\":{\"service_worker\":\"bg.js\"}}");
   File.WriteAllText(Path.Combine(bye,"bg.js"),"chrome.runtime.setUninstallURL('https://example.net/goodluck');");
   var leaving=await browserProfile.AddBrowserExtensionAsync(bye);await Task.Delay(2500);
   var windowsBefore=EngineWindows(pid).Count;
   // Simulate the event being missed: only the engine-window safety net can catch this one.
   StopTargetWatcher(targetWatcher);
   try{await leaving.RemoveAsync();}catch(Exception e){log.Add("remove goodbye: "+e.Message);}
   await Task.Delay(9000);
   RegressionCheck("uninstall page opens as a Mica tab while tabs are frozen",delegate {Require(state.Tabs.Any(t=>t.Url.StartsWith("https://example.net")),"No Mica tab for example.net; engine windows "+windowsBefore+" -> "+string.Join(", ",EngineWindows(pid)));});
   log.Add("engine window sweeps: "+engineSweeps);
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
