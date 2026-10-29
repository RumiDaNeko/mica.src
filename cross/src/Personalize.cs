using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Mica {
 // Personalize, as on Windows (BrowserPersonalize.cs, BrowserTextSize.cs): text size, the new tab page's background
 // and its frosted glass, and the colour around the pages. Same choices, same values, same saved state (Core.cs).
 sealed partial class BrowserWindow {
  internal static readonly double[] TextSizes={1,1.15,1.3};
  internal static readonly string[] TextSizeNames={"Default","Large","Larger"};
  // name, base, glow 1, glow 2, glow 3 (glows at (0.12,0.85), (0.88,0.12), (0.92,0.92), radius 0.75).
  internal static readonly string[][] HomePresets={
   new[]{"Mist","#ECEAF2","#C9D6F0","#F0D8E4","#DCD2F2"},
   new[]{"Blossom","#F7E6EC","#F2A7C3","#C9B6F2","#FFDCC2"},
   new[]{"Meadow","#DDE9CF","#9CC48A","#F3E7B0","#7FB7A4"},
   new[]{"Lagoon","#0F3644","#2BB3A3","#8FD3F4","#1E6091"},
   new[]{"Dusk","#2B2748","#F28C8C","#7B5CC4","#F7C59F"},
   new[]{"Ember","#26171C","#E2725B","#F4A259","#7A2E3A"}};
  static readonly string[] FrameSwatches={"#DDD7E5","#E8DCCF","#D3E2D6","#CFDDEB","#F0D6DE","#2F2A3A","#1F3B45","#3A2A33","#6E5A8E","#C46F5A","#3E7A6E","#202024"};
  string HomePhotoPath {get {return System.IO.Path.Combine(profile,"home-photo");}}
  double TextScale {get {return state.TextScale>1?Math.Min(1.5,state.TextScale):1;}}

  // ── The dialog.
  internal void ShowPersonalize() {
   StackPanel body,actions;var dialog=MicaDialog("Personalize",480,out body,out actions);
   SectionLabel(body,"TEXT SIZE");
   var sizes=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,4)};body.Children.Add(sizes);
   Action renderSizes=null;renderSizes=delegate {
    sizes.Children.Clear();
    for(int i=0;i<TextSizes.Length;i++) {
     double scale=TextSizes[i];bool on=Math.Abs(TextScale-scale)<0.01;
     var b=DialogButton(TextSizeNames[i],on);b.FontSize=12*scale;b.Padding=new Thickness(16,8);b.Margin=new Thickness(0,0,8,0);b.MinWidth=86;b.FontWeight=on?FontWeight.SemiBold:FontWeight.Normal;
     b.Click+=delegate {state.TextScale=scale;ApplyTextScale();Save();renderSizes();};sizes.Children.Add(b);
    }
   };
   renderSizes();
   body.Children.Add(Hint("Makes Mica's sidebar, menus and windows larger, and opens pages at the same size.",new Thickness(2,4,2,10)));

   SectionLabel(body,"HOME BACKGROUND");
   var tiles=new WrapPanel {Margin=new Thickness(0,0,0,4)};body.Children.Add(tiles);
   Action renderTiles=null;renderTiles=delegate {
    tiles.Children.Clear();
    tiles.Children.Add(BackgroundTile("Classic",null,renderTiles));
    foreach(var p in HomePresets)tiles.Children.Add(BackgroundTile(p[0],p[0],renderTiles));
    tiles.Children.Add(BackgroundTile("Your photo…","photo",renderTiles));
   };
   renderTiles();
   var glassRow=new Grid {Margin=new Thickness(2,8,2,4),ColumnDefinitions=new ColumnDefinitions("Auto,*,44")};
   var glassLabel=DialogText("Frosted glass",12,"Ink");glassLabel.Margin=new Thickness(0,0,14,0);glassLabel.VerticalAlignment=VerticalAlignment.Center;glassRow.Children.Add(glassLabel);
   int glass=state.HomeGlass??60;var glassValue=DialogText(glass+"%",11,"Muted");glassValue.HorizontalAlignment=HorizontalAlignment.Right;glassValue.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(glassValue,2);glassRow.Children.Add(glassValue);
   var glassSlider=PillSlider(glass/100.0,null,6,delegate(double v,bool done) {int n=(int)Math.Round(v*100);glassValue.Text=n+"%";state.HomeGlass=n;if(done){Save();RefreshHomePages();}});Grid.SetColumn(glassSlider,1);glassRow.Children.Add(glassSlider);
   body.Children.Add(glassRow);
   body.Children.Add(Hint("Blurs the background behind the clock, search and your sites so they stay easy to read.",new Thickness(2,2,2,10)));

   SectionLabel(body,"FRAME COLOUR");
   Action<Color> movePicker;
   body.Children.Add(ColorPicker(string.IsNullOrEmpty(state.FrameColor)?FrameColor():Color.Parse(state.FrameColor),delegate(Color c,bool done) {
    state.FrameColor="#"+c.R.ToString("X2")+c.G.ToString("X2")+c.B.ToString("X2");ApplyFrame();if(done)Save();
   },out movePicker));

   var reset=DialogButton("Use theme colour",false);reset.Padding=new Thickness(16,9);
   reset.Click+=delegate {state.FrameColor=null;ApplyFrame();Save();movePicker(FrameColor());};actions.Children.Add(reset);
   var done=DialogButton("Done",true);done.Padding=new Thickness(20,9);done.Margin=new Thickness(8,0,0,0);done.Click+=delegate {dialog.IsOpen=false;};actions.Children.Add(done);
   dialog.IsOpen=true;
  }
  static TextBlock Hint(string text,Thickness margin) {var t=DialogText(text,11,"Muted");t.Margin=margin;t.LineHeight=16;return t;}

  // A background tile: a 116×70 face in a frame that turns accent when chosen.
  Control BackgroundTile(string label,string key,Action rerender) {
   bool on=(state.HomeBackground??null)==key;
   var face=new Border {Width=116,Height=70,CornerRadius=new CornerRadius(12),ClipToBounds=true};
   if(key==null){Res(face,Border.BackgroundProperty,"Canvas");face.Child=Glyph(Glyphs.Home,18,"muted");}
   else if(key=="photo") {
    Bitmap(face);
   }
   else face.Child=PresetFace(HomePresets.First(p=>p[0]==key));
   var frame=Res(new Border {CornerRadius=new CornerRadius(14),Padding=new Thickness(2),BorderThickness=new Thickness(on?2:1),Child=face},Border.BorderBrushProperty,on?"Accent":"Line");
   var stack=new StackPanel();stack.Children.Add(frame);
   var name=DialogText(label,11,"Muted");name.HorizontalAlignment=HorizontalAlignment.Center;name.Margin=new Thickness(0,5,0,0);stack.Children.Add(name);
   var tile=new Button {Content=stack,Padding=new Thickness(0),Margin=new Thickness(0,0,10,10),Background=Brushes.Transparent};tile.Classes.Add("tile");ToolTip.SetTip(tile,label);
   tile.Click+=async delegate {
    if(key=="photo"){if(!await ChooseHomePhoto())return;}
    state.HomeBackground=key;Save();RefreshHomePages();rerender();
   };
   return tile;
  }
  void Bitmap(Border face) {
   if(File.Exists(HomePhotoPath)) {
    try {using(var s=File.OpenRead(HomePhotoPath))face.Background=new ImageBrush(new Avalonia.Media.Imaging.Bitmap(s)) {Stretch=Stretch.UniformToFill};return;} catch(Exception) {}
   }
   Res(face,Border.BackgroundProperty,"Canvas");face.Child=Glyph(Glyphs.Plus,16,"muted");
  }
  // The preset's look: its base colour with three soft glows.
  static Control PresetFace(string[] p) {
   var grid=new Grid {Background=new SolidColorBrush(Color.Parse(p[1]))};
   var centres=new[]{new Point(0.12,0.85),new Point(0.88,0.12),new Point(0.92,0.92)};
   for(int i=0;i<3;i++) {
    var c=Color.Parse(p[2+i]);var at=new RelativePoint(centres[i],RelativeUnit.Relative);
    grid.Children.Add(new Border {Background=new RadialGradientBrush {Center=at,GradientOrigin=at,RadiusX=new RelativeScalar(0.75,RelativeUnit.Relative),RadiusY=new RelativeScalar(0.75,RelativeUnit.Relative),GradientStops={new GradientStop(c,0),new GradientStop(Color.FromArgb(0,c.R,c.G,c.B),1)}}});
   }
   return grid;
  }
  async Task<bool> ChooseHomePhoto() {
   var files=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {Title="Choose a home background",AllowMultiple=false,
    FileTypeFilter=new[]{new FilePickerFileType("Pictures") {Patterns=new[]{"*.jpg","*.jpeg","*.png","*.bmp","*.gif","*.webp","*.heic"}},FilePickerFileTypes.All}});
   if(files.Count==0)return false;
   try {
    using(var from=await files[0].OpenReadAsync())using(var to=File.Create(HomePhotoPath))await from.CopyToAsync(to);
    return true;
   } catch(Exception e) {Status("Couldn't use that picture: "+e.Message);return false;}
  }
  // The new tab page's look, for the page itself (BuiltInPages fills it in).
  internal string HomeLook() {
   var look=new Dictionary<string,object>();
   string bg=state.HomeBackground;var preset=HomePresets.FirstOrDefault(p=>p[0]==bg);
   if(preset!=null)look["preset"]=preset.Skip(1).ToArray();
   look["photo"]=bg=="photo"&&File.Exists(HomePhotoPath);
   look["glass"]=state.HomeGlass??60;
   look["private"]=Program.Private;
   look["vi"]=Localizer.Vietnamese;
   return new JavaScriptSerializer().Serialize(look);
  }
  void RefreshHomePages() {
   foreach(var t in tabs.Values)if(t.View!=null&&t.View.IsBrowserInitialized&&t.Entry.Url=="mica://home")t.View.Reload();
  }

  // ── Text size: Mica's own surfaces grow (sidebar, toolbar, menus, dialogs, the island), and pages open at that zoom.
  internal static double PageScale=1;
  void ApplyTextScale() {
   double scale=TextScale;PageScale=scale;
   var transform=Math.Abs(scale-1)<0.001?null:new ScaleTransform(scale,scale);
   sidebarScale.LayoutTransform=transform;toolbarScale.LayoutTransform=transform;
   if(islandScale!=null)islandScale.LayoutTransform=transform;
   frameInside.RowDefinitions[0].Height=new GridLength(44*scale);
   SetSidebar(!sidebarHidden);
   foreach(var t in tabs.Values)SetZoom(t,t.Zoom>0?t.Zoom:1);
  }

  // ── A pill slider (BrowserPersonalize.cs PillSlider): a rounded rail, an accent fill and a white thumb.
  Control PillSlider(double value,IBrush track,double railHeight,Action<double,bool> changed) {
   var host=new Grid {Height=22,Background=Brushes.Transparent,Cursor=new Cursor(StandardCursorType.Hand)};
   var rail=new Border {Height=railHeight,CornerRadius=new CornerRadius(railHeight/2),VerticalAlignment=VerticalAlignment.Center};
   if(track!=null)rail.Background=track;else Res(rail,Border.BackgroundProperty,"Line");host.Children.Add(rail);
   var fill=new Border {Height=railHeight,CornerRadius=new CornerRadius(railHeight/2),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,IsVisible=track==null};Res(fill,Border.BackgroundProperty,"Accent");host.Children.Add(fill);
   var thumb=new Ellipse {Width=18,Height=18,Fill=Brushes.White,StrokeThickness=1,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Effect=new DropShadowEffect {BlurRadius=6,OffsetX=0,OffsetY=1,Opacity=0.25,Color=Colors.Black}};
   thumb.Bind(Shape.StrokeProperty,thumb.GetResourceObservable("Line"));host.Children.Add(thumb);
   double v=Math.Max(0,Math.Min(1,value));
   Action place=delegate {double w=Math.Max(0,host.Bounds.Width-18);thumb.Margin=new Thickness(v*w,0,0,0);fill.Width=v*w+9;};
   host.PropertyChanged+=delegate(object s,AvaloniaPropertyChangedEventArgs e) {if(e.Property==BoundsProperty)place();};
   bool dragging=false;
   Action<PointerEventArgs,bool> at=delegate(PointerEventArgs e,bool done) {double w=Math.Max(1,host.Bounds.Width-18);v=Math.Max(0,Math.Min(1,(e.GetPosition(host).X-9)/w));place();changed(v,done);};
   host.PointerPressed+=delegate(object s,PointerPressedEventArgs e) {dragging=true;e.Pointer.Capture(host);at(e,false);};
   host.PointerMoved+=delegate(object s,PointerEventArgs e) {if(dragging)at(e,false);};
   host.PointerReleased+=delegate(object s,PointerReleasedEventArgs e) {if(!dragging)return;dragging=false;e.Pointer.Capture(null);at(e,true);};
   host.Tag=(Action<double>)delegate(double nv) {v=nv;place();};
   return host;
  }

  // ── The frame colour picker (BrowserPersonalize.cs ColorPicker): a saturation/brightness field, a hue rail, a
  // preview, a hex box and the swatches.
  Control ColorPicker(Color start,Action<Color,bool> changed,out Action<Color> move) {
   double h,s,v;ToHsv(start,out h,out s,out v);
   var root=new StackPanel();
   var field=new Grid {Height=128,ClipToBounds=true,Cursor=new Cursor(StandardCursorType.Cross)};
   var hueLayer=new Border {CornerRadius=new CornerRadius(12)};field.Children.Add(hueLayer);
   field.Children.Add(new Border {CornerRadius=new CornerRadius(12),Background=new LinearGradientBrush {StartPoint=new RelativePoint(0,0,RelativeUnit.Relative),EndPoint=new RelativePoint(1,0,RelativeUnit.Relative),GradientStops={new GradientStop(Colors.White,0),new GradientStop(Color.FromArgb(0,255,255,255),1)}}});
   field.Children.Add(new Border {CornerRadius=new CornerRadius(12),Background=new LinearGradientBrush {StartPoint=new RelativePoint(0,0,RelativeUnit.Relative),EndPoint=new RelativePoint(0,1,RelativeUnit.Relative),GradientStops={new GradientStop(Color.FromArgb(0,0,0,0),0),new GradientStop(Colors.Black,1)}}});
   field.Children.Add(new Border {CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromArgb(28,0,0,0))});
   var ring=new Ellipse {Width=18,Height=18,StrokeThickness=2.5,Stroke=Brushes.White,IsHitTestVisible=false,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Effect=new DropShadowEffect {BlurRadius=5,OffsetX=0,OffsetY=0,Opacity=0.45,Color=Colors.Black}};field.Children.Add(ring);
   root.Children.Add(field);
   var row=new Grid {Margin=new Thickness(0,14,0,0),ColumnDefinitions=new ColumnDefinitions("44,*,104")};
   var preview=Res(new Border {Width=32,Height=32,CornerRadius=new CornerRadius(16),BorderThickness=new Thickness(1),HorizontalAlignment=HorizontalAlignment.Left},Border.BorderBrushProperty,"Line");row.Children.Add(preview);
   var rainbow=new LinearGradientBrush {StartPoint=new RelativePoint(0,0,RelativeUnit.Relative),EndPoint=new RelativePoint(1,0,RelativeUnit.Relative)};
   for(int i=0;i<=6;i++)rainbow.GradientStops.Add(new GradientStop(FromHsv(i*60%360,1,1),i/6.0));
   var hex=new TextBox {FontFamily=new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"),FontSize=12,MaxLength=7,Padding=new Thickness(10,7)};hex.Classes.Add("field");Grid.SetColumn(hex,2);
   bool quiet=false;
   Action render=delegate {
    var c=FromHsv(h,s,v);hueLayer.Background=new SolidColorBrush(FromHsv(h,1,1));ring.Fill=preview.Background=new SolidColorBrush(c);
    double w=field.Bounds.Width,ht=field.Bounds.Height;ring.Margin=new Thickness(s*w-9,(1-v)*ht-9,0,0);
    quiet=true;hex.Text="#"+c.R.ToString("X2")+c.G.ToString("X2")+c.B.ToString("X2");quiet=false;
   };
   field.PropertyChanged+=delegate(object o,AvaloniaPropertyChangedEventArgs e) {if(e.Property==BoundsProperty)render();};
   Control hue=null;
   hue=PillSlider(h/359.9,rainbow,10,delegate(double nv,bool done) {h=nv*359.9;render();changed(FromHsv(h,s,v),done);});
   hue.Margin=new Thickness(0,0,12,0);hue.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(hue,1);row.Children.Add(hue);row.Children.Add(hex);
   root.Children.Add(row);
   bool picking=false;
   Action<PointerEventArgs,bool> pick=delegate(PointerEventArgs e,bool done) {
    var p=e.GetPosition(field);s=Math.Max(0,Math.Min(1,p.X/Math.Max(1,field.Bounds.Width)));v=Math.Max(0,Math.Min(1,1-p.Y/Math.Max(1,field.Bounds.Height)));render();changed(FromHsv(h,s,v),done);
   };
   field.PointerPressed+=delegate(object o,PointerPressedEventArgs e) {picking=true;e.Pointer.Capture(field);pick(e,false);};
   field.PointerMoved+=delegate(object o,PointerEventArgs e) {if(picking)pick(e,false);};
   field.PointerReleased+=delegate(object o,PointerReleasedEventArgs e) {if(!picking)return;picking=false;e.Pointer.Capture(null);pick(e,true);};
   hex.TextChanged+=delegate {
    if(quiet)return;string t=(hex.Text??"").Trim();if(!t.StartsWith("#"))t="#"+t;Color c;
    if(t.Length==7&&Color.TryParse(t,out c)){ToHsv(c,out h,out s,out v);((Action<double>)hue.Tag)(h/359.9);render();changed(c,true);}
   };
   Action<Color> set=delegate(Color c) {ToHsv(c,out h,out s,out v);((Action<double>)hue.Tag)(h/359.9);render();};
   move=set;
   var swatches=new WrapPanel {Margin=new Thickness(0,14,0,0)};
   foreach(var code in FrameSwatches) {
    var c=Color.Parse(code);
    var b=new Button {Padding=new Thickness(3),Margin=new Thickness(0,0,4,4),Background=Brushes.Transparent,Content=new Border {Width=26,Height=26,CornerRadius=new CornerRadius(13),Background=new SolidColorBrush(c),BorderThickness=new Thickness(1),BorderBrush=new SolidColorBrush(Color.FromArgb(30,0,0,0))}};
    b.Classes.Add("tile");ToolTip.SetTip(b,code);b.Click+=delegate {set(c);changed(c,true);};swatches.Children.Add(b);
   }
   root.Children.Add(swatches);
   render();
   return root;
  }
  static Color FromHsv(double h,double s,double v) {
   double c=v*s,x=c*(1-Math.Abs(h/60%2-1)),m=v-c,r=0,g=0,b=0;
   if(h<60){r=c;g=x;}else if(h<120){r=x;g=c;}else if(h<180){g=c;b=x;}else if(h<240){g=x;b=c;}else if(h<300){r=x;b=c;}else{r=c;b=x;}
   return Color.FromRgb((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));
  }
  static void ToHsv(Color c,out double h,out double s,out double v) {
   double r=c.R/255.0,g=c.G/255.0,b=c.B/255.0,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),d=max-min;
   v=max;s=max<=0?0:d/max;
   if(d<=0)h=0;else if(max==r)h=60*(((g-b)/d+6)%6);else if(max==g)h=60*((b-r)/d+2);else h=60*((r-g)/d+4);
  }
 }
}
