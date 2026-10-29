using System;
using System.Runtime.InteropServices;

namespace Mica {
 // Native maximize sizes are physical pixels, including on mixed-DPI monitors.
 static class WindowBounds {
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
  internal static readonly uint UpdateMessage=RegisterWindowMessage("Mica.PrepareForUpdate");
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool SetProp(IntPtr hwnd,string name,IntPtr value);
  internal static void EnableUpdateShutdown(IntPtr hwnd) {SetProp(hwnd,"Mica.UpdateShutdownSupported",new IntPtr(1));}
  [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
  [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
  internal static bool HasNativeFrame(IntPtr hwnd) {return (GetWindowLongPtr(hwnd,-16).ToInt64()&0x00CF0000L)==0x00CF0000L;}
  internal static void EnableNativeFrame(IntPtr hwnd) {
   // Preserve native resize/maximize transitions while WindowChrome draws the
   // custom caption. DWM follows the user's Windows animation preferences.
   const long frame=0x00C00000L|0x00080000L|0x00040000L|0x00020000L|0x00010000L;
   long style=GetWindowLongPtr(hwnd,-16).ToInt64();if((style&frame)==frame)return;
   SetWindowLongPtr(hwnd,-16,new IntPtr(style|frame));
   SetWindowPos(hwnd,IntPtr.Zero,0,0,0,0,0x0020|0x0001|0x0002|0x0004|0x0010);
  }
  [StructLayout(LayoutKind.Sequential)] struct Point {public int X,Y;}
  [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)] struct MonitorInfo {public int Size;public Rect Monitor,Work;public uint Flags;}
  [StructLayout(LayoutKind.Sequential)] struct MinMaxInfo {public Point Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}
  [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
  [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
  public static bool ConstrainMaximize(IntPtr hwnd,IntPtr data) {
   if(data==IntPtr.Zero)return false;
   var monitor=new MonitorInfo {Size=Marshal.SizeOf(typeof(MonitorInfo))};
   if(!GetMonitorInfo(MonitorFromWindow(hwnd,2),ref monitor))return false;
   var limits=(MinMaxInfo)Marshal.PtrToStructure(data,typeof(MinMaxInfo));
   limits.MaxPosition.X=monitor.Work.Left-monitor.Monitor.Left;limits.MaxPosition.Y=monitor.Work.Top-monitor.Monitor.Top;
   limits.MaxSize.X=monitor.Work.Right-monitor.Work.Left;limits.MaxSize.Y=monitor.Work.Bottom-monitor.Work.Top;
   Marshal.StructureToPtr(limits,data,false);return true;
  }
 }
}
