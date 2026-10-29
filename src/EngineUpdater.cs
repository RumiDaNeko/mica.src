using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Mica {
 // Keeps Mica's own engine patched without Edge: once a day it reads Microsoft's
 // WebView2 download page, and when a newer x64 Fixed Version runtime exists it
 // downloads the CAB from Microsoft's CDN, checks Microsoft's Authenticode
 // signature, unpacks it next to the others and uses it from the next launch.
 static class EngineUpdater {
  internal const string FeedPage="https://developer.microsoft.com/en-us/microsoft-edge/webview2/";
  internal static string Root {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Mica","Engines");}}
  internal static Version ParseVersion(string value) {Version version;return value!=null&&Version.TryParse(value.Trim(),out version)?version:null;}
  // Engines that finished installing: (version, folder).
  internal static List<Tuple<Version,string>> Installed(string root) {
   var found=new List<Tuple<Version,string>>();
   try {
    if(!Directory.Exists(root))return found;
    foreach(var folder in Directory.GetDirectories(root)) {
     var version=ParseVersion(Path.GetFileName(folder));
     if(version!=null&&File.Exists(Path.Combine(folder,".ready"))&&File.Exists(Path.Combine(folder,"msedgewebview2.exe")))found.Add(Tuple.Create(version,folder));
    }
   }catch(Exception){}
   return found.OrderByDescending(f=>f.Item1).ToList();
  }
  // Newest x64 Fixed Version runtime on the download page: (version, CAB address).
  internal static Tuple<Version,string> LatestFromPage(string html) {
   html=html.Replace("\\u002F","/").Replace("\\/","/");
   Tuple<Version,string> best=null;
   foreach(Match match in Regex.Matches(html,@"https://msedge\.sf\.dl\.delivery\.mp\.microsoft\.com/filestreamingservice/files/[0-9a-fA-F-]{36}/Microsoft\.WebView2\.FixedVersionRuntime\.(\d+\.\d+\.\d+\.\d+)\.x64\.cab")) {
    var version=ParseVersion(match.Groups[1].Value);if(version!=null&&(best==null||version>best.Item1))best=Tuple.Create(version,match.Value);
   }
   return best;
  }
  static WebClient Client() {var client=new WebClient {Proxy=WebRequest.DefaultWebProxy};client.Headers[HttpRequestHeader.UserAgent]="Mica engine updater";return client;}
  internal static async Task<Tuple<Version,string>> Latest() {using(var client=Client())return LatestFromPage(await client.DownloadStringTaskAsync(FeedPage));}

  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct TrustFileInfo {public int cbStruct;public string pcwszFilePath;public IntPtr hFile;public IntPtr pgKnownSubject;}
  [StructLayout(LayoutKind.Sequential)] struct TrustData {public int cbStruct;public IntPtr pPolicyCallbackData,pSIPClientData;public int dwUIChoice,fdwRevocationChecks,dwUnionChoice;public IntPtr pFile;public int dwStateAction;public IntPtr hWVTStateData,pwszURLReference;public int dwProvFlags,dwUIContext;public IntPtr pSignatureSettings;}
  [StructLayout(LayoutKind.Sequential)] struct ProviderSigner {public int cbStruct,verifyAsOfLow,verifyAsOfHigh,csCertChain;public IntPtr pasCertChain;public int dwSignerType;public IntPtr psSigner;public int dwError,csCounterSigners;public IntPtr pasCounterSigners,pChainContext;}
  [StructLayout(LayoutKind.Sequential)] struct ChainPolicyPara {public int cbSize,dwFlags;public IntPtr pvExtraPolicyPara;}
  [StructLayout(LayoutKind.Sequential)] struct ChainPolicyStatus {public int cbSize,dwError,lChainIndex,lElementIndex;public IntPtr pvExtraPolicyStatus;}
  [DllImport("wintrust.dll",CharSet=CharSet.Unicode)] static extern int WinVerifyTrust(IntPtr window,ref Guid action,ref TrustData data);
  [DllImport("wintrust.dll")] static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
  [DllImport("wintrust.dll")] static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider,int signer,bool counterSigner,int counterSignerIndex);
  [DllImport("crypt32.dll")] static extern bool CertVerifyCertificateChainPolicy(IntPtr policy,IntPtr chain,ref ChainPolicyPara para,ref ChainPolicyStatus status);
  // Valid, unrevoked Authenticode signature by Microsoft Corporation, chaining to a Microsoft root.
  // Downloads need a completed revocation check; offline, an engine already on disk is re-checked
  // against cached revocation data only and accepted when the revocation server can't be reached.
  internal static bool IsMicrosoftSigned(string path) {return IsMicrosoftSigned(path,false);}
  internal static bool IsMicrosoftSigned(string path,bool offline) {
   var file=new TrustFileInfo {cbStruct=Marshal.SizeOf(typeof(TrustFileInfo)),pcwszFilePath=path};
   IntPtr filePointer=Marshal.AllocHGlobal(file.cbStruct);
   var action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
   // WTD_REVOKE_WHOLECHAIN, WTD_STATEACTION_VERIFY; flags: chain revocation without the root, no MD2/MD4, cache-only URLs offline.
   var data=new TrustData {cbStruct=Marshal.SizeOf(typeof(TrustData)),dwUIChoice=2,fdwRevocationChecks=1,dwUnionChoice=1,pFile=filePointer,dwStateAction=1,dwProvFlags=0x80|0x2000|(offline?0x1000:0)};
   try {
    Marshal.StructureToPtr(file,filePointer,false);
    int result=WinVerifyTrust(IntPtr.Zero,ref action,ref data);
    // CRYPT_E_NO_REVOCATION_CHECK, CRYPT_E_REVOCATION_OFFLINE, CERT_E_REVOCATION_FAILURE: status unknown, never "revoked".
    if(result!=0&&!(offline&&(result==unchecked((int)0x80092012)||result==unchecked((int)0x80092013)||result==unchecked((int)0x800B010E))))return false;
    IntPtr provider=WTHelperProvDataFromStateData(data.hWVTStateData),signer=provider==IntPtr.Zero?IntPtr.Zero:WTHelperGetProvSignerFromChain(provider,0,false,0);
    if(signer==IntPtr.Zero)return false;
    IntPtr chain=((ProviderSigner)Marshal.PtrToStructure(signer,typeof(ProviderSigner))).pChainContext;if(chain==IntPtr.Zero)return false;
    // CERT_CHAIN_POLICY_MICROSOFT_ROOT, including the 2011 application root that signs Edge and WebView2.
    var para=new ChainPolicyPara {cbSize=Marshal.SizeOf(typeof(ChainPolicyPara)),dwFlags=0x20000};var status=new ChainPolicyStatus {cbSize=Marshal.SizeOf(typeof(ChainPolicyStatus))};
    if(!CertVerifyCertificateChainPolicy((IntPtr)7,chain,ref para,ref status)||status.dwError!=0)return false;
    using(var certificate=new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
     return certificate.GetNameInfo(X509NameType.SimpleName,false)=="Microsoft Corporation"&&certificate.Subject.Contains("O=Microsoft Corporation");
   }catch(Exception){return false;}
   finally {
    if(data.hWVTStateData!=IntPtr.Zero)try {data.dwStateAction=2;WinVerifyTrust(IntPtr.Zero,ref action,ref data);}catch(Exception){}
    Marshal.DestroyStructure(filePointer,typeof(TrustFileInfo));Marshal.FreeHGlobal(filePointer);
   }
  }
  // Unpacks a verified CAB into root\<version>. Returns the engine folder.
  internal static string InstallCab(string cab,Version version,string root) {
   if(!IsMicrosoftSigned(cab))throw new InvalidOperationException("The engine download isn't signed by Microsoft.");
   string target=Path.Combine(root,version.ToString()),staging=target+".unpack";
   if(Directory.Exists(staging))Directory.Delete(staging,true);Directory.CreateDirectory(staging);
   var expand=new ProcessStartInfo {FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"expand.exe"),Arguments="\""+cab+"\" -F:* \""+staging+"\"",UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true};
   using(var process=Process.Start(expand)){process.StandardOutput.ReadToEnd();process.WaitForExit();if(process.ExitCode!=0)throw new InvalidOperationException("Couldn't unpack the engine (expand "+process.ExitCode+").");}
   string engine=Directory.GetFiles(staging,"msedgewebview2.exe",SearchOption.AllDirectories).Select(Path.GetDirectoryName).FirstOrDefault();
   if(engine==null)throw new InvalidOperationException("The engine package has no msedgewebview2.exe.");
   if(!IsMicrosoftSigned(Path.Combine(engine,"msedgewebview2.exe")))throw new InvalidOperationException("The unpacked engine isn't signed by Microsoft.");
   var inside=ParseVersion(FileVersionInfo.GetVersionInfo(Path.Combine(engine,"msedgewebview2.exe")).FileVersion);
   if(inside!=version)throw new InvalidOperationException("The engine package is version "+inside+", expected "+version+".");
   if(Directory.Exists(target))Directory.Delete(target,true);
   Directory.Move(engine,target);
   try {Directory.Delete(staging,true);}catch(Exception){}
   BundledRuntime.Prepare(target);
   File.WriteAllText(Path.Combine(target,".ready"),DateTime.UtcNow.ToString("o"));
   return target;
  }
  // Removes engines older than the one in use (never the one running now).
  internal static void Prune(string root,Version inUse) {
   if(inUse==null)return;
   try {
    foreach(var folder in Directory.GetDirectories(root)) {
     string name=Path.GetFileName(folder);var version=ParseVersion(name.Replace(".unpack",""));
     if(name.EndsWith(".unpack")||version!=null&&version<inUse)try {Directory.Delete(folder,true);}catch(Exception){}
    }
    foreach(var part in Directory.GetFiles(root,"*.cab*"))try {File.Delete(part);}catch(Exception){}
   }catch(Exception){}
  }
  // Returns a message for the user, or null when there was nothing to do.
  internal static async Task<string> Update(string root,Version current,bool force) {
   Directory.CreateDirectory(root);
   string stamp=Path.Combine(root,"last-check");
   if(!force&&File.Exists(stamp)&&File.GetLastWriteTimeUtc(stamp)>DateTime.UtcNow.AddHours(-20))return null;
   File.WriteAllText(stamp,DateTime.UtcNow.ToString("o"));
   var latest=await Latest();
   if(latest==null)return force?"Couldn't find the engine list on Microsoft's site.":null;
   var installed=Installed(root).Select(i=>i.Item1).DefaultIfEmpty(current).Max();
   if(current!=null&&latest.Item1<=current||installed!=null&&latest.Item1<=installed)
    return force?(installed!=null&&installed>current?"Engine "+installed+" is ready and starts next time you open Mica.":"The engine is up to date ("+current+")."):null;
   // The CAB (~180 MB) unpacks to ~700 MB; leave room for everything else on the disk.
   if(new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))).AvailableFreeSpace<2L*1024*1024*1024)return force?"Not enough disk space to download engine "+latest.Item1+" (needs about 2 GB free).":null;
   string cab=Path.Combine(root,latest.Item1+".cab");
   using(var client=Client())await client.DownloadFileTaskAsync(new Uri(latest.Item2),cab+".part");
   if(File.Exists(cab))File.Delete(cab);File.Move(cab+".part",cab);
   try {await Task.Run(()=>InstallCab(cab,latest.Item1,root));}finally {try {File.Delete(cab);}catch(Exception){}}
   return "Engine "+latest.Item1+" downloaded. It starts next time you open Mica.";
  }
 }
}
