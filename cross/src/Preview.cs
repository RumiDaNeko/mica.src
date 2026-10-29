using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Mica {
 // --preview <folder>: drives the off-screen window through a few scenes and saves a PNG of each, page content
 // included. On Windows the capture goes through PrintWindow (Chromium draws into its own child window, which
 // Avalonia's own renderer can't see); elsewhere only Mica's chrome is captured.
 sealed partial class BrowserWindow {
  [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd,IntPtr hdc,uint flags);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Rect32 rect);
  [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
  [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd,IntPtr hdc);
  [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
  [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc,ref BitmapInfo info,uint usage,out IntPtr bits,IntPtr section,uint offset);
  [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc,IntPtr obj);
  [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
  [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
  [StructLayout(LayoutKind.Sequential)] struct Rect32 {public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)] struct BitmapInfo {public int Size,Width,Height;public short Planes,BitCount;public int Compression,SizeImage,XPels,YPels,ClrUsed,ClrImportant;}

  // Menus are popups, windows of their own: they are drawn into the picture where they sit.
  void CapturePopup(Avalonia.Controls.Primitives.Popup popup,IntPtr target,Rect32 main,int w,int h) {
   if(popup==null||!popup.IsOpen||popup.Host==null)return;
   var top=popup.Host as Avalonia.Controls.TopLevel;var handle=top==null?null:top.TryGetPlatformHandle();if(handle==null)return;
   Rect32 r;GetWindowRect(handle.Handle,out r);int pw=r.Right-r.Left,ph=r.Bottom-r.Top;if(pw<=0||ph<=0)return;
   var info=new BitmapInfo {Size=40,Width=pw,Height=-ph,Planes=1,BitCount=32};
   IntPtr screen=GetDC(IntPtr.Zero),dc=CreateCompatibleDC(screen),bits;IntPtr dib=CreateDIBSection(dc,ref info,0,out bits,IntPtr.Zero,0),old=SelectObject(dc,dib);
   try {
    PrintWindow(handle.Handle,dc,2);
    var src=new byte[pw*ph*4];Marshal.Copy(bits,src,0,src.Length);
    var dst=new byte[w*h*4];Marshal.Copy(target,dst,0,dst.Length);
    for(int y=0;y<ph;y++)for(int x=0;x<pw;x++) {
     int tx=r.Left-main.Left+x,ty=r.Top-main.Top+y;if(tx<0||ty<0||tx>=w||ty>=h)continue;
     int s=(y*pw+x)*4,d=(ty*w+tx)*4;int a=src[s+3];if(src[s]<3&&src[s+1]<3&&src[s+2]<3)continue;
     if(a==0)a=255;for(int c=0;c<3;c++)dst[d+c]=(byte)((src[s+c]*255+dst[d+c]*(255-a))/255);
    }
    Marshal.Copy(dst,0,target,dst.Length);
   } finally {SelectObject(dc,old);DeleteObject(dib);DeleteDC(dc);ReleaseDC(IntPtr.Zero,screen);}
  }
  void Capture(string file) {
   var handle=TryGetPlatformHandle();
   if(OperatingSystem.IsWindows()&&handle!=null) {
    Rect32 r;GetWindowRect(handle.Handle,out r);int w=r.Right-r.Left,h=r.Bottom-r.Top;
    var info=new BitmapInfo {Size=40,Width=w,Height=-h,Planes=1,BitCount=32};
    IntPtr screen=GetDC(IntPtr.Zero),dc=CreateCompatibleDC(screen),bits;
    IntPtr dib=CreateDIBSection(dc,ref info,0,out bits,IntPtr.Zero,0),old=SelectObject(dc,dib);
    try {
     // 2 = PW_RENDERFULLCONTENT: includes GPU-composited content such as Chromium's.
     PrintWindow(handle.Handle,dc,2);
     CapturePopup(island,bits,r,w,h);CapturePopup(openMenu,bits,r,w,h);CapturePopup(openDialog,bits,r,w,h);
     // GDI leaves alpha at 0; the PNG wants it opaque.
     var pixels=new byte[w*h*4];Marshal.Copy(bits,pixels,0,pixels.Length);
     for(int i=3;i<pixels.Length;i+=4)pixels[i]=255;
     using(var shot=new WriteableBitmap(new PixelSize(w,h),new Vector(96,96),Avalonia.Platform.PixelFormat.Bgra8888,Avalonia.Platform.AlphaFormat.Opaque)) {
      using(var fb=shot.Lock())for(int y=0;y<h;y++)Marshal.Copy(pixels,y*w*4,fb.Address+y*fb.RowBytes,w*4);
      shot.Save(file);
     }
    } finally {SelectObject(dc,old);DeleteObject(dib);DeleteDC(dc);ReleaseDC(IntPtr.Zero,screen);}
    return;
   }
   var chrome=new RenderTargetBitmap(new PixelSize((int)Bounds.Width,(int)Bounds.Height),new Vector(96,96));chrome.Render(this);chrome.Save(file);
  }

  async Task Scene(string folder,string name,int settleMs) {
   // As you'd see it with the page in use, not mid-typing (Spotlight and find keep their field).
   if(FocusManager!=null&&!SpotlightOpen&&!findBar.IsVisible)FocusManager.ClearFocus();
   await Task.Delay(settleMs);
   string file=Path.Combine(folder,name+".png");
   try {Capture(file);results.Add("saved "+file);} catch(Exception e) {results.Add("FAIL "+name+": "+e.Message);}
  }

  async Task RunPreview(string folder) {
   trace=new System.Collections.Concurrent.ConcurrentQueue<string>();
   Directory.CreateDirectory(folder);
   try {
    var first=Active;
    var ready=new TaskCompletionSource<bool>();
    if(first.View.IsBrowserInitialized)ready.TrySetResult(true);else first.View.BrowserInitialized+=delegate {ready.TrySetResult(true);};
    await Task.WhenAny(ready.Task,Task.Delay(30000));
    SetDark(false);
    NewTab();var web=Active;
    await Load(web,delegate {Navigate("example.com");},25);
    NewTab();var games=Active;
    await Load(games,delegate {Navigate("mica://games");},20);
    // The new tab page lists sites when it loads, so it is reloaded after the visit above.
    Show(first.Entry.Id);
    await Load(first,Reload,20);
    await Scene(folder,"1-home-light",800);
    Show(games.Entry.Id);
    await Scene(folder,"2-games-light",800);

    SetDark(true);
    Show(first.Entry.Id);
    await Scene(folder,"3-home-dark",800);
    Show(games.Entry.Id);
    await Load(games,delegate {Navigate("mica://2048");},20);
    await Scene(folder,"4-game-dark",800);

    Show(web.Entry.Id);
    OpenSpotlight(false);spotlightInput.Text="exa";
    await Scene(folder,"7-spotlight-dark",600);
    spotlightInput.Text="> zoom";
    await Scene(folder,"8-palette-dark",600);
    CloseSpotlight();
    ShowFind();findInput.Text="domain";Bookmark();ZoomStep(1);
    await Scene(folder,"9-find-zoom-dark",1200);
    CloseFind();ZoomStep(0);
    ShowSiteCard();
    await Scene(folder,"13-site-card-dark",700);
    if(openMenu!=null)openMenu.IsOpen=false;
    ShowMenu(menuButton);
    await Scene(folder,"14-menu-dark",700);
    if(openMenu!=null)openMenu.IsOpen=false;
    ShowExtensions();
    await Scene(folder,"15-extensions-dark",700);
    if(openMenu!=null)openMenu.IsOpen=false;
    await Load(web,delegate {Navigate("https://en.wikipedia.org/wiki/Mica");},25);
    ToggleReader();
    await Scene(folder,"16-reader-dark",2500);
    ToggleReader();await Task.Delay(800);
    Show(first.Entry.Id);
    await Load(first,delegate {Navigate("mica://history");},20);
    await Scene(folder,"10-history-dark",800);
    SetSidebar(false);
    await Load(first,delegate {Navigate("mica://downloads");},20);
    await Scene(folder,"11-downloads-rail-dark",800);
    SetSidebar(true);
    await Load(first,delegate {Navigate("https://mica-nowhere.invalid/");},20);
    await Scene(folder,"12-error-dark",800);
    await Load(first,delegate {Navigate("mica://home");},20);
    Show(web.Entry.Id);
    state.AiPanel=true;state.AiProvider="claude";ApplyAiSetting();ToggleAi();
    for(int i=0;i<100&&(aiView==null||!(aiView.IsBrowserInitialized&&(!claudeLoaded||(aiView.Address??"").Contains("tabId="))));i++)await Task.Delay(200);
    await Scene(folder,"5-claude-dark",4000);
    SetDark(false);
    await Scene(folder,"6-claude-light",1500);
   } catch(Exception e) {results.Add("FAIL preview: "+e);}
   File.WriteAllLines(Path.Combine(folder,"preview.txt"),results);
   Close();
  }

  void CloseExtra() {if(openMenu!=null)openMenu.IsOpen=false;CloseDialog();if(SpotlightOpen)CloseSpotlight();}
  // --shots: the states the Windows harness captures, in the same order and names, for a side-by-side.
  async Task RunShots(string folder) {
   trace=new System.Collections.Concurrent.ConcurrentQueue<string>();
   Directory.CreateDirectory(folder);
   try {
    var first=Active;
    var ready=new TaskCompletionSource<bool>();
    if(first.View.IsBrowserInitialized)ready.TrySetResult(true);else first.View.BrowserInitialized+=delegate {ready.TrySetResult(true);};
    await Task.WhenAny(ready.Task,Task.Delay(30000));await Task.Delay(3000);
    foreach(bool dark in new[]{false,true}) {
     string t=dark?"dark":"light";
     SetDark(dark);CloseExtra();
     await Scene(folder,"01-home-"+t,1500);
     await Load(first,delegate {Navigate("https://example.com/");},25);
     await Scene(folder,"02-web-"+t,800);
     ShowIslandMessage("Bookmarked. Find it in Spotlight.");await Scene(folder,"03-island-"+t,500);
     OpenSpotlight(false);await Scene(folder,"04-spotlight-"+t,900);CloseExtra();await Task.Delay(500);
     ShowSiteCard();await Scene(folder,"05-site-card-"+t,1200);CloseExtra();
     ShowMenu(menuButton);await Scene(folder,"06-menu-"+t,900);CloseExtra();
     ShowSpaces(spaceButton);await Scene(folder,"07-spaces-"+t,900);CloseExtra();
     ToggleSidebar();await Scene(folder,"08-rail-"+t,1200);ToggleSidebar();await Task.Delay(800);
     ShowPersonalize();await Scene(folder,"09-personalize-"+t,1500);CloseExtra();
     ShowExtensions();await Scene(folder,"10-extensions-"+t,1500);CloseExtra();
     ShowShortcuts();await Scene(folder,"11-shortcuts-"+t,1200);CloseExtra();
     await Load(first,delegate {Navigate("mica://history");},20);await Scene(folder,"12-history-"+t,1500);
     await Load(first,delegate {Navigate("mica://downloads");},20);await Scene(folder,"13-downloads-"+t,1200);
     ShowBookmarks();await Scene(folder,"14-bookmarks-"+t,900);CloseExtra();
     await Load(first,delegate {Navigate("https://example.com/");},25);
     state.AiPanel=true;ApplyAiSetting();ToggleAi();await Scene(folder,"15-ai-"+t,5000);ToggleAi();await Task.Delay(600);
     ShowAccount(avatarButton);await Scene(folder,"16-account-"+t,900);CloseExtra();
     await Load(first,delegate {Navigate("mica://home");},20);
    }
    // Vietnamese, then a home background.
    SwitchLanguage("vi");await Task.Delay(1500);
    await Scene(folder,"17-vi-home",1500);
    ShowMenu(menuButton);await Scene(folder,"18-vi-menu",900);CloseExtra();
    ShowPersonalize();await Scene(folder,"19-vi-personalize",1200);CloseExtra();
    SwitchLanguage("en");state.HomeBackground="Dusk";RefreshHomePages();await Task.Delay(2500);
    await Scene(folder,"20-home-dusk-dark",800);
    SetDark(false);state.HomeBackground="Blossom";RefreshHomePages();await Task.Delay(2500);
    await Scene(folder,"21-home-blossom-light",800);
    state.HomeBackground=null;RefreshHomePages();await Task.Delay(800);
   } catch(Exception e) {results.Add("FAIL shots: "+e);}
   File.WriteAllLines(Path.Combine(folder,"preview.txt"),results);
   Close();
  }
 }
}