using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Mica {
 // Mica's own updates come from the latest GitHub release of ReleaseRepo: a tag like
 // v0.12.4 with a Mica-<version>-Setup.exe asset. The setup is downloaded in the
 // on request, checked against the SHA-256 digest GitHub lists for it, and run on
 // "Restart to update"; the installer closes Mica, upgrades it and opens it again.
 static class AppUpdater {
  // owner/name of the GitHub repository that publishes Mica releases.
  internal const string ReleaseRepo="RumiDaNeko/mica.";
  // The repository's numeric id. .NET drops the trailing dot of "mica." from URL paths,
  // so the feed and downloads go through api.github.com/repositories/<id> instead.
  internal const long ReleaseRepoId=1432309505;
  static string RepoApi {get {return "https://api.github.com/repositories/"+ReleaseRepoId;}}
  internal static string RootOverride;
  internal static string Root {get {return RootOverride??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Mica","Updates");}}
  // Tests point this at a local feed; otherwise it is the GitHub API for ReleaseRepo (never a file on disk,
  // which anything running as the user could rewrite to hand Mica another "release").
  internal static string FeedOverride;
  internal static string Feed {get {return FeedOverride??(ReleaseRepoId==0?null:RepoApi+"/releases/latest");}}
  internal class Release {internal Version Version;internal string Tag,Notes,SetupUrl,SetupName,Sha256;internal long Size;
   // The app-only update: the same installer without the web engine, for a copy that already has that engine.
   internal string PatchUrl,PatchSha256,PatchEngine;internal long PatchSize;
   // The lite installer, for a copy that runs on Windows' own engine: it must never be handed the full one.
   internal string LiteUrl,LiteSha256;internal long LiteSize;
   internal bool UsesLite {get {return LiteUrl!=null&&LiteSha256!=null&&(LiteOverride??BundledRuntime.EvergreenMode);}}
   internal bool UsesPatch {get {return PatchUrl!=null&&PatchSha256!=null&&PatchEngine!=null&&PatchEngine==LocalEngine;}}
   internal string PatchName,LiteName,SignaturesUrl;
   internal string DownloadName {get {return UsesLite?LiteName:UsesPatch?PatchName:SetupName;}}
   internal string DownloadUrl {get {return UsesLite?LiteUrl:UsesPatch?PatchUrl:SetupUrl;}}
   internal string DownloadSha256 {get {return UsesLite?LiteSha256:UsesPatch?PatchSha256:Sha256;}}
   internal long DownloadSize {get {return UsesLite?LiteSize:UsesPatch?PatchSize:Size;}}
  }
  // The engine this copy of Mica was installed with (its bundled-engine.json), or null when it has none.
  internal static string LocalEngineOverride;internal static bool? LiteOverride;
  internal static string LocalEngine {get {
   if(LocalEngineOverride!=null)return LocalEngineOverride;
   try {
    string folder=AppDomain.CurrentDomain.BaseDirectory,manifest=Path.Combine(folder,"bundled-engine.json");
    if(!File.Exists(manifest)||!File.Exists(Path.Combine(folder,"Runtime","msedgewebview2.exe")))return null;
    var match=System.Text.RegularExpressions.Regex.Match(File.ReadAllText(manifest),"\"version\"\\s*:\\s*\"([0-9.]+)\"");return match.Success?match.Groups[1].Value:null;
   }catch(Exception){return null;}
  }}

  internal static Version ParseTag(string tag) {
   if(tag==null)return null;tag=tag.Trim().TrimStart('v','V');int dash=tag.IndexOfAny(new[]{'-','+'});if(dash>=0)tag=tag.Substring(0,dash);
   Version version;return Version.TryParse(tag,out version)?version:null;
  }
  internal static Release FromJson(string json) {
   var data=new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string,object>;
   if(data==null)return null;object value;
   if(data.TryGetValue("draft",out value)&&value is bool&&(bool)value||data.TryGetValue("prerelease",out value)&&value is bool&&(bool)value)return null;
   var release=new Release {Tag=data.TryGetValue("tag_name",out value)?value as string:null,Notes=data.TryGetValue("body",out value)?value as string:null};
   release.Version=ParseTag(release.Tag);if(release.Version==null)return null;
   var assets=data.TryGetValue("assets",out value)?value as object[]:null;if(assets==null)return null;
   foreach(var asset in assets.OfType<Dictionary<string,object>>()) {
    string name=asset.TryGetValue("name",out value)?value as string:null;
    if(name!=null&&name.Equals("Mica-"+release.Version+"-signatures.txt",StringComparison.OrdinalIgnoreCase)&&release.SignaturesUrl==null) {
     string at=asset.TryGetValue("browser_download_url",out value)?value as string:null;
     if(FeedOverride==null&&ReleaseRepoId!=0&&asset.TryGetValue("id",out value)&&(value is int||value is long))at=RepoApi+"/releases/assets/"+value;
     release.SignaturesUrl=at;continue;
    }
    bool setup=name!=null&&name.EndsWith("-Setup.exe",StringComparison.OrdinalIgnoreCase),patch=name!=null&&name.EndsWith("-Update.exe",StringComparison.OrdinalIgnoreCase),lite=name!=null&&name.EndsWith("-Setup-Lite.exe",StringComparison.OrdinalIgnoreCase);
    if(name==null||!name.StartsWith("Mica-",StringComparison.OrdinalIgnoreCase)||!setup&&!patch&&!lite||setup&&release.SetupUrl!=null||patch&&release.PatchUrl!=null||lite&&release.LiteUrl!=null)continue;
    string url=asset.TryGetValue("browser_download_url",out value)?value as string:null;
    if(FeedOverride==null&&ReleaseRepoId!=0&&asset.TryGetValue("id",out value)&&(value is int||value is long))url=RepoApi+"/releases/assets/"+value;
    long size=asset.TryGetValue("size",out value)&&value is int?(int)value:asset.TryGetValue("size",out value)&&value is long?(long)value:0;
    string digest=asset.TryGetValue("digest",out value)?value as string:null;
    string sha=digest!=null&&digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase)?digest.Substring(7).ToLowerInvariant():null;
    if(setup){release.SetupName=name;release.SetupUrl=url;release.Size=size;release.Sha256=sha;continue;}
    if(lite){release.LiteName=name;release.LiteUrl=url;release.LiteSize=size;release.LiteSha256=sha;continue;}
    // Its label names the engine it was built for: "engine 156.0.4314.42".
    string label=asset.TryGetValue("label",out value)?value as string:null;
    var engine=label==null?null:System.Text.RegularExpressions.Regex.Match(label,"^engine ([0-9.]+)$");
    if(engine!=null&&engine.Success){release.PatchName=name;release.PatchUrl=url;release.PatchSize=size;release.PatchSha256=sha;release.PatchEngine=engine.Groups[1].Value;}
   }
   return release.SetupUrl==null?null:release;
  }
  static WebClient Client() {var client=new WebClient {Proxy=WebRequest.DefaultWebProxy};client.Headers[HttpRequestHeader.UserAgent]="Mica/"+BrowserApp.AppVersion;client.Headers[HttpRequestHeader.Accept]="application/vnd.github+json";return client;}
  internal static string Sha256(string path) {using(var sha=SHA256.Create())using(var file=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();}

  // A downloaded setup that is newer than this Mica, or null.
  internal static string ReadySetup(Version current) {
   try {
    if(!Directory.Exists(Root))return null;
    return Directory.GetFiles(Root,"Mica-*-Setup.exe").Select(f=>Tuple.Create(ParseTag(Path.GetFileName(f).Substring(5,Path.GetFileName(f).Length-15)),f))
     .Where(t=>t.Item1!=null&&t.Item1>current&&File.Exists(t.Item2+".ok")).OrderByDescending(t=>t.Item1).Select(t=>t.Item2).FirstOrDefault();
   }catch(Exception){return null;}
  }
  // Opens a ready setup so nothing can change it, after checking it is still the file that was downloaded
  // and verified. Keep the handle open until the setup has started; null if it changed.
  // The signature is checked again too, so a file planted beside it (with a made-up .ok) can't pass.
  internal static FileStream LockVerified(string setup) {
   string[] ok;try {ok=File.ReadAllLines(setup+".ok");}catch(Exception){return null;}
   if(ok.Length<3||!SignedBy(ok[1],ok[0],ok[2],SetupVersion(setup)))return null;
   var file=new FileStream(setup,FileMode.Open,FileAccess.Read,FileShare.Read);
   try {using(var sha=SHA256.Create()){string actual=BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();if(actual==ok[0])return file;}}catch(Exception){}
   file.Dispose();return null;
  }
  // Tests sign with a key of their own.
  internal static string ReleaseKeyOverride;
  // A release signs "<asset name>\n<sha-256>" for each installer; the name must be one of this version's installers.
  internal static bool SignedBy(string name,string sha256,string signature,Version version) {
   if(name==null||sha256==null||signature==null||version==null||!name.StartsWith("Mica-"+version+"-",StringComparison.OrdinalIgnoreCase)||!name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))return false;
   try {
    using(var rsa=new RSACryptoServiceProvider()){rsa.PersistKeyInCsp=false;rsa.FromXmlString(ReleaseKeyOverride??ReleaseKey.Public);
     return rsa.VerifyData(System.Text.Encoding.UTF8.GetBytes(name+"\n"+sha256.ToLowerInvariant()),"SHA256",Convert.FromBase64String(signature));}
   }catch(Exception){return false;}
  }
  // The line for this release's chosen installer in its signatures file ("<name> <base64>"), or null.
  static async Task<string> FetchSignature(Release release) {
   if(release.SignaturesUrl==null||release.DownloadName==null)return null;
   if(!release.SignaturesUrl.StartsWith("https://",StringComparison.OrdinalIgnoreCase)&&FeedOverride==null)return null;
   string text;
   using(var client=Client()){client.Headers[HttpRequestHeader.Accept]="application/octet-stream";text=await client.DownloadStringTaskAsync(release.SignaturesUrl);}
   if(text==null||text.Length>65536)return null;
   foreach(var line in text.Split('\n')){var parts=line.Trim().Split(' ');if(parts.Length==2&&parts[0].Equals(release.DownloadName,StringComparison.OrdinalIgnoreCase))return parts[1];}
   return null;
  }
  internal static Version SetupVersion(string setup) {return setup==null?null:ParseTag(Path.GetFileName(setup).Substring(5,Path.GetFileName(setup).Length-15));}
  internal static string Notes(string setup) {try {return setup!=null&&File.Exists(setup+".notes")?File.ReadAllText(setup+".notes"):null;}catch(Exception){return null;}}

  // A newer release that was found but not downloaded yet: Mica asks before fetching it.
  static string AvailablePath {get {return Path.Combine(Root,"available.json");}}
  internal static Release Available(Version current) {
   try {if(!File.Exists(AvailablePath))return null;var found=FromJson(File.ReadAllText(AvailablePath));return found!=null&&found.Version>current&&!File.Exists(SetupPath(found)+".ok")?found:null;}
   catch(Exception){return null;}
  }
  static string SetupPath(Release release) {return Path.Combine(Root,"Mica-"+release.Version+"-Setup.exe");}

  // Returns a message for the user, or null when there was nothing to do. A newer release is only
  // noted as available; Download fetches it once the user agrees (download:true does both at once).
  internal static async Task<string> Update(Version current,bool force,bool download=false) {
   string feed=Feed;if(feed==null)return force?"Mica's release page isn't set up yet.":null;
   Directory.CreateDirectory(Root);
   string stamp=Path.Combine(Root,"last-check");
   if(!force&&File.Exists(stamp)&&File.GetLastWriteTimeUtc(stamp)>DateTime.UtcNow.AddHours(-20))return null;
   File.WriteAllText(stamp,DateTime.UtcNow.ToString("o"));
   Release latest;string json;
   try {using(var client=Client())json=await client.DownloadStringTaskAsync(feed);latest=FromJson(json);}
   catch(WebException e){var response=e.Response as HttpWebResponse;if(response!=null&&response.StatusCode==HttpStatusCode.NotFound)return force?"No Mica release has been published yet.":null;throw;}
   if(latest==null)return force?"The latest release has no Mica setup to download.":null;
   if(latest.Version<=current){try {File.Delete(AvailablePath);}catch(Exception){}return force?"Mica is up to date ("+current+").":null;}
   if(File.Exists(SetupPath(latest)+".ok"))return force?"Mica "+latest.Version+" is ready. Restart to update.":null;
   if(!latest.SetupUrl.StartsWith("https://",StringComparison.OrdinalIgnoreCase)&&FeedOverride==null)return force?"The release download isn't secure (not HTTPS).":null;
   // One that can't be installed also replaces any earlier note of it.
   if(latest.Sha256==null||latest.SignaturesUrl==null)try {File.Delete(AvailablePath);}catch(Exception){}
   if(latest.Sha256==null)return force?"The release doesn't list a checksum for its setup, so Mica won't install it.":null;
   if(latest.SignaturesUrl==null)return force?"The release isn't signed by Mica, so Mica won't install it.":null;
   File.WriteAllText(AvailablePath,json);
   if(download)return await Download(current,null);
   return "Mica "+latest.Version+" is available. Open Updates to download it.";
  }
  // Fetches the available release, reporting (received, total) bytes as it goes.
  internal static async Task<string> Download(Version current,Action<long,long> progress) {
   var latest=Available(current);if(latest==null)return null;string setup=SetupPath(latest);
   if(!latest.DownloadUrl.StartsWith("https://",StringComparison.OrdinalIgnoreCase)&&FeedOverride==null)throw new InvalidOperationException("The release download isn't secure (not HTTPS).");
   // Signed first: the checksum GitHub reports only says the file arrived intact, the signature says Mica's publisher made it.
   string signature=await FetchSignature(latest);
   if(signature==null||!SignedBy(latest.DownloadName,latest.DownloadSha256,signature,latest.Version))throw new InvalidOperationException("The release's signature doesn't check out, so Mica won't install it.");
   using(var client=Client()) {
    client.Headers[HttpRequestHeader.Accept]="application/octet-stream";
    if(progress!=null)client.DownloadProgressChanged+=(s,e)=>progress(e.BytesReceived,e.TotalBytesToReceive>0?e.TotalBytesToReceive:latest.DownloadSize);
    await client.DownloadFileTaskAsync(new Uri(latest.DownloadUrl),setup+".part");
   }
   string actual=Sha256(setup+".part");
   if(actual!=latest.DownloadSha256){File.Delete(setup+".part");throw new InvalidOperationException("The downloaded setup doesn't match the release checksum.");}
   if(File.Exists(setup))File.Delete(setup);File.Move(setup+".part",setup);
   File.WriteAllText(setup+".notes",latest.Notes??"");File.WriteAllLines(setup+".ok",new[]{actual,latest.DownloadName,signature});
   try {File.Delete(AvailablePath);}catch(Exception){}
   Prune(latest.Version);
   return "Mica "+latest.Version+" is ready. Restart to update.";
  }
  // Removes older setups (and this version's too once it is installed) and half-downloads.
  internal static void Prune(Version keep,bool installed=false) {
   try {
    foreach(var file in Directory.GetFiles(Root,"Mica-*")) {
     string name=Path.GetFileName(file);int setupEnd=name.IndexOf("-Setup.exe",StringComparison.OrdinalIgnoreCase);
     var version=setupEnd>5?ParseTag(name.Substring(5,setupEnd-5)):null;
     if(name.EndsWith(".part")||version==null||version<keep||installed&&version==keep)try {File.Delete(file);}catch(Exception){}
    }
    // A found release that is now installed (or older) has nothing left to offer.
    if(installed&&File.Exists(AvailablePath)&&Available(keep)==null)try {File.Delete(AvailablePath);}catch(Exception){}
   }catch(Exception){}
  }
 }
}
