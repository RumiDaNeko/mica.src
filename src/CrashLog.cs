using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Mica {
 // When something goes wrong that Mica can't recover from, it writes what happened to a crash report on
 // this device (never sent anywhere), saves every window's tabs, and on the next start says it closed
 // unexpectedly. A "running" marker tells the two apart: a clean exit removes it, a crash leaves it behind.
 static class CrashLog {
  const int Kept=10;
  internal static string Folder;
  static string Marker {get {return Path.Combine(Folder,"running");}}
  // Set at start: the previous run ended without closing properly.
  internal static bool PreviousRunCrashed;

  internal static void Install(Application app,string profileFolder) {
   Folder=Path.Combine(profileFolder,"Crashes");
   try {Directory.CreateDirectory(Folder);PreviousRunCrashed=File.Exists(Marker);File.WriteAllText(Marker,System.Diagnostics.Process.GetCurrentProcess().Id.ToString());}catch(Exception){}
   app.Exit+=delegate {ClearMarker();};
   app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e) {
    // Reaching into a tab that has just closed (its page view is gone) harms nothing else: noted, and Mica carries on.
    if(ClosedTab(e.Exception)){Write(e.Exception,"recovered");e.Handled=true;return;}
    Crashed(e.Exception);
   };
   AppDomain.CurrentDomain.UnhandledException+=delegate(object sender,UnhandledExceptionEventArgs e) {Crashed(e.ExceptionObject as Exception);};
   // A background task that failed without anyone looking is noted, not fatal.
   TaskScheduler.UnobservedTaskException+=delegate(object sender,UnobservedTaskExceptionEventArgs e) {Write(e.Exception,"background task");e.SetObserved();};
  }
  internal static bool ClosedTab(Exception error) {
   var disposed=error as ObjectDisposedException;
   return disposed!=null&&error.StackTrace!=null&&error.StackTrace.Contains("Microsoft.Web.WebView2.Wpf.WebView2Base.VerifyNotDisposed");
  }
  internal static void ClearMarker() {try {if(Folder!=null)File.Delete(Marker);}catch(Exception){}}
  static bool crashing;
  static void Crashed(Exception error) {
   if(crashing)return;crashing=true;
   Write(error,"crash");
   try {BrowserApp.SaveAllNow();}catch(Exception){}
  }
  // Writes a report and returns its path (null if it couldn't be written).
  internal static string Write(Exception error,string kind) {
   if(Folder==null)return null;
   try {
    Directory.CreateDirectory(Folder);
    string path=Path.Combine(Folder,"crash-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".txt");
    File.WriteAllText(path,"Mica "+BrowserApp.AppVersion+" · "+kind+"\r\n"+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")+"\r\n"+Environment.OSVersion+(Environment.Is64BitProcess?" · 64-bit":"")+"\r\n\r\n"+(error==null?"(no details)":error.ToString())+"\r\n");
    foreach(var old in Directory.GetFiles(Folder,"crash-*.txt").OrderByDescending(f=>f).Skip(Kept))try {File.Delete(old);}catch(Exception){}
    return path;
   }catch(Exception){return null;}
  }
  internal static string Latest() {
   try {return Folder==null||!Directory.Exists(Folder)?null:Directory.GetFiles(Folder,"crash-*.txt").OrderByDescending(f=>f).FirstOrDefault();}catch(Exception){return null;}
  }
 }
}
