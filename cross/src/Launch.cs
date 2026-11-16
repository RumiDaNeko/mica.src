using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Mica {
 // Links opened from other apps (BrowserLaunch.cs). macOS hands a running Mica the link itself (an open-URL event);
 // Linux starts Mica again with the link, and that second start passes it to the Mica already running on the
 // profile through a socket in the profile folder, readable by this user only, then exits. Either way the link
 // opens in a new tab and the window comes to the front.
 static class Launch {
  internal static readonly List<string> Urls=new List<string>();
  // Unix socket paths are limited to about 104 bytes. A profile deeper than that gets its socket in a folder only
  // this user can reach, named after the profile: $XDG_RUNTIME_DIR on Linux, the per-user temp folder on macOS and
  // Windows. Never the shared /tmp, where another user could stand in for Mica and read the links.
  internal static string SocketPath(string profile) {
   string path=Path.Combine(profile,"launch.sock");if(Encoding.UTF8.GetByteCount(path)<100)return path;
   string folder=OperatingSystem.IsLinux()?Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"):Path.GetTempPath();
   if(string.IsNullOrEmpty(folder)||!Directory.Exists(folder))return null;
   string name="mica-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(profile)))).Substring(0,16).ToLowerInvariant()+".sock";
   path=Path.Combine(folder,name);return Encoding.UTF8.GetByteCount(path)<100?path:null;
  }
  // True when a running Mica took the links.
  internal static bool Forward(string profile,IList<string> urls) {
   string path=SocketPath(profile);if(path==null||!File.Exists(path))return false;
   try {
    using(var socket=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified)) {
     socket.SendTimeout=2000;socket.ReceiveTimeout=2000;
     socket.Connect(new UnixDomainSocketEndPoint(path));
     socket.Send(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(urls.Take(4).ToArray())+"\n"));
     return true;
    }
   } catch(Exception) {return false;}
  }
  // Listens until the window goes; each message is a JSON list of up to four links.
  internal static Socket Listen(string profile,Action<string[]> received) {
   string path=SocketPath(profile);if(path==null)return null;
   try {
    if(File.Exists(path)){if(Forward(profile,new string[0]))return null;File.Delete(path);} // Left by a Mica that crashed.
    var server=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
    server.Bind(new UnixDomainSocketEndPoint(path));
    if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    server.Listen(4);
    var thread=new Thread(delegate() {
     while(true) {
      Socket client;try {client=server.Accept();} catch(Exception) {return;} // Closed with the window.
      try {
       using(client) {
        client.ReceiveTimeout=2000;var bytes=new List<byte>();var buffer=new byte[4096];
        while(bytes.Count<65536){int n=client.Receive(buffer);if(n<=0)break;bytes.AddRange(buffer.Take(n));if(buffer.Take(n).Contains((byte)'\n'))break;}
        var urls=new JavaScriptSerializer().Deserialize<string[]>(Encoding.UTF8.GetString(bytes.ToArray()).Trim());
        if(urls!=null)received(urls.Where(u=>u!=null&&u.Length<=8192).Take(4).ToArray());
       }
      } catch(Exception) {}
     }
    }) {IsBackground=true,Name="Mica launch"};
    thread.Start();
    return server;
   } catch(Exception) {return null;}
  }
 }
 sealed partial class BrowserWindow {
  Socket launchSocket;
  internal int broughtToFront;
  void StartLaunchListener() {
   if(Program.Private)return;
   launchSocket=Launch.Listen(profile,delegate(string[] urls) {Dispatcher.UIThread.Post(delegate {HandleLaunch(urls);});});
   Closing+=delegate {
    if(launchSocket==null)return;
    try {launchSocket.Close();} catch(Exception) {}
    try {string path=Launch.SocketPath(profile);if(path!=null)File.Delete(path);} catch(Exception) {}
   };
   // The links Mica was started with.
   if(Launch.Urls.Count>0)Opened+=delegate {Dispatcher.UIThread.Post(delegate {foreach(var url in Launch.Urls)OpenExternal(url);},DispatcherPriority.Background);};
   // macOS: links clicked while Mica runs (and the one that started it) come as open-URL events.
   var events=Avalonia.Application.Current==null?null:Avalonia.Application.Current.TryGetFeature(typeof(IActivatableLifetime)) as IActivatableLifetime;
   if(events!=null)events.Activated+=delegate(object s,ActivatedEventArgs e) {
    var link=e as ProtocolActivatedEventArgs;
    if(link!=null&&link.Kind==ActivationKind.OpenUri&&link.Uri!=null)HandleLaunch(new[]{link.Uri.OriginalString});
    else if(e.Kind==ActivationKind.Reopen)BringToFront();
   };
  }
  internal void HandleLaunch(string[] urls) {
   // Open the links first, so the window that comes forward already shows them.
   foreach(var url in urls)OpenExternal(url);
   BringToFront();
  }
  void OpenExternal(string url) {
   // A plain path first: on Linux and macOS "/home/a.html" also parses as an absolute URI, and handed
   // over as it is, the address bar would search for it.
   string file=null;try {if(!Navigation.IsWeb(url)&&File.Exists(url))file=new Uri(Path.GetFullPath(url)).AbsoluteUri;} catch(Exception) {}
   if(file!=null){Navigate(file,true);return;}
   if(Navigation.IsWeb(url)||Navigation.IsLocalFile(url))Navigate(url,true);
  }
  // A link opened from another app lands in front of that app: restored if minimized, then raised. Where the
  // window manager won't let it take focus, it still rises above the other windows.
  internal void BringToFront() {
   broughtToFront++;
   if(Program.SelfTestResult!=null)return; // The test window stays off-screen and never takes focus.
   if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;
   Show();Activate();
   if(!IsActive&&!Topmost){Topmost=true;Topmost=false;}
  }
 }
}
