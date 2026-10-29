using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace Mica {
 partial class BrowserApp {
  [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr window,IntPtr region);
  [DllImport("gdi32.dll")] static extern bool PtInRegion(IntPtr region,int x,int y);
  // The page window is cut to the frame's rounded bottom corners, and square again in fullscreen.
  async Task RunCornersTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"corners");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;FullscreenBounds.KeepOffscreen=true;
   try {
    Navigate("https://example.com/",false);await WaitForPage();await Task.Delay(600);
    var view=CurrentView();IntPtr handle=((HwndHost)view).Handle;
    var source=PresentationSource.FromVisual(view);double scale=source.CompositionTarget.TransformToDevice.M11;
    int w=(int)Math.Round(view.ActualWidth*scale),h=(int)Math.Round(view.ActualHeight*scale);
    Func<int,int,bool> inside=delegate(int x,int y) {IntPtr region=CreateRectRgn(0,0,0,0);try {return GetWindowRgn(handle,region)>1&&PtInRegion(region,x,y);}finally {DeleteObject(region);}};
    log.Add("page "+w+"x"+h+" at scale "+scale+"; shape "+(pageCornerShapes.ContainsKey(handle)?pageCornerShapes[handle]:"none"));
    RegressionCheck("the page's bottom corners are cut round",delegate {Require(!inside(1,h-2)&&!inside(w-2,h-2),"corner pixels still drawn");});
    RegressionCheck("the rest of the page still shows, top corners included",delegate {Require(inside(w/2,h-2)&&inside(1,h/2)&&inside(1,1)&&inside(w-2,1),"page cut too much");});
    userFullscreen=true;ApplyFullscreen();await Task.Delay(800);
    RegressionCheck("a fullscreen page is square",delegate {IntPtr region=CreateRectRgn(0,0,0,0);int kind=GetWindowRgn(handle,region);DeleteObject(region);Require(kind==0,"region kind "+kind);});
    userFullscreen=false;ApplyFullscreen();await Task.Delay(800);
    RegressionCheck("leaving fullscreen rounds it again",delegate {int nw=(int)Math.Round(view.ActualWidth*scale),nh=(int)Math.Round(view.ActualHeight*scale);Require(!inside(1,nh-2)&&inside(nw/2,nh/2),"not round");});
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   FullscreenBounds.KeepOffscreen=false;
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
