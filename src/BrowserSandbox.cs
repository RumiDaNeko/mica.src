using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace Mica {
 // Opening a download never runs it directly on the desktop:
 //  * With Windows Sandbox on, the file is copied into a throwaway VM (read-only
 //    folder, no network, no clipboard) and opened there.
 //  * Without it, apps run in an AppContainer ("restricted"): they can't see the
 //    user's files, Mica's passwords or the network, and installers that need admin
 //    rights are refused instead of elevating.
 partial class BrowserApp {
  static string SandboxProgram {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsSandbox.exe");}}
  static bool SandboxAvailable() {return File.Exists(SandboxProgram);}
  static readonly HashSet<string> restrictedRunnable=new HashSet<string>(StringComparer.OrdinalIgnoreCase) {".exe",".com",".scr"};
  static bool CanRunRestricted(string path) {return restrictedRunnable.Contains(Path.GetExtension(path??""));}
  const string SandboxDesktop=@"C:\Users\WDAGUtilityAccount\Desktop\Download";
  static string SandboxConfig(string hostFolder,string fileName) {
   string inside=SandboxDesktop+"\\"+fileName;
   return "<Configuration>\r\n"+
    " <VGpu>Disable</VGpu>\r\n <Networking>Disable</Networking>\r\n <ClipboardRedirection>Disable</ClipboardRedirection>\r\n <PrinterRedirection>Disable</PrinterRedirection>\r\n"+
    " <AudioInput>Disable</AudioInput>\r\n <VideoInput>Disable</VideoInput>\r\n <ProtectedClient>Enable</ProtectedClient>\r\n"+
    " <MappedFolders><MappedFolder><HostFolder>"+SecurityElement.Escape(hostFolder)+"</HostFolder><SandboxFolder>"+SandboxDesktop+"</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder></MappedFolders>\r\n"+
    " <LogonCommand><Command>"+SecurityElement.Escape("explorer.exe \""+inside+"\"")+"</Command></LogonCommand>\r\n"+
    "</Configuration>\r\n";
  }
  string SandboxStaging() {
   string root=Path.Combine(profile,"Sandbox");Directory.CreateDirectory(root);
   foreach(var old in Directory.GetDirectories(root))try {if(Directory.GetLastWriteTimeUtc(old)<DateTime.UtcNow.AddHours(-12))Directory.Delete(old,true);}catch(Exception){}
   string folder=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);return folder;
  }
  // Returns the .wsb path that was launched.
  string OpenInSandbox(string path) {
   string folder=SandboxStaging(),files=Path.Combine(folder,"Download");Directory.CreateDirectory(files);
   string name=Path.GetFileName(path);File.Copy(path,Path.Combine(files,name));
   string config=Path.Combine(folder,"open.wsb");File.WriteAllText(config,SandboxConfig(files,name),new UTF8Encoding(false));
   Process.Start(new ProcessStartInfo {FileName=SandboxProgram,Arguments="\""+config+"\"",UseShellExecute=false});
   return config;
  }
  void OpenDownloadSafely(string path) {
   if(SandboxAvailable()) {
    try {OpenInSandbox(path);Status("Opening "+Path.GetFileName(path)+" in Windows Sandbox. Nothing it does reaches this PC.");}
    catch(Exception e) {Status("Couldn't start Windows Sandbox: "+e.Message);}
    return;
   }
   if(!IsRiskyDownload(path)) {
    try {Process.Start(new ProcessStartInfo {FileName=path,UseShellExecute=true});Status("Opened "+Path.GetFileName(path)+". Windows checks downloaded files before they open.");}
    catch(Exception e) {Status("Couldn't open file: "+e.Message);}
    return;
   }
   bool runnable=CanRunRestricted(path);
   var choices=new List<string[]> {new[]{"Turn on Sandbox","enable",""},new[]{"Show in folder","folder",runnable?"":"primary"}};
   if(runnable)choices.Add(new[]{"Run restricted","restricted",""});
   choices.Add(new[]{"Run anyway","run",runnable?"primary":""});
   ShowChoiceBubble(Find<System.Windows.Controls.Button>("AddressButton"),"\uE83D","Windows Sandbox is off",Path.GetFileName(path),"Mica opens downloads in Windows Sandbox when it's on.",
    runnable?"Restricted mode seals the app off: it can't see your files, passwords or the internet. Many apps and installers won't work that way. Run anyway opens it normally, so only do that if you trust where it came from.":"This file type can run code. Turn on Windows Sandbox to open it safely, or run it anyway if you trust where it came from.",
    choices.ToArray(),()=>!closing,delegate(string choice) {
     if(choice=="enable")EnableSandboxHelp();
     else if(choice=="folder")try {Process.Start(new ProcessStartInfo {FileName="explorer.exe",Arguments="/select,\""+path+"\"",UseShellExecute=true});}catch(Exception){}
     else if(choice=="run") {
      // Your call: it opens like any program you start yourself, and Windows still checks it (SmartScreen, UAC).
      try {Process.Start(new ProcessStartInfo {FileName=path,UseShellExecute=true});Status("Opened "+Path.GetFileName(path)+".");}
      catch(System.ComponentModel.Win32Exception e) {if(e.NativeErrorCode!=1223)Status("Couldn't open file: "+e.Message);} // 1223: you said no to Windows' prompt
      catch(Exception e) {Status("Couldn't open file: "+e.Message);}
     }
     else if(choice=="restricted") {
      IntPtr process;int error=RunRestricted(path,null,out process);if(process!=IntPtr.Zero)CloseHandle(process);
      Status(error==0?"Running "+Path.GetFileName(path)+" in restricted mode.":error==740?"This installer needs admin rights, which restricted mode blocks. Turn on Windows Sandbox to try it safely.":"Couldn't run in restricted mode (error "+error+").");
     }
    });
  }
  void EnableSandboxHelp() {
   try {Process.Start(new ProcessStartInfo {FileName="optionalfeatures.exe",UseShellExecute=true});Status("Tick \"Windows Sandbox\", press OK, then restart Windows.");}
   catch(Exception) {Status("Open \"Turn Windows features on or off\" and tick Windows Sandbox.");}
  }

  // Restricted launch: the app runs in a Windows AppContainer (the sandbox Store apps and Edge's renderers
  // use). It sees none of your files, Mica's profile or passwords, has no network or loopback access, and
  // can only write inside its own container folder. The download is copied there, since the container
  // can't read your Downloads folder. Many desktop apps don't work this way; those need Windows Sandbox.
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct StartupInfo {public int cb;public string lpReserved,lpDesktop,lpTitle;public int dwX,dwY,dwXSize,dwYSize,dwXCountChars,dwYCountChars,dwFillAttribute,dwFlags;public short wShowWindow,cbReserved2;public IntPtr lpReserved2,hStdInput,hStdOutput,hStdError;}
  [StructLayout(LayoutKind.Sequential)] struct StartupInfoEx {public StartupInfo StartupInfo;public IntPtr AttributeList;}
  [StructLayout(LayoutKind.Sequential)] struct ProcessInformation {public IntPtr hProcess,hThread;public int dwProcessId,dwThreadId;}
  [StructLayout(LayoutKind.Sequential)] struct SecurityCapabilities {public IntPtr AppContainerSid,Capabilities;public uint CapabilityCount,Reserved;}
  [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
  [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int infoClass,IntPtr info,int length,out int returned);
  [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthority(IntPtr sid,uint index);
  [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
  [DllImport("advapi32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool ConvertSidToStringSid(IntPtr sid,out IntPtr text);
  [DllImport("advapi32.dll")] static extern IntPtr FreeSid(IntPtr sid);
  [DllImport("userenv.dll",CharSet=CharSet.Unicode)] static extern int CreateAppContainerProfile(string name,string displayName,string description,IntPtr capabilities,uint count,out IntPtr sid);
  [DllImport("userenv.dll",CharSet=CharSet.Unicode)] static extern int DeriveAppContainerSidFromAppContainerName(string name,out IntPtr sid);
  [DllImport("userenv.dll",CharSet=CharSet.Unicode)] static extern int GetAppContainerFolderPath(string sid,out IntPtr path);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool InitializeProcThreadAttributeList(IntPtr list,int count,int flags,ref IntPtr size);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool UpdateProcThreadAttribute(IntPtr list,uint flags,IntPtr attribute,IntPtr value,IntPtr size,IntPtr previous,IntPtr returned);
  [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
  [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool CreateProcess(string application,StringBuilder commandLine,IntPtr processAttributes,IntPtr threadAttributes,bool inherit,uint flags,string environment,string directory,ref StartupInfoEx startup,out ProcessInformation info);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
  [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
  const string RestrictedContainerName="Mica.Restricted";
  // The container's SID (free with FreeSid), creating its profile the first time.
  static IntPtr RestrictedContainer() {
   IntPtr sid;
   if(CreateAppContainerProfile(RestrictedContainerName,"Mica restricted apps","Downloads Mica runs in restricted mode",IntPtr.Zero,0,out sid)==0)return sid;
   return DeriveAppContainerSidFromAppContainerName(RestrictedContainerName,out sid)==0?sid:IntPtr.Zero;
  }
  // The only folder a restricted app can write to.
  static string RestrictedFolder() {
   IntPtr sid=RestrictedContainer();if(sid==IntPtr.Zero)throw new InvalidOperationException("Windows couldn't create the restricted container.");
   try {
    IntPtr text;if(!ConvertSidToStringSid(sid,out text))throw new InvalidOperationException("Windows couldn't name the restricted container.");
    string name;try {name=Marshal.PtrToStringUni(text);}finally {LocalFree(text);}
    IntPtr path;if(GetAppContainerFolderPath(name,out path)!=0)throw new InvalidOperationException("Windows couldn't find the restricted container's folder.");
    try {return Marshal.PtrToStringUni(path);}finally {Marshal.FreeCoTaskMem(path);}
   }finally {FreeSid(sid);}
  }
  static string RestrictedTemp() {string temp=Path.Combine(RestrictedFolder(),"Temp");Directory.CreateDirectory(temp);return temp;}
  static string RestrictedEnvironment(string temp) {
   var values=Environment.GetEnvironmentVariables();var names=values.Keys.Cast<string>().Where(n=>!n.Equals("TEMP",StringComparison.OrdinalIgnoreCase)&&!n.Equals("TMP",StringComparison.OrdinalIgnoreCase)).OrderBy(n=>n,StringComparer.OrdinalIgnoreCase).ToList();
   var block=new StringBuilder();foreach(var name in names)block.Append(name).Append('=').Append(values[name]).Append('\0');
   block.Append("TEMP=").Append(temp).Append('\0').Append("TMP=").Append(temp).Append('\0').Append('\0');
   return block.ToString();
  }
  // Returns 0 or a Win32 error (740 = the app requires elevation).
  static int RunRestricted(string path,string arguments,out IntPtr process) {
   process=IntPtr.Zero;IntPtr sid=IntPtr.Zero,list=IntPtr.Zero,capabilities=IntPtr.Zero;
   try {
    string container=RestrictedFolder(),runs=Path.Combine(container,"Run");Directory.CreateDirectory(runs);
    foreach(var old in Directory.GetDirectories(runs))try {if(Directory.GetLastWriteTimeUtc(old)<DateTime.UtcNow.AddHours(-12))Directory.Delete(old,true);}catch(Exception){}
    string folder=Path.Combine(runs,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
    string copy=Path.Combine(folder,Path.GetFileName(path));File.Copy(path,copy);
    sid=RestrictedContainer();if(sid==IntPtr.Zero)return 1168;
    var security=new SecurityCapabilities {AppContainerSid=sid};capabilities=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(SecurityCapabilities)));Marshal.StructureToPtr(security,capabilities,false);
    IntPtr size=IntPtr.Zero;InitializeProcThreadAttributeList(IntPtr.Zero,1,0,ref size);list=Marshal.AllocHGlobal(size);
    if(!InitializeProcThreadAttributeList(list,1,0,ref size)){Marshal.FreeHGlobal(list);list=IntPtr.Zero;return Marshal.GetLastWin32Error();}
    if(!UpdateProcThreadAttribute(list,0,(IntPtr)0x00020009,capabilities,(IntPtr)Marshal.SizeOf(typeof(SecurityCapabilities)),IntPtr.Zero,IntPtr.Zero))return Marshal.GetLastWin32Error();
    var startup=new StartupInfoEx {AttributeList=list};startup.StartupInfo.cb=Marshal.SizeOf(typeof(StartupInfoEx));ProcessInformation info;
    var command=new StringBuilder("\""+copy+"\""+(string.IsNullOrEmpty(arguments)?"":" "+arguments));
    // EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT
    if(!CreateProcess(copy,command,IntPtr.Zero,IntPtr.Zero,false,0x00080000|0x00000400,RestrictedEnvironment(RestrictedTemp()),folder,ref startup,out info))return Marshal.GetLastWin32Error();
    CloseHandle(info.hThread);process=info.hProcess;return 0;
   }catch(IOException){return 5;}catch(UnauthorizedAccessException){return 5;}catch(InvalidOperationException){return 1168;}
   finally {if(list!=IntPtr.Zero){DeleteProcThreadAttributeList(list);Marshal.FreeHGlobal(list);}if(capabilities!=IntPtr.Zero)Marshal.FreeHGlobal(capabilities);if(sid!=IntPtr.Zero)FreeSid(sid);}
  }  // Integrity RID of a process (0x1000 = Low, 0x2000 = Medium).
  static int ProcessIntegrity(IntPtr process) {
   IntPtr token;if(!OpenProcessToken(process,0x0008,out token))return -1;
   try {
    int size;GetTokenInformation(token,25,IntPtr.Zero,0,out size);IntPtr buffer=Marshal.AllocHGlobal(size);
    try {
     if(!GetTokenInformation(token,25,buffer,size,out size))return -1;
     IntPtr sid=Marshal.ReadIntPtr(buffer);int count=Marshal.ReadByte(GetSidSubAuthorityCount(sid));
     return Marshal.ReadInt32(GetSidSubAuthority(sid,(uint)(count-1)));
    }finally {Marshal.FreeHGlobal(buffer);}
   }finally {CloseHandle(token);}
  }
 }
}
