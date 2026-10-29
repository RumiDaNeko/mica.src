using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Mica {
 partial class BrowserApp {
  // Closing a window: a picture of it shrinks into a dot, the dot pops into a burst of stars, and they fade away.
  // The real window closes at once; the animation plays in a borderless see-through window on top of where it was.
  const double FarewellShrink=340,FarewellLength=820;
  [StructLayout(LayoutKind.Sequential)] struct WindowRect {public int Left,Top,Right,Bottom;}
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out WindowRect rect);
  [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
  [DllImport("user32.dll",EntryPoint="PrintWindow")] static extern bool CaptureWindow(IntPtr hwnd,IntPtr hdc,uint flags);
  [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
  // Windows' own close fade would play over ours; with it off, the window is gone the moment the picture takes its place.
  static void NoWindowsTransition(IntPtr hwnd) {int on=1;try {DwmSetWindowAttribute(hwnd,3,ref on,4);}catch(Exception){}}

  internal string farewellNote;Window farewellOverlay;BitmapSource farewellShot;
  // It plays for a restart to update too: the installer waits for Mica's files to be let go, and the
  // overlay (a window of this process) only holds Mica open for the animation's 0.8 s.
  bool FarewellWanted() {
   return SystemParameters.ClientAreaAnimation&&CloseEffect()!="off"&&!smoke&&window.IsVisible&&window.WindowState!=WindowState.Minimized;
  }
  void PlayFarewell() {
   try {
    var handle=new WindowInteropHelper(window).Handle;WindowRect r;
    if(handle==IntPtr.Zero||!GetWindowRect(handle,out r)||r.Right-r.Left<16||r.Bottom-r.Top<16)return;
    // The window as Windows composes it, not the visual tree: the page is drawn by the engine in its own window.
    // PrintWindow's full-content mode includes that and works even if another window covers Mica.
    BitmapSource shot;
    using(var bitmap=new System.Drawing.Bitmap(r.Right-r.Left,r.Bottom-r.Top))using(var g=System.Drawing.Graphics.FromImage(bitmap)) {
     var dc=g.GetHdc();bool printed;try {printed=CaptureWindow(handle,dc,2);}finally {g.ReleaseHdc(dc);}
     if(!printed)g.CopyFromScreen(r.Left,r.Top,0,0,bitmap.Size);
     var hbitmap=bitmap.GetHbitmap();
     try {shot=Imaging.CreateBitmapSourceFromHBitmap(hbitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());}finally {DeleteObject(hbitmap);}
    }
    shot.Freeze();farewellShot=shot;
    var source=PresentationSource.FromVisual(window);var toDip=source!=null?source.CompositionTarget.TransformFromDevice:Matrix.Identity;
    var topLeft=toDip.Transform(new Point(r.Left,r.Top));var size=(Vector)toDip.Transform(new Point(r.Right-r.Left,r.Bottom-r.Top));
    var accent=window.Resources["Accent"] as SolidColorBrush;
    Action<double> seek;double length;var scene=FarewellScene(CloseEffect(),shot,new Size(size.X,size.Y),accent!=null?accent.Color:Color.FromRgb(0x71,0x60,0x8C),out seek,out length);
    // On top for its short life, so nothing that comes forward as Mica leaves can hide it.
    var overlay=new Window {WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,ShowActivated=false,ResizeMode=ResizeMode.NoResize,Topmost=true,
     Left=topLeft.X,Top=topLeft.Y,Width=size.X,Height=size.Y,Content=scene,Title="Mica"};
    overlay.SourceInitialized+=delegate {NoWindowsTransition(new WindowInteropHelper(overlay).Handle);};
    NoWindowsTransition(handle);
    seek(0);overlay.Show();farewellOverlay=overlay;farewellNote="playing";
    var clock=Stopwatch.StartNew();bool done=false;EventHandler frame=null;
    Action finish=delegate {if(done)return;done=true;System.Windows.Media.CompositionTarget.Rendering-=frame;overlay.Close();farewellNote="finished after "+clock.ElapsedMilliseconds+" ms";};
    frame=delegate {double t=clock.Elapsed.TotalMilliseconds;seek(Math.Min(t,length));if(t>=length)finish();};
    System.Windows.Media.CompositionTarget.Rendering+=frame;
    // Never leave the picture behind, whatever happens to the frame clock.
    var guard=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(length+900)};guard.Tick+=delegate {guard.Stop();finish();};guard.Start();
   }catch(Exception e){farewellNote="failed: "+e.GetType().Name+": "+e.Message;}
  }

  // Close animations you can pick in Personalize; the first is the default.
  internal static readonly string[][] CloseEffects={new[]{"stars","Stars"},new[]{"fade","Fade"},new[]{"drop","Drop"},new[]{"shatter","Shatter"},new[]{"tv","Switch off"},new[]{"swirl","Swirl"},new[]{"melt","Melt"},new[]{"dissolve","Dissolve"},new[]{"off","Off"}};
  string CloseEffect() {foreach(var e in CloseEffects)if(e[0]==state.CloseEffect)return e[0];return "stars";}
  // The animation as a function of time, so it can be played live or drawn at any moment.
  static Grid FarewellScene(string effect,BitmapSource shot,Size size,Color accent,out Action<double> seek,out double length) {
   if(effect=="fade"){length=300;return FadeScene(shot,size,out seek);}
   if(effect=="drop"){length=520;return DropScene(shot,size,out seek);}
   if(effect=="shatter"){length=820;return ShatterScene(shot,size,out seek);}
   if(effect=="tv"){length=460;return SwitchOffScene(shot,size,out seek);}
   if(effect=="swirl"){length=620;return SwirlScene(shot,size,out seek);}
   if(effect=="melt"){length=900;return MeltScene(shot,size,out seek);}
   if(effect=="dissolve"){length=720;return DissolveScene(shot,size,out seek);}
   length=FarewellLength;return FarewellScene(shot,size,accent,out seek);
  }
  static double Clamp01(double x) {return x<0?0:x>1?1:x;}
  // A little Mica window for the preview in Personalize: a sidebar of tabs and a page, in the current colours.
  BitmapSource ClosePreviewPicture(Size size) {
   var visual=new DrawingVisual();double w=size.Width,h=size.Height;
   using(var dc=visual.RenderOpen()) {
    dc.DrawRoundedRectangle(Brush("Panel"),new Pen(Brush("Line"),1),new Rect(0.5,0.5,w-1,h-1),8,8);
    var accent=new SolidColorBrush(FrameColor());accent.Freeze();
    dc.DrawRoundedRectangle(accent,null,new Rect(6,6,w*0.26,h-12),5,5);
    var soft=new SolidColorBrush(Color.FromArgb(110,255,255,255));soft.Freeze();
    for(int i=0;i<4;i++)dc.DrawRoundedRectangle(soft,null,new Rect(10,14+i*14,w*0.26-8,8),3,3);
    dc.DrawRoundedRectangle(Brush("Canvas"),null,new Rect(w*0.26+12,6,w*0.74-18,h-12),5,5);
    for(int i=0;i<4;i++)dc.DrawRoundedRectangle(Brush("Line"),null,new Rect(w*0.26+20,18+i*14,(w*0.74-34)*(i==3?0.55:1),6),3,3);
   }
   var bitmap=new RenderTargetBitmap((int)(w*2),(int)(h*2),192,192,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();return bitmap;
  }
  static Border FarewellPicture(BitmapSource shot,Size size) {return new Border {Width=size.Width,Height=size.Height,CornerRadius=new CornerRadius(12),Background=new ImageBrush(shot) {Stretch=Stretch.Fill},RenderTransformOrigin=new Point(0.5,0.5)};}
  // The window eases back a little and fades, like Windows' own close.
  static Grid FadeScene(BitmapSource shot,Size size,out Action<double> seek) {
   var root=new Grid {Width=size.Width,Height=size.Height,IsHitTestVisible=false};var picture=FarewellPicture(shot,size);var scale=new ScaleTransform(1,1);picture.RenderTransform=scale;root.Children.Add(picture);
   seek=delegate(double t) {double p=1-Math.Pow(1-Clamp01(t/300),2);scale.ScaleX=scale.ScaleY=1-0.07*p;picture.Opacity=1-p;};
   return root;
  }
  // The window tips forward and falls away off the bottom.
  static Grid DropScene(BitmapSource shot,Size size,out Action<double> seek) {
   var root=new Grid {Width=size.Width,Height=size.Height,IsHitTestVisible=false};var picture=FarewellPicture(shot,size);
   var scale=new ScaleTransform(1,1);var turn=new RotateTransform(0);var move=new TranslateTransform(0,0);var group=new TransformGroup();group.Children.Add(scale);group.Children.Add(turn);group.Children.Add(move);picture.RenderTransform=group;root.Children.Add(picture);
   seek=delegate(double t) {double q=Clamp01(t/520),p=q*q*q;move.Y=size.Height*0.55*p;turn.Angle=5*p;scale.ScaleX=scale.ScaleY=1-0.12*q;picture.Opacity=1-Clamp01((q-0.35)/0.65);};
   return root;
  }
  // The window breaks into pieces that fly apart, spin and fall.
  static Grid ShatterScene(BitmapSource shot,Size size,out Action<double> seek) {
   double w=size.Width,h=size.Height;int cols=8,rows=6;double pw=w/cols,ph=h/rows;
   var root=new Grid {Width=w,Height=h,IsHitTestVisible=false};var layer=new Canvas {Width=w,Height=h};root.Children.Add(layer);
   var random=new Random(11);var pieces=new List<Tuple<UIElement,TranslateTransform,RotateTransform,ScaleTransform,double[]>>();
   for(int y=0;y<rows;y++)for(int x=0;x<cols;x++) {
    var piece=new Border {Width=pw+0.5,Height=ph+0.5,Background=new ImageBrush(shot) {Viewbox=new Rect((double)x/cols,(double)y/rows,1.0/cols,1.0/rows),Stretch=Stretch.Fill},RenderTransformOrigin=new Point(0.5,0.5)};
    var move=new TranslateTransform(x*pw,y*ph);var turn=new RotateTransform(0);var scale=new ScaleTransform(1,1);var group=new TransformGroup();group.Children.Add(scale);group.Children.Add(turn);group.Children.Add(move);piece.RenderTransform=group;layer.Children.Add(piece);
    double dx=(x+0.5)*pw-w/2,dy=(y+0.5)*ph-h/2,len=Math.Max(1,Math.Sqrt(dx*dx+dy*dy)),push=0.25+random.NextDouble()*0.35;
    pieces.Add(Tuple.Create((UIElement)piece,move,turn,scale,new[]{x*pw,y*ph,dx/len*w*push,dy/len*h*push-h*0.15,(random.NextDouble()-0.5)*220,random.NextDouble()*90}));
   }
   seek=delegate(double t) {
    foreach(var p in pieces) {
     double[] v=p.Item5;double q=Clamp01((t-v[5])/(820-v[5]-20)),out1=1-Math.Pow(1-q,2);
     p.Item2.X=v[0]+v[2]*out1;p.Item2.Y=v[1]+v[3]*out1+h*0.6*q*q;p.Item3.Angle=v[4]*q;p.Item4.ScaleX=p.Item4.ScaleY=1-0.35*q;p.Item1.Opacity=1-Clamp01((q-0.3)/0.7);
    }
   };
   return root;
  }
  // Like an old television: the picture squashes to a bright line, then the line shrinks to a dot and goes out.
  static Grid SwitchOffScene(BitmapSource shot,Size size,out Action<double> seek) {
   var root=new Grid {Width=size.Width,Height=size.Height,IsHitTestVisible=false};var picture=FarewellPicture(shot,size);var scale=new ScaleTransform(1,1);picture.RenderTransform=scale;root.Children.Add(picture);
   var flash=new Border {Width=size.Width,Height=size.Height,CornerRadius=new CornerRadius(12),Background=Brushes.White,Opacity=0,RenderTransformOrigin=new Point(0.5,0.5),RenderTransform=scale};root.Children.Add(flash);
   seek=delegate(double t) {
    double a=Clamp01(t/230),b=Clamp01((t-230)/230),ea=a*a*(3-2*a),eb=1-Math.Pow(1-b,3);
    scale.ScaleY=Math.Max(2/size.Height,1-ea*(1-2/size.Height));scale.ScaleX=1-0.985*eb;
    flash.Opacity=0.85*ea;picture.Opacity=1-ea*0.6;root.Opacity=1-Clamp01((t-380)/80);
   };
   return root;
  }
  // The window spins into its own middle and disappears.
  static Grid SwirlScene(BitmapSource shot,Size size,out Action<double> seek) {
   var root=new Grid {Width=size.Width,Height=size.Height,IsHitTestVisible=false};var picture=FarewellPicture(shot,size);
   var scale=new ScaleTransform(1,1);var turn=new RotateTransform(0);var group=new TransformGroup();group.Children.Add(scale);group.Children.Add(turn);picture.RenderTransform=group;root.Children.Add(picture);
   seek=delegate(double t) {double q=Clamp01(t/620),p=q*q*(3-2*q);turn.Angle=-300*p;scale.ScaleX=scale.ScaleY=Math.Max(0.001,1-p);picture.Opacity=1-Clamp01((q-0.55)/0.45);};
   return root;
  }
  // Strips slide down one after another, as if the window ran off the screen.
  static Grid MeltScene(BitmapSource shot,Size size,out Action<double> seek) {
   double w=size.Width,h=size.Height;int strips=18;double sw=w/strips;var random=new Random(7);
   var root=new Grid {Width=w,Height=h,IsHitTestVisible=false};var layer=new Canvas {Width=w,Height=h,ClipToBounds=false};root.Children.Add(layer);
   var pieces=new List<Tuple<UIElement,TranslateTransform,double>>();
   for(int i=0;i<strips;i++) {
    var strip=new Border {Width=sw+0.6,Height=h,Background=new ImageBrush(shot) {Viewbox=new Rect((double)i/strips,0,1.0/strips,1),Stretch=Stretch.Fill}};
    var move=new TranslateTransform(i*sw,0);strip.RenderTransform=move;layer.Children.Add(strip);
    // A ripple from the middle outwards, with a little jitter so it doesn't look mechanical.
    double delay=Math.Abs(i-(strips-1)/2.0)/(strips/2.0)*260+random.NextDouble()*90;pieces.Add(Tuple.Create((UIElement)strip,move,delay));
   }
   seek=delegate(double t) {foreach(var p in pieces){double q=Clamp01((t-p.Item3)/(900-360)),fall=q*q;p.Item2.Y=h*1.05*fall;p.Item1.Opacity=1-Clamp01((q-0.45)/0.55);}};
   return root;
  }
  // The picture breaks into small tiles that wink out in a scattered order.
  static Grid DissolveScene(BitmapSource shot,Size size,out Action<double> seek) {
   double w=size.Width,h=size.Height;int cols=Math.Max(8,(int)Math.Round(w/40)),rows=Math.Max(6,(int)Math.Round(h/40));double pw=w/cols,ph=h/rows;var random=new Random(3);
   var root=new Grid {Width=w,Height=h,IsHitTestVisible=false};var layer=new Canvas {Width=w,Height=h};root.Children.Add(layer);
   var pieces=new List<Tuple<UIElement,ScaleTransform,double>>();
   for(int y=0;y<rows;y++)for(int x=0;x<cols;x++) {
    var tile=new Border {Width=pw+0.5,Height=ph+0.5,Background=new ImageBrush(shot) {Viewbox=new Rect((double)x/cols,(double)y/rows,1.0/cols,1.0/rows),Stretch=Stretch.Fill},RenderTransformOrigin=new Point(0.5,0.5)};
    var scale=new ScaleTransform(1,1);tile.RenderTransform=scale;Canvas.SetLeft(tile,x*pw);Canvas.SetTop(tile,y*ph);layer.Children.Add(tile);
    pieces.Add(Tuple.Create((UIElement)tile,scale,random.NextDouble()*480));
   }
   seek=delegate(double t) {foreach(var p in pieces){double q=Clamp01((t-p.Item3)/240);p.Item2.ScaleX=p.Item2.ScaleY=1-0.6*q;p.Item1.Opacity=1-q;}};
   return root;
  }
  static Grid FarewellScene(BitmapSource shot,Size size,Color accent,out Action<double> seek) {
   double w=size.Width,h=size.Height,cx=w/2,cy=h/2,dot=16;
   var root=new Grid {Width=w,Height=h,IsHitTestVisible=false};
   var picture=new Border {Width=w,Height=h,CornerRadius=new CornerRadius(12),Background=new ImageBrush(shot) {Stretch=Stretch.Fill},RenderTransformOrigin=new Point(0.5,0.5)};
   var squeeze=new ScaleTransform(1,1);picture.RenderTransform=squeeze;root.Children.Add(picture);
   var layer=new Canvas {Width=w,Height=h};root.Children.Add(layer);
   Color light=Color.FromRgb((byte)((accent.R+255*2)/3),(byte)((accent.G+255*2)/3),(byte)((accent.B+255*2)/3));
   var glow=new Ellipse {Width=dot*4,Height=dot*4,Fill=new RadialGradientBrush(Color.FromArgb(150,accent.R,accent.G,accent.B),Color.FromArgb(0,accent.R,accent.G,accent.B)),Opacity=0,RenderTransformOrigin=new Point(0.5,0.5)};
   var glowScale=new ScaleTransform(1,1);glow.RenderTransform=glowScale;Canvas.SetLeft(glow,cx-dot*2);Canvas.SetTop(glow,cy-dot*2);layer.Children.Add(glow);
   var core=new Ellipse {Width=dot,Height=dot,Fill=new SolidColorBrush(accent),Stroke=new SolidColorBrush(light),StrokeThickness=2,Opacity=0,RenderTransformOrigin=new Point(0.5,0.5)};
   var coreScale=new ScaleTransform(1,1);core.RenderTransform=coreScale;Canvas.SetLeft(core,cx-dot/2);Canvas.SetTop(core,cy-dot/2);layer.Children.Add(core);
   // Five-point stars and four-point sparkles, alternating, in the frame's accent, a pale and a deep accent and gold,
   // so some of them show on any desktop.
   var five=Geometry.Parse("M0,-1 L0.235,-0.324 L0.951,-0.309 L0.38,0.124 L0.588,0.809 L0,0.4 L-0.588,0.809 L-0.38,0.124 L-0.951,-0.309 L-0.235,-0.324 Z");
   var four=Geometry.Parse("M0,-1 Q0.12,-0.12 1,0 Q0.12,0.12 0,1 Q-0.12,0.12 -1,0 Q-0.12,-0.12 0,-1 Z");five.Freeze();four.Freeze();
   Color deep=Color.FromRgb((byte)(accent.R*0.55),(byte)(accent.G*0.55),(byte)(accent.B*0.55));
   var colors=new[]{accent,Color.FromRgb(0xFF,0xC8,0x3D),light,deep};
   var random=new Random(7);int count=16;
   var stars=new List<Tuple<FrameworkElement,TranslateTransform,ScaleTransform,RotateTransform,double[]>>();
   for(int i=0;i<count;i++) {
    double angle=(i*360.0/count+random.Next(-10,10))*Math.PI/180,reach=90+random.NextDouble()*110,radius=(i%2==0?11:8)+random.NextDouble()*4,spin=(random.NextDouble()-0.5)*260,delay=random.NextDouble()*60;
    var shape=new Path {Data=i%2==0?five:four,Fill=new SolidColorBrush(colors[i%colors.Length]),Opacity=0};
    var move=new TranslateTransform(cx,cy);var scale=new ScaleTransform(radius,radius);var turn=new RotateTransform(0);
    var group=new TransformGroup();group.Children.Add(scale);group.Children.Add(turn);group.Children.Add(move);shape.RenderTransform=group;
    layer.Children.Add(shape);stars.Add(Tuple.Create((FrameworkElement)shape,move,scale,turn,new[]{Math.Cos(angle)*reach,Math.Sin(angle)*reach,radius,spin,delay}));
   }
   Func<double,double> clamp=delegate(double x) {return x<0?0:x>1?1:x;};
   Func<double,double> easeIn=delegate(double x) {return x*x*x;};
   Func<double,double> easeOut=delegate(double x) {return 1-Math.Pow(1-x,3);};
   seek=delegate(double t) {
    // 1. The window shrinks towards its centre and rounds off into a dot.
    double p=easeIn(clamp(t/FarewellShrink));
    squeeze.ScaleX=1+(dot/w-1)*p;squeeze.ScaleY=1+(dot/h-1)*p;
    picture.CornerRadius=new CornerRadius(12+(Math.Min(w,h)/2-12)*p*p);
    picture.Opacity=1-clamp((t-FarewellShrink*0.72)/(FarewellShrink*0.28));
    // 2. The dot glows and pops.
    double appear=clamp((t-FarewellShrink*0.7)/(FarewellShrink*0.3)),pop=clamp((t-FarewellShrink)/140);
    core.Opacity=appear*(1-pop);double coreSize=0.7+0.3*appear+0.6*easeOut(pop);coreScale.ScaleX=coreScale.ScaleY=coreSize;
    glow.Opacity=appear*(1-clamp((t-FarewellShrink-40)/220));glowScale.ScaleX=glowScale.ScaleY=0.6+1.4*easeOut(clamp((t-FarewellShrink*0.8)/260));
    // 3. Stars fly out, turning, and fade.
    foreach(var star in stars) {
     double[] v=star.Item5;double q=clamp((t-FarewellShrink-v[4])/(FarewellLength-FarewellShrink-v[4]-20));double travel=easeOut(q);
     star.Item2.X=cx+v[0]*travel;star.Item2.Y=cy+v[1]*travel;
     double grow=q<0.25?q/0.25:1-(q-0.25)/0.75*0.65;star.Item3.ScaleX=star.Item3.ScaleY=v[2]*Math.Max(0.01,grow);
     star.Item4.Angle=v[3]*travel;star.Item1.Opacity=q<=0?0:q<0.55?1:1-(q-0.55)/0.45;
    }
   };
   return root;
  }
 }
}
