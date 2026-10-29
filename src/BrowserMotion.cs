using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Mica {
 // A double-valued layout property lets WPF interpolate the sidebar column.
 // There are no timers or continuous render handlers when motion is idle.
 public sealed class MotionProperties : DependencyObject {
  public static readonly DependencyProperty DevToolsWidthProperty=DependencyProperty.RegisterAttached(
   "DevToolsWidth",typeof(double),typeof(MotionProperties),new PropertyMetadata(0.0,delegate(DependencyObject target,DependencyPropertyChangedEventArgs e) {
    var grid=target as Grid;if(grid!=null&&grid.ColumnDefinitions.Count>2)grid.ColumnDefinitions[2].Width=new GridLength(Math.Max(0,(double)e.NewValue));
   }));
  public static readonly DependencyProperty SidebarWidthProperty=DependencyProperty.RegisterAttached(
   "SidebarWidth",typeof(double),typeof(MotionProperties),new PropertyMetadata(238.0,delegate(DependencyObject target,DependencyPropertyChangedEventArgs e) {
    var grid=target as Grid;if(grid!=null&&grid.ColumnDefinitions.Count>0)grid.ColumnDefinitions[0].Width=new GridLength(Math.Max(0,(double)e.NewValue)*(double)grid.GetValue(SidebarScaleProperty));
   }));
  // Large text: the sidebar column grows with the text size (its width above stays in unscaled units).
  public static readonly DependencyProperty SidebarScaleProperty=DependencyProperty.RegisterAttached(
   "SidebarScale",typeof(double),typeof(MotionProperties),new PropertyMetadata(1.0,delegate(DependencyObject target,DependencyPropertyChangedEventArgs e) {
    var grid=target as Grid;if(grid!=null&&grid.ColumnDefinitions.Count>0)grid.ColumnDefinitions[0].Width=new GridLength(Math.Max(0,(double)grid.GetValue(SidebarWidthProperty))*(double)e.NewValue);
   }));
 }
 partial class BrowserApp {
  int sidebarTransition,spotlightTransition;
  bool spotlightClosing;
  bool reloadTurning;
  int reloadTransition;
  void UpdateReloadMotion() {
   NavigationProgress progress;bool loading=state.ActiveId!=null&&navigations.TryGetValue(state.ActiveId,out progress)&&progress.IsLoading;
   SetReloadMotion(loading);
  }
  void SetReloadMotion(bool loading) {
   if(reloadTurning==loading)return;reloadTurning=loading;int transition=++reloadTransition;
   var rotation=(RotateTransform)Find<TextBlock>("ReloadGlyph").RenderTransform;
   double angle=rotation.Angle%360;rotation.BeginAnimation(RotateTransform.AngleProperty,null);rotation.Angle=angle;
   if(loading&&SystemParameters.ClientAreaAnimation) {
    rotation.BeginAnimation(RotateTransform.AngleProperty,new DoubleAnimation(angle,angle+360,TimeSpan.FromMilliseconds(900)) {RepeatBehavior=RepeatBehavior.Forever});
   } else Tween(rotation,RotateTransform.AngleProperty,loading?0:360,180,delegate {if(transition==reloadTransition){rotation.BeginAnimation(RotateTransform.AngleProperty,null);rotation.Angle=0;}});
  }
  void InitializeMotion() {
   EventManager.RegisterClassHandler(typeof(Button),UIElement.MouseEnterEvent,new MouseEventHandler(delegate(object sender,MouseEventArgs e) {AnimateButton((Button)sender,1.025);}));
   EventManager.RegisterClassHandler(typeof(Button),UIElement.MouseLeaveEvent,new MouseEventHandler(delegate(object sender,MouseEventArgs e) {AnimateButton((Button)sender,1);}));
   EventManager.RegisterClassHandler(typeof(Button),UIElement.PreviewMouseLeftButtonDownEvent,new MouseButtonEventHandler(delegate(object sender,MouseButtonEventArgs e) {AnimateButton((Button)sender,0.96);}));
   EventManager.RegisterClassHandler(typeof(Button),UIElement.PreviewMouseLeftButtonUpEvent,new MouseButtonEventHandler(delegate(object sender,MouseButtonEventArgs e) {AnimateButton((Button)sender,((Button)sender).IsMouseOver?1.025:1);}));
   window.SourceInitialized+=delegate {
    var source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
    WindowBounds.EnableNativeFrame(source.Handle);
    WindowBounds.EnableUpdateShutdown(source.Handle);
    WindowsIntegration.SetWindow(source.Handle,System.Reflection.Assembly.GetExecutingAssembly().Location);
    source.AddHook(delegate(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled) {
     if((uint)message==WindowBounds.UpdateMessage){handled=true;window.Dispatcher.BeginInvoke(new Action(Program.CloseForUpdate));return IntPtr.Zero;}
     // Keep WPF's minimum-size processing after supplying monitor work bounds.
     if(message==0x24)WindowBounds.ConstrainMaximize(hwnd,lParam);
     if(message==0x231){Tween(Find<Border>("ResizeGlow"),UIElement.OpacityProperty,0.8,160,null);BeginLiveResize();}
     if(message==0x232){Tween(Find<Border>("ResizeGlow"),UIElement.OpacityProperty,0,220,null);EndLiveResize();}
     return IntPtr.Zero;
    });
   };
   Find<Border>("ContentFrame").SizeChanged+=delegate(object sender,SizeChangedEventArgs e) {
    double width=e.NewSize.Width;
    int ms=liveResize?0:160;
    Tween(Find<TextBlock>("HomeHeadline"),TextBlock.FontSizeProperty,Math.Max(56,Math.Min(76,(width-700)/12+56)),ms,null);
    Tween(Find<Viewbox>("HomeArtwork"),FrameworkElement.WidthProperty,Math.Max(260,Math.Min(340,width/3.2)),ms,null);
   };
  }
  void AnimateButton(Button button,double scale) {
   // Tab backgrounds fill the row: scaling them clips against the sidebar and moves the selection edge.
   if(button.Tag is string&&Grid.GetColumnSpan(button)==2)return;
   if(button.Template==null)return;var surface=button.Template.FindName("Surface",button) as Border;if(surface==null)return;
   var transform=surface.RenderTransform as ScaleTransform;if(transform==null){transform=new ScaleTransform(1,1);surface.RenderTransform=transform;surface.RenderTransformOrigin=new Point(0.5,0.5);}
   Tween(transform,ScaleTransform.ScaleXProperty,scale,140,null);Tween(transform,ScaleTransform.ScaleYProperty,scale,140,null);
  }
  // Fading a subtree makes WPF re-render it (and any shadow effect inside) every
  // frame. Caching it as a bitmap for the animation keeps the work on the GPU.
  static readonly BitmapCache motionCache=FrozenCache();
  static BitmapCache FrozenCache() {var cache=new BitmapCache {EnableClearType=true,SnapsToDevicePixels=true};cache.Freeze();return cache;}
  void AnimateArrival(UIElement element) {
   var transform=new TranslateTransform(0,6);element.RenderTransform=transform;element.Opacity=0;
   bool cached=SystemParameters.ClientAreaAnimation&&(element.CacheMode==null||element.CacheMode==motionCache);if(cached)element.CacheMode=motionCache;
   Tween(element,UIElement.OpacityProperty,1,170,null);Tween(transform,TranslateTransform.YProperty,0,210,delegate {if(cached&&element.CacheMode==motionCache)element.CacheMode=null;});
  }
  // A popup's first frame (creating its window and drawing it the first time) takes longer than one frame,
  // so its card waits, invisible, until that frame is on screen and then animates from a warm start.
  void AnimatePopupArrival(UIElement card) {
   card.Opacity=0;card.RenderTransform=new TranslateTransform(0,6);
   if(!SystemParameters.ClientAreaAnimation){AnimateArrival(card);return;}
   int frames=0;EventHandler tick=null;
   tick=delegate {if(++frames<2)return;CompositionTarget.Rendering-=tick;AnimateArrival(card);};
   CompositionTarget.Rendering+=tick;
  }
  void AnimateHome() {if(Find<ScrollViewer>("Home").IsVisible)AnimateArrival(Find<ScrollViewer>("Home"));}
  static void Tween(IAnimatable target,DependencyProperty property,double to,int milliseconds,Action complete) {
   var dependency=(DependencyObject)target;
   double from=(double)dependency.GetValue(property);
   target.BeginAnimation(property,null);
   dependency.SetValue(property,to);
   if(!SystemParameters.ClientAreaAnimation||Math.Abs(from-to)<0.0001) {if(complete!=null)complete();return;}
   var animation=new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(milliseconds)) {
    EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop
   };
   if(complete!=null)animation.Completed+=delegate {complete();};
   target.BeginAnimation(property,animation,HandoffBehavior.SnapshotAndReplace);
  }
  // Home-page size tweens are skipped while the user drags a window edge.
  bool liveResize;
  void BeginLiveResize() {liveResize=true;}
  void EndLiveResize() {liveResize=false;}
  void AnimateSidebar() {
   int transition=++sidebarTransition;
   var sidebar=Find<Grid>("Sidebar");
   var rail=Find<Grid>("CompactRail");
   sidebar.Margin=sidebarHidden?new Thickness(0):new Thickness(9,0,15,0);
   Find<Button>("ToggleSidebar").HorizontalAlignment=sidebarHidden?HorizontalAlignment.Center:HorizontalAlignment.Right;
   // Each panel keeps its full width while the column slides (the sidebar clips it). Otherwise every frame
   // re-measures every tab row's title, which with many tabs costs more than a frame.
   const double FullWidth=238-9-15,RailWidth=64;
   foreach(UIElement child in sidebar.Children) {
    if(child==Find<Grid>("BrandBar"))continue;
    bool visible=child==rail?sidebarHidden:!sidebarHidden;
    var panel=child as FrameworkElement;if(panel!=null&&SystemParameters.ClientAreaAnimation){panel.Width=child==rail?RailWidth:FullWidth;panel.HorizontalAlignment=HorizontalAlignment.Left;}
    child.Visibility=Visibility.Visible;child.IsHitTestVisible=visible;
    var faded=child;faded.CacheMode=motionCache;
    Tween(child,UIElement.OpacityProperty,visible?1:0,160,delegate {if(faded.CacheMode==motionCache)faded.CacheMode=null;});
   }
   // The sidebar itself keeps one width while its column slides, and the toolbar sits at its final width on the
   // right, as the page does below: neither is laid out again until the slide ends. Measured: a slide frame's
   // layout went from ~27 ms (the tab list re-measured every frame) to a few ms.
   var toolbar=Find<FrameworkElement>("ToolbarBar");bool pinned=SystemParameters.ClientAreaAnimation;
   if(pinned) {
    sidebar.Width=Math.Max(0,238-sidebar.Margin.Left-sidebar.Margin.Right);sidebar.HorizontalAlignment=HorizontalAlignment.Left;
    if(toolbar.IsVisible&&toolbar.ActualWidth>0){toolbar.Width=Math.Max(0,toolbar.ActualWidth+(sidebarHidden?238-64:64-238));toolbar.HorizontalAlignment=HorizontalAlignment.Right;}
   }
   var wordmark=Find<TextBlock>("BrandWordmark");wordmark.Visibility=Visibility.Visible;
   Tween(wordmark,UIElement.OpacityProperty,sidebarHidden?0:1,120,null);
   // Resizing WebView2 every animation frame forces a page reflow per frame.
   // Pin the page to its final (or current) width at the right edge and let
   // the sidebar slide beside it; the page then resizes exactly once.
   var host=Find<Grid>("WebHost");
   if(host.IsVisible&&SystemParameters.ClientAreaAnimation) {
    double current=host.ActualWidth,target=current+(sidebarHidden?238-64:64-238)*UiScale;
    host.HorizontalAlignment=HorizontalAlignment.Right;host.Width=Math.Max(0,Math.Min(current,target));
   }
   Tween(Find<Grid>("AppLayout"),MotionProperties.SidebarWidthProperty,sidebarHidden?64:238,220,delegate {
    if(transition!=sidebarTransition)return;
    host.Width=double.NaN;host.HorizontalAlignment=HorizontalAlignment.Stretch;
    sidebar.Width=double.NaN;sidebar.ClearValue(FrameworkElement.HorizontalAlignmentProperty);toolbar.Width=double.NaN;toolbar.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
    foreach(UIElement child in sidebar.Children)if(child!=Find<Grid>("BrandBar")){child.Visibility=(child==rail?sidebarHidden:!sidebarHidden)?Visibility.Visible:Visibility.Collapsed;var panel=child as FrameworkElement;if(panel!=null){panel.Width=double.NaN;panel.ClearValue(FrameworkElement.HorizontalAlignmentProperty);}}
    wordmark.Visibility=sidebarHidden?Visibility.Collapsed:Visibility.Visible;
   });
  }
  TranslateTransform SpotlightTranslation() {
   var card=Find<Grid>("SpotlightCard");var transform=card.RenderTransform as TranslateTransform;
   if(transform==null){transform=new TranslateTransform();card.RenderTransform=transform;}
   return transform;
  }
  void AnimateSpotlightOpen(bool wasVisible) {
   ++spotlightTransition;spotlightClosing=false;
   var overlay=Find<Grid>("SpotlightOverlay");var transform=SpotlightTranslation();
   if(!wasVisible){overlay.BeginAnimation(UIElement.OpacityProperty,null);overlay.Opacity=0;transform.BeginAnimation(TranslateTransform.YProperty,null);transform.Y=10;}
   Tween(overlay,UIElement.OpacityProperty,1,180,null);
   Tween(transform,TranslateTransform.YProperty,0,200,null);
  }
  void AnimateSpotlightClose(Action complete) {
   if(spotlightClosing)return;
   spotlightClosing=true;int transition=++spotlightTransition;
   Tween(SpotlightTranslation(),TranslateTransform.YProperty,6,130,null);
   Tween(Find<Grid>("SpotlightOverlay"),UIElement.OpacityProperty,0,130,delegate {
    if(transition!=spotlightTransition)return;
    spotlightClosing=false;complete();
   });
  }
 }
}
