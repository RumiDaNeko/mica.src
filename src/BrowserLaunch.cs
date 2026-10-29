using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;

namespace Mica {
 static partial class Program {
  internal static readonly List<BrowserApp> Browsers=new List<BrowserApp>();
  static NamedPipeServerStream pipe;
  static bool stopping;
  internal static bool PreparingForUpdate;
  internal static void CloseForUpdate() {
   if(PreparingForUpdate)return;
   try {foreach(var browser in Browsers.ToArray())browser.SaveForUpdate();}
   catch(IOException){foreach(var browser in Browsers.ToArray())browser.ReportUpdateSaveError();return;}
   catch(UnauthorizedAccessException){foreach(var browser in Browsers.ToArray())browser.ReportUpdateSaveError();return;}
   catch(InvalidOperationException){foreach(var browser in Browsers.ToArray())browser.ReportUpdateSaveError();return;}
   PreparingForUpdate=true;
   foreach(var browser in Browsers.ToArray())browser.CloseForUpdate();
  }
  static string pipeName;
  internal static string NormalProfile {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MicaBrowser");}}
  internal static string PrivateRoot {get {return Path.Combine(NormalProfile,"PrivateSessions");}}
  [STAThread] static void Main(string[] args) {
   if(args.Length==1&&args[0]=="--mcp"){RunMcp();return;}
   // Test runs are started with flags only; a launch that carries a link (from another app) never counts as one.
   bool smoke=args.Length>0&&args[0].StartsWith("--",StringComparison.Ordinal)&&args[0].EndsWith("-test",StringComparison.Ordinal)&&args.All(a=>a.StartsWith("--",StringComparison.Ordinal)&&a.IndexOf(':')<0);bool owns;
   pipeName="MicaBrowser-"+WindowsIdentity.GetCurrent().User.Value;
   using(var mutex=new Mutex(true,smoke?"MicaBrowserSmoke":"MicaBrowserDesktop",out owns)) {
    if(!owns){if(!smoke)SendLaunch(args);return;}
    if(!smoke)DesktopShell.SetIdentity();
    // csc builds lack TargetFramework metadata, so .NET would otherwise offer legacy TLS only.
    System.Net.ServicePointManager.SecurityProtocol=System.Net.SecurityProtocolType.Tls12|(System.Net.SecurityProtocolType)12288;
    var app=new Application {ShutdownMode=ShutdownMode.OnLastWindowClose};
    string testProfile=smoke?Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","smoke-"+Guid.NewGuid().ToString("N"))):null;
    // The chosen language is read before the first window is built, so it opens in that language.
    LanguageFolder=testProfile??NormalProfile;Localizer.Use(Localizer.Resolve(ReadLanguage()));
    CrashLog.Install(app,testProfile??NormalProfile);
    app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e) {
     if(smoke){Directory.CreateDirectory(testProfile);File.AppendAllText(Path.Combine(testProfile,"errors.log"),DateTime.UtcNow+" "+e.Exception+Environment.NewLine);app.Shutdown(1);e.Handled=true;}
    };
    if(!smoke){CleanupOldPrivateSessions();Task.Run((Action)Listen);Task.Run((Action)ListenControl);}
    app.Exit+=delegate {stopping=true;if(pipe!=null)pipe.Dispose();};
    OpenWindow(args,smoke,testProfile);app.Run();
   }
  }
  internal static BrowserApp OpenWindow(string[] args,bool testing=false,string testProfile=null) {
   bool isPrivate=args.Contains("--private");
   string folder=isPrivate?(testing?Path.Combine(testProfile,"PrivateSessions",Guid.NewGuid().ToString("N")):Path.Combine(PrivateRoot,Guid.NewGuid().ToString("N"))):testProfile??NormalProfile;
   // Another profile: its own folder under Profiles, sharing the main folder's engine.
   int named=Array.IndexOf(args,"--profile");
   if(!isPrivate&&named>=0&&named+1<args.Length&&BrowserApp.IsProfileId(args[named+1]))folder=Path.Combine(testProfile??NormalProfile,"Profiles",args[named+1]);
   Directory.CreateDirectory(folder);
   var regular=Browsers.FirstOrDefault(b=>b.CanShareProfile(folder));
   BrowserState initial=regular==null?null:regular.NewWindowState();
   if(isPrivate) {
    var themeSource=Browsers.FirstOrDefault(b=>b.CanShareProfile(testProfile??NormalProfile))??Browsers.FirstOrDefault();
    initial=new BrowserState {Dark=themeSource!=null?themeSource.ThemeDark:StateStore.Load(Path.Combine(testProfile??NormalProfile,"session.json")).Dark};
   }
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Mica.Shell.xaml")) {
    var window=(Window)XamlReader.Load(stream);var browser=new BrowserApp(window,folder,testing,isPrivate,initial,Browsers.Count==0);
    string url=LaunchUrl(args);
    if(url!=null)window.Loaded+=delegate {browser.OpenExternalUrl(url);};
    if(testing&&(args.Contains("--avatar-test")||args.Contains("--popups-test")||args.Contains("--resize-test")||args.Contains("--vault-test")||args.Contains("--home-test")||args.Contains("--motw-test")||args.Contains("--extwin-test")||args.Contains("--keys-test")||args.Contains("--motion-test")||args.Contains("--sandbox-test")||args.Contains("--devtools-test")||args.Contains("--protection-test")||args.Contains("--notice-test")||args.Contains("--alert-test")||args.Contains("--navprobe-test")||args.Contains("--fullscreen-test")||args.Contains("--tetris-test")||args.Contains("--autofill-test")||args.Contains("--totp-test")||args.Contains("--navhistory-test")||args.Contains("--claudeext-test")||args.Contains("--chatgptext-test")||args.Contains("--aipanel-test")||args.Contains("--import-test")||args.Contains("--memory-test")||args.Contains("--gallery-test")||args.Contains("--updates-test")||args.Contains("--personalize-test")||args.Contains("--tabs-test")||args.Contains("--features-test")||args.Contains("--more-test")||args.Contains("--perf-test"))){window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-6000;window.Top=-6000;window.ShowInTaskbar=false;}
    window.Show();return browser;
   }
  }
  static string LaunchUrl(string[] args) {int index=Array.IndexOf(args,"--url");if(index<0||index+1>=args.Length||args[index+1].Length>8192)return null;string value=args[index+1];return Navigation.IsWeb(value)?value:Navigation.LocalFileAddress(value);}
  // Pipe names are predictable, so another account on this PC could create ours first. A client only
  // talks to a server running as this same user.
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe,out uint processId);
  [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,uint processId);
  [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
  [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint processId);
  internal static bool OwnPipeServer(NamedPipeClientStream client) {
   uint id;if(!GetNamedPipeServerProcessId(client.SafePipeHandle,out id))return false;
   IntPtr process=OpenProcess(0x1000,false,id);if(process==IntPtr.Zero)return false;
   try {
    IntPtr token;if(!OpenProcessToken(process,0x0008,out token))return false;
    try {using(var owner=new WindowsIdentity(token))return owner.User!=null&&owner.User.Equals(WindowsIdentity.GetCurrent().User);}finally {CloseHandle(token);}
   }finally {CloseHandle(process);}
  }
  static void SendLaunch(string[] args) {
   try {using(var client=new NamedPipeClientStream(".",pipeName,PipeDirection.Out)){client.Connect(3000);if(!OwnPipeServer(client))return;
    // This process was just started by the click, so Windows lets it come forward; pass that on to the Mica already running.
    uint server;if(GetNamedPipeServerProcessId(client.SafePipeHandle,out server))AllowSetForegroundWindow(server);
    using(var writer=new StreamWriter(client)){writer.WriteLine(new JavaScriptSerializer().Serialize(args.Take(4).ToArray()));}}}catch(IOException){}catch(TimeoutException){}
  }
  static void Listen() {
   while(!stopping)try {
    var security=new PipeSecurity();security.SetAccessRuleProtection(true,false);security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
    using(var server=new NamedPipeServerStream(pipeName,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,16384,16384,security)) {
     pipe=server;server.WaitForConnection();
     // A same-user sender must complete a small request promptly.
     var read=Task.Run(delegate {using(var reader=new StreamReader(server)){var text=new System.Text.StringBuilder();for(int i=0;i<16384;i++){int c=reader.Read();if(c<0||c=='\n')return text.ToString();text.Append((char)c);}return null;}});
     if(!read.Wait(2000))continue;string line=read.Result;if(line==null)continue;
     var args=new JavaScriptSerializer().Deserialize<string[]>(line);if(args==null)continue;
     Application.Current.Dispatcher.BeginInvoke(new Action(delegate {
      if(stopping)return;
      if(args.Contains("--private")||args.Contains("--new-window"))OpenWindow(args);
      else {var browser=Browsers.FirstOrDefault(b=>b.CanShareProfile(NormalProfile));if(browser==null)OpenWindow(args);else browser.HandleLaunch(LaunchUrl(args));}
     }));
    }
   }catch(UnauthorizedAccessException){Thread.Sleep(5000);} // Another account holds the name; links from other apps can't reach Mica until it's gone.
   catch(Exception e){if(!(e is IOException||e is ObjectDisposedException||e is ArgumentException||e is InvalidOperationException||e is AggregateException))throw;}
  }
  static void CleanupOldPrivateSessions() {
   if(!Directory.Exists(PrivateRoot))return;
   foreach(string folder in Directory.GetDirectories(PrivateRoot))DeletePrivateDirectory(folder,PrivateRoot);
  }
  static bool DeletePrivateDirectory(string folder,string root) {
   string full=Path.GetFullPath(folder),parent=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   Guid id;if(!full.StartsWith(parent,StringComparison.OrdinalIgnoreCase)||!Guid.TryParseExact(Path.GetFileName(full),"N",out id))return false;
   try {if(Directory.Exists(full)){if((File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)return false;Directory.Delete(full,true);}return true;}catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
  }
  internal static void SchedulePrivateCleanup(string folder) {
   // Engine shutdown can hold files briefly. A crashed/interrupted cleanup retries on next launch.
   var cleanup=new Thread(delegate() {for(int i=0;i<12;i++){Thread.Sleep(500);if(DeletePrivateDirectory(folder,Path.GetDirectoryName(folder)))break;}});
   cleanup.IsBackground=false;cleanup.Name="Mica private profile cleanup";cleanup.Start();
  }
 }
 partial class BrowserApp {
  internal void SaveForUpdate() {if(!privateMode)StateStore.Save(statePath,SnapshotForSave());}
  internal void ReportUpdateSaveError() {Status("Mica couldn't save your session. Free some disk space or check folder permissions, then retry setup.");}
  internal void CloseForUpdate() {window.Close();}
  internal bool CanShareProfile(string folder) {return !privateMode&&!closing&&string.Equals(profile,folder,StringComparison.OrdinalIgnoreCase);}
  internal bool ThemeDark {get{return state.Dark;}}
  internal BrowserState NewWindowState() {return new BrowserState {History=state.History,Bookmarks=state.Bookmarks,RecentlyClosed=state.RecentlyClosed,AmbientYoutube=state.AmbientYoutube,DockedDevTools=state.DockedDevTools,SiteZoom=state.SiteZoom,ReaderSize=state.ReaderSize,ReaderSerif=state.ReaderSerif,StrictTracking=state.StrictTracking,TrackingLevel=state.TrackingLevel,Dark=state.Dark,Space=state.Space,FrameColor=state.FrameColor,HomeBackground=state.HomeBackground,HomeGlass=state.HomeGlass,Jars=state.Jars,SiteKeywords=state.SiteKeywords,SiteMemory=state.SiteMemory,AutoClearSites=state.AutoClearSites,TreeTabs=state.TreeTabs,CloseEffect=state.CloseEffect,AiPanel=state.AiPanel,AiProvider=state.AiProvider,AiCustomUrl=state.AiCustomUrl,Spaces=new List<string>(state.Spaces)};}
  internal void OpenExternalUrl(string url) {if(Navigation.IsWeb(url)||Navigation.IsLocalFile(url))Navigate(url,true);}
  // Files dropped on Mica's own parts (the New tab page, the sidebar) open in new tabs; on a page, the page's
  // engine takes the drop itself. Up to 10 at a time.
  void AcceptFileDrops() {
   window.AllowDrop=true;
   window.DragOver+=delegate(object sender,DragEventArgs e) {if(e.Data.GetDataPresent(DataFormats.FileDrop)){e.Effects=DragDropEffects.Copy;e.Handled=true;}};
   window.Drop+=delegate(object sender,DragEventArgs e) {
    var files=e.Data.GetData(DataFormats.FileDrop) as string[];if(files==null)return;e.Handled=true;
    foreach(var path in files.Take(10)){string url=Navigation.LocalFileAddress(path);if(url!=null&&File.Exists(path))Navigate(url,true);}
   };
  }
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
  internal void HandleLaunch(string url) {
   if(window.WindowState==WindowState.Minimized)window.WindowState=WindowState.Normal;
   window.Activate();SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle);
   if(url!=null)OpenExternalUrl(url);
  }
  void LaunchBrowserWindow(bool privateWindow) {Program.OpenWindow(privateWindow?new[]{"--private"}:new[]{"--new-window"},smoke,smoke?profile:null);}
  BrowserState SnapshotForSave() {
   var snapshot=new BrowserState {History=state.History,Bookmarks=state.Bookmarks,RecentlyClosed=state.RecentlyClosed,AmbientYoutube=state.AmbientYoutube,DockedDevTools=state.DockedDevTools,SiteZoom=state.SiteZoom,ReaderSize=state.ReaderSize,ReaderSerif=state.ReaderSerif,StrictTracking=state.StrictTracking,TrackingLevel=state.TrackingLevel,Dark=state.Dark,Space=state.Space,ActiveId=state.ActiveId,FrameColor=state.FrameColor,HomeBackground=state.HomeBackground,HomeGlass=state.HomeGlass,Jars=state.Jars,SiteKeywords=state.SiteKeywords,SiteMemory=state.SiteMemory,AutoClearSites=state.AutoClearSites,TreeTabs=state.TreeTabs,CloseEffect=state.CloseEffect,AiPanel=state.AiPanel,AiProvider=state.AiProvider,AiCustomUrl=state.AiCustomUrl,Spaces=new List<string>(state.Spaces)};
   foreach(var browser in Program.Browsers.Where(b=>!b.privateMode&&b.profile==profile)){snapshot.Tabs.AddRange(browser.state.Tabs);foreach(var space in browser.state.Spaces)if(!snapshot.Spaces.Contains(space))snapshot.Spaces.Add(space);snapshot.Groups.AddRange(browser.state.Groups.Where(g=>!snapshot.Groups.Any(x=>x.Id==g.Id)));}
   if(snapshot.Tabs.Count==0){snapshot.Tabs.AddRange(state.Tabs);snapshot.Groups.AddRange(state.Groups);}return snapshot;
  }
  void RefreshPersonalization() {foreach(var browser in Program.Browsers.Where(b=>!b.closing&&b.profile==profile)){browser.RenderHome();browser.UpdateJumpList();if(browser.libraryPage!=null&&browser.libraryPage.IsVisible)browser.RenderLibrary();}}
  // A synced tab keeps its space when that space came along with it (and there is room for it here).
  string SnapshotSpace(BrowserState snapshot,string space) {if(space!=null&&!state.Spaces.Contains(space)&&snapshot.Spaces!=null&&snapshot.Spaces.Contains(space))state.AddSpace(space);return state.KnownSpace(space);}
  async Task ImportVaultSnapshot(BrowserState snapshot) {
   if(privateMode||snapshot==null)return;
   state.History.Clear();state.History.AddRange((snapshot.History??new List<PageEntry>()).Where(p=>p!=null&&Navigation.IsWeb(p.Url)).Take(500));
   state.Bookmarks.Clear();state.Bookmarks.AddRange((snapshot.Bookmarks??new List<PageEntry>()).Where(p=>p!=null&&Navigation.IsWeb(p.Url)).Take(2000));
   foreach(string id in views.Keys.ToList())DisposeView(id);
   state.Tabs.Clear();foreach(var tab in (snapshot.Tabs??new List<PageEntry>()).Where(p=>p!=null&&(Navigation.IsWeb(p.Url)||Navigation.IsInternal(p.Url))).Take(100))state.Tabs.Add(new PageEntry {Id=Guid.NewGuid().ToString("N"),Title=tab.Title,Url=tab.Url,Space=SnapshotSpace(snapshot,tab.Space),Pinned=tab.Pinned,LastUsed=DateTime.UtcNow});
   if(state.Tabs.Count==0)state.AddTab("mica://home");state.ActiveId=state.Tabs[0].Id;state.Space=state.Tabs[0].Space;state.Dark=snapshot.Dark;
   ApplyTheme();Save();RefreshPersonalization();await Activate(state.ActiveId);
  }
 }
}
