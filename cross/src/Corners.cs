using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xilium.CefGlue.Avalonia;

namespace Mica {
 // The page frame has rounded corners, but a page is Chromium's own native view, which Avalonia can't clip, so its
 // bottom corners poked out square. As on Windows (BrowserCorners.cs), each page view (and the Claude panel) gets
 // its own shape, rounding the corners that sit in the frame's bottom corners: a window region on Windows, a
 // rounded layer on macOS, an X11 shape on Linux. Full-screen pages stay square.
 sealed partial class BrowserWindow {
  const double PageCornerRadius=11; // The frame's 12 px corner, inside its 1 px border.
  Border frameBorder;
  readonly Dictionary<IntPtr,string> cornerShapes=new Dictionary<IntPtr,string>();
  bool cornersQueued;

  void WatchCorners(Border frame) {
   frameBorder=frame;
   frame.PropertyChanged+=delegate(object s,AvaloniaPropertyChangedEventArgs e) {if(e.Property==BoundsProperty)QueueCorners();};
   pages.PropertyChanged+=delegate(object s,AvaloniaPropertyChangedEventArgs e) {if(e.Property==BoundsProperty)QueueCorners();};
   aiDock.PropertyChanged+=delegate(object s,AvaloniaPropertyChangedEventArgs e) {if(e.Property==BoundsProperty||e.Property==IsVisibleProperty)QueueCorners();};
   // A page's native view appears a moment after its browser starts; look again shortly after.
   var settle=new DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};settle.Tick+=delegate {QueueCorners();};settle.Start();
  }
  // Coalesced: layout changes come in bursts (resizes, the sidebar folding).
  internal void QueueCorners() {
   if(cornersQueued)return;cornersQueued=true;
   Dispatcher.UIThread.Post(delegate {cornersQueued=false;UpdateCorners();},DispatcherPriority.Render);
  }
  void UpdateCorners() {
   if(frameBorder==null)return;
   foreach(var tab in tabs.Values)if(tab.View!=null)ShapePage(tab.View,frameBorder);
   // The AI panel is a card of its own; its chat is rounded to that card's corners.
   if(aiView!=null)ShapePage(aiView,aiDock);
  }
  static readonly FieldInfo browserHandleField=typeof(AvaloniaCefBrowser).Assembly.GetType("Xilium.CefGlue.Avalonia.Platform.ExtendedAvaloniaNativeControlHost")?.GetField("_browserHandle",BindingFlags.NonPublic|BindingFlags.Instance);
  // The native view CefGlue hosts the page in.
  static IntPtr NativeHandle(AvaloniaCefBrowser view) {
   if(browserHandleField==null)return IntPtr.Zero;
   foreach(var host in view.GetVisualDescendants().OfType<NativeControlHost>())if(browserHandleField.DeclaringType.IsInstanceOfType(host))return (IntPtr)browserHandleField.GetValue(host);
   return IntPtr.Zero;
  }
  void ShapePage(AvaloniaCefBrowser view,Border card) {
   if(!view.IsVisible||view.Bounds.Width<1)return;
   IntPtr handle=NativeHandle(view);if(handle==IntPtr.Zero)return;
   bool left=false,right=false;
   if(WindowState!=WindowState.FullScreen&&card.CornerRadius.BottomLeft>0) {
    var corner=view.TranslatePoint(new Point(0,0),card);if(corner==null)return;const double near=2.5;
    var at=corner.Value;var frame=card.Bounds;
    bool bottom=Math.Abs(at.Y+view.Bounds.Height-(frame.Height-1))<near;
    left=bottom&&Math.Abs(at.X-1)<near;right=bottom&&Math.Abs(at.X+view.Bounds.Width-(frame.Width-1))<near;
   }
   double scale=RenderScaling;
   int width=(int)Math.Round(view.Bounds.Width*scale),height=(int)Math.Round(view.Bounds.Height*scale),radius=(int)Math.Round(PageCornerRadius*(OperatingSystem.IsMacOS()?1:scale));
   string shape=left||right?width+"x"+height+(left?"L":"")+(right?"R":"")+radius:"square";
   string applied;if(cornerShapes.TryGetValue(handle,out applied)&&applied==shape)return;
   cornerShapes[handle]=shape;
   try {
    if(OperatingSystem.IsWindows())Win32Corners.Apply(handle,width,height,radius,left,right);
    else if(OperatingSystem.IsMacOS())MacCorners.Apply(handle,radius,left,right);
    else if(OperatingSystem.IsLinux())X11Corners.Apply(handle,width,height,radius,left,right);
   } catch(Exception e) {Trace("corners: "+e.Message);}
  }

