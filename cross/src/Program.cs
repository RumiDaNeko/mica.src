using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Xilium.CefGlue;
using Xilium.CefGlue.Common;

namespace Mica {
 static class Program {
  internal static string Profile,SelfTestResult,PreviewFolder;
  internal static int ExitCode;
  static readonly List<string> installs=new List<string>();
  internal static readonly List<string> InstallErrors=new List<string>();
  // Mica's profile for this shell. On Windows it is kept apart from the Windows build's MicaBrowser folder,
  // so trying the preview never touches a real session.
  static string DefaultProfile() {
   string root=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
   if(string.IsNullOrEmpty(root))root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".local","share");
   return Path.Combine(root,OperatingSystem.IsWindows()?"MicaCross":"Mica");
  }
  [STAThread]
  static int Main(string[] args) {
   for(int i=0;i<args.Length;i++) {
    if(args[i]=="--self-test"&&i+1<args.Length)SelfTestResult=Path.GetFullPath(args[++i]);
    else if(args[i]=="--preview"&&i+1<args.Length)PreviewFolder=Path.GetFullPath(args[++i]);
    else if(args[i]=="--profile"&&i+1<args.Length)Profile=Path.GetFullPath(args[++i]);
    else if(args[i]=="--install-extension"&&i+1<args.Length)installs.Add(args[++i]);
   }
   if(Profile==null)Profile=SelfTestResult!=null?Path.Combine(Path.GetTempPath(),"mica-cross-test-"+Guid.NewGuid().ToString("N").Substring(0,8)):DefaultProfile();
   Directory.CreateDirectory(Profile);
   // --install-extension <store id or address>: installs before the engine starts, so it loads this run.
   foreach(var input in installs) {
    try {CrossExtensions.Install(Profile,input,CefRuntime.ChromeVersion).GetAwaiter().GetResult();}
    catch(Exception e) {InstallErrors.Add(input+": "+e.Message);}
   }
   AppBuilder.Configure<App>().UsePlatformDetect().AfterSetup(delegate {
    string engine=Path.Combine(Profile,"Engine");
    // The Chromium packages leave locales under runtimes/<rid>/native when the app runs from the build folder.
    string locales=Path.Combine(AppContext.BaseDirectory,"locales"),runtimes=Path.Combine(AppContext.BaseDirectory,"runtimes");
    if((!Directory.Exists(locales)||Directory.GetFiles(locales).Length==0)&&Directory.Exists(runtimes))locales=Directory.GetDirectories(runtimes,"locales",SearchOption.AllDirectories).FirstOrDefault()??locales;
    var flags=new List<KeyValuePair<string,string>>();
    var extensions=CrossExtensions.Folders(Profile);
    if(extensions.Count>0)flags.Add(new KeyValuePair<string,string>("load-extension",string.Join(",",extensions)));
    // As on Windows: an extension driving a page (Claude for Chrome) doesn't get Chrome's "is debugging" bar.
    flags.Add(new KeyValuePair<string,string>("silent-debugger-extension-api",""));
    CefRuntimeLoader.Initialize(new CefSettings {RootCachePath=engine,CachePath=Path.Combine(engine,"Default"),PersistSessionCookies=true,LogSeverity=CefLogSeverity.Warning,LogFile=Path.Combine(Profile,"engine.log"),LocalesDirPath=Directory.Exists(locales)?locales:null},flags.ToArray(),BuiltInPages.Schemes());
   }).StartWithClassicDesktopLifetime(args);
   return ExitCode;
  }
 }
 sealed class App : Application {
  public override void Initialize() {
   Styles.Add(new FluentTheme());
   Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Mica/")) {Source=new Uri("avares://Mica/src/Theme.axaml")});
  }
  public override void OnFrameworkInitializationCompleted() {
   var desktop=ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
   if(desktop!=null)desktop.MainWindow=new BrowserWindow(Program.Profile,Program.SelfTestResult);
   base.OnFrameworkInitializationCompleted();
  }
  internal void ApplyTheme(bool dark) {RequestedThemeVariant=dark?ThemeVariant.Dark:ThemeVariant.Light;}
 }
}
