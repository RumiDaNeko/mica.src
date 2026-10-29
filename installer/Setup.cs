using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Mica {
 internal static class Setup {
  static Window window;static bool busy,finished;static readonly InstallEngine engine=new InstallEngine();
  static T Find<T>(string name) where T:FrameworkElement {return (T)window.FindName(name);}
  [DllImport("kernel32.dll")] static extern bool SetDefaultDllDirectories(uint flags);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool MoveFileEx(string existing,string replacement,int flags);
  [STAThread] static int Main(string[] args) {
   // Setup runs from Downloads: load DLLs from System32 only, never from beside this exe.
   SetDefaultDllDirectories(0x800);
   // Mica 0.12.6 and earlier start the updater inside the install folder; holding it as the current
   // directory would stop that folder from being swapped for the new version.
   try {Environment.CurrentDirectory=Path.GetTempPath();}catch(Exception){}
   string self=Assembly.GetExecutingAssembly().Location;
   if(args.Length==2&&args[0]=="--self-test")return SelfTest(self,args[1]);
   bool uninstall=Array.IndexOf(args,"--uninstall")>=0,update=Array.IndexOf(args,"--update")>=0,rollback=Array.IndexOf(args,"--rollback")>=0,previewUpdate=args.Length==2&&args[0]=="--preview-update";
   // Earlier temporary uninstallers (a running one stays locked and is skipped).
   try {foreach(string old in Directory.GetFiles(Path.GetTempPath(),"MicaUninstall-*.exe"))if(!string.Equals(Path.GetFullPath(old),Path.GetFullPath(self),StringComparison.OrdinalIgnoreCase))try {File.Delete(old);}catch(Exception){}}catch(Exception){}
   // Run the small uninstaller outside its installation so its own file can be removed.
   if((uninstall||rollback)&&string.Equals(Path.GetDirectoryName(self),engine.Root,StringComparison.OrdinalIgnoreCase)) {
    string temp=Path.Combine(Path.GetTempPath(),"MicaUninstall-"+Guid.NewGuid().ToString("N")+".exe");File.Copy(self,temp);MoveFileEx(temp,null,4/*MOVEFILE_DELAY_UNTIL_REBOOT; needs admin, else the next run cleans up*/);Process.Start(new ProcessStartInfo(temp,uninstall?"--uninstall":"--rollback") {UseShellExecute=true,WorkingDirectory=Path.GetTempPath()});return 0;
   }
   RenderOptions.ProcessRenderMode=RenderMode.SoftwareOnly;
   var app=new Application();using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Mica.Setup.xaml"))window=(Window)XamlReader.Load(stream);
   Find<TextBlock>("InstallPath").Text=engine.Root;Find<TextBlock>("InstallPath").ToolTip=engine.Root;
   Find<Border>("DragRegion").MouseLeftButtonDown+=delegate {window.DragMove();};Find<Button>("Close").Click+=delegate {window.Close();};window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(busy)e.Cancel=true;};
   // Started by Mica's "Restart to update": install right away, then reopen Mica.
   if(update||previewUpdate||rollback) {
    Find<TextBlock>("Heading").Text="Updating\nMica.";
    Find<TextBlock>("Description").Text="Make yourself a cup of tea and sit back. Mica will open again with your tabs in a moment.";
    // The card only says where Mica lives; the rest is for first installs.
    Find<StackPanel>("CardTitle").Visibility=Visibility.Collapsed;Find<TextBlock>("CardDetail").Visibility=Visibility.Collapsed;
    var path=Find<TextBlock>("InstallPath");path.Margin=new Thickness(0);path.FontSize=12;path.Foreground=new SolidColorBrush(Color.FromRgb(0xC5,0xB8,0xD0));
    Find<TextBlock>("Eyebrow").Text="A NEW VERSION IS HERE";Find<Button>("Action").Content="Updating…";
    // Keep the desktop shortcut the way it is: an update never adds one back.
    var shortcut=Find<CheckBox>("DesktopShortcut");shortcut.IsChecked=File.Exists(engine.DesktopLink);shortcut.Visibility=Visibility.Collapsed;
    if(rollback){Find<TextBlock>("Heading").Text="Going\nback.";Find<TextBlock>("Description").Text="Mica will open again on the version you had before, with your tabs.";Find<TextBlock>("Eyebrow").Text="UNDOING THE UPDATE";Find<Button>("Action").Content="Going back…";}
   }
   if(uninstall){Find<TextBlock>("Heading").Text="See you\naround.";Find<TextBlock>("Description").Text="Remove Mica from this computer. Your history, bookmarks and vault will stay on this device.";Find<CheckBox>("DesktopShortcut").Visibility=Visibility.Collapsed;Find<Button>("Action").Content="Uninstall Mica";}
   Find<Button>("Action").Click+=async delegate {
    if(finished){if(!uninstall)Process.Start(new ProcessStartInfo(Path.Combine(engine.Root,"Mica.exe")){UseShellExecute=true});window.Close();return;}
    bool owns=false;using(var mutex=new Mutex(false,"MicaBrowserDesktop")) {
     busy=true;Find<Button>("Action").IsEnabled=false;Find<Button>("Close").IsEnabled=false;bool desktop=Find<CheckBox>("DesktopShortcut").IsChecked==true;Find<CheckBox>("DesktopShortcut").Visibility=Visibility.Collapsed;Find<ProgressBar>("Progress").Visibility=Visibility.Visible;
     try {
      Find<TextBlock>("Status").Text="Closing Mica before continuing…";
      await Task.Run(delegate {CloseBrowser.RequestAndWait();});
      // Something else can hold the lock for a while: a Mica still shutting down, a Mica started meanwhile, a test
      // build or another setup. Wait for it rather than failing at once. The mutex belongs to the thread that takes
      // it, so poll here on the UI thread instead of blocking a worker; ask any Mica that reappeared to close again.
      var waited=Stopwatch.StartNew();long asked=0;
      while(true) {
       try {owns=mutex.WaitOne(0);}catch(AbandonedMutexException){owns=true;}
       if(owns||waited.ElapsedMilliseconds>=180000)break;
       Find<TextBlock>("Status").Text="Waiting for another Mica or setup window to close…";
       if(waited.ElapsedMilliseconds-asked>=10000){asked=waited.ElapsedMilliseconds;try {await Task.Run(delegate {CloseBrowser.RequestAndWait();});}catch(IOException){}}
       await Task.Delay(500);
      }
      if(!owns)throw new IOException("Another Mica or setup window is still running. Close that window, then try again.");
      var progress=new Progress<Tuple<double,string>>(value=>{Find<ProgressBar>("Progress").Value=value.Item1*100;Find<TextBlock>("Status").Text=value.Item2;});
      Action<double,string> report=(amount,text)=>((IProgress<Tuple<double,string>>)progress).Report(Tuple.Create(amount,text));
      await Task.Run(delegate {if(uninstall)engine.Uninstall();else if(rollback)engine.Rollback(report);else engine.Install(self,desktop,report);});
      finished=true;Find<ProgressBar>("Progress").Value=100;Find<TextBlock>("Heading").Text=uninstall?"Until next\ntime.":"Welcome\nhome.";Find<TextBlock>("Description").Text=uninstall?"Mica has been removed. Your browsing data is still safe on this device.":"Mica is ready in your Start menu. Right-click its taskbar icon for new windows and your recent pages.";Find<TextBlock>("Status").Text=uninstall?"Uninstalled.":"Installed for your Windows account.";Find<Button>("Action").Content=uninstall?"Done":"Open Mica   →";
     }catch(Exception error){Find<TextBlock>("Status").Text=error.Message;Find<Button>("Action").Content="Try again";if(!uninstall)Find<CheckBox>("DesktopShortcut").Visibility=Visibility.Visible;}
     finally {busy=false;Find<Button>("Action").IsEnabled=true;Find<Button>("Close").IsEnabled=true;if(owns)mutex.ReleaseMutex();}
    }
    if((update||rollback)&&finished){Process.Start(new ProcessStartInfo(Path.Combine(engine.Root,"Mica.exe")){UseShellExecute=true});window.Close();}
   };
   window.Loaded+=async delegate {
    if(SystemParameters.ClientAreaAnimation)Find<Border>("Surface").BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(240)));
    if((update||rollback)&&!uninstall)Find<Button>("Action").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    if(args.Length==2&&(args[0]=="--preview"||previewUpdate)) {await Task.Delay(400);window.UpdateLayout();var bitmap=new RenderTargetBitmap(740,530,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(args[1]))encoder.Save(output);window.Close();}
   };
   return app.Run(window);
  }
  static int SelfTest(string setup,string result) {
   string root=Path.Combine(Path.GetTempPath(),"MicaSetupTest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var test=new InstallEngine(root);var log=new System.Collections.Generic.List<string>();int exit=0;
   try {
    test.Install(setup,true,(p,t)=>{});if(!File.Exists(Path.Combine(test.Root,"Runtime","msedgewebview2.exe")))throw new Exception("Missing engine");log.Add("PASS full bundled payload extracted");
    if(WindowsIntegration.ShortcutIdentity(test.StartLink)!=WindowsIntegration.AppId||WindowsIntegration.ShortcutIdentity(test.DesktopLink)!=WindowsIntegration.AppId)throw new Exception("Shortcut identity mismatch");log.Add("PASS Start/Desktop shortcuts carry matching AppUserModelID");
    using(var key=Registry.CurrentUser.OpenSubKey(test.RegistryPrefix+"Classes\\Mica.Url\\shell\\open\\command"))if(key==null||!((string)key.GetValue("")).Contains("--url"))throw new Exception("URL registration missing");log.Add("PASS isolated browser and uninstall registry entries");
    using(var key=Registry.CurrentUser.OpenSubKey(test.RegistryPrefix+"Microsoft\\Windows\\CurrentVersion\\Uninstall\\Mica"))if(key==null||!((string)key.GetValue("UninstallString")).Contains("MicaUninstall.exe"))throw new Exception("Uninstaller registration missing");
    using(var browser=Process.Start(new ProcessStartInfo(Path.Combine(test.Root,"Mica.exe"),"--runtime-test") {UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden})) {
     if(!browser.WaitForExit(55000)){browser.Kill();throw new Exception("Installed browser timed out");}
     if(browser.ExitCode!=0)throw new Exception("Installed browser runtime test failed");
    }
    string runtimeResult=Path.Combine(root,"artifacts","runtime-result.txt");if(!File.Exists(runtimeResult)||!File.ReadAllText(runtimeResult).Contains("PASS bundled engine navigates"))throw new Exception("Installed runtime test did not complete");
    File.Copy(runtimeResult,Path.Combine(Path.GetDirectoryName(Path.GetFullPath(result)),"installed-runtime-result.txt"),true);log.Add("PASS installed browser launches its bundled engine and executes JavaScript");Thread.Sleep(2000);
    using(var browser=Process.Start(new ProcessStartInfo(Path.Combine(test.Root,"Mica.exe"),"--update-close-test") {UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden})) {
     string ready=Path.Combine(root,"artifacts","update-close-ready.txt");
     for(int i=0;i<100&&!File.Exists(ready)&&!browser.HasExited;i++)Thread.Sleep(100);
     if(!File.Exists(ready))throw new Exception("Update close fixture did not start");
     string session=File.ReadAllText(ready);CloseBrowser.RequestAndWait(browser.Id);
     if(!browser.HasExited||browser.ExitCode!=0)throw new Exception("Graceful update shutdown failed");
     string saved=File.ReadAllText(session);if(!saved.Contains("Update window one")||!saved.Contains("Update window two"))throw new Exception("Multi-window session was lost");
     log.Add("PASS automatic graceful shutdown saves tabs from both browser windows");
    }
    string sentinel=Path.Combine(root,"profile-kept.txt");File.WriteAllText(sentinel,"keep");Environment.CurrentDirectory=Path.Combine(test.Root,"Runtime");test.Install(setup,true,(p,t)=>{});if(Environment.CurrentDirectory.StartsWith(test.Root,StringComparison.OrdinalIgnoreCase))throw new Exception("Setup kept the install folder busy");log.Add("PASS upgrade over existing managed installation, started from inside it");
    if(!Directory.Exists(test.Previous)||InstallEngine.VersionOf(test.Root)!=InstallEngine.Version)throw new Exception("The replaced copy wasn't kept for undo");log.Add("PASS the replaced copy is kept so the update can be undone");
    // The app-only update built beside this installer: no engine inside, the installed one is reused.
    string patch=Path.Combine(Path.GetDirectoryName(setup),Path.GetFileName(setup).Replace("-Setup.exe","-Update.exe"));
    if(File.Exists(patch)) {
     if(new FileInfo(patch).Length>new FileInfo(setup).Length/4)throw new Exception("The app-only update still carries the engine");
     File.WriteAllText(Path.Combine(test.Root,"before-patch.txt"),"old");test.Install(patch,true,(p,t)=>{});
     if(!File.Exists(Path.Combine(test.Root,"Runtime","msedgewebview2.exe"))||File.Exists(Path.Combine(test.Root,"before-patch.txt"))||!File.Exists(Path.Combine(test.Previous,"before-patch.txt")))throw new Exception("App-only update didn't reuse the engine");
     using(var browser=Process.Start(new ProcessStartInfo(Path.Combine(test.Root,"Mica.exe"),"--runtime-test") {UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden})){if(!browser.WaitForExit(55000)){browser.Kill();throw new Exception("Browser after app-only update timed out");}if(browser.ExitCode!=0)throw new Exception("Browser after app-only update failed its runtime test");}
     log.Add("PASS the app-only update reuses the installed engine and runs");Thread.Sleep(2000);
     var bare=new InstallEngine(Path.Combine(root,"bare"));bool refused=false;try {bare.Install(patch,false,(p,t)=>{});}catch(InvalidDataException){refused=true;}
     if(!refused||Directory.Exists(bare.Root))throw new Exception("App-only update installed without an engine");log.Add("PASS the app-only update refuses a computer without the engine");
    }else log.Add("SKIP no app-only update beside this installer");
    // Undo the update, then redo it.
    File.WriteAllText(Path.Combine(test.Root,"newest.txt"),"new");test.Rollback((p,t)=>{});
    if(File.Exists(Path.Combine(test.Root,"newest.txt"))||!File.Exists(Path.Combine(test.Previous,"newest.txt"))||!File.Exists(Path.Combine(test.Root,"Mica.exe")))throw new Exception("Undo didn't bring the earlier copy back");
    test.Rollback((p,t)=>{});if(!File.Exists(Path.Combine(test.Root,"newest.txt")))throw new Exception("Redo didn't bring the newer copy back");
    log.Add("PASS an update can be undone, and redone");
    // The lite installer: no engine inside; Windows' WebView2 runtime instead, for new installs only.
    string liteSetup=Path.Combine(Path.GetDirectoryName(setup),Path.GetFileName(setup).Replace("-Setup.exe","-Setup-Lite.exe"));
    if(File.Exists(liteSetup)) {
     if(new FileInfo(liteSetup).Length>new FileInfo(setup).Length/4)throw new Exception("The lite installer still carries the engine");
     bool keptOwn=false;try {test.Install(liteSetup,false,(p,t)=>{});}catch(InvalidDataException){keptOwn=true;}
     if(!keptOwn||!File.Exists(Path.Combine(test.Root,"Runtime","msedgewebview2.exe")))throw new Exception("Lite installer replaced an install that has its own engine");log.Add("PASS the lite installer leaves an install with its own engine alone");
     var newer=new InstallEngine(Path.Combine(root,"lite-newer"));Directory.CreateDirectory(Path.Combine(newer.ProfileRoot,"WebView","EBWebView"));File.WriteAllText(Path.Combine(newer.ProfileRoot,"WebView","EBWebView","Last Version"),"999.0.0.0");
     bool guarded=false;try {newer.Install(liteSetup,false,(p,t)=>{});}catch(InvalidDataException){guarded=true;}
     if(!guarded||Directory.Exists(newer.Root))throw new Exception("Lite installer ignored data from a newer engine");log.Add("PASS the lite installer won't put newer Mica data on an older engine");
     var lite=new InstallEngine(Path.Combine(root,"lite"));
     if(InstallEngine.WindowsEngineVersion()==null){bool refused=false;try {lite.Install(liteSetup,false,(p,t)=>{});}catch(InvalidDataException){refused=true;}if(!refused)throw new Exception("Lite installed without Windows' engine");log.Add("PASS the lite installer refuses a computer without Windows' engine");}
     else {
      try {
       lite.Install(liteSetup,false,(p,t)=>{});
       if(Directory.Exists(Path.Combine(lite.Root,"Runtime"))||File.ReadAllText(Path.Combine(lite.Root,"engine-mode.txt")).Trim()!="evergreen")throw new Exception("Lite install isn't set up for Windows' engine");
       using(var browser=Process.Start(new ProcessStartInfo(Path.Combine(lite.Root,"Mica.exe"),"--runtime-test") {UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden})){if(!browser.WaitForExit(55000)){browser.Kill();throw new Exception("Lite browser timed out");}if(browser.ExitCode!=0)throw new Exception("Lite browser failed its runtime test");}
       string liteResult=Path.Combine(Path.GetDirectoryName(lite.Root),"artifacts","runtime-result.txt");
       if(!File.Exists(liteResult)||!File.ReadAllText(liteResult).Contains("Windows' WebView2 runtime"))throw new Exception("Lite browser didn't run on Windows' engine");
       log.Add("PASS a lite install runs on Windows' own engine (~"+Math.Round(new FileInfo(liteSetup).Length/1048576.0)+" MB download)");
       InstallEngine.WaitForProcessesIn(lite.Root,20000);lite.Uninstall();
      }finally {Registry.CurrentUser.DeleteSubKeyTree(lite.RegistryPrefix.TrimEnd('\\'),false);}
     }
    }else log.Add("SKIP no lite installer beside this one");
    test.Uninstall();if(Directory.Exists(test.Root)||Directory.Exists(test.Previous)||File.Exists(test.StartLink)||File.Exists(test.DesktopLink)||!File.Exists(sentinel))throw new Exception("Uninstall scope failed");log.Add("PASS uninstall removes application/shortcuts and preserves outside data");
    Directory.CreateDirectory(test.Root);File.WriteAllText(Path.Combine(test.Root,"user-file.txt"),"keep");bool rejected=false;try{test.Install(setup,false,(p,t)=>{});}catch(IOException){rejected=true;}if(!rejected||!File.Exists(Path.Combine(test.Root,"user-file.txt")))throw new Exception("Unmanaged folder overwritten");log.Add("PASS unmanaged destination is refused");
   }catch(Exception e){exit=1;log.Add("FAIL "+e);}
   finally {Environment.CurrentDirectory=Path.GetTempPath();Registry.CurrentUser.DeleteSubKeyTree(test.RegistryPrefix.TrimEnd('\\'),false);File.WriteAllLines(result,log);
    // The engine from the last browser launch can outlive it for a few seconds; cleanup waits, and never hides the results.
    try {InstallEngine.WaitForProcessesIn(root,20000);InstallEngine.DeleteTree(root,Path.GetTempPath());}catch(Exception e){File.AppendAllText(result,"NOTE cleanup left "+root+": "+e.Message+Environment.NewLine);}}
   return exit;
  }
 }
}