  static class Win32Corners {
   [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
   [DllImport("gdi32.dll")] static extern IntPtr CreateEllipticRgn(int left,int top,int right,int bottom);
   [DllImport("gdi32.dll")] static extern int CombineRgn(IntPtr destination,IntPtr first,IntPtr second,int mode);
   [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
   [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr window,IntPtr region,bool redraw);
   [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr window);
   [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,System.Text.StringBuilder name,int count);
   const int RegionDiff=4;
   internal static void Apply(IntPtr handle,int width,int height,int radius,bool left,bool right) {
    // Avalonia holds the page in a window of its own; it gets the same shape so its background can't show.
    var targets=new List<IntPtr> {handle};IntPtr parent=GetParent(handle);
    var name=new System.Text.StringBuilder(64);if(parent!=IntPtr.Zero&&GetClassName(parent,name,64)>0&&name.ToString().Contains("NativeControlHost"))targets.Add(parent);
    foreach(var target in targets) {
     if(!left&&!right){SetWindowRgn(target,IntPtr.Zero,true);continue;}
     IntPtr region=CreateRectRgn(0,0,width,height);
     if(left)CutCorner(region,0,height-radius,radius,height,0,height-2*radius);
     if(right)CutCorner(region,width-radius,height-radius,width,height,width-2*radius,height-2*radius);
     if(SetWindowRgn(target,region,true)==0)DeleteObject(region); // On success the window owns the region.
    }
   }
   // Takes away the part of a corner square that lies outside the circle drawn from (circleLeft, circleTop).
   static void CutCorner(IntPtr region,int left,int top,int right,int bottom,int circleLeft,int circleTop) {
    int size=(right-left)*2;
    IntPtr square=CreateRectRgn(left,top,right,bottom),circle=CreateEllipticRgn(circleLeft,circleTop,circleLeft+size+1,circleTop+size+1),outside=CreateRectRgn(0,0,0,0);
    CombineRgn(outside,square,circle,RegionDiff);CombineRgn(region,region,outside,RegionDiff);
    DeleteObject(square);DeleteObject(circle);DeleteObject(outside);
   }
  }

  // macOS: the page's NSView gets a layer with rounded bottom corners (Core Animation clips it and its subviews).
  static class MacCorners {
   const string ObjC="/usr/lib/libobjc.A.dylib";
   [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
   [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern IntPtr Send(IntPtr target,IntPtr selector);
   [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern void SendBool(IntPtr target,IntPtr selector,bool value);
   [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern void SendDouble(IntPtr target,IntPtr selector,double value);
   [DllImport(ObjC,EntryPoint="objc_msgSend")] static extern void SendULong(IntPtr target,IntPtr selector,ulong value);
   // CACornerMask: the layer isn't flipped, so the bottom corners are the Min-Y ones.
   const ulong MinXMinY=1,MaxXMinY=2;
   internal static void Apply(IntPtr view,int radius,bool left,bool right) {
    SendBool(view,sel_registerName("setWantsLayer:"),true);
    IntPtr layer=Send(view,sel_registerName("layer"));if(layer==IntPtr.Zero)return;
    SendDouble(layer,sel_registerName("setCornerRadius:"),left||right?radius:0);
    SendULong(layer,sel_registerName("setMaskedCorners:"),(left?MinXMinY:0)|(right?MaxXMinY:0));
    SendBool(layer,sel_registerName("setMasksToBounds:"),left||right);
   }
  }

  // Linux (X11, XWayland included): the page's window gets a bounding shape made of rows, rounded at the bottom.
  static class X11Corners {
   [StructLayout(LayoutKind.Sequential)] struct XRectangle {public short X,Y;public ushort Width,Height;}
   [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
   [DllImport("libX11.so.6")] static extern int XFlush(IntPtr display);
   [DllImport("libXext.so.6")] static extern void XShapeCombineRectangles(IntPtr display,IntPtr window,int destKind,int x,int y,XRectangle[] rectangles,int count,int op,int ordering);
   [DllImport("libXext.so.6")] static extern void XShapeCombineMask(IntPtr display,IntPtr window,int destKind,int x,int y,IntPtr mask,int op);
   const int ShapeBounding=0,ShapeSet=0,Unsorted=0;
   static IntPtr display;
   internal static void Apply(IntPtr window,int width,int height,int radius,bool left,bool right) {
    if(display==IntPtr.Zero)display=XOpenDisplay(IntPtr.Zero);if(display==IntPtr.Zero)return;
    if(!left&&!right){XShapeCombineMask(display,window,ShapeBounding,0,0,IntPtr.Zero,ShapeSet);XFlush(display);return;}
    var rows=new List<XRectangle> {new XRectangle {X=0,Y=0,Width=(ushort)width,Height=(ushort)Math.Max(0,height-radius)}};
    for(int y=0;y<radius;y++) {
     // Inset of row y inside the corner: the circle's edge at that height.
     double dy=y+0.5;int inset=(int)Math.Ceiling(radius-Math.Sqrt(Math.Max(0,radius*radius-dy*dy)));
     int x0=left?inset:0,x1=width-(right?inset:0);
     rows.Add(new XRectangle {X=(short)x0,Y=(short)(height-radius+y),Width=(ushort)Math.Max(0,x1-x0),Height=1});
    }
    XShapeCombineRectangles(display,window,ShapeBounding,0,0,rows.ToArray(),rows.Count,ShapeSet,Unsorted);
    XFlush(display);
   }
  }
 }
}
