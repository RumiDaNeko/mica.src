using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Xilium.CefGlue;

namespace Mica {
 // Which Chromium engine (CEF build) the shell runs, as BundledRuntime does for the Windows build: the one it shipped
 // with, or a newer build of the same CEF branch that CefEngineUpdater downloaded into the per-user Engines folder
 // (never into the app, which stays signed). The newest engine that loads and reports CefGlue's API hash wins from
 // the next launch. One that fails to load is set aside at once; one that twice didn't stay up after starting is set
 // aside on the next launch; either way the next one, and in the end the bundled engine, is used instead.
 static class CefEngine {
  const string Ready=".ready",Failed=".failed",Starts=".starts";
  static Timer watch;
  internal static string ChosenFolder,ChosenVersion;

  internal static string Root {get {
   string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
   if(OperatingSystem.IsMacOS())return Path.Combine(home,"Library","Application Support","Mica","Engines");
   if(OperatingSystem.IsLinux()) {
    string data=Environment.GetEnvironmentVariable("XDG_DATA_HOME");
    return Path.Combine(string.IsNullOrEmpty(data)?Path.Combine(home,".local","share"):data,"Mica","Engines");
   }
   // Windows builds the shell for development only; the Windows build's own engines live in Mica\Engines.
   return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MicaCross","Engines");
  }}

  // "156.0.14+g4fa4ab5+chromium-156.0.8078.48" -> 156.0.14.
  internal static Version ParseVersion(string cefVersion) {
   if(string.IsNullOrEmpty(cefVersion))return null;
   int plus=cefVersion.IndexOf('+');Version version;
   return Version.TryParse(plus<0?cefVersion:cefVersion.Substring(0,plus),out version)&&version.Build>=0?version:null;
  }
  // The engine the app shipped with: beside the app (Windows, macOS) or beside CefGlue's helper (Linux).
  internal static string BundledFolder() {
   foreach(var folder in new[]{AppContext.BaseDirectory,Path.Combine(AppContext.BaseDirectory,"CefGlueBrowserProcess")})
    if(File.Exists(Path.Combine(folder,CefRuntime.LibCefFileName)))return folder;
   return null;
  }
  internal static string FolderVersion(string folder) {
   try {return File.ReadAllText(Path.Combine(folder,"cef.version")).Trim();} catch(IOException) {} catch(UnauthorizedAccessException) {}
   return null;
  }

  // Engines that finished installing, newest first: (version, folder). Each folder is named after its CEF version.
  internal static List<Tuple<Version,string>> Installed(string root) {
   var found=new List<Tuple<Version,string>>();
   try {
    if(!Directory.Exists(root))return found;
    foreach(var folder in Directory.GetDirectories(root)) {
     var version=ParseVersion(Path.GetFileName(folder));
     if(version!=null&&File.Exists(Path.Combine(folder,Ready))&&File.Exists(Path.Combine(folder,CefRuntime.LibCefFileName)))found.Add(Tuple.Create(version,folder));
    }
   } catch(IOException) {} catch(UnauthorizedAccessException) {}
   return found.OrderByDescending(f=>f.Item1).ToList();
  }
  internal static bool IsFailed(string folder) {return File.Exists(Path.Combine(folder,Failed));}
  internal static void MarkFailed(string folder,string why) {
   try {File.WriteAllText(Path.Combine(folder,Failed),DateTime.UtcNow.ToString("o")+" "+why);} catch(IOException) {} catch(UnauthorizedAccessException) {}
   Log(Path.GetDirectoryName(folder),Path.GetFileName(folder)+" set aside: "+why);
  }
  // Launches that chose this engine and haven't yet shown it stays up.
  internal static int StartCount(string folder) {
   try {int count;return int.TryParse(File.ReadAllText(Path.Combine(folder,Starts)).Trim(),out count)?count:0;} catch(IOException) {return 0;} catch(UnauthorizedAccessException) {return 0;}
  }
  internal static void RecordStart(string folder) {try {File.WriteAllText(Path.Combine(folder,Starts),(StartCount(folder)+1).ToString());} catch(IOException) {} catch(UnauthorizedAccessException) {}}
  internal static void Confirm(string folder) {try {File.Delete(Path.Combine(folder,Starts));} catch(IOException) {} catch(UnauthorizedAccessException) {}}

