using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace Mica {
 internal sealed class InstallEngine {
  internal const string Version="0.12.33";
  internal readonly string Root,StartLink,DesktopLink,RegistryPrefix;
  internal const string Marker="mica-install.txt";
  // An app-only update leaves the engine out and names the engine it was built for; the installed one is reused.
  internal const string PayloadKind="payload-kind",EngineVersion="engine-version",VersionFile="version.txt";
  internal string Previous {get {return Root+".previous";}}
  [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)] static extern bool CreateHardLink(string link,string existing,IntPtr security);
  internal InstallEngine(string testRoot=null) {
   Root=testRoot==null?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Mica"):Path.Combine(testRoot,"Mica");
   StartLink=Path.Combine(testRoot??Environment.GetFolderPath(Environment.SpecialFolder.Programs),"Mica.lnk");
   DesktopLink=Path.Combine(testRoot??Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),testRoot==null?"Mica.lnk":"Desktop-Mica.lnk");
   RegistryPrefix=testRoot==null?"Software\\":"Software\\MicaInstallerTests\\"+Path.GetFileName(testRoot)+"\\";
   testProfileRoot=testRoot==null?null:Path.Combine(testRoot,"MicaBrowser");
  }
  internal static void NoLinks(string path) {
   for(var p=new DirectoryInfo(Path.GetFullPath(path));p!=null;p=p.Parent)if(p.Exists&&(p.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Install folder must not be a junction or symbolic link.");
  }
  internal static void DeleteTree(string path,string parent) {
   string full=Path.GetFullPath(path),prefix=Path.GetFullPath(parent).TrimEnd('\\')+"\\";
   if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||full==prefix.TrimEnd('\\'))throw new IOException("Unsafe cleanup path.");
   if(!Directory.Exists(full))return;NoLinks(full);
   // Walk one level at a time: never traverse reparse points during cleanup.
   foreach(var file in Directory.GetFiles(full)){if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Unexpected link in installation.");File.Delete(file);}
   foreach(var directory in Directory.GetDirectories(full))DeleteTree(directory,parent);
   Directory.Delete(full);
  }
  static string Hash(Stream stream) {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
  // MICAZIP1: a ZIP of the files. MICAZIP2: a ZIP of stored entries, each one LZMS-packed (see Lzms),
  // plus a "payload-size" entry with the unpacked total.
  internal const string PackedSize="payload-size";
  internal static long PayloadOffset(string setup,out long length) {bool packed;return PayloadOffset(setup,out length,out packed);}
  internal static long PayloadOffset(string setup,out long length,out bool packed) {
   using(var stream=File.OpenRead(setup))using(var reader=new BinaryReader(stream)) {
    if(stream.Length<48)throw new InvalidDataException("Installer payload missing.");stream.Position=stream.Length-48;length=reader.ReadInt64();byte[] expected=reader.ReadBytes(32);string magic=System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8));
    packed=magic=="MICAZIP2";
    if(magic!="MICAZIP1"&&!packed||length<=0||length>stream.Length-48)throw new InvalidDataException("Installer payload invalid.");
    long offset=stream.Length-48-length;
    using(var slice=new SliceStream(setup,offset,length))if(Hash(slice)!=BitConverter.ToString(expected).Replace("-",""))throw new InvalidDataException("Installer is damaged. Download it again.");
    return offset;
   }
  }
  // Mica's web engine (Runtime\msedgewebview2.exe) keeps running for a few seconds after Mica itself has
  // closed, and holds files in the install folder until then; the folder can't be swapped while it does.
  internal static void WaitForProcessesIn(string folder,int timeout) {
   string prefix=Path.GetFullPath(folder).TrimEnd('\\')+"\\";var watch=Stopwatch.StartNew();
   while(true) {
    var holding=ProcessesIn(prefix);if(holding.Count==0||watch.ElapsedMilliseconds>=timeout)return;
    System.Threading.Thread.Sleep(200);
   }
  }
  static List<string> ProcessesIn(string prefix) {
   var names=new List<string>();int self=Process.GetCurrentProcess().Id;
   foreach(var process in Process.GetProcesses()) {
    try {if(process.Id!=self&&process.MainModule.FileName.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))names.Add(Path.GetFileName(process.MainModule.FileName));}
    catch(Exception){}finally {process.Dispose();}
   }
   return names;
  }
  // Antivirus and search indexing briefly open new files too; a move that hits one of them is tried again.
  static void Retry(Action move,string folder) {
   for(int attempt=1;;attempt++) {
    try {move();return;}
    catch(Exception e) {
     if(!(e is IOException||e is UnauthorizedAccessException))throw;
     if(attempt>=30) {
      var holding=ProcessesIn(Path.GetFullPath(folder).TrimEnd('\\')+"\\").Distinct().ToList();
      throw new IOException(holding.Count>0?"Mica's files are still in use by "+string.Join(", ",holding)+". Close it and try again.":"Mica's files are still in use. Wait a moment and try again. ("+e.Message+")",e);
     }
     System.Threading.Thread.Sleep(500);
    }
   }
  }
  // A process can't rename a folder it is standing in.
  static void LeaveFolder(string folder,string parent) {
   string here=Path.GetFullPath(Environment.CurrentDirectory).TrimEnd('\\')+"\\";
   if(here.StartsWith(Path.GetFullPath(folder).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))Environment.CurrentDirectory=parent;
  }
  // Left over when a previous update couldn't remove the old copy.
  void ClearOldBackups(string parent) {
   foreach(var old in Directory.GetDirectories(parent,Path.GetFileName(Root)+".backup-*"))try {DeleteTree(old,parent);}catch(IOException){}catch(UnauthorizedAccessException){}
  }
  internal void Install(string setup,bool desktop,Action<double,string> progress) {
   NoLinks(Root);string parent=Path.GetDirectoryName(Root);Directory.CreateDirectory(parent);ClearOldBackups(parent);
   if(Directory.Exists(Root)&&(!File.Exists(Path.Combine(Root,Marker))||File.ReadAllText(Path.Combine(Root,Marker)).Trim()!=WindowsIntegration.AppId))throw new IOException("This folder already exists and is not a managed Mica installation.");
   string stage=Root+".stage-"+Guid.NewGuid().ToString("N"),backup=Root+".backup-"+Guid.NewGuid().ToString("N");bool movedOld=false,activated=false,appOnly=false,lite=false;string wantedEngine=null;
   try {
    progress(0,"Checking your installer…");long length;bool packed;long offset=PayloadOffset(setup,out length,out packed);
    using(var slice=new SliceStream(setup,offset,length))using(var zip=new ZipArchive(slice,ZipArchiveMode.Read)) {
     var sizeEntry=packed?zip.GetEntry(PackedSize):null;if(packed&&sizeEntry==null)throw new InvalidDataException("Installer payload invalid.");
     var kindEntry=zip.GetEntry(PayloadKind);var engineEntry=zip.GetEntry(EngineVersion);
     string kind=null;if(kindEntry!=null)using(var text=new StreamReader(kindEntry.Open()))kind=text.ReadToEnd().Trim();
     appOnly=kind=="app";lite=kind=="lite";if(appOnly)using(var text=new StreamReader(engineEntry.Open()))wantedEngine=text.ReadToEnd().Trim();
     if(lite)CheckLite();
     if(appOnly&&InstalledEngine(Root)!=wantedEngine)throw new InvalidDataException("This update needs the full Mica installer: the web engine changed.");
     long total,done=0,stored=Math.Max(1,zip.Entries.Sum(e=>e.Length));
     if(packed)using(var text=new StreamReader(sizeEntry.Open()))total=long.Parse(text.ReadToEnd().Trim());else total=stored;
     if(new DriveInfo(Path.GetPathRoot(parent)).AvailableFreeSpace<total+64L*1024*1024)throw new IOException("Not enough space. Free at least "+Math.Ceiling((total+64L*1024*1024)/1048576.0)+" MB and try again.");
     Directory.CreateDirectory(stage);var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
     foreach(var entry in zip.Entries) {
      if(entry==sizeEntry||entry.FullName==PayloadKind||entry.FullName==EngineVersion)continue;
      string name=entry.FullName.Replace('/',Path.DirectorySeparatorChar);
      string destination=Path.GetFullPath(Path.Combine(stage,name));
      if(name.Contains(":")||Path.IsPathRooted(name)||!destination.StartsWith(stage+"\\",StringComparison.OrdinalIgnoreCase)||!names.Add(destination))throw new InvalidDataException("Unsafe payload path.");
      if(name.EndsWith("\\")){Directory.CreateDirectory(destination);continue;}
      Directory.CreateDirectory(Path.GetDirectoryName(destination));using(var input=entry.Open())using(var output=new FileStream(destination,FileMode.CreateNew)){if(packed)Lzms.Unpack(input,output);else input.CopyTo(output);}
      done+=entry.Length;progress(0.05+0.85*done/stored,name.StartsWith("Runtime\\")?"Making room for the web…":"Adding the finishing touches…");
     }
    }
    if(appOnly){progress(.9,"Reusing the web engine you have…");LinkTree(Path.Combine(Root,"Runtime"),Path.Combine(stage,"Runtime"));}
    File.WriteAllText(Path.Combine(stage,VersionFile),Version);
    foreach(string required in lite?new[]{"Mica.exe","MicaUninstall.exe","engine-mode.txt"}:new[]{"Mica.exe","MicaUninstall.exe","Runtime\\msedgewebview2.exe","bundled-engine.json"})if(!File.Exists(Path.Combine(stage,required)))throw new InvalidDataException("Incomplete browser package.");
    File.WriteAllText(Path.Combine(stage,Marker),WindowsIntegration.AppId);
    if(Directory.Exists(Root)){progress(.92,"Waiting for Mica to finish closing…");WaitForProcessesIn(Root,20000);LeaveFolder(Root,parent);Retry(delegate {Directory.Move(Root,backup);},Root);movedOld=true;}
    Retry(delegate {Directory.Move(stage,Root);},stage);activated=true;progress(.94,"Adding Mica to Windows…");
    Register();WindowsIntegration.Shortcut(StartLink,Path.Combine(Root,"Mica.exe"),"");if(desktop)WindowsIntegration.Shortcut(DesktopLink,Path.Combine(Root,"Mica.exe"),"");
   }catch {
    if(activated)DeleteTree(Root,parent);
    if(movedOld){Retry(delegate {Directory.Move(backup,Root);},backup);Register();WindowsIntegration.Shortcut(StartLink,Path.Combine(Root,"Mica.exe"),"");}
    else if(activated)Unregister();
    throw;
   }finally {if(Directory.Exists(stage))DeleteTree(stage,parent);}
   // The old copy stays as Mica.previous, so the update can be undone; the one before it goes. If it can't be
   // kept, it's removed once nothing holds it (or cleared on the next install).
   if(movedOld) {
    bool kept=false;
    try {if(Directory.Exists(Previous))DeleteTree(Previous,parent);Retry(delegate {Directory.Move(backup,Previous);},backup);kept=true;}catch(IOException){}catch(UnauthorizedAccessException){}
    if(!kept)try {DeleteTree(backup,parent);}catch(IOException){}catch(UnauthorizedAccessException){}
   }
   progress(1,"Mica is ready.");
  }
  // The lite installer brings no engine and uses Windows' WebView2 runtime, so: that runtime must be here, an
  // install with its own engine isn't swapped onto it, and Mica data already opened by a newer engine is left
  // alone (an older engine can't open it).
  internal string ProfileRoot {get {return testProfileRoot??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MicaBrowser");}}
  internal string testProfileRoot;
  void CheckLite() {
   var windows=WindowsEngineVersion();
   if(windows==null)throw new InvalidDataException("Windows' web engine (WebView2 Runtime) isn't installed here. Use the full Mica installer.");
   if(File.Exists(Path.Combine(Root,"Runtime","msedgewebview2.exe")))throw new InvalidDataException("This Mica has its own web engine. Update it from Mica, or use the full installer.");
   foreach(var mark in new[]{Path.Combine(ProfileRoot,"WebView","EBWebView","Last Version"),Path.Combine(ProfileRoot,"WebView","Last Version")}) {
    System.Version used;try {if(File.Exists(mark)&&System.Version.TryParse(File.ReadAllText(mark).Trim(),out used)&&used>windows)throw new InvalidDataException("Your Mica data was last opened by a newer web engine ("+used+") than Windows has ("+windows+"). Use the full Mica installer.");}catch(IOException){}
   }
  }
  internal static System.Version WindowsEngineVersion() {
   foreach(var hive in new[]{Registry.LocalMachine,Registry.CurrentUser})
    foreach(var path in new[]{"SOFTWARE\\WOW6432Node\\Microsoft\\EdgeUpdate\\Clients\\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}","SOFTWARE\\Microsoft\\EdgeUpdate\\Clients\\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"})
     try {using(var key=hive.OpenSubKey(path)){System.Version version;if(key!=null&&System.Version.TryParse(key.GetValue("pv") as string??"",out version)&&version>new System.Version(0,0,0,0))return version;}}catch(Exception){}
   return null;
  }
  // The engine version an installed copy was built with (from its bundled-engine.json), or null.
  internal static string InstalledEngine(string folder) {
   try {
    string manifest=Path.Combine(folder,"bundled-engine.json");if(!File.Exists(manifest)||!File.Exists(Path.Combine(folder,"Runtime","msedgewebview2.exe")))return null;
    var match=System.Text.RegularExpressions.Regex.Match(File.ReadAllText(manifest),"\"version\"\\s*:\\s*\"([0-9.]+)\"");return match.Success?match.Groups[1].Value:null;
   }catch(Exception){return null;}
  }
  // The installed engine's files, linked into the new copy: no second copy on disk, and nothing to download.
  // Engine files are never changed in place, so the two copies can share them. Copies if links aren't possible.
  static void LinkTree(string from,string to) {
   Directory.CreateDirectory(to);
   foreach(var file in Directory.GetFiles(from)) {string target=Path.Combine(to,Path.GetFileName(file));if(!CreateHardLink(target,file,IntPtr.Zero))File.Copy(file,target);}
   foreach(var folder in Directory.GetDirectories(from)) {
    if((new DirectoryInfo(folder).Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("The installed engine folder contains a link; use the full installer.");
    LinkTree(folder,Path.Combine(to,Path.GetFileName(folder)));
   }
  }
  internal static string VersionOf(string folder) {try {string file=Path.Combine(folder,VersionFile);return File.Exists(file)?File.ReadAllText(file).Trim():null;}catch(Exception){return null;}}
  // Undo: the previous copy comes back and the current one becomes the previous, so it can be redone.
  internal void Rollback(Action<double,string> progress) {
   NoLinks(Root);NoLinks(Previous);string parent=Path.GetDirectoryName(Root);
   foreach(var folder in new[]{Root,Previous})if(!File.Exists(Path.Combine(folder,Marker))||File.ReadAllText(Path.Combine(folder,Marker)).Trim()!=WindowsIntegration.AppId||!File.Exists(Path.Combine(folder,"Mica.exe")))throw new IOException("There's no earlier version of Mica to go back to.");
   string aside=Root+".backup-"+Guid.NewGuid().ToString("N");
   progress(.2,"Waiting for Mica to finish closing…");WaitForProcessesIn(Root,20000);LeaveFolder(Root,parent);
   Retry(delegate {Directory.Move(Root,aside);},Root);
   try {progress(.6,"Bringing back the earlier version…");Retry(delegate {Directory.Move(Previous,Root);},Previous);}
   catch {Retry(delegate {Directory.Move(aside,Root);},aside);throw;}
   Retry(delegate {Directory.Move(aside,Previous);},aside);
   progress(.9,"Adding Mica to Windows…");Register();WindowsIntegration.Shortcut(StartLink,Path.Combine(Root,"Mica.exe"),"");if(File.Exists(DesktopLink))WindowsIntegration.Shortcut(DesktopLink,Path.Combine(Root,"Mica.exe"),"");
   progress(1,"Mica is back to the earlier version.");
  }
  void Set(string path,string name,object value) {using(var key=Registry.CurrentUser.CreateSubKey(RegistryPrefix+path))key.SetValue(name,value);}
  internal void Register() {
   string exe=Path.Combine(Root,"Mica.exe"),command="\""+exe+"\" --url \"%1\"";
   // Only a ProgID for http/https: no "URL Protocol", so mica.url: is not a launchable scheme (older versions set it).
   Set("Classes\\Mica.Url","","Mica web link");using(var key=Registry.CurrentUser.OpenSubKey(RegistryPrefix+"Classes\\Mica.Url",true))key.DeleteValue("URL Protocol",false);Set("Classes\\Mica.Url\\DefaultIcon","",exe+",0");Set("Classes\\Mica.Url\\shell\\open\\command","",command);
   string client="Clients\\StartMenuInternet\\Mica";
   Set(client,"","Mica");Set(client+"\\DefaultIcon","",exe+",0");Set(client+"\\shell\\open\\command","","\""+exe+"\"");
   Set(client+"\\Capabilities","ApplicationName","Mica");Set(client+"\\Capabilities","ApplicationDescription","A quieter space for the web.");Set(client+"\\Capabilities","ApplicationIcon",exe+",0");
   foreach(string protocol in new[]{"http","https"})Set(client+"\\Capabilities\\URLAssociations",protocol,"Mica.Url");
   // Files a browser shows well: Mica is offered under "Open with" for them, never made the default.
   Set("Classes\\Mica.File","","Mica file");Set("Classes\\Mica.File\\DefaultIcon","",exe+",0");Set("Classes\\Mica.File\\shell\\open\\command","",command);
   foreach(string type in FileTypes){Set(client+"\\Capabilities\\FileAssociations",type,"Mica.File");Set("Classes\\"+type+"\\OpenWithProgids","Mica.File",new byte[0]);}
   Set("RegisteredApplications","Mica",RegistryPrefix+client+"\\Capabilities");
   Set("Microsoft\\Windows\\CurrentVersion\\App Paths\\Mica.exe","",exe);Set("Microsoft\\Windows\\CurrentVersion\\App Paths\\Mica.exe","Path",Root);
   string uninstall="Microsoft\\Windows\\CurrentVersion\\Uninstall\\Mica";
   Set(uninstall,"DisplayName","Mica");Set(uninstall,"DisplayVersion",Version);Set(uninstall,"Publisher","Mica");Set(uninstall,"DisplayIcon",exe+",0");Set(uninstall,"InstallLocation",Root);Set(uninstall,"UninstallString","\""+Path.Combine(Root,"MicaUninstall.exe")+"\" --uninstall");Set(uninstall,"NoModify",1);Set(uninstall,"NoRepair",1);
  }
  static readonly string[] FileTypes={".pdf",".htm",".html",".svg",".webp",".avif",".png",".jpg",".jpeg",".gif",".mp4",".webm",".mp3",".ogg",".wav",".flac",".m4a"};
  internal void Unregister() {
   foreach(string type in FileTypes)using(var key=Registry.CurrentUser.OpenSubKey(RegistryPrefix+"Classes\\"+type+"\\OpenWithProgids",true))if(key!=null)key.DeleteValue("Mica.File",false);
   foreach(string path in new[]{"Classes\\Mica.Url","Classes\\Mica.File","Clients\\StartMenuInternet\\Mica","Microsoft\\Windows\\CurrentVersion\\App Paths\\Mica.exe","Microsoft\\Windows\\CurrentVersion\\Uninstall\\Mica"})Registry.CurrentUser.DeleteSubKeyTree(RegistryPrefix+path,false);
   using(var key=Registry.CurrentUser.OpenSubKey(RegistryPrefix+"RegisteredApplications",true))if(key!=null)key.DeleteValue("Mica",false);
   foreach(string path in new[]{StartLink,DesktopLink})if(File.Exists(path)&&WindowsIntegration.ShortcutIdentity(path)==WindowsIntegration.AppId)File.Delete(path);
  }
  internal void Uninstall() {
   NoLinks(Root);if(!File.Exists(Path.Combine(Root,Marker))||File.ReadAllText(Path.Combine(Root,Marker)).Trim()!=WindowsIntegration.AppId)throw new IOException("Managed installation was not found.");
   WaitForProcessesIn(Root,20000);DeleteTree(Root,Path.GetDirectoryName(Root));if(Directory.Exists(Previous))try {DeleteTree(Previous,Path.GetDirectoryName(Root));}catch(IOException){}catch(UnauthorizedAccessException){}Unregister();
  }
 }
 internal sealed class SliceStream:Stream {
  readonly FileStream file;readonly long start,size;long position;
  internal SliceStream(string path,long offset,long length){file=File.OpenRead(path);start=offset;size=length;file.Position=start;}
  public override bool CanRead{get{return true;}}public override bool CanSeek{get{return true;}}public override bool CanWrite{get{return false;}}public override long Length{get{return size;}}
  public override long Position{get{return position;}set{Seek(value,SeekOrigin.Begin);}}
  public override int Read(byte[] buffer,int offset,int count){int read=file.Read(buffer,offset,(int)Math.Min(count,size-position));position+=read;return read;}
  public override long Seek(long offset,SeekOrigin origin){long next=origin==SeekOrigin.Begin?offset:origin==SeekOrigin.Current?position+offset:size+offset;if(next<0||next>size)throw new IOException("Invalid payload offset.");file.Position=start+next;return position=next;}
  public override void Flush(){}public override void SetLength(long value){throw new NotSupportedException();}public override void Write(byte[] buffer,int offset,int count){throw new NotSupportedException();}protected override void Dispose(bool disposing){if(disposing)file.Dispose();base.Dispose(disposing);}
 }
}
