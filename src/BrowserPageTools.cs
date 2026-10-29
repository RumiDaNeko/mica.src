using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // Page tools: translate, picture-in-picture, screenshots, forgetting a site
 // when its last tab closes, and what's playing, in the island.
 partial class BrowserApp {
  // ---- Translate: Google Translate's page proxy, on request (the address goes to Google) ----
  internal void TranslatePage(string language) {
   var tab=Active;if(tab==null||!Navigation.IsWeb(tab.Url)){Status("Open a website to translate it.");return;}
   Uri uri=new Uri(tab.Url);if(uri.Host.EndsWith(".translate.goog",StringComparison.OrdinalIgnoreCase)){Status("This page is already translated. Go back to see the original.");return;}
   Navigate("https://translate.google.com/translate?sl=auto&tl="+Uri.EscapeDataString(language)+"&u="+Uri.EscapeDataString(tab.Url),false);
   Status("Translating to "+LanguageName(language)+" with Google Translate. Go back for the original.");
  }

  // ---- Picture-in-picture: the biggest (or playing) video floats in its own window ----
  // The page needs a user gesture for it, which DevTools can grant on your behalf.
  internal async void PictureInPicture() {
   var view=CurrentView();var core=view==null?null:LiveCore(view);if(core==null){Status("Open a page with a video first.");return;}
   const string script="(async()=>{if(document.pictureInPictureElement){await document.exitPictureInPicture();return 'closed';}const v=[...document.querySelectorAll('video')].filter(x=>x.readyState>0).sort((a,b)=>(b.clientWidth*b.clientHeight)-(a.clientWidth*a.clientHeight));const p=v.find(x=>!x.paused)||v[0];if(!p)return 'none';p.disablePictureInPicture=false;await p.requestPictureInPicture();return 'open';})()";
   string reply;
   try {reply=await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"expression",script},{"awaitPromise",true},{"userGesture",true},{"returnByValue",true}}));}
   catch(Exception e){Status("Picture-in-picture didn't start: "+e.Message);return;}
   string result=null;try {var parsed=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(reply);var inner=parsed["result"] as Dictionary<string,object>;object value;if(inner!=null&&inner.TryGetValue("value",out value))result=value as string;}catch(Exception){}
   Status(result=="open"?"The video is floating. Close its window to bring it back.":result=="closed"?"Picture-in-picture closed.":result=="none"?"There's no video on this page.":"This page doesn't allow picture-in-picture.");
  }

  // ---- Screenshots: what's visible, the whole page, or an area you drag; then copy, save or mark up ----
  internal async void TakeScreenshot(string mode) {
   var view=CurrentView();var core=view==null?null:LiveCore(view);var tab=Active;
   if(core==null||tab==null||!Navigation.IsWeb(tab.Url)){Status("Open a website to take a screenshot of it.");return;}
   BitmapSource image;
   try {image=await CapturePage(core,mode=="full");}catch(Exception e){Status("Couldn't take the screenshot: "+e.Message);return;}
   if(image==null){Status("Couldn't take the screenshot.");return;}
   if(mode=="region"){var area=await PickArea(view,image);if(area==null)return;image=new CroppedBitmap(image,area.Value);}
   ShowScreenshot(image,tab.Title);
  }
  internal async Task<BitmapSource> CapturePage(CoreWebView2 core,bool full) {
   var json=new JavaScriptSerializer();var options=new Dictionary<string,object>{{"format","png"}};
   if(full) {
    var metrics=json.Deserialize<Dictionary<string,object>>(await core.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics","{}"));
    var size=(metrics.ContainsKey("cssContentSize")?metrics["cssContentSize"]:metrics["contentSize"]) as Dictionary<string,object>;
    double width=Convert.ToDouble(size["width"]),height=Math.Min(16000,Convert.ToDouble(size["height"]));
    options["captureBeyondViewport"]=true;options["clip"]=new Dictionary<string,object>{{"x",0},{"y",0},{"width",width},{"height",height},{"scale",1}};
   }
   var reply=json.Deserialize<Dictionary<string,object>>(await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot",json.Serialize(options)));
   var bytes=Convert.FromBase64String((string)reply["data"]);
   var bitmap=new BitmapImage();using(var stream=new MemoryStream(bytes)){bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();}
   bitmap.Freeze();return bitmap;
  }
  // A dimmed copy of the page over it; drag out the part you want, Esc to cancel.
  internal Func<Int32Rect?> pickAreaForTest;
  Task<Int32Rect?> PickArea(WebView2 view,BitmapSource image) {
   var done=new TaskCompletionSource<Int32Rect?>();
   if(pickAreaForTest!=null){done.SetResult(pickAreaForTest());return done.Task;}
   var host=Find<Grid>("WebHost");var layer=new Grid {Background=Brushes.Black,Cursor=Cursors.Cross,Focusable=true,Tag="screenshot-pick"};
   Grid.SetColumn(layer,Grid.GetColumn(view));layer.Children.Add(new Image {Source=image,Stretch=Stretch.Fill,Opacity=0.55});
   var canvas=new Canvas();layer.Children.Add(canvas);var box=new Border {BorderThickness=new Thickness(2),BorderBrush=Brushes.White,Background=new SolidColorBrush(Color.FromArgb(40,255,255,255)),Visibility=Visibility.Collapsed};canvas.Children.Add(box);
   var hint=new Border {Background=new SolidColorBrush(Color.FromArgb(200,20,20,24)),CornerRadius=new CornerRadius(8),Padding=new Thickness(12,6,12,6),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,16,0,0),IsHitTestVisible=false,Child=new TextBlock {Text="Drag over the part you want. Esc cancels.",Foreground=Brushes.White,FontSize=12}};
   layer.Children.Add(hint);Point? from=null;
   Action<Int32Rect?> finish=delegate(Int32Rect? r) {host.Children.Remove(layer);done.TrySetResult(r);};
   layer.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) {from=e.GetPosition(canvas);layer.CaptureMouse();box.Visibility=Visibility.Visible;Canvas.SetLeft(box,from.Value.X);Canvas.SetTop(box,from.Value.Y);box.Width=box.Height=0;};
   layer.MouseMove+=delegate(object sender,MouseEventArgs e) {if(from==null)return;var at=e.GetPosition(canvas);Canvas.SetLeft(box,Math.Min(at.X,from.Value.X));Canvas.SetTop(box,Math.Min(at.Y,from.Value.Y));box.Width=Math.Abs(at.X-from.Value.X);box.Height=Math.Abs(at.Y-from.Value.Y);};
   layer.MouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e) {
    if(from==null)return;layer.ReleaseMouseCapture();var at=e.GetPosition(canvas);double sx=image.PixelWidth/Math.Max(1,layer.ActualWidth),sy=image.PixelHeight/Math.Max(1,layer.ActualHeight);
    int x=(int)(Math.Min(at.X,from.Value.X)*sx),y=(int)(Math.Min(at.Y,from.Value.Y)*sy),w=(int)(Math.Abs(at.X-from.Value.X)*sx),h=(int)(Math.Abs(at.Y-from.Value.Y)*sy);
    from=null;if(w<8||h<8){box.Visibility=Visibility.Collapsed;return;}
    x=Math.Max(0,x);y=Math.Max(0,y);w=Math.Min(w,image.PixelWidth-x);h=Math.Min(h,image.PixelHeight-y);finish(new Int32Rect(x,y,w,h));
   };
   layer.KeyDown+=delegate(object sender,KeyEventArgs e) {if(e.Key==Key.Escape){e.Handled=true;finish(null);}};
   host.Children.Add(layer);layer.Focus();Keyboard.Focus(layer);
   return done.Task;
  }
  internal Window screenshotWindow;
  void ShowScreenshot(BitmapSource image,string title) {
   if(screenshotWindow!=null)try {screenshotWindow.Close();}catch(Exception){}
   var win=new Window {Owner=window,Title="Screenshot — Mica",Width=Math.Min(1100,Math.Max(520,image.PixelWidth/2.0+60)),Height=Math.Min(820,Math.Max(380,image.PixelHeight/2.0+120)),WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false};
   if(smoke){win.WindowStartupLocation=WindowStartupLocation.Manual;win.Left=-6000;win.Top=-6000;}
   win.SetResourceReference(Window.BackgroundProperty,"Canvas");win.Resources=window.Resources;screenshotWindow=win;win.Closed+=delegate {if(screenshotWindow==win)screenshotWindow=null;};
   var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition());win.Content=layout;
   // Marking up: a pen and a highlighter, drawn at the picture's own size so the saved image keeps them.
   var surface=new Grid {Width=image.PixelWidth,Height=image.PixelHeight,Tag="screenshot-surface"};surface.Children.Add(new Image {Source=image,Width=image.PixelWidth,Height=image.PixelHeight});
   var ink=new InkCanvas {Background=Brushes.Transparent,EditingMode=InkCanvasEditingMode.None,Tag="screenshot-ink"};surface.Children.Add(ink);
   var scroll=new ScrollViewer {HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=new Viewbox {Child=surface,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly},Padding=new Thickness(16)};
   Grid.SetRow(scroll,1);layout.Children.Add(scroll);
   var bar=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(12,10,12,4)};layout.Children.Add(bar);
   Func<string,string,Action,Button> button=delegate(string text,string tag,Action run) {var b=new Button {Content=text,Padding=new Thickness(12,5,12,5),Margin=new Thickness(0,0,6,0),Tag=tag};b.Click+=delegate {run();};bar.Children.Add(b);return b;};
   Button pen=null,marker=null;
   Action<string> tool=delegate(string which) {
    if(which=="none"){ink.EditingMode=InkCanvasEditingMode.None;}
    else {ink.EditingMode=InkCanvasEditingMode.Ink;var a=ink.DefaultDrawingAttributes;a.IsHighlighter=which=="marker";a.Color=which=="marker"?Color.FromRgb(0xFF,0xE2,0x6A):Color.FromRgb(0xE5,0x3E,0x3E);a.Width=a.Height=which=="marker"?Math.Max(14,image.PixelWidth/90.0):Math.Max(3,image.PixelWidth/400.0);a.FitToCurve=true;}
    pen.SetResourceReference(Button.BackgroundProperty,which=="pen"?"Hover":"Canvas");marker.SetResourceReference(Button.BackgroundProperty,which=="marker"?"Hover":"Canvas");
   };
   pen=button("Pen","screenshot-pen",delegate {tool(ink.EditingMode==InkCanvasEditingMode.Ink&&!ink.DefaultDrawingAttributes.IsHighlighter?"none":"pen");});
   marker=button("Highlighter","screenshot-marker",delegate {tool(ink.EditingMode==InkCanvasEditingMode.Ink&&ink.DefaultDrawingAttributes.IsHighlighter?"none":"marker");});
   button("Undo","screenshot-undo",delegate {if(ink.Strokes.Count>0)ink.Strokes.RemoveAt(ink.Strokes.Count-1);});
   bar.Children.Add(new Border {Width=12});
   button("Copy","screenshot-copy",delegate {try {Clipboard.SetImage(RenderScreenshot(surface));Status("Screenshot copied.");}catch(Exception e){Status("Couldn't copy it: "+e.Message);}});
   button("Save…","screenshot-save",delegate {
    var dialog=new Microsoft.Win32.SaveFileDialog {Filter="PNG image|*.png",FileName="Screenshot "+DateTime.Now.ToString("yyyy-MM-dd HHmmss")+".png",InitialDirectory=Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)};
    if(dialog.ShowDialog(win)!=true)return;
    try {SaveScreenshot(surface,dialog.FileName);Status("Saved "+Path.GetFileName(dialog.FileName)+".");}catch(Exception e){Status("Couldn't save it: "+e.Message);}
   });
   var size=DialogText(image.PixelWidth+" × "+image.PixelHeight,11,"Muted");size.VerticalAlignment=VerticalAlignment.Center;size.Margin=new Thickness(10,0,0,0);bar.Children.Add(size);
   win.KeyDown+=delegate(object sender,KeyEventArgs e) {if(e.Key==Key.Escape)win.Close();else if(e.Key==Key.C&&Keyboard.Modifiers==ModifierKeys.Control){try {Clipboard.SetImage(RenderScreenshot(surface));Status("Screenshot copied.");}catch(Exception){}}};
   win.Show();
  }
  internal static BitmapSource RenderScreenshot(FrameworkElement surface) {
   surface.Measure(new Size(surface.Width,surface.Height));surface.Arrange(new Rect(0,0,surface.Width,surface.Height));
   var target=new RenderTargetBitmap((int)surface.Width,(int)surface.Height,96,96,PixelFormats.Pbgra32);target.Render(surface);target.Freeze();return target;
  }
  internal static void SaveScreenshot(FrameworkElement surface,string path) {
   var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(RenderScreenshot(surface)));using(var file=File.Create(path))encoder.Save(file);
  }

  // ---- Forget a site when its last tab closes ----
  internal bool AutoClears(string site) {return site!=null&&state.AutoClearSites!=null&&state.AutoClearSites.Contains(site);}
  internal void ToggleAutoClear(string site) {
   if(site==null||privateMode)return;bool on=!AutoClears(site);if(on)state.AutoClearSites.Add(site);else state.AutoClearSites.Remove(site);Save();
   Status(on?"Mica forgets "+site+"'s cookies and data when you close its last tab.":site+" is remembered again.");
  }
  readonly Dictionary<string,string> tabSite=new Dictionary<string,string>();
  // Called when a tab closes or leaves a site: once no tab in this profile (and jar) shows it, its data goes.
  void LeftSite(string site,string jar) {
   if(site==null||!AutoClears(site))return;
   window.Dispatcher.BeginInvoke(new Action(async delegate {
    await Task.Delay(1200);if(closing)return;
    bool still=Program.Browsers.Where(b=>!b.closing&&b.profile==profile).Any(b=>b.state.Tabs.Any(t=>t.Jar==jar&&Navigation.IsWeb(t.Url)&&SiteFor(t.Url)==site));
    if(!still)await ClearSiteData(site,jar);
   }));
  }
  internal async Task ClearSiteData(string site,string jar) {
   WebView2 helper=null;CoreWebView2 core=null;
   foreach(var pair in views){var tab=state.Tabs.FirstOrDefault(t=>t.Id==pair.Key);var c=LiveCore(pair.Value);if(tab!=null&&tab.Jar==jar&&c!=null){core=c;break;}}
   try {
    if(core==null) {
     helper=new WebView2 {Width=1,Height=1,Visibility=Visibility.Hidden,IsHitTestVisible=false,Focusable=false};Find<Grid>("Root").Children.Add(helper);
     var engine=await EnsureEnvironment();await helper.EnsureCoreWebView2Async(engine,ViewOptions(engine,jar));core=helper.CoreWebView2;RegisterOwnTarget(core);
    }
    foreach(var cookie in await core.CookieManager.GetCookiesAsync(null)) {
     string domain=cookie.Domain.TrimStart('.').ToLowerInvariant();if(domain==site||domain.EndsWith("."+site))core.CookieManager.DeleteCookie(cookie);
    }
    foreach(var origin in new[]{"https://"+site,"https://www."+site,"http://"+site,"http://www."+site})
     await core.CallDevToolsProtocolMethodAsync("Storage.clearDataForOrigin","{\"origin\":\""+origin+"\",\"storageTypes\":\"all\"}");
    Status("Forgot "+site+": its cookies and data are gone.");
   }catch(Exception e){Status("Couldn't forget "+site+": "+e.Message);}
   finally {if(helper!=null){Find<Grid>("Root").Children.Remove(helper);try {helper.Dispose();}catch(Exception){}}}
  }

  // ---- What's playing, in the island: a tab you're not looking at that makes sound ----
  StackPanel islandMedia;
  internal bool IslandHasMedia {get {return islandMedia!=null&&islandMedia.Children.Count>0;}}
  string islandMediaTab,islandMediaShown;DateTime islandMediaPausedAt;
  internal void UpdateIslandMedia() {
   if(islandMedia==null||closing)return;
   PageEntry playing=null;
   foreach(var tab in state.Tabs.OrderByDescending(t=>{DateTime at;return audibleAt.TryGetValue(t.Id,out at)?at:DateTime.MinValue;})) {
    WebView2 view;var core=views.TryGetValue(tab.Id,out view)?LiveCore(view):null;if(core==null||tab.Id==state.ActiveId)continue;
    bool sound=false;try {sound=core.IsDocumentPlayingAudio||core.IsMuted&&RecentlyAudible(tab.Id,view);}catch(Exception){}
    if(sound||tab.Id==islandMediaTab&&DateTime.UtcNow-islandMediaPausedAt<TimeSpan.FromMinutes(1)){playing=tab;break;}
   }
   islandMedia.Children.Clear();islandMediaShown=playing==null?null:playing.Id;if(playing==null){PlaceIsland();return;}
   var t2=playing;WebView2 v2;views.TryGetValue(t2.Id,out v2);var c2=LiveCore(v2);bool muted=false,audible=false;try {muted=c2.IsMuted;audible=c2.IsDocumentPlayingAudio;}catch(Exception){}
   var row=new Grid {Width=250,Margin=new Thickness(-4,0,-6,6),Tag="island-media"};
   row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(22)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
   var icon=new TextBlock {Text="",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=12,VerticalAlignment=VerticalAlignment.Center};icon.SetResourceReference(TextBlock.ForegroundProperty,"Accent");row.Children.Add(icon);
   var name=new TextBlock {Text=t2.Title,FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,Cursor=Cursors.Hand,ToolTip="Go to the tab"};name.SetResourceReference(TextBlock.ForegroundProperty,"Ink");Grid.SetColumn(name,1);row.Children.Add(name);
   name.MouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e) {e.Handled=true;if(t2.Space!=state.Space)state.Space=t2.Space;SelectTab(t2.Id);};
   Func<string,string,int,Action,Button> small=delegate(string glyph,string tip,int column,Action run) {
    var b=new Button {Content=glyph,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=10,Width=24,Height=22,Padding=new Thickness(0),ToolTip=tip,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Tag="island-media-"+column};
    b.SetResourceReference(Button.ForegroundProperty,"Ink");b.Click+=delegate {run();};Grid.SetColumn(b,column);row.Children.Add(b);return b;
   };
   small(audible?"":"",audible?"Pause":"Play",2,delegate {PlayPause(t2.Id);});
   small(muted?"":"",muted?"Unmute":"Mute",3,delegate {ToggleMute(t2.Id);});
   islandMedia.Children.Add(row);
   if(islandAway&&islandCard!=null){islandAway=false;Animate(islandCard,UIElement.OpacityProperty,1,220);islandCard.IsHitTestVisible=true;}
   PlaceIsland();
  }
  internal async void PlayPause(string id) {
   WebView2 view;var core=views.TryGetValue(id,out view)?LiveCore(view):null;if(core==null)return;
   const string script="(()=>{const m=[...document.querySelectorAll('video,audio')];const p=m.find(x=>!x.paused&&!x.ended);if(p){p.pause();return 'paused';}const q=m.find(x=>x.currentTime>0)||m[0];if(q){q.play();return 'playing';}return 'none';})()";
   try {
    string reply=await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"expression",script},{"userGesture",true},{"returnByValue",true}}));
    if(reply.Contains("\"paused\"")){islandMediaTab=id;islandMediaPausedAt=DateTime.UtcNow;}
   }catch(Exception){}
   // Sound can take a moment to start (or stop); look again once it has.
   await Task.Delay(300);UpdateIslandMedia();RefreshTabRow(id);await Task.Delay(900);if(!closing){UpdateIslandMedia();RefreshTabRow(id);}
  }
 }
 partial class BrowserApp {
  // The site card's rows for this site's memory saver, forgetting it and its cookie jar.
  void SiteCardExtras(StackPanel body,PageEntry tab,WebView2 view,string site) {
   Func<string,string,FrameworkElement,Grid> row=delegate(string title,string note,FrameworkElement choice) {
    var grid=new Grid {Margin=new Thickness(12,4,8,4)};grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
    var text=new StackPanel();text.Children.Add(DialogText(title,12,"Ink"));if(note!=null){var n=DialogText(note,10,"Muted");n.TextWrapping=TextWrapping.Wrap;text.Children.Add(n);}grid.Children.Add(text);
    Grid.SetColumn(choice,1);choice.VerticalAlignment=VerticalAlignment.Center;grid.Children.Add(choice);body.Children.Add(grid);return grid;
   };
   string mode=SiteMemoryMode(tab);
   var memory=SegmentedChoice(new[]{"Usual","Awake","Rest soon"},mode=="never"?1:mode=="quick"?2:0,delegate(int picked,Action undo) {SetSiteMemory(site,picked==1?"never":picked==2?"quick":null);});
   long bytes;string used=tabMemory.TryGetValue(tab.Id,out bytes)?"Using "+Math.Max(1,bytes>>20)+" MB now":null;row("Memory saver",used,memory).Tag="site-memory";
   if(!privateMode) {
    var forget=SegmentedChoice(new[]{"Keep","Forget"},AutoClears(site)?1:0,delegate(int picked,Action undo) {if((picked==1)!=AutoClears(site))ToggleAutoClear(site);});
    row("When its last tab closes",null,forget).Tag="site-forget";
    var jar=JarOf(tab);
    var change=new Button {Content=jar==null?"None":jar.Name,Padding=new Thickness(10,3,10,3),Tag="site-jar",ToolTip="Move this tab into a cookie jar"};
    change.Click+=delegate {var menu=CreateContextMenu();foreach(var item in JarMenu(tab).Items.Cast<object>().ToList()){var element=item as Control;var parent=LogicalTreeHelper.GetParent(element) as ItemsControl;if(parent!=null)parent.Items.Remove(item);menu.Items.Add(item);}menu.PlacementTarget=change;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;siteCard.IsOpen=false;menu.IsOpen=true;};
    row("Cookie jar",jar==null?"This profile's own cookies":"Separate cookies and sign-ins",change);
   }
  }
 }
}
