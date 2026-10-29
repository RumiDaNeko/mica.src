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
   // As you'd see it with the page in use, not mid-typing in the address field.
   if(FocusManager!=null)FocusManager.ClearFocus();
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
    ToggleAi();
    for(int i=0;i<100&&aiView!=null&&!(aiView.IsBrowserInitialized&&(!claudeLoaded||(aiView.Address??"").Contains("tabId=")));i++)await Task.Delay(200);
    await Scene(folder,"5-claude-dark",4000);
    SetDark(false);
    await Scene(folder,"6-claude-light",1500);
   } catch(Exception e) {results.Add("FAIL preview: "+e);}
   File.WriteAllLines(Path.Combine(folder,"preview.txt"),results);
   Close();
  }
 }
}
