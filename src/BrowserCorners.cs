using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // The page frame has rounded corners, but a web page is a native window WPF can't clip, so its bottom
 // corners poked out square. Each page (and docked DevTools) gets a window region instead, rounding the
 // corners that sit in the frame's bottom corners. Fullscreen pages stay square.
 partial class BrowserApp {
  [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
  [DllImport("gdi32.dll")] static extern IntPtr CreateEllipticRgn(int left,int top,int right,int bottom);
  [DllImport("gdi32.dll")] static extern int CombineRgn(IntPtr destination,IntPtr first,IntPtr second,int mode);
  [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr window,IntPtr region,bool redraw);
  const int RegionAnd=1,RegionDiff=4;
  const double PageCornerRadius=11; // The frame's 12 px corner, inside its 1 px border.
  readonly Dictionary<IntPtr,string> pageCornerShapes=new Dictionary<IntPtr,string>();
  bool pageCornersQueued;

  // Coalesced: layout changes come in bursts (resizes, the DevTools slide).
  internal void QueuePageCorners() {
   if(pageCornersQueued||closing)return;pageCornersQueued=true;
   window.Dispatcher.BeginInvoke(new Action(delegate {pageCornersQueued=false;UpdatePageCorners();}),DispatcherPriority.Render);
  }
  void UpdatePageCorners() {
   if(closing)return;var frame=Find<System.Windows.Controls.Border>("ContentFrame");
   var pages=views.Values.Cast<WebView2>().ToList();if(devToolsView!=null&&!devToolsDetached)pages.Add(devToolsView);
   foreach(var view in pages)ShapePage(view,frame);
  }
  void ShapePage(WebView2 view,FrameworkElement frame) {
   IntPtr handle=((HwndHost)view).Handle;if(handle==IntPtr.Zero||!view.IsVisible||view.ActualWidth<1)return;
   bool left=false,right=false;
   if(!fullscreenApplied&&frame.IsVisible)try {
    var corner=view.TransformToAncestor(frame).Transform(new Point(0,0));const double near=2.5;
    bool bottom=Math.Abs(corner.Y+view.ActualHeight-(frame.ActualHeight-1))<near;
    left=bottom&&Math.Abs(corner.X-1)<near;right=bottom&&Math.Abs(corner.X+view.ActualWidth-(frame.ActualWidth-1))<near;
   }catch(InvalidOperationException){return;}
   var source=PresentationSource.FromVisual(view);double scale=source!=null&&source.CompositionTarget!=null?source.CompositionTarget.TransformToDevice.M11:1;
   int width=(int)Math.Round(view.ActualWidth*scale),height=(int)Math.Round(view.ActualHeight*scale),radius=(int)Math.Round(PageCornerRadius*scale);
   string shape=left||right?width+"x"+height+(left?"L":"")+(right?"R":"")+radius:"square";
   string applied;if(pageCornerShapes.TryGetValue(handle,out applied)&&applied==shape)return;
   pageCornerShapes[handle]=shape;
   if(shape=="square"){SetWindowRgn(handle,IntPtr.Zero,true);return;}
   IntPtr region=CreateRectRgn(0,0,width,height);
   if(left)CutCorner(region,0,height-radius,radius,height,0,height-2*radius);
   if(right)CutCorner(region,width-radius,height-radius,width,height,width-2*radius,height-2*radius);
   if(SetWindowRgn(handle,region,true)==0)DeleteObject(region); // On success the window owns the region.
  }
  // Takes away the part of a corner square that lies outside the circle drawn from (circleLeft, circleTop).
  static void CutCorner(IntPtr region,int left,int top,int right,int bottom,int circleLeft,int circleTop) {
   int size=(right-left)*2;
   IntPtr square=CreateRectRgn(left,top,right,bottom),circle=CreateEllipticRgn(circleLeft,circleTop,circleLeft+size+1,circleTop+size+1),outside=CreateRectRgn(0,0,0,0);
   CombineRgn(outside,square,circle,RegionDiff);CombineRgn(region,region,outside,RegionDiff);
   DeleteObject(square);DeleteObject(circle);DeleteObject(outside);
  }
  void ForgetPageCorners(WebView2 view) {try {IntPtr handle=((HwndHost)view).Handle;if(handle!=IntPtr.Zero)pageCornerShapes.Remove(handle);}catch(Exception){}}
 }
}