  // The engine to run: the newest downloaded one of the bindings' CEF major that is newer than the bundled engine,
  // not set aside, and verified; otherwise the bundled one.
  internal static string Choose(string root,string bundled,Version bundledVersion,int major,Func<string,bool> verify) {
   foreach(var engine in Installed(root)) {
    string folder=engine.Item2;
    if(engine.Item1.Major!=major||bundledVersion!=null&&engine.Item1<=bundledVersion||IsFailed(folder))continue;
    int starts=StartCount(folder);
    if(starts>=2){MarkFailed(folder,"it didn't stay up after "+starts+" starts");continue;}
    if(!verify(folder)){MarkFailed(folder,"its libcef didn't load or doesn't match CefGlue's API hash");continue;}
    return folder;
   }
   return bundled;
  }

  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr ApiHashFunction(int version,int entry);
  // Loads the engine's libcef and asks it for the API hash of CefGlue's API version. A library macOS refuses to load
  // (library validation), a broken download or another CEF branch fails here, before CEF starts. On success the
  // library stays loaded; CefGlue's resolver opens the same file again and gets the same handle.
  internal static bool VerifyLibrary(string folder) {
   IntPtr library;
   if(!NativeLibrary.TryLoad(Path.Combine(folder,CefRuntime.LibCefFileName),out library))return false;
   bool ok=false;
   try {
    IntPtr export;
    if(NativeLibrary.TryGetExport(library,"cef_api_hash",out export)) {
     var hash=Marshal.PtrToStringAnsi(Marshal.GetDelegateForFunctionPointer<ApiHashFunction>(export)(CefRuntime.ApiVersion,0));
     ok=string.Equals(hash,CefRuntime.ApiHash,StringComparison.OrdinalIgnoreCase);
    }
   } catch(Exception) {ok=false;}
   if(!ok)NativeLibrary.Free(library);
   return ok;
  }

  // Picks the engine before CEF starts (CefGlue loads libcef lazily, at the first browser) and, when asked, starts
  // the daily update check. An engine named in CEFGLUE_LIBCEF_DIR by the user is left alone.
  internal static void Use(bool update) {
   string bundled=BundledFolder();
   if(bundled==null||CefRuntime.LibCefDirectory!=null)return;
   string bundledVersion=FolderVersion(bundled)??CefRuntime.BindingsCefVersion,root=Root;
   string chosen=Choose(root,bundled,ParseVersion(bundledVersion),CefRuntime.BindingsCefVersionMajor,VerifyLibrary);
   ChosenFolder=chosen;ChosenVersion=chosen==bundled?bundledVersion:Path.GetFileName(chosen);
   if(chosen!=bundled) {
    RecordStart(chosen);
    Environment.SetEnvironmentVariable(CefRuntime.LibCefDirectoryVariable,chosen);
    // Up for 20 seconds after CEF started, or a normal exit after it started: the engine works.
    DateTime started=DateTime.MinValue;
    watch=new Timer(delegate {
     if(!CefRuntime.IsInitialized)return;
     if(started==DateTime.MinValue){started=DateTime.UtcNow;return;}
     if(DateTime.UtcNow-started<TimeSpan.FromSeconds(20))return;
     Confirm(chosen);watch.Dispose();
    },null,5000,5000);
    AppDomain.CurrentDomain.ProcessExit+=delegate {if(CefRuntime.IsInitialized)Confirm(chosen);};
    Log(root,"using "+ChosenVersion);
   }
   if(update&&UpdatesAllowed())CefEngineUpdater.Schedule(root,ChosenVersion,chosen);
  }

  // In a signed, notarized Mica.app the hardened runtime's library validation refuses a libcef that isn't signed with
  // Mica's own Team ID, which a CEF build from the CDN never is. Loading it anyway takes the
  // com.apple.security.cs.disable-library-validation entitlement, which lets any library be loaded into Mica. That is
  // a packaging decision, so inside an app bundle the updater stays off unless its Info.plist opts in with
  // <key>MicaEngineUpdates</key><true/> (see docs/porting-mac-linux.md). Builds run outside a bundle aren't affected.
  internal static bool UpdatesAllowed() {
   if(!OperatingSystem.IsMacOS())return true;
   string macOS=Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('/');
   if(!macOS.EndsWith(".app/Contents/MacOS",StringComparison.Ordinal))return true;
   try {
    string plist=File.ReadAllText(Path.Combine(Path.GetDirectoryName(macOS),"Info.plist"));
    return System.Text.RegularExpressions.Regex.IsMatch(plist,@"<key>MicaEngineUpdates</key>\s*<true\s*/>");
   } catch(IOException) {return false;} catch(UnauthorizedAccessException) {return false;}
  }

  internal static void Log(string root,string line) {
   try {
    Directory.CreateDirectory(root);
    string file=Path.Combine(root,"engines.log");
    if(File.Exists(file)&&new FileInfo(file).Length>64*1024)File.Delete(file);
    File.AppendAllText(file,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" "+line+Environment.NewLine);
   } catch(IOException) {} catch(UnauthorizedAccessException) {}
  }
 }
}
