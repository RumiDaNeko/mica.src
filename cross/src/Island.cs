using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Mica {
 // The island, as on Windows (BrowserIsland.cs, BrowserIslandDownloads.cs): a small card in the page's bottom-right
 // corner. It shows Mica's memory use, widens to say something worth saying, carries downloads and what a
 // background tab is playing, and slips out of the way when the pointer comes near. Pages are native views
 // nothing can be drawn over, so it is a popup, a window of its own.
 sealed partial class BrowserWindow {
  Popup island;Border islandCard;
  readonly Ticker islandMemory=new Ticker {FontSizeValue=11};
  readonly TextBlock islandMessage=new TextBlock {FontSize=11,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=0,Opacity=0};
  readonly Border islandDivider=new Border {Width=1,Height=12,Opacity=0,VerticalAlignment=VerticalAlignment.Center};
  readonly StackPanel islandDownloads=new StackPanel(),islandMedia=new StackPanel();
  readonly TranslateTransform islandSlide=new TranslateTransform();
  DispatcherTimer islandHide,islandWatch,islandMemoryTimer;bool islandAway;
  // The last thing the island said (the self-test reads it).
  internal string lastStatus="";
  static DoubleTransition Ease(AvaloniaProperty property,int ms) {return new DoubleTransition {Property=property,Duration=TimeSpan.FromMilliseconds(ms),Easing=new CubicEaseOut()};}

  void BuildIsland() {
   if(OperatingSystem.IsWindows())islandMemory.Family=new FontFamily("Segoe UI Variable Text, Segoe UI");
   Res(islandMessage,TextBlock.ForegroundProperty,"Ink");Res(islandDivider,Border.BackgroundProperty,"Line");
   var dot=Res(new Border {Width=6,Height=6,CornerRadius=new CornerRadius(3),Margin=new Thickness(0,0,7,0),VerticalAlignment=VerticalAlignment.Center},Border.BackgroundProperty,"Accent");
   var row=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
   row.Children.Add(islandMessage);row.Children.Add(islandDivider);row.Children.Add(dot);row.Children.Add(islandMemory);
   var stack=new StackPanel {HorizontalAlignment=HorizontalAlignment.Right};stack.Children.Add(islandMedia);stack.Children.Add(islandZoom);stack.Children.Add(islandDownloads);stack.Children.Add(row);
   islandScale=new LayoutTransformControl {Child=stack};
   // As on Windows: radius 13, 8 px from the page's right edge and 14 px from its bottom, a soft shadow.
   islandCard=Res(Res(new Border {CornerRadius=new CornerRadius(13),BorderThickness=new Thickness(1),Padding=new Thickness(11,5,12,5),Margin=new Thickness(8,8,8,14),Child=islandScale,BoxShadow=BoxShadows.Parse("0 3 16 0 #24252032"),RenderTransform=islandSlide},Border.BackgroundProperty,"Panel"),Border.BorderBrushProperty,"Line");
   ToolTip.SetTip(islandCard,"Memory used by Mica and its web engine. Shared pages may be counted more than once.");
   island=new Popup {Child=islandCard,PlacementTarget=pages,Placement=PlacementMode.AnchorAndGravity,PlacementAnchor=Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.BottomRight,PlacementGravity=Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.TopLeft,IsLightDismissEnabled=false,ShouldUseOverlayLayer=false,Focusable=false};
   island.Opened+=delegate {KeepIslandLow();};
   root.Children.Add(island);
   islandHide=new DispatcherTimer {Interval=TimeSpan.FromSeconds(4.5)};islandHide.Tick+=delegate {islandHide.Stop();CollapseIsland();};
   // A cheap look at the pointer while the island is up: the page is a native view, so its pointer moves never
   // reach Mica. Windows does the same.
   islandWatch=new DispatcherTimer(DispatcherPriority.Background) {Interval=TimeSpan.FromMilliseconds(120)};islandWatch.Tick+=delegate {WatchIslandPointer();};
   islandMemoryTimer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(10)};islandMemoryTimer.Tick+=delegate {UpdateIslandMemory();};islandMemoryTimer.Start();
   Opened+=delegate {RefreshIsland();UpdateIslandMemory();};
   PropertyChanged+=delegate(object s,AvaloniaPropertyChangedEventArgs e) {if(e.Property==WindowStateProperty||e.Property==BoundsProperty||e.Property==IsVisibleProperty)RefreshIsland();};
   // The island is a window of its own. On Windows it sits just above Mica in the z-order (KeepIslandLow); elsewhere
   // a popup floats over every app, so it leaves while Mica isn't the window in front.
   Activated+=delegate {islandInactive=false;RefreshIsland();};
   Deactivated+=delegate {if(!OperatingSystem.IsWindows()){islandInactive=true;RefreshIsland();}};
   PositionChanged+=delegate {PlaceIsland();};
   pages.PropertyChanged+=delegate(object s,AvaloniaPropertyChangedEventArgs e) {if(e.Property==BoundsProperty)PlaceIsland();};
   Closing+=delegate {island.IsOpen=false;islandWatch.Stop();};
  }
  bool islandFullscreen,islandInactive;
  bool IslandWanted {get {return IsVisible&&WindowState!=WindowState.Minimized&&!islandFullscreen&&!islandInactive;}}
  void RefreshIsland() {
   if(island==null)return;bool want=IslandWanted;
   if(want&&!island.IsOpen){island.IsOpen=true;PlaceIsland();}
   else if(!want&&island.IsOpen)island.IsOpen=false;
   if(want)islandWatch.Start();else islandWatch.Stop();
  }
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd,int index);
  [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd,int index,int value);
  IntPtr IslandHandle() {var top=island.Host as TopLevel;var handle=top==null?null:top.TryGetPlatformHandle();return handle==null?IntPtr.Zero:handle.Handle;}
  // Avalonia's popups are topmost windows; the island isn't. It stays just above Mica, so it never floats over
  // other apps when Mica is behind them (BrowserIsland.cs KeepIslandLow).
  void KeepIslandLow() {
   if(!OperatingSystem.IsWindows())return;IntPtr handle=IslandHandle();if(handle==IntPtr.Zero)return;
   const uint NoSize=1,NoMove=2,NoActivate=0x10;SetWindowPos(handle,new IntPtr(-2),0,0,0,0,NoSize|NoMove|NoActivate);
   if(islandAway)SetIslandClickThrough(true);
  }
  // Mica's own pages keep links along the bottom edge ("Keyboard shortcuts"); the island sits 44 px higher there.
  void PlaceIsland() {
   if(island==null)return;var tab=Active;
   bool own=tab!=null&&(tab.Entry.Url=="mica://home"||Navigation.IsLibrary(tab.Entry.Url));
   island.HorizontalOffset=0;island.VerticalOffset=own?-44:0;
   if(island.IsOpen){island.IsOpen=false;island.IsOpen=true;}
  }
  async void UpdateIslandMemory() {
   string summary=await Task.Run(()=>MemorySummary());
   int cut=summary.IndexOf(" in use",StringComparison.Ordinal);islandMemory.Text=cut>0?summary.Substring(0,cut):summary;
   ToolTip.SetTip(islandCard,summary+"\nMica and its web engine. Shared pages may be counted more than once.");
  }
  // Routine progress isn't worth a word (BrowserIsland.cs IsRoutine).
  static bool IsRoutine(string text) {
   return string.IsNullOrEmpty(text)||text=="Finding your way…"||text=="Ready when you are."||text=="All yours. Take your time."||text=="Your library, kept on this device."||text=="Loading stopped. Available page content is kept."||text.StartsWith("Page couldn't load")||text.StartsWith("Downloading ");
  }
  // A word about what just happened: the island widens to say it (260 ms), then folds back after 4.5 s (240 ms).
  void ShowIslandMessage(string text) {
   lastStatus=text;
   if(island==null||IsRoutine(text))return;
   islandMessage.Text=text;ToolTip.SetTip(islandMessage,text);
   var probe=new TextBlock {Text=Localizer.Translate(text),FontSize=islandMessage.FontSize};probe.Measure(Size.Infinity);
   islandMessage.Transitions=new Transitions {Ease(TextBlock.MaxWidthProperty,260),Ease(Visual.OpacityProperty,200)};
   islandDivider.Transitions=new Transitions {Ease(Visual.OpacityProperty,200)};
   islandMessage.MaxWidth=Math.Min(460,probe.DesiredSize.Width+2);islandMessage.Opacity=1;
   islandDivider.Margin=new Thickness(9,0);islandDivider.Opacity=1;
   if(islandAway)StepAside(false);
   islandHide.Stop();islandHide.Start();
  }
  void CollapseIsland() {
   islandMessage.Transitions=new Transitions {Ease(TextBlock.MaxWidthProperty,240),Ease(Visual.OpacityProperty,160)};
   islandDivider.Transitions=new Transitions {Ease(Visual.OpacityProperty,160)};
   islandMessage.MaxWidth=0;islandMessage.Opacity=0;islandDivider.Opacity=0;islandDivider.Margin=new Thickness(0);
  }

  // Out of the way while the pointer is within 36 px: it fades and drops 10 px in 120 ms and lets clicks through,
  // then comes back in 220 ms once the pointer has gone. Downloads, media and zoom stay put so they can be clicked.
  void StepAside(bool away) {
   if(away&&(islandDownloads.Children.Count>0||islandMedia.Children.Count>0||IslandHasZoom))return;
   if(away==islandAway)return;islandAway=away;
   islandCard.Transitions=new Transitions {Ease(Visual.OpacityProperty,away?120:220)};
   islandSlide.Transitions=new Transitions {new DoubleTransition {Property=TranslateTransform.YProperty,Duration=TimeSpan.FromMilliseconds(away?120:220),Easing=away?(Easing)new LinearEasing():new CubicEaseOut()}};
   islandCard.Opacity=away?0:1;islandSlide.Y=away?10:0;islandCard.IsHitTestVisible=!away;
   SetIslandClickThrough(away);
  }
  // While it's aside, clicks go to the page underneath: on Windows the island's window turns transparent to the
  // mouse; elsewhere the popup leaves once it has faded and comes back when the pointer has gone.
  void SetIslandClickThrough(bool through) {
   if(OperatingSystem.IsWindows()) {
    IntPtr handle=IslandHandle();if(handle==IntPtr.Zero)return;
    const int ExStyle=-20,Transparent=0x20,Layered=0x80000;int style=GetWindowLong(handle,ExStyle);
    SetWindowLong(handle,ExStyle,through?style|Transparent|Layered:style&~Transparent);
   }
   else if(through)DispatcherTimer.RunOnce(delegate {if(islandAway&&island.IsOpen)island.IsOpen=false;},TimeSpan.FromMilliseconds(130));
   else if(IslandWanted&&!island.IsOpen){island.IsOpen=true;PlaceIsland();}
  }
  void WatchIslandPointer() {
   PixelPoint cursor;if(!PointerOnScreen.TryGet(out cursor))return;
   if(Mac)cursor=new PixelPoint((int)(cursor.X*RenderScaling),(int)(cursor.Y*RenderScaling));
   double scale=RenderScaling,reach=36*scale,w=islandCard.Bounds.Width*scale,h=islandCard.Bounds.Height*scale;
   // Where the card is (or would be, while it's away): 8 px in from the page's right edge, 14 px up from its bottom.
   var corner=pages.PointToScreen(new Point(pages.Bounds.Width,pages.Bounds.Height));
   double lift=island.VerticalOffset*-1*scale;
   double right=corner.X-8*scale,bottom=corner.Y-14*scale-lift,left=right-w,top=bottom-h;
   bool near=cursor.X>=left-reach&&cursor.X<=right+reach&&cursor.Y>=top-reach&&cursor.Y<=bottom+reach;
   StepAside(near);
  }

  // ── Downloads in the island (BrowserIslandDownloads.cs): the two newest with progress; a finished one stays 8 s.
  readonly Dictionary<string,DateTime> islandDoneAt=new Dictionary<string,DateTime>();
  readonly HashSet<string> islandSeenLive=new HashSet<string>();
  readonly Dictionary<string,double[]> islandRates=new Dictionary<string,double[]>(); // received, seconds, rate
  DispatcherTimer islandDownloadsTimer;
  void RefreshIslandDownloads() {
   if(island==null)return;var now=DateTime.UtcNow;
   foreach(var d in downloads)if(IsDownloadLive(d))islandSeenLive.Add(d.Id);
   foreach(var id in islandSeenLive.ToList()){var d=downloads.FirstOrDefault(x=>x.Id==id);if(d==null){islandSeenLive.Remove(id);islandDoneAt.Remove(id);continue;}if(!IsDownloadLive(d)&&!islandDoneAt.ContainsKey(id))islandDoneAt[id]=now;}
   var shown=downloads.Where(d=>islandSeenLive.Contains(d.Id)&&(IsDownloadLive(d)||now-islandDoneAt[d.Id]<TimeSpan.FromSeconds(8))).OrderByDescending(d=>d.StartedUtc).Take(2).ToList();
   islandDownloads.Children.Clear();
   foreach(var d in shown)islandDownloads.Children.Add(IslandDownloadRow(d));
   islandDownloads.Margin=new Thickness(0,0,0,shown.Count>0?6:0);
   if(shown.Count>0&&islandAway)StepAside(false);
   if(islandDownloadsTimer==null){islandDownloadsTimer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};islandDownloadsTimer.Tick+=delegate {RefreshIslandDownloads();};}
   if(shown.Count>0)islandDownloadsTimer.Start();else islandDownloadsTimer.Stop();
  }
  Control IslandDownloadRow(DownloadEntry d) {
   var entry=d;bool done=d.Status=="Completed",paused=d.Status=="Paused",live=IsDownloadLive(d);
   var grid=new Grid {ColumnDefinitions=new ColumnDefinitions("22,*,Auto"),RowDefinitions=new RowDefinitions("*,Auto")};
   var icon=Glyph(done?Glyphs.Check:paused?Glyphs.Pause:live?Glyphs.Download:Glyphs.Info,12,"accent");icon.HorizontalAlignment=HorizontalAlignment.Left;Grid.SetRowSpan(icon,2);grid.Children.Add(icon);
   var name=new TextBlock {Text=d.FileName,FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};name.Classes.Add("user");Grid.SetColumn(name,1);grid.Children.Add(name);
   // Speed, smoothed: rate = rate*0.6 + instant*0.4, sampled at least 0.75 s apart.
   double rate=0;double[] last;double t=(DateTime.UtcNow-DateTime.UnixEpoch).TotalSeconds;
   if(live&&!paused) {
    if(islandRates.TryGetValue(d.Id,out last)) {
     rate=last[2];
     if(t-last[1]>=0.75){double instant=Math.Max(0,(d.Received-last[0])/(t-last[1]));rate=last[2]>0?last[2]*0.6+instant*0.4:instant;islandRates[d.Id]=new[]{(double)d.Received,t,rate};}
    }
    else islandRates[d.Id]=new[]{(double)d.Received,t,0.0};
   }
   string noteText=done?"Open":paused?"Paused":live?(d.Total>0?Math.Floor(100.0*d.Received/d.Total)+"%":ByteSize(d.Received))+(rate>0?" · "+ByteSize((long)rate)+"/s":""):d.Status;
   var note=Muted(new TextBlock {Text=noteText,FontSize=10,Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center});Grid.SetColumn(note,2);grid.Children.Add(note);
   var track=new Grid {Height=3,Margin=new Thickness(0,4,0,0),ClipToBounds=true,Opacity=done?0.6:1};Grid.SetRow(track,1);Grid.SetColumn(track,1);Grid.SetColumnSpan(track,2);
   track.Children.Add(Res(new Border {CornerRadius=new CornerRadius(1.5)},Border.BackgroundProperty,"Line"));
   double fraction=done?1:d.Total>0?Math.Min(1,(double)d.Received/d.Total):0;
   track.Children.Add(Res(new Border {CornerRadius=new CornerRadius(1.5),HorizontalAlignment=HorizontalAlignment.Left,Width=(250-6-6-22)*fraction},Border.BackgroundProperty,"Accent"));
   grid.Children.Add(track);
   var row=new Border {Width=250,Padding=new Thickness(6,5,6,6),Margin=new Thickness(-6,0,-6,1),CornerRadius=new CornerRadius(8),Background=Brushes.Transparent,Cursor=new Cursor(StandardCursorType.Hand),Child=grid};
   row.PointerEntered+=delegate {Res(row,Border.BackgroundProperty,"Hover");};row.PointerExited+=delegate {row.Background=Brushes.Transparent;};
   ToolTip.SetTip(row,done?d.FileName+"\nDownloaded. Click to open it.":d.FileName+"\n"+ByteSize(d.Received)+(d.Total>0?" of "+ByteSize(d.Total):"")+(rate>0?" at "+ByteSize((long)rate)+"/s":"")+". Click for Downloads.");
   row.PointerPressed+=delegate(object s,PointerPressedEventArgs e) {e.Handled=true;if(entry.Status=="Completed"&&System.IO.File.Exists(entry.FilePath))OpenPath(entry.FilePath,false);else OpenLibraryPage("mica://downloads");};
   return row;
  }
  static string ByteSize(long bytes) {string[] u={"B","KB","MB","GB"};double n=bytes;int i=0;while(n>=1024&&i<3){n/=1024;i++;}return (i==0?n.ToString("0"):n.ToString("0.0"))+" "+u[i];}

  // ── What a background tab is playing: pages tell Mica when their media starts or stops (a console line the
  // page script below writes, kept out of the console).
  internal const string MediaScript="(()=>{if(window.__micaMedia)return;window.__micaMedia=1;let last='';const report=()=>{const m=[...document.querySelectorAll('video,audio')];const on=m.some(x=>!x.paused&&!x.ended&&!x.muted&&x.volume>0);const s=on?'1':'0';if(s!==last){last=s;console.debug('__mica_media:'+s);}};for(const e of ['play','playing','pause','ended','volumechange','emptied'])document.addEventListener(e,report,true);report();})()";
  string islandMediaTab;DateTime islandMediaPausedAt;
  void MediaChanged(CrossTab tab,bool playing) {tab.Playing=playing;if(playing)tab.AudibleAt=DateTime.UtcNow;UpdateIslandMedia();}
  void UpdateIslandMedia() {
   if(island==null)return;
   var playing=tabs.Values.Where(t=>t.View!=null&&t.Entry.Id!=state.ActiveId&&(t.Playing||t.Entry.Id==islandMediaTab&&DateTime.UtcNow-islandMediaPausedAt<TimeSpan.FromMinutes(1))).OrderByDescending(t=>t.AudibleAt).FirstOrDefault();
   islandMedia.Children.Clear();
   if(playing==null)return;
   var tab=playing;var row=new Grid {Width=250,ColumnDefinitions=new ColumnDefinitions("22,*,Auto,Auto"),Margin=new Thickness(-4,0,-6,6)};
   var icon=Glyph(Glyphs.Note,12,"accent");icon.HorizontalAlignment=HorizontalAlignment.Left;row.Children.Add(icon);
   var name=new TextBlock {Text=TabTitle(tab.Entry),FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,Cursor=new Cursor(StandardCursorType.Hand)};name.Classes.Add("user");ToolTip.SetTip(name,"Go to the tab");
   name.PointerPressed+=delegate(object s,PointerPressedEventArgs e) {e.Handled=true;Show(tab.Entry.Id);};Grid.SetColumn(name,1);row.Children.Add(name);
   var play=CaptionButton(tab.Playing?Glyphs.Pause:Glyphs.Play,tab.Playing?"Pause":"Play");play.Width=24;play.Height=22;play.Click+=delegate {PlayPause(tab);};Grid.SetColumn(play,2);row.Children.Add(play);
   var mute=CaptionButton(tab.Muted?Glyphs.Muted:Glyphs.Sound,tab.Muted?"Unmute":"Mute");mute.Width=24;mute.Height=22;mute.Click+=delegate {ToggleMute(tab);UpdateIslandMedia();};Grid.SetColumn(mute,3);row.Children.Add(mute);
   islandMedia.Children.Add(row);
   if(islandAway)StepAside(false);
  }
  void PlayPause(CrossTab tab) {
   if(tab.View==null)return;
   if(tab.Playing){islandMediaTab=tab.Entry.Id;islandMediaPausedAt=DateTime.UtcNow;}
   tab.View.ExecuteJavaScript("(()=>{const m=[...document.querySelectorAll('video,audio')];const p=m.find(x=>!x.paused&&!x.ended);if(p){p.pause();return;}const q=m.find(x=>x.currentTime>0)||m[0];if(q)q.play();})()","",1);
   // As on Windows: looked at again shortly after, once the page has reacted.
   DispatcherTimer.RunOnce(UpdateIslandMedia,TimeSpan.FromMilliseconds(300));DispatcherTimer.RunOnce(UpdateIslandMedia,TimeSpan.FromMilliseconds(1200));
  }
 }

 // Where the pointer is on screen, wherever it is (over a page, another app): pages are native views whose pointer
 // moves never reach Avalonia.
 static class PointerOnScreen {
  [StructLayout(LayoutKind.Sequential)] struct Win32Point {public int X,Y;}
  [DllImport("user32.dll")] static extern bool GetCursorPos(out Win32Point point);
  [StructLayout(LayoutKind.Sequential)] struct CGPoint {public double X,Y;}
  [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")] static extern IntPtr CGEventCreate(IntPtr source);
  [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")] static extern CGPoint CGEventGetLocation(IntPtr evt);
  [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] static extern void CFRelease(IntPtr obj);
  [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
  [DllImport("libX11.so.6")] static extern IntPtr XDefaultRootWindow(IntPtr display);
  [DllImport("libX11.so.6")] static extern bool XQueryPointer(IntPtr display,IntPtr window,out IntPtr root,out IntPtr child,out int rootX,out int rootY,out int x,out int y,out uint mask);
  static IntPtr display;static bool broken;
  internal static bool TryGet(out PixelPoint point) {
   point=default(PixelPoint);if(broken)return false;
   try {
    if(OperatingSystem.IsWindows()){Win32Point p;if(!GetCursorPos(out p))return false;point=new PixelPoint(p.X,p.Y);return true;}
    if(OperatingSystem.IsMacOS()) {
     // Points from the top-left of the main screen, the same space Avalonia's screen points use on a Mac.
     IntPtr e=CGEventCreate(IntPtr.Zero);if(e==IntPtr.Zero)return false;var at=CGEventGetLocation(e);CFRelease(e);
     point=new PixelPoint((int)at.X,(int)at.Y);return true;
    }
    if(display==IntPtr.Zero)display=XOpenDisplay(IntPtr.Zero);if(display==IntPtr.Zero){broken=true;return false;}
    IntPtr root,child;int rx,ry,x,y;uint mask;
    if(!XQueryPointer(display,XDefaultRootWindow(display),out root,out child,out rx,out ry,out x,out y,out mask))return false;
    point=new PixelPoint(rx,ry);return true;
   } catch(Exception) {broken=true;return false;} // No X server (Wayland without XWayland): the island just stays put.
  }
 }
}
