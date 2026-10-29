using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Xilium.CefGlue;

namespace Mica {
 // Keeps the shell's Chromium patched between app updates, as EngineUpdater does for the Windows build: once a day it
 // reads the index of Spotify's CEF build CDN, and when a newer stable build of the same CEF major exists for this
 // platform (CefGlue's bindings are tied to the branch's API, so new majors ship with app updates) it downloads the
 // minimal distribution over HTTPS, checks the SHA-1 the index lists, unpacks it into the Engines folder and uses it
 // from the next launch (CefEngine). Nothing inside the app changes.
 static class CefEngineUpdater {
  internal const string Cdn="https://cef-builds.spotifycdn.com/";
  static Timer timer;

  internal sealed class Build {internal string Version,File,Sha1;internal Version Parsed;internal long Size;}

  internal static string Platform {get {
   bool arm=RuntimeInformation.ProcessArchitecture==Architecture.Arm64;
   if(OperatingSystem.IsMacOS())return arm?"macosarm64":"macosx64";
   if(OperatingSystem.IsLinux())return arm?"linuxarm64":"linux64";
   return arm?"windowsarm64":"windows64";
  }}

  // Newest stable build of a CEF major for a platform in the CDN's index.json, or null.
  internal static Build LatestFromIndex(Stream json,string platform,int major) {
   Build best=null;
   using(var document=JsonDocument.Parse(json)) {
    JsonElement entry,versions;
    if(!document.RootElement.TryGetProperty(platform,out entry)||!entry.TryGetProperty("versions",out versions)||versions.ValueKind!=JsonValueKind.Array)return null;
    foreach(var item in versions.EnumerateArray()) {
     JsonElement channel,cefVersion,files;
     if(!item.TryGetProperty("channel",out channel)||channel.GetString()!="stable"||!item.TryGetProperty("cef_version",out cefVersion)||!item.TryGetProperty("files",out files))continue;
     var parsed=CefEngine.ParseVersion(cefVersion.GetString());
     if(parsed==null||parsed.Major!=major||best!=null&&parsed<=best.Parsed)continue;
     foreach(var file in files.EnumerateArray()) {
      JsonElement type,name,sha1,size;
      if(!file.TryGetProperty("type",out type)||type.GetString()!="minimal"||!file.TryGetProperty("name",out name)||!file.TryGetProperty("sha1",out sha1))continue;
      string fileName=name.GetString(),hash=sha1.GetString();
      // Only a plain file name for exactly this version and platform, and a real SHA-1.
      if(fileName!="cef_binary_"+cefVersion.GetString()+"_"+platform+"_minimal.tar.bz2"||!Regex.IsMatch(fileName,@"^[A-Za-z0-9._+-]+$")||hash==null||!Regex.IsMatch(hash,"^[0-9a-fA-F]{40}$"))continue;
      best=new Build {Version=cefVersion.GetString(),Parsed=parsed,File=fileName,Sha1=hash.ToLowerInvariant(),Size=file.TryGetProperty("size",out size)&&size.ValueKind==JsonValueKind.Number?size.GetInt64():0};
     }
    }
   }
   return best;
  }

  internal static string Sha1Of(string path) {
   using(var stream=File.OpenRead(path))using(var sha1=SHA1.Create())return Convert.ToHexString(sha1.ComputeHash(stream)).ToLowerInvariant();
  }
  static string Tar() {return OperatingSystem.IsWindows()?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"tar.exe"):"tar";}
  internal static void RunTar(string arguments) {
   var start=new ProcessStartInfo(Tar()) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
   foreach(var argument in arguments.Split('\n'))start.ArgumentList.Add(argument);
   using(var process=Process.Start(start)) {
    // Unpacking takes a while; it shouldn't compete with browsing.
    try {process.PriorityClass=ProcessPriorityClass.BelowNormal;} catch(Exception) {}
    var errors=process.StandardError.ReadToEndAsync();process.StandardOutput.ReadToEnd();process.WaitForExit();
    if(process.ExitCode!=0)throw new InvalidOperationException("tar failed ("+process.ExitCode+"): "+errors.Result.Trim());
   }
  }

