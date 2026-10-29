using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Mica {
 partial class BrowserApp {
  delegate bool ChildWindowProc(IntPtr hwnd,IntPtr data);
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,ChildWindowProc proc,IntPtr data);
  [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,System.Text.StringBuilder name,int size);
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out WheelRect rect);
  struct WheelRect {public int Left,Top,Right,Bottom;}
  static IntPtr PageWidgetWindow(IntPtr top) {
   IntPtr found=IntPtr.Zero;
   EnumChildWindows(top,delegate(IntPtr hwnd,IntPtr data) {var name=new System.Text.StringBuilder(64);GetClassName(hwnd,name,64);if(name.ToString()=="Chrome_RenderWidgetHostHWND"&&found==IntPtr.Zero)found=hwnd;return true;},IntPtr.Zero);
   return found;
  }
  // WM_MOUSEWHEEL with MK_CONTROL: the engine reads Ctrl from the message itself.
  static void PostCtrlWheel(IntPtr hwnd,int delta) {
   WheelRect r;GetWindowRect(hwnd,out r);int x=(r.Left+r.Right)/2,y=(r.Top+r.Bottom)/2;
   PostMessage(hwnd,0x020A,new IntPtr((delta<<16)|0x0008),new IntPtr((y<<16)|(x&0xFFFF)));
  }
  // Downloads in the island: at most two, newest first, staying put under the pointer, and leaving after their linger.
  async Task RunIslandDownloadsTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"island-downloads");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;
   try {
    await Task.Delay(800);
    var now=DateTime.UtcNow;
    foreach(var item in new[]{new[]{"a","old.zip","Completed"},new[]{"b","report.pdf","Completed"},new[]{"c","photos.zip","Interrupted"}}) {
     var entry=new DownloadEntry {Id=item[0],FileName=item[1],FilePath=Path.Combine(dir,item[1]),Url="https://example.com/"+item[1],Status=item[2],Received=600,Total=1000,Detail=item[2]=="Interrupted"?"Download interrupted: NetworkFailed.":"",StartedUtc=now.AddSeconds(item[0]=="a"?-30:item[0]=="b"?-20:-10)};
     downloads.Insert(0,entry);islandSeenLive.Add(entry.Id);islandDoneAt[entry.Id]=now;
    }
    RefreshIslandDownloads();await Task.Delay(400);
    var rows=islandDownloads.Children.OfType<Border>().Select(b=>(string)b.Tag).ToList();
    log.Add("rows: "+string.Join(", ",rows));
    RegressionCheck("the island shows at most two downloads, newest first",delegate {Require(rows.SequenceEqual(new[]{"island-download:c","island-download:b"}),string.Join(", ",rows));});
    var note=(TextBlock)((Grid)((Border)islandDownloads.Children[1]).Child).Children[2];
    RegressionCheck("a finished one offers to open",delegate {Require(note.Text=="Open",note.Text);});
    CaptureElement(islandCard,"island-downloads.png");
    // The pointer check says "near", but with downloads up it stays.
    islandAway=false;WatchIslandPointer();
    RegressionCheck("downloads keep the island up under the pointer",delegate {Require(!islandAway&&islandCard.IsHitTestVisible,"stepped aside");});
    islandAway=true;RefreshIslandDownloads();
    RegressionCheck("a hidden island comes back for downloads",delegate {Require(!islandAway&&islandCard.IsHitTestVisible,"still away");});
    foreach(var id in new[]{"a","b","c"})islandDoneAt[id]=now-TimeSpan.FromSeconds(9);
    RefreshIslandDownloads();
    RegressionCheck("finished downloads leave after their linger",delegate {Require(!IslandHasDownloads,islandDownloads.Children.Count+" left");});
    downloads.RemoveAll(d=>d.Id.Length==1);
    // Speed: nothing on the first sample, then bytes per second, smoothed.
    var t0=DateTime.UtcNow;double r0=IslandDownloadSpeed("speed",0,t0),r1=IslandDownloadSpeed("speed",2000000,t0.AddSeconds(1)),r2=IslandDownloadSpeed("speed",3000000,t0.AddSeconds(2));
    log.Add("speeds: "+r0+", "+r1+", "+r2+" shown as "+FormatBytes((long)r2)+"/s");
    RegressionCheck("downloads show a smoothed speed",delegate {Require(r0<0&&Math.Abs(r1-2000000)<1&&Math.Abs(r2-1600000)<1,r0+", "+r1+", "+r2);});
    islandSpeeds.Remove("speed");
    // Zoom: a change of your own (Ctrl +/-, Ctrl+wheel) brings up − 110% + in the island; its buttons zoom; it leaves on its own.
    Navigate("https://example.com/",false);await WaitForPage();var view=CurrentView();
    for(int i=0;i<60&&!(LiveCore(view)!=null&&LiveCore(view).Source.StartsWith("https://example.com",StringComparison.Ordinal));i++)await Task.Delay(250);
    await Task.Delay(1200);
    // As a person would: Ctrl+wheel up, posted to the page's window so the real mouse stays put.
    // Setting ZoomFactor from code isn't reported by the engine, and DevTools key presses don't reach its shortcuts.
    IntPtr page=PageWidgetWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle);
    for(int i=0;i<2;i++){PostCtrlWheel(page,120);await Task.Delay(400);}
    await Task.Delay(1000);
    double wheeled=view.ZoomFactor;
    RegressionCheck("zooming the page shows the zoom row in the island",delegate {Require(wheeled>1.05&&IslandHasZoom&&islandZoomLevel.Text==Math.Round(wheeled*100)+"%",wheeled+" "+(IslandHasZoom?islandZoomLevel.Text:"no row"));});
    CaptureElement(islandCard,"island-zoom.png");
    Func<string,Button> zoomButton=tag=>((Grid)islandZoom.Children[0]).Children.OfType<Button>().First(b=>(string)b.Tag==tag);
    zoomButton("island-zoom-out").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(300);
    double smaller=NextZoom(wheeled,-1);
    RegressionCheck("the island's minus zooms out a step",delegate {Require(Math.Abs(view.ZoomFactor-smaller)<0.01&&islandZoomLevel.Text==Math.Round(smaller*100)+"%",view.ZoomFactor+" "+islandZoomLevel.Text);});
    islandAway=false;WatchIslandPointer();
    RegressionCheck("the zoom row keeps the island up under the pointer",delegate {Require(!islandAway,"stepped aside");});
    zoomButton("island-zoom-reset").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(300);
    RegressionCheck("reset goes back to 100% and isn't kept",delegate {Require(Math.Abs(view.ZoomFactor-1)<0.01&&islandZoomLevel.Text=="100%"&&!state.SiteZoom.ContainsKey("example.com"),view.ZoomFactor+" "+islandZoomLevel.Text);});
    await Task.Delay(IslandZoomLinger+TimeSpan.FromMilliseconds(600));
    RegressionCheck("the zoom row leaves on its own",delegate {Require(!IslandHasZoom,"still shown");});
    ZoomView(view,1.5);await Task.Delay(300);
    RegressionCheck("Mica's own zoom (site card, menu) doesn't bring it up",delegate {Require(!IslandHasZoom,"shown");});
    ZoomView(view,1);await Task.Delay(300);
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
