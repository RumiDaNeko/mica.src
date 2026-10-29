using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Xilium.CefGlue;
using Xilium.CefGlue.Common;

namespace Mica {
 static class Program {
  internal static string Profile,SelfTestResult,PreviewFolder;
  // --shots: the preview walks the same states as the Windows capture harness, with the same file names.
  internal static bool Shots;
  internal static int ExitCode;
  // --private: a window whose profile is thrown away when it closes.
  internal static bool Private;
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
    else if(args[i]=="--private")Private=true;
    else if(args[i]=="--shots")Shots=true;
   }
   if(Profile==null&&Private)Profile=Path.Combine(Path.GetTempPath(),"mica-private-"+Guid.NewGuid().ToString("N").Substring(0,8));
   if(Profile==null)Profile=SelfTestResult!=null?Path.Combine(Path.GetTempPath(),"mica-cross-test-"+Guid.NewGuid().ToString("N").Substring(0,8)):DefaultProfile();
   Directory.CreateDirectory(Profile);
   string chosen=null;try {chosen=File.ReadAllText(Path.Combine(Profile,"language")).Trim();} catch(Exception) {}
   Localizer.Use(Localizer.Resolve(chosen));
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
    // Chromium's own UI stays away (ChromiumUi.cs): its profile settings are written before it starts.
    ChromiumUi.SeedPreferences(Path.Combine(engine,"Default"));
    foreach(var name in ChromiumUi.Switches)flags.Add(new KeyValuePair<string,string>(name,""));
    // The self-test asks for the camera on machines that have none (build machines): Chromium's stand-in camera.
    if(SelfTestResult!=null)flags.Add(new KeyValuePair<string,string>("use-fake-device-for-media-stream",""));
    // Machines with no usable GPU (build machines): Chromium's GPU process would crash and restart over and over,
    // stalling pages, so it draws in software from the start.
    if(Environment.GetEnvironmentVariable("MICA_NO_GPU")=="1"){flags.Add(new KeyValuePair<string,string>("disable-gpu",""));flags.Add(new KeyValuePair<string,string>("disable-gpu-compositing",""));}
    CefRuntimeLoader.DefaultClient=new ExtensionWindows();
    CefRuntimeLoader.Initialize(new CefSettings {RootCachePath=engine,CachePath=Path.Combine(engine,"Default"),PersistSessionCookies=true,LogSeverity=CefLogSeverity.Warning,LogFile=Path.Combine(Profile,"engine.log"),LocalesDirPath=Directory.Exists(locales)?locales:null},flags.ToArray(),BuiltInPages.Schemes());
   }).StartWithClassicDesktopLifetime(args);
   if(Private)try {Directory.Delete(Profile,true);} catch(Exception) {}
   return ExitCode;
  }
 }
 sealed class App : Application {
  public override void Initialize() {
   // The Mac menu bar's app menu takes this name; without it Avalonia shows "Avalonia Application".
   Name="Mica";
   Styles.Add(new FluentTheme());
   Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Mica/")) {Source=new Uri("avares://Mica/src/Theme.axaml")});
  }
  public override void OnFrameworkInitializationCompleted() {
   var desktop=ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
   if(desktop!=null) {
    var window=new BrowserWindow(Program.Profile,Program.SelfTestResult);desktop.MainWindow=window;
    // The Mac app menu: Mica's own items instead of Avalonia's "About Avalonia". macOS adds Services, Hide Mica,
    // Hide Others, Show All and Quit Mica (Cmd+Q) after them.
    if(OperatingSystem.IsMacOS()) {
     var about=new NativeMenuItem("About Mica");about.Click+=delegate {window.ShowAbout();};
     var settings=new NativeMenuItem("Settings…") {Gesture=new Avalonia.Input.KeyGesture(Avalonia.Input.Key.OemComma,Avalonia.Input.KeyModifiers.Meta)};settings.Click+=delegate {window.ShowPersonalize();};
     var menu=new NativeMenu();menu.Add(about);menu.Add(new NativeMenuItemSeparator());menu.Add(settings);
     NativeMenu.SetMenu(this,menu);
    }
   }
   base.OnFrameworkInitializationCompleted();
  }
  internal void ApplyTheme(bool dark) {RequestedThemeVariant=dark?ThemeVariant.Dark:ThemeVariant.Light;}
 }
}