  // Lays out an unpacked distribution like fetch-cef.ps1 / fetch-cef.sh do for the bundled engine.
  internal static void Normalize(string distribution,string target) {
   Directory.CreateDirectory(target);
   string release=Path.Combine(distribution,"Release"),resources=Path.Combine(distribution,"Resources");
   if(OperatingSystem.IsMacOS()) {
    string framework=Path.Combine(release,"Chromium Embedded Framework.framework");
    Directory.CreateDirectory(Path.Combine(target,"Resources"));
    File.Move(Path.Combine(framework,"Chromium Embedded Framework"),Path.Combine(target,CefRuntime.LibCefFileName));
    foreach(var file in Directory.GetFiles(Path.Combine(framework,"Libraries")))File.Move(file,Path.Combine(target,Path.GetFileName(file)));
    foreach(var file in Directory.GetFiles(Path.Combine(framework,"Resources")))File.Move(file,Path.Combine(target,"Resources",Path.GetFileName(file)));
    foreach(var file in Directory.GetFiles(Path.Combine(framework,"Resources","en.lproj")))File.Move(file,Path.Combine(target,"Resources",Path.GetFileName(file)),true);
    return;
   }
   foreach(var source in new[]{release,resources}) {
    foreach(var file in Directory.GetFiles(source)) {
     string name=Path.GetFileName(file);
     if(name.EndsWith(".lib",StringComparison.OrdinalIgnoreCase)||name.StartsWith("bootstrap",StringComparison.OrdinalIgnoreCase)&&name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))continue;
     File.Move(file,Path.Combine(target,name),true);
    }
    foreach(var folder in Directory.GetDirectories(source))Directory.Move(folder,Path.Combine(target,Path.GetFileName(folder)));
   }
   if(OperatingSystem.IsLinux())Strip(target);
  }
  // The Linux distribution keeps its symbols (libcef.so is about 1 GB); strip them when binutils is there.
  static void Strip(string folder) {
   foreach(var name in new[]{"libcef.so","libEGL.so","libGLESv2.so","libvk_swiftshader.so","libvulkan.so.1"}) {
    string file=Path.Combine(folder,name);if(!File.Exists(file))continue;
    try {using(var process=Process.Start(new ProcessStartInfo("strip") {UseShellExecute=false,CreateNoWindow=true,ArgumentList={"-s",file}}))process.WaitForExit();}
    catch(Exception) {return;}
   }
  }

  // Checks a downloaded archive and unpacks it into root/<version>. Returns the engine folder.
  internal static string Install(string archive,Build build,string root) {
   string actual=Sha1Of(archive);
   if(actual!=build.Sha1)throw new InvalidOperationException("The engine download has SHA-1 "+actual+", the CEF index lists "+build.Sha1+".");
   string target=Path.Combine(root,build.Version),staging=target+".unpack";
   if(Directory.Exists(staging))Directory.Delete(staging,true);
   Directory.CreateDirectory(staging);
   try {
    RunTar("-xjf\n"+archive+"\n-C\n"+staging);
    string distribution=Directory.GetDirectories(staging).FirstOrDefault(d=>Path.GetFileName(d).StartsWith("cef_binary_",StringComparison.Ordinal));
    if(distribution==null)throw new InvalidOperationException("The engine download has no cef_binary folder.");
    if(Directory.Exists(target))Directory.Delete(target,true);
    Normalize(distribution,target+".layout");
    Directory.Move(target+".layout",target);
   } finally {try {Directory.Delete(staging,true);} catch(IOException) {}}
   if(!File.Exists(Path.Combine(target,CefRuntime.LibCefFileName)))throw new InvalidOperationException("The engine download has no "+CefRuntime.LibCefFileName+".");
   File.WriteAllText(Path.Combine(target,"cef.version"),build.Version);
   File.WriteAllText(Path.Combine(target,".ready"),build.Sha1);
   return target;
  }

  // Removes leftovers, engines that were set aside, of another CEF major, or not newer than the one in use (never
  // the folder in use itself).
  internal static void Prune(string root,string inUse,string currentVersion,int major) {
   var current=CefEngine.ParseVersion(currentVersion);
   try {
    foreach(var folder in Directory.GetDirectories(root)) {
     if(inUse!=null&&string.Equals(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar),Path.GetFullPath(inUse).TrimEnd(Path.DirectorySeparatorChar),StringComparison.Ordinal))continue;
     string name=Path.GetFileName(folder);var version=CefEngine.ParseVersion(name);
     bool leftover=name.EndsWith(".unpack")||name.EndsWith(".layout");
     // A set-aside engine goes, but its version is remembered so it isn't downloaded again.
     if(!leftover&&version!=null&&CefEngine.IsFailed(folder))try {File.AppendAllText(Path.Combine(root,"failed.txt"),name+"\n");} catch(IOException) {}
     if(leftover||version!=null&&(CefEngine.IsFailed(folder)||version.Major!=major||current!=null&&version<=current))try {Directory.Delete(folder,true);} catch(IOException) {} catch(UnauthorizedAccessException) {}
    }
    foreach(var part in Directory.GetFiles(root,"*.tar.bz2*"))try {File.Delete(part);} catch(IOException) {}
   } catch(IOException) {} catch(UnauthorizedAccessException) {}
  }

  static HttpClient Client() {
   var client=new HttpClient(new HttpClientHandler {AutomaticDecompression=DecompressionMethods.All}) {Timeout=TimeSpan.FromMinutes(30)};
   client.DefaultRequestHeaders.UserAgent.ParseAdd("Mica-engine-updater");
   return client;
  }

  // Returns a line for the log, or null when there was nothing to do.
  internal static async Task<string> Update(string root,string currentVersion,string inUse,bool force) {
   Directory.CreateDirectory(root);
   // One updater per user at a time.
   FileStream gate;
   try {gate=new FileStream(Path.Combine(root,".lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);} catch(IOException) {return null;}
   using(gate) {
    string stamp=Path.Combine(root,"last-check"),etagFile=Path.Combine(root,"index.etag");
    if(!force&&File.Exists(stamp)&&File.GetLastWriteTimeUtc(stamp)>DateTime.UtcNow.AddHours(-20))return null;
    File.WriteAllText(stamp,DateTime.UtcNow.ToString("o"));
    Prune(root,inUse,currentVersion,CefRuntime.BindingsCefVersionMajor);
    var current=CefEngine.ParseVersion(currentVersion);
    var newestInstalled=CefEngine.Installed(root).Where(e=>!CefEngine.IsFailed(e.Item2)).Select(e=>e.Item1).DefaultIfEmpty(current).Max();
    Build latest;string newEtag,archive;
    using(var client=Client()) {
     // index.json is 10 MB (1 MB compressed) and changes a few times a week; ask only for a changed one.
     var request=new HttpRequestMessage(HttpMethod.Get,Cdn+"index.json");
     string etag=File.Exists(etagFile)?File.ReadAllText(etagFile).Trim():null;
     if(!force&&!string.IsNullOrEmpty(etag))request.Headers.TryAddWithoutValidation("If-None-Match",etag);
     using(var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead)) {
      if(response.StatusCode==HttpStatusCode.NotModified)return null;
      response.EnsureSuccessStatusCode();
      using(var body=await response.Content.ReadAsStreamAsync())latest=LatestFromIndex(body,Platform,CefRuntime.BindingsCefVersionMajor);
      newEtag=response.Headers.ETag?.ToString();
     }
     // The ETag is kept only once this index has been dealt with, so a failed download is retried the next day.
     if(latest==null||newestInstalled!=null&&latest.Parsed<=newestInstalled) {
      if(newEtag!=null)File.WriteAllText(etagFile,newEtag);
      return force?"Engine "+currentVersion+" is up to date.":null;
     }
     string failed=Path.Combine(root,"failed.txt");
     if(File.Exists(failed)&&File.ReadAllLines(failed).Contains(latest.Version))return null;
     // The archive (135-430 MB) unpacks to 0.4 GB (1.3 GB on Linux); leave room for everything else on the disk.
     long needed=(OperatingSystem.IsLinux()?4L:2L)*1024*1024*1024;
     if(new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))).AvailableFreeSpace<needed)return "Not enough disk space to download engine "+latest.Version+".";
     archive=Path.Combine(root,latest.File);
     using(var response=await client.GetAsync(Cdn+Uri.EscapeDataString(latest.File),HttpCompletionOption.ResponseHeadersRead)) {
      response.EnsureSuccessStatusCode();
      using(var body=await response.Content.ReadAsStreamAsync())using(var file=File.Create(archive))await body.CopyToAsync(file);
     }
    }
    try {await Task.Run(()=>Install(archive,latest,root));} finally {try {File.Delete(archive);} catch(IOException) {}}
    if(newEtag!=null)File.WriteAllText(etagFile,newEtag);
    return "Engine "+latest.Version+" downloaded; it starts next time.";
   }
  }

  // Checks a few minutes after start, then every few hours while the app runs (Update itself keeps it to once a day).
  internal static void Schedule(string root,string currentVersion,string inUse) {
   timer=new Timer(async delegate {
    try {var message=await Update(root,currentVersion,inUse,false);if(message!=null)CefEngine.Log(root,message);}
    catch(Exception e) {CefEngine.Log(root,"update failed: "+e.Message);}
   },null,TimeSpan.FromMinutes(3),TimeSpan.FromHours(6));
  }
 }
}
