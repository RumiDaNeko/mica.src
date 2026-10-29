using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // Fullscreen covers the whole monitor (taskbar included) and hides all shell
 // chrome. Page requests (video players) and F11 share one presentation state.
 static class FullscreenBounds {
  [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)] struct MonitorInfo {public int Size;public Rect Monitor,Work;public uint Flags;}
  [StructLayout(LayoutKind.Sequential)] struct Point {public int X,Y;}
  [StructLayout(LayoutKind.Sequential)] internal struct Placement {public int Length,Flags,ShowCmd;Point Min,Max;Rect Normal;}
  [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
  [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
  [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hwnd,ref Placement placement);
  [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr hwnd,ref Placement placement);
  [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
  internal static bool KeepOffscreen;
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
  internal static Placement Enter(IntPtr hwnd) {
   var saved=new Placement {Length=Marshal.SizeOf(typeof(Placement))};GetWindowPlacement(hwnd,ref saved);
   var monitor=new MonitorInfo {Size=Marshal.SizeOf(typeof(MonitorInfo))};GetMonitorInfo(MonitorFromWindow(hwnd,2),ref monitor);
   ShowWindow(hwnd,9);
   // Tests keep the window off-screen: monitor-sized, but where it already is.
   if(KeepOffscreen){Rect now;GetWindowRect(hwnd,out now);monitor.Monitor.Right+=now.Left-monitor.Monitor.Left;monitor.Monitor.Bottom+=now.Top-monitor.Monitor.Top;monitor.Monitor.Left=now.Left;monitor.Monitor.Top=now.Top;}
   SetWindowPos(hwnd,IntPtr.Zero,monitor.Monitor.Left,monitor.Monitor.Top,monitor.Monitor.Right-monitor.Monitor.Left,monitor.Monitor.Bottom-monitor.Monitor.Top,0x0020|0x0200|0x0004);
   return saved;
  }
  internal static void Leave(IntPtr hwnd,Placement saved) {
   if(saved.ShowCmd==2)saved.ShowCmd=1;
   SetWindowPlacement(hwnd,ref saved);SetWindowPos(hwnd,IntPtr.Zero,0,0,0,0,0x0020|0x0001|0x0002|0x0004|0x0200);
  }
 }
 partial class BrowserApp {
  bool pageFullscreen,userFullscreen,fullscreenApplied;
  FullscreenBounds.Placement fullscreenPlacement;
  WindowChrome fullscreenChrome;
  internal bool IsFullscreen {get {return fullscreenApplied;}}
  // The engine reports fullscreen to the page before Mica has grown the window, so
  // "fullscreenchange" (and requestFullscreen's promise) arrive while the page is
  // still window-sized. Chrome resizes first. Hold both until the viewport matches
  // the screen (or 800 ms), so pages that verify fullscreen (exams) see what Chrome shows.
  internal const string FullscreenShim=@"(()=>{if(window.__micaFullscreen)return;window.__micaFullscreen=1;
const active=()=>document.fullscreenElement||document.webkitFullscreenElement;
const fits=()=>Math.abs(innerWidth-screen.width)<=2&&Math.abs(innerHeight-screen.height)<=2;
const settle=()=>new Promise(done=>{if(!active()||fits())return done();let over=false;const finish=()=>{if(over)return;over=true;removeEventListener('resize',check);done();};const check=()=>{if(fits())finish();};addEventListener('resize',check);setTimeout(finish,800);});
let replaying=false;
for(const name of ['fullscreenchange','webkitfullscreenchange'])addEventListener(name,e=>{if(replaying||!active()||fits())return;e.stopImmediatePropagation();const target=e.target||document;settle().then(()=>{replaying=true;try{target.dispatchEvent(new Event(name,{bubbles:true,composed:true}));}finally{replaying=false;}});},true);
for(const key of ['requestFullscreen','webkitRequestFullscreen']){const original=Element.prototype[key];if(!original)continue;const wrapped=function(){const result=original.apply(this,arguments);return result&&result.then?result.then(()=>settle()):result;};Object.defineProperty(wrapped,'toString',{value:()=>original.toString()});Object.defineProperty(Element.prototype,key,{value:wrapped,writable:true,configurable:true});}
})();";
  void WatchFullscreen(string id,WebView2 view) {
   AddPageScript(view.CoreWebView2,FullscreenShim);AddPageScript(view.CoreWebView2,ScrollbarScript);
   view.CoreWebView2.ContainsFullScreenElementChanged+=delegate {
    if(!IsCurrent(id,view)||state.ActiveId!=id)return;
    pageFullscreen=view.CoreWebView2.ContainsFullScreenElement;ApplyFullscreen();
   };
  }
  void ToggleUserFullscreen() {
   if(pageFullscreen) {
    // Only if the page really is fullscreen; a closed or backgrounded tab can leave this set.
    var view=CurrentView();if(view!=null&&view.CoreWebView2!=null&&view.CoreWebView2.ContainsFullScreenElement){ExitPageFullscreen();return;}
    pageFullscreen=false;if(fullscreenApplied){userFullscreen=false;ApplyFullscreen();return;}
   }
   userFullscreen=!userFullscreen;ApplyFullscreen();
  }
  async void ExitPageFullscreen() {
   pageFullscreen=false;ApplyFullscreen();
   var view=CurrentView();if(view!=null&&view.CoreWebView2!=null)try {await view.CoreWebView2.ExecuteScriptAsync("document.fullscreenElement&&document.exitFullscreen()");}catch(Exception){}
  }
  void ScrollActiveTabIntoView() {
   string id=state.ActiveId;
   window.Dispatcher.BeginInvoke(new Action(delegate {
    foreach(var element in TabListElements().Concat(Find<StackPanel>("MiniTabs").Children.OfType<FrameworkElement>())) {
     var button=element as Button??(element is Grid?((Grid)element).Children[0] as Button:null);
     if(button!=null&&(string)button.Tag==id&&element.IsVisible)element.BringIntoView();
    }   }),System.Windows.Threading.DispatcherPriority.Loaded);
  }
  // Switching away from a fullscreen page ends page fullscreen; F11 persists.
  // With the sidebar and address bar gone a page could draw a fake Mica, so its fullscreen always starts
  // by saying whose it is. A popup, since nothing in the window can sit above the page.
  System.Windows.Controls.Primitives.Popup fullscreenNotice;
  void ShowFullscreenNotice(string host) {
   if(fullscreenNotice!=null)fullscreenNotice.IsOpen=false;
   var text=new TextBlock {Text=host+" is full screen  ·  Press Esc to exit",FontSize=13,Foreground=Brushes.White,Margin=new Thickness(18,10,18,10)};
   var card=new Border {Child=text,CornerRadius=new CornerRadius(12),Background=new SolidColorBrush(Color.FromArgb(0xE6,0x20,0x20,0x24))};
   var notice=new System.Windows.Controls.Primitives.Popup {Child=card,AllowsTransparency=true,PlacementTarget=window,Placement=System.Windows.Controls.Primitives.PlacementMode.Relative,IsHitTestVisible=false,StaysOpen=true};
   card.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));notice.HorizontalOffset=Math.Max(0,(window.ActualWidth-card.DesiredSize.Width)/2);notice.VerticalOffset=28;
   fullscreenNotice=notice;notice.IsOpen=true;AnimateArrival(card);
   var timer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(3.5)};timer.Tick+=delegate {timer.Stop();notice.IsOpen=false;if(fullscreenNotice==notice)fullscreenNotice=null;};timer.Start();
  }
  void LeavePageFullscreenForTabSwitch() {if(pageFullscreen)ExitPageFullscreen();}
  bool fullscreenFromPage;
  void ApplyFullscreen() {
   bool on=pageFullscreen||userFullscreen;if(on==fullscreenApplied)return;fullscreenApplied=on;
   var hwnd=new WindowInteropHelper(window).Handle;
   var layout=Find<Grid>("AppLayout");var frame=Find<Border>("ContentFrame");var rows=((Grid)frame.Child).RowDefinitions;
   window.Dispatcher.BeginInvoke(new Action(RefreshIsland));
   if(on) {
    CloseMenus();CloseContextMenu();
    fullscreenChrome=WindowChrome.GetWindowChrome(window);
    WindowChrome.SetWindowChrome(window,new WindowChrome {CaptionHeight=0,ResizeBorderThickness=new Thickness(0),CornerRadius=new CornerRadius(0),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});
    layout.Margin=new Thickness(0);Find<Grid>("Sidebar").Visibility=Visibility.Collapsed;
    layout.BeginAnimation(MotionProperties.SidebarWidthProperty,null);layout.ColumnDefinitions[0].Width=new GridLength(0);
    frame.CornerRadius=new CornerRadius(0);frame.BorderThickness=new Thickness(0);rows[0].Height=new GridLength(0);rows[2].Height=new GridLength(0);
    fullscreenPlacement=FullscreenBounds.Enter(hwnd);
    if(userFullscreen&&!pageFullscreen)Status("Press F11 to leave fullscreen.");
    if(pageFullscreen&&Active!=null)ShowFullscreenNotice(OriginLabel(Active.Url));
   } else {
    FullscreenBounds.Leave(hwnd,fullscreenPlacement);
    WindowChrome.SetWindowChrome(window,fullscreenChrome);
    layout.Margin=new Thickness(7);Find<Grid>("Sidebar").Visibility=Visibility.Visible;
    layout.ColumnDefinitions[0].Width=new GridLength((double)layout.GetValue(MotionProperties.SidebarWidthProperty)*(double)layout.GetValue(MotionProperties.SidebarScaleProperty));
    frame.CornerRadius=new CornerRadius(12);frame.BorderThickness=new Thickness(1);rows[0].Height=new GridLength(44);rows[2].Height=new GridLength(0);
   }
   LayoutDevTools();LayoutAi();
   // In split view the fullscreen page takes the whole window; the other side comes back after.
   if(SplitOpen){LayoutSplit();if(!on)foreach(var pane in splitPanes){WebView2 other;if(views.TryGetValue(pane,out other))other.Visibility=Visibility.Visible;}}
   // Re-focusing a page that already has focus makes it see blur+focus, which exam
   // pages treat as leaving the test. Only move focus when it is elsewhere.
   // The page asked for fullscreen, so focus is already in it: leave it alone.
   var view=CurrentView();if(view!=null&&!pageFullscreen&&!fullscreenFromPage&&!view.IsKeyboardFocusWithin)view.Focus();
   fullscreenFromPage=pageFullscreen;
  }
 }
}
