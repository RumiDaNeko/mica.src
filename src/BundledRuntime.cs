using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Mica {
 // Mica runs on its own copy of the WebView2 engine and never needs Edge or the
 // system WebView2. It ships one engine in Runtime\ and downloads newer, signed
 // builds into %LOCALAPPDATA%\Mica\Engines (see EngineUpdater); the newest wins.
 static class BundledRuntime {
  static string chosen;
  // Signature checks of downloaded engines, per folder, valid while msedgewebview2.exe keeps its size and write time.
  static readonly Dictionary<string,Tuple<long,DateTime,bool>> verified=new Dictionary<string,Tuple<long,DateTime,bool>>(StringComparer.OrdinalIgnoreCase);
  internal static bool Verified(string folder) {
   try {
    var exe=new FileInfo(Path.Combine(folder,"msedgewebview2.exe"));if(!exe.Exists)return false;long size=exe.Length;DateTime written=exe.LastWriteTimeUtc;Tuple<long,DateTime,bool> known;
    lock(verified)if(verified.TryGetValue(folder,out known)&&known.Item1==size&&known.Item2==written)return known.Item3;
    bool ok=EngineUpdater.IsMicrosoftSigned(exe.FullName,true);
    lock(verified)verified[folder]=Tuple.Create(size,written,ok);return ok;
   }catch(Exception){return false;}
  }
  internal static string ChosenVersion {get; private set;}
  // Installed by the lite installer: no engine of its own, Windows' WebView2 runtime (kept current by Windows) instead.
  internal static bool EvergreenMode {get {try {string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"engine-mode.txt");return File.Exists(file)&&File.ReadAllText(file).Trim()=="evergreen";}catch(Exception){return false;}}}
  internal static string BundledVersion(string folder) {try {return System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(folder,"msedgewebview2.exe")).FileVersion;}catch(Exception){return null;}}
  internal static string Folder() {
   if(chosen!=null)return chosen;
   string bundled=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Runtime");
   bool shipped=File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bundled-engine.json"));
   bool hasBundled=File.Exists(Path.Combine(bundled,"msedgewebview2.exe"));
   if(Directory.Exists(bundled)&&!hasBundled)throw new InvalidOperationException("Mica's Runtime folder is incomplete. Extract the full portable package again.");
   string folder=hasBundled?bundled:null;Version best=hasBundled?EngineUpdater.ParseVersion(BundledVersion(bundled)):null;
   // Newest first: the first downloaded engine newer than the bundled one that is still Microsoft-signed wins.
   // Its folder name must be its real version, or an old signed engine could pose as a newer one.
   foreach(var engine in EngineUpdater.Installed(EngineUpdater.Root))
    if((best==null||engine.Item1>best)&&engine.Item1==EngineUpdater.ParseVersion(BundledVersion(engine.Item2))&&Verified(engine.Item2)){best=engine.Item1;folder=engine.Item2;break;}
   if(folder==null&&EvergreenMode)return null;
   if(folder==null) {
    if(shipped)throw new InvalidOperationException("Mica's bundled engine is missing. Extract the complete Mica ZIP, including the Runtime folder, and open it from a local folder.");
    return null; // Development builds may use the installed Evergreen runtime.
   }
   Prepare(folder);
   ChosenVersion=best==null?null:best.ToString();chosen=folder;return folder;
  }
  // Fixed Version >=120 requires AppContainer read/execute rights on Windows 10.
  // Only public engine binaries receive these rights, never browser profile data.
  internal static void Prepare(string folder) {
   var security=Directory.GetAccessControl(folder);bool changed=false;
   foreach(string id in new[]{"S-1-15-2-1","S-1-15-2-2"}) {
    var sid=new SecurityIdentifier(id);bool granted=false;
    foreach(FileSystemAccessRule rule in security.GetAccessRules(true,true,typeof(SecurityIdentifier)))
     if(rule.IdentityReference.Equals(sid)&&rule.AccessControlType==AccessControlType.Allow&&(rule.FileSystemRights&FileSystemRights.ReadAndExecute)==FileSystemRights.ReadAndExecute&&(rule.InheritanceFlags&(InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit))==(InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit))granted=true;
    if(!granted){security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.ReadAndExecute,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));changed=true;}
   }
   if(changed)try {Directory.SetAccessControl(folder,security);}catch(UnauthorizedAccessException){throw new InvalidOperationException("Extract Mica into a folder you own so its bundled engine can be prepared, then reopen it.");}
  }
 }
}
