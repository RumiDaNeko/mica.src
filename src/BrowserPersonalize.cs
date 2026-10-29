using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Mica {
 // Personalize: a home background (a soft gradient preset or your own photo) with frosted glass
 // behind the clock, search and sites so they stay easy to read, and a colour for the frame
 // around the page.
 partial class BrowserApp {
  Window personalizeWindow;
  internal static readonly string[][] HomePresets={
   // name, base, three glow colours
   new[]{"Mist","#ECEAF2","#C9D6F0","#F0D8E4","#DCD2F2"},
   new[]{"Blossom","#F7E6EC","#F2A7C3","#C9B6F2","#FFDCC2"},
   new[]{"Meadow","#DDE9CF","#9CC48A","#F3E7B0","#7FB7A4"},
   new[]{"Lagoon","#0F3644","#2BB3A3","#8FD3F4","#1E6091"},
   new[]{"Dusk","#2B2748","#F28C8C","#7B5CC4","#F7C59F"},
   new[]{"Ember","#26171C","#E2725B","#F4A259","#7A2E3A"},
  };
  internal static readonly string[] FrameSwatches={"#DDD7E5","#E8DCCF","#D3E2D6","#CFDDEB","#F0D6DE","#2F2A3A","#1F3B45","#3A2A33","#6E5A8E","#C46F5A","#3E7A6E","#202024"};

  static Color ParseColor(string hex) {try {return (Color)ColorConverter.ConvertFromString(hex);}catch(Exception){return Colors.Transparent;}}
  static string Hex(Color c) {return "#"+c.R.ToString("X2")+c.G.ToString("X2")+c.B.ToString("X2");}
  static double Luminance(Color c) {
   Func<byte,double> lin=delegate(byte v) {double s=v/255.0;return s<=0.03928?s/12.92:Math.Pow((s+0.055)/1.055,2.4);};
   return 0.2126*lin(c.R)+0.7152*lin(c.G)+0.0722*lin(c.B);
  }
  static Color Mix(Color a,Color b,double t) {return Color.FromRgb((byte)(a.R+(b.R-a.R)*t),(byte)(a.G+(b.G-a.G)*t),(byte)(a.B+(b.B-a.B)*t));}
  // Light text when it contrasts more than dark text would (WCAG contrast; they tie near luminance 0.18).
  internal static bool WantsLightText(Color c) {double l=Luminance(c);return 1.05/(l+0.05)>(l+0.05)/0.05;}
  bool SidebarIsDark() {return WantsLightText(FrameColor());}
  Brush SideBrush(string key) {var found=Find<Grid>("Sidebar").TryFindResource(key) as Brush;return found??Brush(key);}

  // The colour behind the sidebar and around the page. Dark mode deepens a custom colour so it stays calm at night.
  Color FrameColor() {
   if(string.IsNullOrEmpty(state.FrameColor))return ParseColor(state.Dark?"#332C40":"#DDD7E5");
   var chosen=ParseColor(state.FrameColor);
   // Black is the one colour that flips with the theme: a white frame in light mode.
   if(!state.Dark&&IsBlack(chosen))return ParseColor("#F1F1F2");
   return state.Dark&&Luminance(chosen)>0.08?Mix(chosen,Color.FromRgb(0x1A,0x18,0x20),0.55):chosen;
  }
  void ApplyFrame() {
   var frame=FrameColor();window.Background=new SolidColorBrush(frame);
   bool custom=!string.IsNullOrEmpty(state.FrameColor);
   // Sidebar text follows the frame: light on dark colours, dark on light ones.
   bool lightText=WantsLightText(frame);var side=Find<Grid>("Sidebar").Resources;
   foreach(var key in new[]{"Ink","Muted","Panel","SideCard","Hover","Line","Accent"})side.Remove(key);
   if(custom) {
    // Cards, the active tab and hovers are flat tints of the frame colour itself, lifted towards white.
    Color white=Colors.White,black=Colors.Black;
    side["Ink"]=new SolidColorBrush(lightText?ParseColor("#F7F4FA"):Mix(frame,black,0.82));
    side["Muted"]=new SolidColorBrush(lightText?Mix(frame,white,0.68):Mix(frame,black,0.6));
    side["SideCard"]=new SolidColorBrush(lightText?Mix(frame,white,0.11):Mix(frame,white,0.5));
    side["Panel"]=new SolidColorBrush(lightText?Mix(frame,white,0.19):Mix(frame,white,0.78));
    side["Hover"]=new SolidColorBrush(lightText?Mix(frame,white,0.15):Mix(frame,white,0.6));
    side["Line"]=new SolidColorBrush(lightText?Mix(frame,white,0.2):Mix(frame,black,0.12));
    side["Accent"]=new SolidColorBrush(lightText?Mix(frame,white,0.72):Mix(frame,black,0.5));
   }
   else if(lightText!=state.Dark) {
    string[] values=lightText?new[]{"#F5F2F9","#CFC8DA","#2EFFFFFF","#1FFFFFFF","#24FFFFFF","#33FFFFFF","#E2D4F5"}:new[]{"#2A2730","#55505F","#B8FFFFFF","#60FFFFFF","#55FFFFFF","#30000000","#5E4D78"};
    string[] keys={"Ink","Muted","Panel","SideCard","Hover","Line","Accent"};
    for(int i=0;i<keys.Length;i++)side[keys[i]]=new SolidColorBrush(ParseColor(values[i]));
   }
   // Icons everywhere (menus, dialogs, the toolbar) use the frame's hue as their accent.
   double h,s,v;FrameHsv(custom?ParseColor(state.FrameColor):frame,out h,out s,out v);bool grey=s<0.12;
   // Menus, the island and other popups share the top bar's tone of the frame colour.
   if(custom){var tone=BarTone(0);window.Resources["PopupPanel"]=new SolidColorBrush(tone);window.Resources["PopupLine"]=new SolidColorBrush(state.Dark?Mix(tone,Colors.White,0.12):Mix(tone,Colors.Black,0.12));}
   else {window.Resources["PopupPanel"]=window.Resources["Panel"];window.Resources["PopupLine"]=window.Resources["Line"];}
   window.Resources["Accent"]=new SolidColorBrush(!custom?ParseColor(state.Dark?"#C8B4E0":"#71608C"):state.Dark?FromHsv(h,grey?s:Math.Min(Math.Max(s,0.22),0.45),0.86):FromHsv(h,grey?s:Math.Min(Math.Max(s,0.3),0.6),0.46));
   window.Resources["Hover"]=new SolidColorBrush(!custom?ParseColor(state.Dark?"#453C52":"#ECE7F2"):state.Dark?FromHsv(h,Math.Min(s,0.22),0.3):FromHsv(h,Math.Min(s,0.09),0.94));
   // A clear edge between the frame and the page, like the default look has.
   var page=Find<Border>("ContentFrame");
   if(custom)page.BorderBrush=new SolidColorBrush(state.Dark?Mix(frame,Colors.White,0.2):Mix(frame,Colors.Black,0.16));
   else page.BorderBrush=new SolidColorBrush(Color.FromArgb(0x30,255,255,255));
   // Theme switch shows what it will switch to.
   foreach(var name in new[]{"ThemeButton","MiniTheme"}) {
    var toggle=Find<Button>(name);toggle.Content=state.Dark?"":"";toggle.ToolTip=state.Dark?"Switch to light mode":"Switch to dark mode";
   }
   ApplyBar();
  }
  static bool IsBlack(Color c) {return Math.Max(c.R,Math.Max(c.G,c.B))<=0x30&&Math.Max(c.R,Math.Max(c.G,c.B))-Math.Min(c.R,Math.Min(c.G,c.B))<=10;}
  // Near-greys (black, charcoal, silver) have a hue only by rounding; tinting with it turns black lavender.
  static void FrameHsv(Color color,out double h,out double s,out double v) {
   ToHsv(color,out h,out s,out v);
   int chroma=Math.Max(color.R,Math.Max(color.G,color.B))-Math.Min(color.R,Math.Min(color.G,color.B));
   if(chroma<=10||s<0.1)s=0;
  }
  // A pale (or, in dark mode, deep) version of the frame's own hue, so the bar reads as the same colour family.
  Color BarTone(int step) {
   double h,s,v;FrameHsv(FrameColor(),out h,out s,out v);
   return state.Dark?FromHsv(h,Math.Min(s,0.32),step==0?0.25:0.21):FromHsv(h,Math.Min(s,step==0?0.06:0.08),step==0?0.975:0.945);
  }
  // The top bar takes a little of the frame colour; over a home background it turns to frosted glass.
  void ApplyBar() {
   var bar=Find<Border>("ToolbarBar");
   bool custom=!string.IsNullOrEmpty(state.FrameColor);
   if(custom)bar.BorderBrush=new SolidColorBrush(state.Dark?Mix(BarTone(0),Colors.White,0.12):Mix(BarTone(0),Colors.Black,0.12));else bar.SetResourceReference(Border.BorderBrushProperty,"Line");
   bool frosted=Find<Grid>("HomeBackdropLayers").Visibility==Visibility.Visible&&Find<ScrollViewer>("Home").Visibility==Visibility.Visible;
   if(frosted){bar.Background=Brushes.Transparent;return;}
   bar.Background=custom?new SolidColorBrush(BarTone(0)):null;
  }

  static Brush PresetBrush(string[] preset) {
   var group=new DrawingGroup();
   group.Children.Add(new GeometryDrawing(new SolidColorBrush(ParseColor(preset[1])),null,new RectangleGeometry(new Rect(0,0,1,1))));
   var spots=new[]{new Point(0.12,0.85),new Point(0.88,0.12),new Point(0.92,0.92)};
   for(int i=0;i<3;i++) {
    var glow=new RadialGradientBrush(ParseColor(preset[2+i]),Color.FromArgb(0,ParseColor(preset[2+i]).R,ParseColor(preset[2+i]).G,ParseColor(preset[2+i]).B));
    group.Children.Add(new GeometryDrawing(glow,null,new EllipseGeometry(spots[i],0.75,0.75)));
   }
   group.ClipGeometry=new RectangleGeometry(new Rect(0,0,1,1));
   var brush=new DrawingBrush(group) {Stretch=Stretch.Fill};brush.Freeze();return brush;
  }
  string HomePhotoPath() {
   try {return Directory.GetFiles(profile,"home-photo.*").FirstOrDefault();}catch(Exception){return null;}
  }
  static ImageBrush PhotoBrush(string path,int width) {
   var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=width;image.UriSource=new Uri(path);image.EndInit();image.Freeze();
   var brush=new ImageBrush(image) {Stretch=Stretch.UniformToFill};brush.Freeze();return brush;
  }
  Brush HomeBackgroundBrush(string name,int width) {
   if(string.IsNullOrEmpty(name))return null;
   if(name=="photo"){string path=HomePhotoPath();try {return path==null?null:PhotoBrush(path,width);}catch(Exception){return null;}}
   var preset=HomePresets.FirstOrDefault(p=>p[0]==name);return preset==null?null:PresetBrush(preset);
  }
  int HomeGlassStrength() {return Math.Max(0,Math.Min(100,state.HomeGlass??60));}
  // The frosted layer is a small blurred picture made once, not a live blur: a live BlurEffect over the
  // whole background is redone on every frame of any animation that resizes the page (sidebar, Spotlight).
  string blurKey;Brush blurBrush;
  Brush BlurredBackground(Brush source,double radius) {
   string key=state.HomeBackground+"|"+radius+"|"+(state.HomeBackground=="photo"?HomePhotoPath()+File.GetLastWriteTimeUtc(HomePhotoPath()??"").Ticks:"");
   if(key==blurKey&&blurBrush!=null)return blurBrush;
   var photo=source as ImageBrush;var image=photo==null?null:photo.ImageSource as BitmapSource;
   double aspect=image!=null&&image.PixelHeight>0?image.PixelWidth/(double)image.PixelHeight:1.6;
   int w=512,h=Math.Max(64,(int)Math.Round(w/aspect));
   double r=Math.Max(2,radius/2);int m=(int)Math.Ceiling(r*2)+2;
   // Mirror the picture around its edges so the blur doesn't fade into transparency at the borders.
   var tiled=source.Clone();var tile=tiled as TileBrush;
   if(tile!=null){tile.TileMode=TileMode.FlipXY;tile.ViewportUnits=BrushMappingMode.Absolute;tile.Viewport=new Rect(m,m,w,h);tile.Stretch=photo!=null?Stretch.UniformToFill:Stretch.Fill;}
   var host=new Rectangle {Width=w+2*m,Height=h+2*m,Fill=tiled,Effect=new BlurEffect {Radius=r,KernelType=KernelType.Gaussian,RenderingBias=RenderingBias.Quality}};
   host.Measure(new Size(host.Width,host.Height));host.Arrange(new Rect(0,0,host.Width,host.Height));
   var bitmap=new RenderTargetBitmap(w+2*m,h+2*m,96,96,PixelFormats.Pbgra32);bitmap.Render(host);
   var crop=new CroppedBitmap(bitmap,new Int32Rect(m,m,w,h));crop.Freeze();
   var result=new ImageBrush(crop) {Stretch=photo!=null?Stretch.UniformToFill:Stretch.Fill};result.Freeze();
   blurKey=key;blurBrush=result;return result;
  }

  bool homeBackdropWired;
  void ApplyHomeBackdrop() {
   var layers=Find<Grid>("HomeBackdropLayers");var brush=HomeBackgroundBrush(state.HomeBackground,2400);
   if(!homeBackdropWired) {
    homeBackdropWired=true;
    var backdrop=Find<Grid>("HomeBackdrop");
    backdrop.SizeChanged+=delegate {backdrop.Clip=new RectangleGeometry(new Rect(0,0,backdrop.ActualWidth,backdrop.ActualHeight),11,11);UpdateHomeGlass();};
    Find<ScrollViewer>("Home").IsVisibleChanged+=delegate {ApplyBar();};
    Find<ScrollViewer>("Home").ScrollChanged+=delegate {UpdateHomeGlass();};
    Find<StackPanel>("HomeColumn").SizeChanged+=delegate {UpdateHomeGlass();};
   }
   Find<Viewbox>("HomeArtwork").Visibility=brush==null?Visibility.Visible:Visibility.Collapsed;
   if(brush==null){layers.Visibility=Visibility.Collapsed;Find<Rectangle>("HomeWallpaper").Fill=null;Find<Rectangle>("HomeWallpaperBlur").Fill=null;ApplyBar();return;}
   layers.Visibility=Visibility.Visible;
   Find<Rectangle>("HomeWallpaper").Fill=brush;
   var blurred=Find<Rectangle>("HomeWallpaperBlur");blurred.Effect=null;glassArea=Rect.Empty;
   var canvas=((SolidColorBrush)Brush("Canvas")).Color;
   // A light veil at the top and bottom keeps the date and footer readable on busy photos.
   var veil=new LinearGradientBrush {StartPoint=new Point(0,0),EndPoint=new Point(0,1)};
   veil.GradientStops.Add(new GradientStop(Color.FromArgb(205,canvas.R,canvas.G,canvas.B),0));veil.GradientStops.Add(new GradientStop(Color.FromArgb(150,canvas.R,canvas.G,canvas.B),0.07));veil.GradientStops.Add(new GradientStop(Color.FromArgb(0,canvas.R,canvas.G,canvas.B),0.2));
   veil.GradientStops.Add(new GradientStop(Color.FromArgb(0,canvas.R,canvas.G,canvas.B),0.8));veil.GradientStops.Add(new GradientStop(Color.FromArgb(150,canvas.R,canvas.G,canvas.B),0.93));veil.GradientStops.Add(new GradientStop(Color.FromArgb(205,canvas.R,canvas.G,canvas.B),1));
   Find<Rectangle>("HomeScrim").Fill=veil;
   UpdateHomeGlass();ApplyBar();
  }
  // Frosted glass: a blurred copy of the background, clipped to a rounded card behind the home column, with a soft tint.
  Rect glassArea=Rect.Empty;Size glassSize;int glassStrength=-1;
  void UpdateHomeGlass() {
   var layers=Find<Grid>("HomeBackdropLayers");if(layers.Visibility!=Visibility.Visible)return;
   var backdrop=Find<Grid>("HomeBackdrop");var column=Find<StackPanel>("HomeColumn");var blurred=Find<Rectangle>("HomeWallpaperBlur");var glass=Find<System.Windows.Shapes.Path>("HomeGlass");
   int strength=HomeGlassStrength();var canvas=((SolidColorBrush)Brush("Canvas")).Color;
   if(backdrop.ActualWidth<1)return;
   // Nothing moved: keep the shapes we have (this runs on every layout pass while things animate).
   Rect columnArea=Rect.Empty;
   if(column.IsVisible)try {columnArea=column.TransformToVisual(backdrop).TransformBounds(new Rect(0,0,column.ActualWidth,column.ActualHeight));}catch(InvalidOperationException){}
   var size=new Size(backdrop.ActualWidth,backdrop.ActualHeight);
   if(columnArea==glassArea&&size==glassSize&&strength==glassStrength&&blurred.Fill!=null)return;
   glassArea=columnArea;glassSize=size;glassStrength=strength;
   var source=Find<Rectangle>("HomeWallpaper").Fill;if(source!=null)blurred.Fill=BlurredBackground(source,strength>0?12+strength*0.5:30);
   // The top bar is always frosted over a background; the card behind the home column follows the slider.
   var barArea=new RectangleGeometry(new Rect(0,0,backdrop.ActualWidth,44));barArea.Freeze();
   var frosted=new GeometryGroup();frosted.Children.Add(barArea);
   var tint=Find<Rectangle>("HomeBarTint");var tone=string.IsNullOrEmpty(state.FrameColor)?canvas:BarTone(0);
   tint.Fill=new SolidColorBrush(Color.FromArgb(state.Dark?(byte)140:(byte)160,tone.R,tone.G,tone.B));
   bool card=strength>0&&column.IsVisible;
   if(card) {
    Rect area;try {area=column.TransformToVisual(backdrop).TransformBounds(new Rect(0,0,column.ActualWidth,column.ActualHeight));}catch(InvalidOperationException){return;}
    area.Inflate(30,26);
    var shape=new RectangleGeometry(area,26,26);shape.Freeze();frosted.Children.Add(shape);
    glass.Data=shape;glass.Fill=new SolidColorBrush(Color.FromArgb((byte)(40+strength*1.5),canvas.R,canvas.G,canvas.B));
    glass.Stroke=new SolidColorBrush(state.Dark?Color.FromArgb(34,255,255,255):Color.FromArgb(120,255,255,255));
   }
   glass.Visibility=card?Visibility.Visible:Visibility.Collapsed;
   frosted.Freeze();blurred.Visibility=Visibility.Visible;blurred.Clip=frosted;
  }

  // Mica's scrollbar on every page: a slim rounded thumb on a clear track that firms up on hover.
  // Standard scrollbar-color/width would override the ::-webkit-scrollbar rules, so they are reset.
  internal const string ScrollbarScript=@"(function(){
 if(window.__micaScrollbar)return;window.__micaScrollbar=1;
 var css='*{scrollbar-color:auto!important;scrollbar-width:auto!important}'+
  '::-webkit-scrollbar{width:11px!important;height:11px!important;background:transparent!important}'+
  '::-webkit-scrollbar-track,::-webkit-scrollbar-corner{background:transparent!important;border:0!important}'+
  '::-webkit-scrollbar-thumb{background:rgba(124,114,142,.42)!important;border-radius:8px!important;border:3px solid transparent!important;background-clip:padding-box!important;min-height:36px}'+
  '::-webkit-scrollbar-thumb:hover{background:rgba(124,114,142,.72)!important;border-width:2px!important;background-clip:padding-box!important}'+
  '::-webkit-scrollbar-thumb:active{background:rgba(113,96,140,.88)!important;background-clip:padding-box!important}'+
  '::-webkit-scrollbar-button{display:none!important;width:0!important;height:0!important}';
 var style=document.createElement('style');style.setAttribute('data-mica','scrollbar');style.textContent=css;
 function put(){var host=document.head||document.documentElement;if(host&&style.parentNode!==host)host.appendChild(style);}
 if(document.documentElement)put();
 new MutationObserver(function(m,o){if(document.head){put();o.disconnect();}else put();}).observe(document,{childList:true,subtree:true});
 document.addEventListener('DOMContentLoaded',put);
})();";

  void ApplyPersonalization() {ApplyFrame();ApplyHomeBackdrop();}
  // Shows the sidebar's scrollbar only when the tab list really overflows.
  void WatchSidebarScroll() {
   foreach(var name in new[]{"ExpandedTabScroller"}) {
    var scroller=Find<ScrollViewer>(name);
    scroller.ScrollChanged+=delegate {var wanted=scroller.ExtentHeight-scroller.ViewportHeight>4?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden;if(scroller.VerticalScrollBarVisibility!=wanted)scroller.VerticalScrollBarVisibility=wanted;};
   }
  }
  // Right-click the home page: change its background without opening the menu.
  void WatchHomeMenu() {
   var home=Find<ScrollViewer>("Home");
   home.MouseRightButtonUp+=delegate(object sender,MouseButtonEventArgs e) {
    if(e.OriginalSource is DependencyObject&&FindParent<Button>((DependencyObject)e.OriginalSource)!=null)return;
    var menu=new ContextMenu {PlacementTarget=home,Resources=window.Resources};
    Action<string,string,Action> item=delegate(string glyph,string text,Action act) {
     var entry=new MenuItem {Header=text,Icon=new TextBlock {Text=glyph,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=13}};entry.Click+=delegate {act();};menu.Items.Add(entry);
    };
    item("","Change background…",delegate {ChooseHomePhoto(window);});
    if(!string.IsNullOrEmpty(state.HomeBackground))item("","Remove background",delegate {state.HomeBackground=null;PersonalizeEverywhere();});
    item("","Personalize…",ShowPersonalize);
    menu.IsOpen=true;e.Handled=true;
   };
  }
  static T FindParent<T>(DependencyObject child) where T:DependencyObject {
   while(child!=null&&!(child is T))child=child is Visual||child is System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(child):LogicalTreeHelper.GetParent(child);
   return child as T;
  }
  bool ChooseHomePhoto(Window owner) {
   var picker=new Microsoft.Win32.OpenFileDialog {Title="Choose a home background",Filter="Pictures|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.heic|All files|*.*"};
   if(picker.ShowDialog(owner)!=true||!SetHomePhoto(picker.FileName))return false;
   state.HomeBackground="photo";PersonalizeEverywhere();return true;
  }
  void PersonalizeEverywhere() {
   foreach(var browser in Program.Browsers.ToArray())if(!browser.closing){
    if(browser!=this){browser.state.FrameColor=state.FrameColor;browser.state.HomeBackground=state.HomeBackground;browser.state.HomeGlass=state.HomeGlass;browser.state.CloseEffect=state.CloseEffect;}
    browser.ApplyPersonalization();browser.RenderTabs();browser.Save();
   }
  }

  void ShowPersonalize() {
   CloseMenus();if(personalizeWindow!=null){personalizeWindow.Activate();return;}
   StackPanel body,actions;var dialog=CreateMicaDialog(window,"Personalize",480,900,out body,out actions);personalizeWindow=dialog;
   dialog.Closed+=delegate {personalizeWindow=null;};
   // Text size first: someone who needs it shouldn't have to scroll to find it.
   SectionLabel(body,"TEXT SIZE");
   var sizes=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,4),Tag="text-sizes"};body.Children.Add(sizes);var sizeButtons=new List<Button>();
   Action markSizes=delegate {for(int i=0;i<sizeButtons.Count;i++){bool on=Math.Abs(TextSizes[i]-UiScale)<0.01;sizeButtons[i].FontWeight=on?FontWeights.SemiBold:FontWeights.Normal;if(on)sizeButtons[i].SetResourceReference(Button.BackgroundProperty,"Hover");else sizeButtons[i].ClearValue(Button.BackgroundProperty);}};
   for(int i=0;i<TextSizes.Length;i++) {
    double size=TextSizes[i];
    var choice=new Button {Content=TextSizeNames[i],FontSize=12*size,Padding=new Thickness(16,8,16,8),Margin=new Thickness(0,0,8,0),Tag="text-size:"+size,MinWidth=86};
    choice.Click+=delegate {SetTextScale(size);markSizes();};sizeButtons.Add(choice);sizes.Children.Add(choice);
   }
   markSizes();
   var sizeHint=DialogText("Makes Mica's sidebar, menus and windows larger, and opens pages at the same size.",11,"Muted");sizeHint.Margin=new Thickness(2,4,2,10);sizeHint.LineHeight=16;body.Children.Add(sizeHint);
   SectionLabel(body,"HOME BACKGROUND");
   var tiles=new WrapPanel {Margin=new Thickness(0,0,0,4)};body.Children.Add(tiles);
   var tileBorders=new Dictionary<string,Border>();
   Action markTiles=delegate {foreach(var pair in tileBorders){bool on=(pair.Key??"")==(state.HomeBackground??"");pair.Value.BorderThickness=new Thickness(on?2:1);pair.Value.SetResourceReference(Border.BorderBrushProperty,on?"Accent":"Line");}};
   Action<string> choose=delegate(string name) {state.HomeBackground=name;PersonalizeEverywhere();markTiles();};
   Func<string,string,Brush,Border> tile=delegate(string key,string label,Brush fill) {
    var face=new Border {Width=116,Height=70,CornerRadius=new CornerRadius(12),Background=fill,ClipToBounds=true};
    if(fill==null){face.SetResourceReference(Border.BackgroundProperty,"Canvas");var art=DialogText("",18,"Muted");art.FontFamily=new FontFamily("Segoe MDL2 Assets");art.HorizontalAlignment=HorizontalAlignment.Center;art.VerticalAlignment=VerticalAlignment.Center;face.Child=art;}
    var frame=new Border {CornerRadius=new CornerRadius(14),Padding=new Thickness(2),BorderThickness=new Thickness(1),Child=face};
    var name=DialogText(label,11,"Muted");name.HorizontalAlignment=HorizontalAlignment.Center;name.Margin=new Thickness(0,5,0,0);
    var stack=new StackPanel();stack.Children.Add(frame);stack.Children.Add(name);
    var button=new Button {Content=stack,Padding=new Thickness(0),Margin=new Thickness(0,0,10,10),Background=Brushes.Transparent,Tag="bg-"+(key??"none"),ToolTip=label};
    System.Windows.Automation.AutomationProperties.SetName(button,label+" background");
    tileBorders[key??""]=frame;tiles.Children.Add(button);return frame;
   };
   tile(null,"Classic",null);
   ((Button)tiles.Children[0]).Click+=delegate {choose(null);};
   foreach(var preset in HomePresets){var name=preset[0];tile(name,name,PresetBrush(preset));((Button)tiles.Children[tiles.Children.Count-1]).Click+=delegate {choose(name);};}
   var photoFace=tile("photo","Your photo…",HomeBackgroundBrush("photo",400));
   ((Button)tiles.Children[tiles.Children.Count-1]).Click+=delegate {
    if(ChooseHomePhoto(dialog)){((Border)photoFace.Child).Background=HomeBackgroundBrush("photo",400);((Border)photoFace.Child).Child=null;markTiles();}
   };
   if(((Border)photoFace.Child).Background==null){var plus=DialogText("",16,"Muted");plus.FontFamily=new FontFamily("Segoe MDL2 Assets");plus.HorizontalAlignment=HorizontalAlignment.Center;plus.VerticalAlignment=VerticalAlignment.Center;((Border)photoFace.Child).Child=plus;}
   markTiles();
   var glassRow=new Grid {Margin=new Thickness(2,8,2,4)};glassRow.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});glassRow.ColumnDefinitions.Add(new ColumnDefinition());glassRow.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(44)});
   var glassLabel=DialogText("Frosted glass",12,"Ink");glassLabel.VerticalAlignment=VerticalAlignment.Center;glassLabel.Margin=new Thickness(0,0,14,0);glassRow.Children.Add(glassLabel);
   var glassValue=DialogText(HomeGlassStrength()+"%",11,"Muted");glassValue.VerticalAlignment=VerticalAlignment.Center;glassValue.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(glassValue,2);glassRow.Children.Add(glassValue);
   var glassSlider=PillSlider(HomeGlassStrength()/100.0,null,delegate(double v) {state.HomeGlass=(int)Math.Round(v*100);glassValue.Text=state.HomeGlass+"%";foreach(var b in Program.Browsers.ToArray())if(!b.closing){b.state.HomeGlass=state.HomeGlass;b.UpdateHomeGlass();}},delegate {PersonalizeEverywhere();});
   glassSlider.Tag="glass";Grid.SetColumn(glassSlider,1);glassRow.Children.Add(glassSlider);body.Children.Add(glassRow);
   var hint=DialogText("Blurs the background behind the clock, search and your sites so they stay easy to read.",11,"Muted");hint.Margin=new Thickness(2,2,2,10);hint.LineHeight=16;body.Children.Add(hint);

   SectionLabel(body,"FRAME COLOUR");
   var picker2=ColorPicker(string.IsNullOrEmpty(state.FrameColor)?FrameColor():ParseColor(state.FrameColor),delegate(Color c,bool done) {
    state.FrameColor=Hex(c);
    if(done)PersonalizeEverywhere();else foreach(var b in Program.Browsers.ToArray())if(!b.closing){b.state.FrameColor=state.FrameColor;b.ApplyFrame();}
   });
   body.Children.Add(picker2);
   SectionLabel(body,"CLOSING ANIMATION");
   // A small window plays the chosen animation over and over, so you see it before you close Mica.
   var stage=new Border {Height=150,CornerRadius=new CornerRadius(12),ClipToBounds=true,Margin=new Thickness(0,0,0,8),Tag="close-preview"};stage.SetResourceReference(Border.BackgroundProperty,"Canvas");body.Children.Add(stage);
   var effects=new WrapPanel {Margin=new Thickness(0,0,0,4),Tag="close-effects"};body.Children.Add(effects);var effectButtons=new List<Button>();
   EventHandler previewFrame=null;System.Diagnostics.Stopwatch previewClock=null;
   Action stopPreview=delegate {if(previewFrame!=null)System.Windows.Media.CompositionTarget.Rendering-=previewFrame;previewFrame=null;stage.Child=null;};
   Action playPreview=delegate {
    stopPreview();string effect=CloseEffect();
    if(effect=="off"){var none=DialogText("Mica closes at once, without an animation.",11,"Muted");none.HorizontalAlignment=HorizontalAlignment.Center;none.VerticalAlignment=VerticalAlignment.Center;stage.Child=none;return;}
    var sample=ClosePreviewPicture(new Size(180,104));Action<double> seek;double length;
    var scene=FarewellScene(effect,sample,new Size(180,104),FrameColor(),out seek,out length);var holder=new Canvas {Width=180,Height=104};holder.Children.Add(scene);
    stage.Child=new Viewbox {Child=new Border {Width=420,Height=150,Child=new Grid {Children={holder}}},Stretch=Stretch.Uniform};holder.HorizontalAlignment=HorizontalAlignment.Center;holder.VerticalAlignment=VerticalAlignment.Center;
    previewClock=System.Diagnostics.Stopwatch.StartNew();seek(0);
    // Played, then a pause showing the window again, round and round while the dialog is open.
    previewFrame=delegate {double t=previewClock.Elapsed.TotalMilliseconds%(length+900)-350;seek(t<0?0:Math.Min(t,length));};
    System.Windows.Media.CompositionTarget.Rendering+=previewFrame;
   };
   Action markEffects=delegate {for(int i=0;i<effectButtons.Count;i++){bool on=CloseEffects[i][0]==CloseEffect();effectButtons[i].FontWeight=on?FontWeights.SemiBold:FontWeights.Normal;if(on)effectButtons[i].SetResourceReference(Button.BackgroundProperty,"Hover");else effectButtons[i].ClearValue(Button.BackgroundProperty);}};
   foreach(var item in CloseEffects) {
    string key=item[0];var choice=new Button {Content=item[1],Padding=new Thickness(16,8,16,8),Margin=new Thickness(0,0,8,8),Tag="close-effect:"+key,MinWidth=70};
    choice.Click+=delegate {state.CloseEffect=key=="stars"?null:key;PersonalizeEverywhere();markEffects();playPreview();};effectButtons.Add(choice);effects.Children.Add(choice);
   }
   markEffects();dialog.Loaded+=delegate {playPreview();};dialog.Closed+=delegate {stopPreview();};
   var reset=new Button {Content="Use theme colour",Padding=new Thickness(16,9,16,9),Tag="frame-reset"};
   reset.Click+=delegate {state.FrameColor=null;PersonalizeEverywhere();((Action<Color>)picker2.Tag)(FrameColor());};
   actions.Children.Add(reset);
   var done2=new Button {Content="Done",Padding=new Thickness(20,9,20,9),Margin=new Thickness(8,0,0,0)};done2.SetResourceReference(Button.BackgroundProperty,"Hover");done2.Click+=delegate {dialog.Close();};actions.Children.Add(done2);
   dialog.Show();
  }
  // Copies the picture into the profile so the original can move or be deleted.
  internal bool SetHomePhoto(string source) {
   try {
    string ext=System.IO.Path.GetExtension(source).ToLowerInvariant();if(ext.Length==0||ext.Length>6)ext=".img";
    PhotoBrush(source,64);
    foreach(var old in Directory.GetFiles(profile,"home-photo.*"))File.Delete(old);
    File.Copy(source,System.IO.Path.Combine(profile,"home-photo"+ext),true);return true;
   }catch(Exception e){Status("Couldn't use that picture: "+e.Message);return false;}
  }

  // A slim rounded track with a round thumb; reports 0–1 while dragging and once more when released.
  FrameworkElement PillSlider(double value,Brush track,Action<double> changed,Action released) {
   var host=new Grid {Height=22,Background=Brushes.Transparent,Cursor=Cursors.Hand,VerticalAlignment=VerticalAlignment.Center};
   var rail=new Border {Height=6,CornerRadius=new CornerRadius(3),VerticalAlignment=VerticalAlignment.Center};
   if(track!=null)rail.Background=track;else rail.SetResourceReference(Border.BackgroundProperty,"Line");host.Children.Add(rail);
   var fill=new Border {Height=6,CornerRadius=new CornerRadius(3),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Left};if(track==null)fill.SetResourceReference(Border.BackgroundProperty,"Accent");host.Children.Add(fill);
   var thumb=new Ellipse {Width=18,Height=18,Fill=Brushes.White,StrokeThickness=1,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Effect=new DropShadowEffect {BlurRadius=6,ShadowDepth=1,Opacity=0.25}};thumb.SetResourceReference(Shape.StrokeProperty,"Line");host.Children.Add(thumb);
   double current=Math.Max(0,Math.Min(1,value));
   Action place=delegate {double w=Math.Max(0,host.ActualWidth-18);thumb.Margin=new Thickness(current*w,0,0,0);fill.Width=current*w+9;};
   Action<double> set=delegate(double x) {current=Math.Max(0,Math.Min(1,(x-9)/Math.Max(1,host.ActualWidth-18)));place();changed(current);};
   host.SizeChanged+=delegate {place();};
   host.MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e) {host.CaptureMouse();set(e.GetPosition(host).X);e.Handled=true;};
   host.MouseMove+=delegate(object s,MouseEventArgs e) {if(host.IsMouseCaptured)set(e.GetPosition(host).X);};
   host.MouseLeftButtonUp+=delegate {if(host.IsMouseCaptured){host.ReleaseMouseCapture();if(released!=null)released();}};
   host.Tag=(Action<double>)delegate(double v) {current=Math.Max(0,Math.Min(1,v));place();};
   return host;
  }

  static Color FromHsv(double h,double s,double v) {
   h=((h%360)+360)%360;double c=v*s,x=c*(1-Math.Abs(h/60%2-1)),m=v-c;double r=0,g=0,b=0;
   if(h<60){r=c;g=x;}else if(h<120){r=x;g=c;}else if(h<180){g=c;b=x;}else if(h<240){g=x;b=c;}else if(h<300){r=x;b=c;}else {r=c;b=x;}
   return Color.FromRgb((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));
  }
  static void ToHsv(Color color,out double h,out double s,out double v) {
   double r=color.R/255.0,g=color.G/255.0,b=color.B/255.0,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),d=max-min;
   v=max;s=max==0?0:d/max;
   if(d==0)h=0;else if(max==r)h=60*(((g-b)/d)%6);else if(max==g)h=60*((b-r)/d+2);else h=60*((r-g)/d+4);
   if(h<0)h+=360;
  }
  // Mica's colour picker: a saturation/brightness field, a hue rail, curated swatches and a hex field.
  // changed(colour, done): done is false while dragging and true when a choice settles.
  // The returned element's Tag is an Action<Color> that moves the picker to a colour.
  FrameworkElement ColorPicker(Color initial,Action<Color,bool> changed) {
   double hue,sat,val;ToHsv(initial,out hue,out sat,out val);
   var root=new StackPanel();
   var field=new Grid {Height=128,Cursor=Cursors.Cross,ClipToBounds=true,Background=Brushes.Transparent};
   var hueLayer=new Border {CornerRadius=new CornerRadius(12)};
   var whiteLayer=new Border {CornerRadius=new CornerRadius(12),Background=new LinearGradientBrush(Colors.White,Color.FromArgb(0,255,255,255),0)};
   var blackLayer=new Border {CornerRadius=new CornerRadius(12),Background=new LinearGradientBrush(Color.FromArgb(0,0,0,0),Colors.Black,90)};
   var edge=new Border {CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromArgb(28,0,0,0))};
   var ring=new Ellipse {Width=18,Height=18,StrokeThickness=2.5,Stroke=Brushes.White,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false,Effect=new DropShadowEffect {BlurRadius=5,ShadowDepth=0,Opacity=0.45}};
   field.Children.Add(hueLayer);field.Children.Add(whiteLayer);field.Children.Add(blackLayer);field.Children.Add(edge);field.Children.Add(ring);
   field.Tag="color-field";root.Children.Add(field);
   var rainbow=new LinearGradientBrush {StartPoint=new Point(0,0),EndPoint=new Point(1,0)};
   for(int i=0;i<=6;i++)rainbow.GradientStops.Add(new GradientStop(FromHsv(i*60,1,1),i/6.0));
   var row=new Grid {Margin=new Thickness(0,14,0,0)};row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(44)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(104)});
   var preview=new Border {Width=32,Height=32,CornerRadius=new CornerRadius(16),BorderThickness=new Thickness(1),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};preview.SetResourceReference(Border.BorderBrushProperty,"Line");row.Children.Add(preview);
   TextBox hex=null;Action<bool> publish=null;Action sync=null;
   var hueRail=(Grid)PillSlider(hue/360,rainbow,delegate(double v) {hue=v*359.9;sync();publish(false);},delegate {publish(true);});
   var setHue=(Action<double>)hueRail.Tag;hueRail.Tag="hue";foreach(var child in hueRail.Children.OfType<Border>())child.Height=10;Grid.SetColumn(hueRail,1);hueRail.Margin=new Thickness(0,0,12,0);row.Children.Add(hueRail);
   hex=new TextBox {FontFamily=new FontFamily("Consolas"),FontSize=12,VerticalAlignment=VerticalAlignment.Center,Tag="hex",MaxLength=7};Grid.SetColumn(hex,2);row.Children.Add(hex);
   root.Children.Add(row);
   var swatches=new WrapPanel {Margin=new Thickness(0,14,0,0)};root.Children.Add(swatches);
   bool typing=false;
   sync=delegate {
    var color=FromHsv(hue,sat,val);
    hueLayer.Background=new SolidColorBrush(FromHsv(hue,1,1));
    preview.Background=new SolidColorBrush(color);
    double w=Math.Max(1,field.ActualWidth),h=Math.Max(1,field.ActualHeight);ring.Margin=new Thickness(sat*w-9,(1-val)*h-9,0,0);
    ring.Fill=new SolidColorBrush(color);
    if(!typing){typing=true;try {hex.Text=Hex(color);}finally {typing=false;}}
   };
   publish=delegate(bool done) {changed(FromHsv(hue,sat,val),done);};
   Action<Color,bool> moveTo=delegate(Color c,bool announce) {
    ToHsv(c,out hue,out sat,out val);setHue(hue/360);sync();if(announce)publish(true);
   };
   Action<Point> pick=delegate(Point p) {sat=Math.Max(0,Math.Min(1,p.X/Math.Max(1,field.ActualWidth)));val=1-Math.Max(0,Math.Min(1,p.Y/Math.Max(1,field.ActualHeight)));sync();publish(false);};
   field.MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e) {field.CaptureMouse();pick(e.GetPosition(field));e.Handled=true;};
   field.MouseMove+=delegate(object s,MouseEventArgs e) {if(field.IsMouseCaptured)pick(e.GetPosition(field));};
   field.MouseLeftButtonUp+=delegate {if(field.IsMouseCaptured){field.ReleaseMouseCapture();publish(true);}};
   field.SizeChanged+=delegate {sync();};
   hex.TextChanged+=delegate {
    if(typing||!hex.IsKeyboardFocused)return;string text=hex.Text.Trim();if(!text.StartsWith("#"))text="#"+text;
    if(text.Length!=7||!text.Skip(1).All(Uri.IsHexDigit))return;
    typing=true;try {moveTo(ParseColor(text),true);}finally {typing=false;}
   };
   foreach(var swatch in FrameSwatches) {
    var color=ParseColor(swatch);
    var dot=new Border {Width=26,Height=26,CornerRadius=new CornerRadius(13),Background=new SolidColorBrush(color),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromArgb(30,0,0,0))};
    var button=new Button {Content=dot,Padding=new Thickness(3),Margin=new Thickness(0,0,4,4),Background=Brushes.Transparent,ToolTip=swatch,Tag="swatch-"+swatch};
    System.Windows.Automation.AutomationProperties.SetName(button,"Colour "+swatch);
    button.Click+=delegate {moveTo(color,true);};swatches.Children.Add(button);
   }
   root.Tag=(Action<Color>)delegate(Color c) {moveTo(c,false);};
   sync();return root;
  }
 }
}
