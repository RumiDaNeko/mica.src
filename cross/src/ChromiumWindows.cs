using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Mica {
 // Chromium's own windows (its download bubble, prompts, dialogs) are top-level windows of Mica's process with
 // Chromium's window class; the pages themselves are child windows. The self-test checks none ever shows. Windows
 // only: elsewhere null, and the check is skipped.
 static class ChromiumWindows {
  delegate bool EnumProc(IntPtr hwnd,IntPtr data);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc,IntPtr data);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder name,int max);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int max);
  internal static List<string> Visible() {
   if(!OperatingSystem.IsWindows())return null;
   var found=new List<string>();uint me=(uint)Environment.ProcessId;
   EnumWindows(delegate(IntPtr hwnd,IntPtr data) {
    uint pid;GetWindowThreadProcessId(hwnd,out pid);
    if(pid!=me||!IsWindowVisible(hwnd))return true;
    var name=new StringBuilder(256);GetClassName(hwnd,name,256);
    if(name.ToString().StartsWith("Chrome_WidgetWin",StringComparison.Ordinal)){var text=new StringBuilder(256);GetWindowText(hwnd,text,256);found.Add(name+" \""+text+"\"");}
    return true;
   },IntPtr.Zero);
   return found;
  }
 }
}
