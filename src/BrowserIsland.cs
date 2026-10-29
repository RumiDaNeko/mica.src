using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Mica {
 // The status bar became a small "island" in the page's bottom-right corner: it shows
 // Mica's memory use, widens to say something worth saying, and slips out of the way
 // when the pointer comes near (page content in that corner stays reachable).
 // It's an owned popup because web pages are native windows WPF can't draw over.
 partial class BrowserApp {
  Popup island;Border islandCard;TextBlock islandMemory,islandMessage;Border islandDivider;
  DispatcherTimer islandHide,islandWatch;bool islandAway;
  internal string islandShowing;

  // Everyday navigation chatter isn't news; it stays in the (hidden) status text only. A download shows as its own row.
  static readonly string[] RoutineStatus={"Finding your way…","Ready when you are.","All yours. Take your time.","Your library, kept on this device.","Loading stopped. Available page content is kept."};
  static bool IsRoutine(string text) {return string.IsNullOrEmpty(text)||RoutineStatus.Contains(text)||text.StartsWith("Page couldn't load",StringComparison.Ordinal)||text.StartsWith("Downloading ",StringComparison.Ordinal);}

  void BuildIsland() {
   if(island!=null)return;
   islandMemory=new TextBlock {FontSize=11,VerticalAlignment=VerticalAlignment.Center,FontFamily=new FontFamily("Segoe UI Variable Text, Segoe UI")};islandMemory.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
   islandMessage=new TextBlock {FontSize=11,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=0,Opacity=0};islandMessage.SetResourceReference(TextBlock.ForegroundProperty,"Ink");
   islandDivider=new Border {Width=1,Height=12,Margin=new Thickness(0,0,0,0),Opacity=0,VerticalAlignment=VerticalAlignment.Center};islandDivider.SetResourceReference(Border.BackgroundProperty,"Line");
   var dot=new Border {Width=6,Height=6,CornerRadius=new CornerRadius(3),Margin=new Thickness(0,0,7,0),VerticalAlignment=VerticalAlignment.Center};dot.SetResourceReference(Border.BackgroundProperty,"Accent");
   var row=new StackPanel {Orientation=Orientation.Horizontal};row.Children.Add(islandMessage);row.Children.Add(islandDivider);row.Children.Add(dot);row.Children.Add(islandMemory);
   islandDownloads=new StackPanel();islandMedia=new StackPanel();var stack=new StackPanel {HorizontalAlignment=HorizontalAlignment.Right};stack.Children.Add(islandMedia);stack.Children.Add(islandDownloads);row.HorizontalAlignment=HorizontalAlignment.Right;stack.Children.Add(row);
   islandCard=new Border {CornerRadius=new CornerRadius(13),BorderThickness=new Thickness(1),Padding=new Thickness(11,5,12,5),Margin=new Thickness(8,8,8,14),Child=stack,Resources=window.Resources,RenderTransform=new TranslateTransform(),Tag="island",
    ToolTip="Memory used by Mica and its web engine. Shared pages may be counted more than once."};
   islandCard.SetResourceReference(Border.BackgroundProperty,"PopupPanel");islandCard.SetResourceReference(Border.BorderBrushProperty,"PopupLine");
   islandCard.Effect=new System.Windows.Media.Effects.DropShadowEffect {BlurRadius=16,ShadowDepth=3,Opacity=0.14,Color=Color.FromRgb(0x25,0x20,0x32)};
   island=new Popup {AllowsTransparency=true,StaysOpen=true,Placement=PlacementMode.Relative,PlacementTarget=Find<Grid>("PageSurface"),PopupAnimation=PopupAnimation.None,Focusable=false,Child=islandCard};
   island.Opened+=delegate {
    IntPtr handle=PopupHandle(island),owner=new System.Windows.Interop.WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;
    SetWindowLongPtr(handle,-8,owner);KeepIslandLow();
   };
   islandCard.SizeChanged+=delegate {PlaceIsland();};
   var surface=Find<Grid>("PageSurface");surface.SizeChanged+=delegate {PlaceIsland();};
   Find<ScrollViewer>("Home").IsVisibleChanged+=delegate {PlaceIsland();};if(libraryPage!=null)libraryPage.IsVisibleChanged+=delegate {PlaceIsland();};
   window.LocationChanged+=delegate {PlaceIsland();};
   window.StateChanged+=delegate {RefreshIsland();};window.IsVisibleChanged+=delegate {RefreshIsland();};
   islandHide=new DispatcherTimer {Interval=TimeSpan.FromSeconds(4.5)};islandHide.Tick+=delegate {islandHide.Stop();CollapseIsland();};
   // Only while the island is up: a cheap pointer check, so it can step aside.
   islandWatch=new DispatcherTimer(DispatcherPriority.Background) {Interval=TimeSpan.FromMilliseconds(120)};islandWatch.Tick+=delegate {WatchIslandPointer();};
   RefreshIsland();
  }
  bool IslandWanted {get {return !closing&&window.IsVisible&&window.WindowState!=WindowState.Minimized&&!fullscreenApplied;}}
  void RefreshIsland() {
   if(island==null)return;bool want=IslandWanted;
   if(want&&!island.IsOpen){island.IsOpen=true;PlaceIsland();islandWatch.Start();}
   else if(!want&&island.IsOpen){island.IsOpen=false;islandWatch.Stop();}
  }
  void PlaceIsland() {
   if(island==null||islandCard==null)return;var surface=Find<Grid>("PageSurface");
   double pageWidth=surface.ColumnDefinitions[0].ActualWidth>0?surface.ColumnDefinitions[0].ActualWidth:surface.ActualWidth;
   islandCard.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));var size=islandCard.DesiredSize;
   // Mica's own pages keep links along the bottom edge ("Keyboard shortcuts"); sit above them.
   double lift=Find<ScrollViewer>("Home").IsVisible||(libraryPage!=null&&libraryPage.IsVisible)?44:0;
   island.HorizontalOffset=Math.Max(0,pageWidth-size.Width);island.VerticalOffset=Math.Max(0,surface.ActualHeight-size.Height-lift);
   // Popups don't follow their window; nudging the offset makes WPF place it again.
   double h=island.HorizontalOffset;island.HorizontalOffset=h+0.01;island.HorizontalOffset=h;
   KeepIslandLow();
  }
  // WPF makes a popup topmost again whenever it places it, which put the island over Mica's pop-up windows
  // (sign-in, peek, screenshots) and other apps. It belongs just above its own window and nothing more.
  [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetWindow")] static extern IntPtr IslandWindowAbove(IntPtr hwnd,uint relation);
  void KeepIslandLow() {
   if(island==null||!island.IsOpen)return;IntPtr handle=PopupHandle(island),owner=new System.Windows.Interop.WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero||owner==IntPtr.Zero)return;
   const uint flags=0x0001|0x0002|0x0010|0x0200;// no size, no move, no activate, no owner z-order
   SetWindowPos(handle,new IntPtr(-2),0,0,0,0,flags);
   IntPtr above=IslandWindowAbove(owner,3);// the window directly above Mica's
   // Under a topmost one (an open menu) the island would turn topmost too; it waits for the next placing.
   if(above!=IntPtr.Zero&&above!=handle&&((long)GetWindowLongPtr(above,-20)&0x8)==0)SetWindowPos(handle,above,0,0,0,0,flags);
  }
  void UpdateIslandMemory(string summary) {
   if(islandMemory==null)return;int cut=summary.IndexOf(" ·",StringComparison.Ordinal);RollText(islandMemory,cut>0?summary.Substring(0,cut):summary);
   islandCard.ToolTip=summary+"\nMica and its web engine. Shared pages may be counted more than once.";
  }
  void ShowIslandMessage(string text) {
   if(islandMessage==null||IsRoutine(text))return;text=Localizer.Translate(text); // Measured as it will read.
   islandShowing=text;islandMessage.Text=text;islandMessage.ToolTip=text;
   Animate(islandMessage,TextBlock.MaxWidthProperty,Math.Min(460,MeasureText(text)+2),260);Animate(islandMessage,UIElement.OpacityProperty,1,200);
   islandDivider.Margin=new Thickness(9,0,9,0);Animate(islandDivider,UIElement.OpacityProperty,1,200);
   islandHide.Stop();islandHide.Start();
  }
  void CollapseIsland() {
   if(islandMessage==null)return;islandShowing=null;
   Animate(islandMessage,TextBlock.MaxWidthProperty,0,240);Animate(islandMessage,UIElement.OpacityProperty,0,160);Animate(islandDivider,UIElement.OpacityProperty,0,160);
   islandDivider.Margin=new Thickness(0);
  }
  double MeasureText(string text) {
   var probe=new TextBlock {Text=text,FontSize=islandMessage.FontSize,FontFamily=islandMessage.FontFamily};probe.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));return probe.DesiredSize.Width;
  }
  static void Animate(UIElement target,DependencyProperty property,double to,int ms) {
   if(!SystemParameters.ClientAreaAnimation){target.BeginAnimation(property,null);target.SetValue(property,to);return;}
   target.BeginAnimation(property,new DoubleAnimation(to,TimeSpan.FromMilliseconds(ms)) {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut}});
  }
  // The island's on-screen rectangle in device-independent units, so a tool (Find) can grow out of it.
  internal Rect? IslandBounds() {
   if(island==null||!island.IsOpen||islandCard==null||!islandCard.IsVisible)return null;
   try {
    var source=PresentationSource.FromVisual(window);if(source==null)return null;var fromDevice=source.CompositionTarget.TransformFromDevice;
    var topLeft=fromDevice.Transform(islandCard.PointToScreen(new Point(0,0)));var bottomRight=fromDevice.Transform(islandCard.PointToScreen(new Point(islandCard.ActualWidth,islandCard.ActualHeight)));
    return new Rect(topLeft,bottomRight);
   }catch(InvalidOperationException){return null;}
  }
  // While a tool has grown out of the island, the island itself steps away.
  bool islandYielded;
  internal void YieldIsland(bool yield,int delay=0) {
   if(islandCard==null||islandYielded==yield)return;islandYielded=yield;islandAway=false;islandCard.IsHitTestVisible=!yield;
   ((TranslateTransform)islandCard.RenderTransform).BeginAnimation(TranslateTransform.YProperty,null);
   var fade=new DoubleAnimation(yield?0:1,TimeSpan.FromMilliseconds(yield?110:200)) {BeginTime=TimeSpan.FromMilliseconds(delay)};
   if(SystemParameters.ClientAreaAnimation)islandCard.BeginAnimation(UIElement.OpacityProperty,fade);else {islandCard.BeginAnimation(UIElement.OpacityProperty,null);islandCard.Opacity=yield?0:1;}
  }
  // Several things can grow out of the island at once (a tool and a site prompt); it comes back
  // when the last of them is gone.
  int islandHolds;
  internal void HoldIsland(bool hold,int delay=0) {islandHolds=Math.Max(0,islandHolds+(hold?1:-1));YieldIsland(islandHolds>0,delay);}
  static void MorphCard(Border card,double width,double height,int ms,Action done) {
   if(!SystemParameters.ClientAreaAnimation){card.BeginAnimation(FrameworkElement.WidthProperty,null);card.BeginAnimation(FrameworkElement.HeightProperty,null);card.Width=width;card.Height=height;if(done!=null)done();return;}
   var ease=new CubicEase {EasingMode=EasingMode.EaseOut};var grow=new DoubleAnimation(width,TimeSpan.FromMilliseconds(ms)) {EasingFunction=ease};
   if(done!=null)grow.Completed+=delegate {done();};
   card.BeginAnimation(FrameworkElement.WidthProperty,grow);card.BeginAnimation(FrameworkElement.HeightProperty,new DoubleAnimation(height,TimeSpan.FromMilliseconds(ms)) {EasingFunction=ease});
  }
  // The island's bottom-right corner in PageSurface coordinates; above a docked tool when one is open.
  Point IslandCorner() {
   var surface=Find<Grid>("PageSurface");double pageWidth=surface.ColumnDefinitions[0].ActualWidth>0?surface.ColumnDefinitions[0].ActualWidth:surface.ActualWidth;
   double lift=Find<ScrollViewer>("Home").IsVisible||(libraryPage!=null&&libraryPage.IsVisible)?44:0;
   double bottom=surface.ActualHeight-14-lift;
   if(dockedTool!=null&&dockedTool.IsVisible)bottom-=dockedTool.ActualHeight+4;
   return new Point(pageWidth-8,Math.Max(80,bottom));
  }
  readonly Dictionary<Popup,Action<Action>> islandPopupClosers=new Dictionary<Popup,Action<Action>>();
  // A site prompt as a popup that grows out of the island's corner. Close it with CloseIslandPopup.
  Popup IslandPopup(Border card) {
   var content=(FrameworkElement)card.Child;double fixedWidth=card.Width;
   var holder=new Grid {Background=Brushes.Transparent};card.Margin=new Thickness(26,22,8,14);card.HorizontalAlignment=HorizontalAlignment.Right;card.VerticalAlignment=VerticalAlignment.Bottom;card.ClipToBounds=true;holder.Children.Add(card);
   var surface=Find<Grid>("PageSurface");
   var popup=new Popup {AllowsTransparency=true,StaysOpen=true,Placement=PlacementMode.Relative,PlacementTarget=surface,PopupAnimation=PopupAnimation.None,Child=holder};
   Size full=Size.Empty;bool held=false,closingNow=false;
   Action place=delegate {
    if(full.IsEmpty)return;var corner=IslandCorner();var m=card.Margin;
    popup.HorizontalOffset=corner.X-m.Left-full.Width;popup.VerticalOffset=corner.Y-m.Top-full.Height;
    double h=popup.HorizontalOffset;popup.HorizontalOffset=h+0.01;popup.HorizontalOffset=h;
   };
   SizeChangedEventHandler resized=delegate {place();};
   popup.Opened+=delegate {
    card.BeginAnimation(FrameworkElement.WidthProperty,null);card.BeginAnimation(FrameworkElement.HeightProperty,null);card.Width=fixedWidth;card.Height=double.NaN;content.Width=double.NaN;content.Height=double.NaN;
    card.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));var desired=card.DesiredSize;var m=card.Margin;
    full=new Size(desired.Width-m.Left-m.Right,desired.Height-m.Top-m.Bottom);
    var border=card.BorderThickness;var pad=card.Padding;
    content.Width=Math.Max(0,full.Width-border.Left-border.Right-pad.Left-pad.Right);content.Height=Math.Max(0,full.Height-border.Top-border.Bottom-pad.Top-pad.Bottom);
    content.HorizontalAlignment=HorizontalAlignment.Right;content.VerticalAlignment=VerticalAlignment.Bottom;
    holder.Width=full.Width+m.Left+m.Right;holder.Height=full.Height+m.Top+m.Bottom;
    place();surface.SizeChanged+=resized;
    var spot=islandCard!=null&&islandCard.IsVisible&&islandHolds==0?new Size(islandCard.ActualWidth,islandCard.ActualHeight):new Size(Math.Min(full.Width,120),Math.Min(full.Height,30));
    if(!held){held=true;HoldIsland(true);}
    card.Width=Math.Min(full.Width,spot.Width);card.Height=Math.Min(full.Height,spot.Height);content.Opacity=0;
    MorphCard(card,full.Width,full.Height,300,null);
    if(SystemParameters.ClientAreaAnimation)content.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(170)) {BeginTime=TimeSpan.FromMilliseconds(130)});else content.Opacity=1;
   };
   islandPopupClosers[popup]=delegate(Action after) {
    if(closingNow)return;closingNow=true;surface.SizeChanged-=resized;
    Action gone=delegate {popup.IsOpen=false;islandPopupClosers.Remove(popup);if(held){held=false;HoldIsland(false);}if(after!=null)after();};
    if(!popup.IsOpen||full.IsEmpty||closing||!SystemParameters.ClientAreaAnimation){gone();return;}
    content.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,TimeSpan.FromMilliseconds(90)));
    double w=islandCard!=null?islandCard.ActualWidth:120,h=islandCard!=null?islandCard.ActualHeight:30;
    MorphCard(card,Math.Min(card.ActualWidth,w),Math.Min(card.ActualHeight,h),200,gone);
   };
   return popup;
  }
  internal void CloseIslandPopup(Popup popup,Action after=null) {Action<Action> close;if(islandPopupClosers.TryGetValue(popup,out close))close(after);else {popup.IsOpen=false;if(after!=null)after();}}
  // Page tools (Find, Print, Cast) are cards that grow out of the island: the card starts at the
  // island's size in the same corner and widens into place, the controls fade in once there's
  // room, and closing runs it backwards into the island. One docked tool at a time.
  Window dockedTool;
  internal void DockToIsland(Window dialog,Border card,FrameworkElement content) {
   if(dockedTool!=null&&dockedTool!=dialog)dockedTool.Close();dockedTool=dialog;
   dialog.WindowStartupLocation=WindowStartupLocation.Manual;
   Size seed=Size.Empty;bool shrinking=false,shrunk=false,held=false;
   Action position=delegate {
    if(!window.IsVisible)return;var margin=card.Margin;
    var spot=IslandBounds();if(spot.HasValue){dialog.Left=spot.Value.Right+margin.Right-dialog.Width;dialog.Top=spot.Value.Bottom+margin.Bottom-dialog.Height;return;}
    var frame=Find<Border>("ContentFrame");var point=frame.PointToScreen(new Point(Math.Max(0,frame.ActualWidth-dialog.Width-12+margin.Right),Math.Max(0,frame.ActualHeight-dialog.Height-12+margin.Bottom)));var source=PresentationSource.FromVisual(window);if(source!=null)point=source.CompositionTarget.TransformFromDevice.Transform(point);dialog.Left=point.X;dialog.Top=point.Y;
   };
   Action<double,double,int,Action> morph=delegate(double width,double height,int ms,Action done) {
    if(!SystemParameters.ClientAreaAnimation){card.BeginAnimation(FrameworkElement.WidthProperty,null);card.BeginAnimation(FrameworkElement.HeightProperty,null);card.Width=width;card.Height=height;if(done!=null)done();return;}
    var ease=new CubicEase {EasingMode=EasingMode.EaseOut};var grow=new DoubleAnimation(width,TimeSpan.FromMilliseconds(ms)) {EasingFunction=ease};
    if(done!=null)grow.Completed+=delegate {done();};
    card.BeginAnimation(FrameworkElement.WidthProperty,grow);card.BeginAnimation(FrameworkElement.HeightProperty,new DoubleAnimation(height,TimeSpan.FromMilliseconds(ms)) {EasingFunction=ease});
   };
   dialog.Loaded+=delegate {
    // Freeze the laid-out size, then let the card (not its content) change size.
    dialog.UpdateLayout();dialog.SizeToContent=SizeToContent.Manual;
    double fullWidth=card.ActualWidth,fullHeight=card.ActualHeight;
    content.Width=content.ActualWidth;content.Height=content.ActualHeight;content.HorizontalAlignment=HorizontalAlignment.Right;content.VerticalAlignment=VerticalAlignment.Bottom;
    card.HorizontalAlignment=HorizontalAlignment.Right;card.VerticalAlignment=VerticalAlignment.Bottom;card.ClipToBounds=true;
    // The content keeps its size while the card morphs, so a bitmap of it is reused every frame.
    if(SystemParameters.ClientAreaAnimation&&content.CacheMode==null)content.CacheMode=motionCache;
    var spot=IslandBounds();if(!held){held=true;HoldIsland(true);}position();
    seed=spot.HasValue?spot.Value.Size:new Size(fullWidth*0.4,fullHeight*0.4);
    card.Width=Math.Min(fullWidth,seed.Width);card.Height=Math.Min(fullHeight,seed.Height);content.Opacity=0;
    morph(fullWidth,fullHeight,300,delegate {
     // Once grown, let the card follow its content again (a device list or status line can appear later).
     if(shrinking)return;if(content.CacheMode==motionCache)content.CacheMode=null;card.BeginAnimation(FrameworkElement.WidthProperty,null);card.BeginAnimation(FrameworkElement.HeightProperty,null);card.Width=double.NaN;card.Height=double.NaN;content.Width=double.NaN;content.Height=double.NaN;card.HorizontalAlignment=HorizontalAlignment.Stretch;card.VerticalAlignment=VerticalAlignment.Stretch;content.HorizontalAlignment=HorizontalAlignment.Stretch;content.VerticalAlignment=VerticalAlignment.Stretch;
     dialog.SizeChanged+=delegate {if(!shrinking)position();};dialog.SizeToContent=SizeToContent.Height;
    });
    if(SystemParameters.ClientAreaAnimation)content.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(170)) {BeginTime=TimeSpan.FromMilliseconds(130)});else content.Opacity=1;
   };
   dialog.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e) {
    if(shrunk||closing||seed.IsEmpty||!window.IsVisible||!SystemParameters.ClientAreaAnimation)return;
    e.Cancel=true;if(shrinking)return;shrinking=true;
    dialog.SizeToContent=SizeToContent.Manual;content.Width=content.ActualWidth;content.Height=content.ActualHeight;card.Width=card.ActualWidth;card.Height=card.ActualHeight;content.HorizontalAlignment=HorizontalAlignment.Right;content.VerticalAlignment=VerticalAlignment.Bottom;card.HorizontalAlignment=HorizontalAlignment.Right;card.VerticalAlignment=VerticalAlignment.Bottom;
    if(content.CacheMode==null)content.CacheMode=motionCache;
    content.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,TimeSpan.FromMilliseconds(90)));
    if(held){held=false;HoldIsland(false,150);}
    morph(Math.Min(card.ActualWidth,seed.Width),Math.Min(card.ActualHeight,seed.Height),210,delegate {shrunk=true;dialog.Close();});
   };
   EventHandler moved=delegate {position();};SizeChangedEventHandler resized=delegate {position();};window.LocationChanged+=moved;window.SizeChanged+=resized;
   dialog.Closed+=delegate {window.LocationChanged-=moved;window.SizeChanged-=resized;if(dockedTool==dialog)dockedTool=null;if(held){held=false;HoldIsland(false);}};
  }
  [StructLayout(LayoutKind.Sequential)] struct CursorPoint {public int X,Y;}
  [DllImport("user32.dll")] static extern bool GetCursorPos(out CursorPoint point);
  void WatchIslandPointer() {
   if(island==null||!island.IsOpen||islandCard==null||!islandCard.IsVisible||islandYielded)return;
   CursorPoint cursor;if(!GetCursorPos(out cursor))return;
   Point topLeft,bottomRight;
   try {topLeft=islandCard.PointToScreen(new Point(0,0));bottomRight=islandCard.PointToScreen(new Point(islandCard.ActualWidth,islandCard.ActualHeight));}catch(InvalidOperationException){return;}
   const double reach=36;
   bool near=cursor.X>=topLeft.X-reach&&cursor.X<=bottomRight.X+reach&&cursor.Y>=topLeft.Y-reach&&cursor.Y<=bottomRight.Y+reach;
   if(IslandHasDownloads||IslandHasMedia)near=false;
   if(near==islandAway)return;islandAway=near;
   // Fully transparent popups let clicks through to the page underneath.
   Animate(islandCard,UIElement.OpacityProperty,near?0:1,near?120:220);
   ((TranslateTransform)islandCard.RenderTransform).BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(near?10:0,TimeSpan.FromMilliseconds(near?120:220)));
   islandCard.IsHitTestVisible=!near;
  }
 }
}
