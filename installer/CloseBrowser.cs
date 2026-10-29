using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace Mica {
 internal static class CloseBrowser {
  delegate bool WindowCallback(IntPtr hwnd,IntPtr data);
  [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback,IntPtr data);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
  [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetProp(IntPtr hwnd,string name);
  [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
  [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
  static bool SameUser(Process process) {
   IntPtr token;if(!OpenProcessToken(process.Handle,8,out token))return false;
   try {using(var identity=new WindowsIdentity(token))return identity.User==WindowsIdentity.GetCurrent().User;}finally{CloseHandle(token);}
  }
  internal static void RequestAndWait(int? onlyPid=null,int timeout=20000) {
   var processes=new List<Process>();int session=Process.GetCurrentProcess().SessionId;
   try {
    foreach(var process in Process.GetProcessesByName("Mica")) {
     bool keep=false;try {keep=(!onlyPid.HasValue||process.Id==onlyPid.Value)&&process.SessionId==session&&SameUser(process)&&string.Equals(Path.GetFileName(process.MainModule.FileName),"Mica.exe",StringComparison.OrdinalIgnoreCase);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
     if(keep)processes.Add(process);else process.Dispose();
    }
    var ids=new HashSet<int>();foreach(var process in processes)ids.Add(process.Id);
    var windows=new List<IntPtr>();EnumWindows(delegate(IntPtr hwnd,IntPtr unused){uint pid;GetWindowThreadProcessId(hwnd,out pid);if(ids.Contains((int)pid)&&GetWindow(hwnd,4)==IntPtr.Zero)windows.Add(hwnd);return true;},IntPtr.Zero);
    // New versions save all normal windows together before closing. Old versions
    // ignore this message, then receive the same WM_CLOSE as their close button.
    uint prepare=RegisterWindowMessage("Mica.PrepareForUpdate");foreach(var hwnd in windows)PostMessage(hwnd,prepare,IntPtr.Zero,IntPtr.Zero);
    Thread.Sleep(500);
    foreach(var hwnd in windows){uint pid;GetWindowThreadProcessId(hwnd,out pid);if(ids.Contains((int)pid)&&GetProp(hwnd,"Mica.UpdateShutdownSupported")==IntPtr.Zero)PostMessage(hwnd,0x10,IntPtr.Zero,IntPtr.Zero);}
    var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<timeout) {
     bool running=false;foreach(var process in processes)if(!process.HasExited)running=true;if(!running)return;Thread.Sleep(100);
    }
    throw new IOException("Mica is still closing. Check for an open dialog, then try again. Your browser has not been forced to quit.");
   }finally {foreach(var process in processes)process.Dispose();}
  }
 }
}
