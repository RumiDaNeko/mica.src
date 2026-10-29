using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Microsoft.Web.WebView2.Core;

namespace Mica {
 partial class BrowserApp {
  [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
  async Task RunSandboxTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"sandbox");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;
   log.Add("Windows Sandbox installed: "+SandboxAvailable());
   RegressionCheck("sandbox config isolates the copy",delegate {
    var xml=XDocument.Parse(SandboxConfig(@"C:\stage\Download","setup & run.exe")).Root;
    Require(xml.Element("Networking").Value=="Disable"&&xml.Element("ClipboardRedirection").Value=="Disable"&&xml.Element("VGpu").Value=="Disable","Isolation switches missing");
    var folder=xml.Element("MappedFolders").Element("MappedFolder");
    Require(folder.Element("ReadOnly").Value=="true"&&folder.Element("HostFolder").Value==@"C:\stage\Download","Mapped folder is not read-only");
    Require(xml.Element("LogonCommand").Element("Command").Value==@"explorer.exe ""C:\Users\WDAGUtilityAccount\Desktop\Download\setup & run.exe""","Logon command: "+xml.Element("LogonCommand").Element("Command").Value);
   });
   string blocked=Path.Combine(dir,"medium-probe.txt"),allowed=Path.Combine(RestrictedTemp(),"mica-probe.txt");if(File.Exists(allowed))File.Delete(allowed);
   string cmd=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe");
   string secret=Path.Combine(profile,"restricted-secret.txt"),leaked=Path.Combine(RestrictedTemp(),"mica-read.txt");File.WriteAllText(secret,"password");if(File.Exists(leaked))File.Delete(leaked);
   IntPtr process;int error=RunRestricted(cmd,"/c ping -n 2 127.0.0.1 >nul & echo x>\""+blocked+"\" & echo x>\"%TEMP%\\mica-probe.txt\" & type \""+secret+"\" >\"%TEMP%\\mica-read.txt\"",out process);
   int integrity=process==IntPtr.Zero?-1:ProcessIntegrity(process);
   if(process!=IntPtr.Zero){await Task.Run(()=>WaitForSingleObject(process,15000));CloseHandle(process);}
   RegressionCheck("restricted apps run sealed in a container at Low integrity",delegate {Require(error==0,"Launch error "+error);Require(integrity==0x1000,"Integrity 0x"+integrity.ToString("X"));});
   RegressionCheck("restricted apps can't write to your folders",delegate {Require(!File.Exists(blocked),"Wrote into a Medium-integrity folder");});
   RegressionCheck("restricted apps get a writable temp folder",delegate {Require(File.Exists(allowed),"No file in "+allowed);});
   RegressionCheck("restricted apps can't read your profile",delegate {Require(!File.Exists(leaked)||!File.ReadAllText(leaked).Contains("password"),"Read "+secret);});
   if(File.Exists(leaked))File.Delete(leaked);File.Delete(secret);
   if(File.Exists(allowed))File.Delete(allowed);
   if(!SandboxAvailable()) {
    string fake=Path.Combine(dir,"setup.exe");File.WriteAllText(fake,"not a program");
    OpenDownloadSafely(fake);await Task.Delay(700);
    RegressionCheck("risky open without Sandbox asks first",delegate {Require(siteBubble!=null&&siteBubble.IsOpen,"No bubble");var buttons=VaultElements<Button>(siteBubble.Child).Select(b=>b.Tag as string).ToList();Require(buttons.Contains("enable")&&buttons.Contains("restricted")&&buttons.Contains("run"),"Choices: "+string.Join(",",buttons));});
    if(siteBubble!=null){var card=(FrameworkElement)siteBubble.Child;card.UpdateLayout();CapturePopup(siteBubble,"mica-sandbox-bubble.png");
     RegressionCheck("every choice fits inside the bubble",delegate {foreach(var b in VaultElements<Button>(card).Where(b=>b.Tag is string&&(string)b.Tag!="")){var at=b.TransformToAncestor(card).Transform(new Point(0,0));Require(at.X>=0&&at.X+b.ActualWidth<=card.ActualWidth-8,b.Tag+" at "+at.X+"+"+b.ActualWidth+" of "+card.ActualWidth);}});}
    if(siteBubble!=null)((Action)siteBubble.Tag)();
   }
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
  async Task RunDownloadNoticeTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"notice");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;
   Environment.SetEnvironmentVariable("MICA_TEST_DOWNLOAD_PROMPT","1");
   Navigate("https://httpbin.org/response-headers?Content-Disposition=attachment%3B%20filename%3Dmica-notice.txt",true);
   for(int i=0;i<120&&!(siteBubble!=null&&siteBubble.IsOpen&&Find<Border>("ErrorPanel").IsVisible);i++)await Task.Delay(250);
   await Task.Delay(500);
   log.Add("title: "+Find<TextBlock>("ErrorTitle").Text+" | "+Find<TextBlock>("ErrorText").Text);
   RegressionCheck("a link that is a file shows a download notice, not an error",delegate {Require(Find<Border>("ErrorPanel").IsVisible&&Find<TextBlock>("ErrorTitle").Text=="A download is waiting."&&!Find<Button>("RetryButton").IsVisible,"Panel: "+Find<TextBlock>("ErrorTitle").Text+" / "+Find<TextBlock>("ErrorText").Text);});
   RegressionCheck("the download card stays with the Mica window",delegate {Require(siteBubble!=null&&siteBubble.IsOpen,"No card");Require(!BubbleIsTopmost(),"Card is always-on-top");});
   SaveWindowImage(Path.Combine(dir,"notice.png"));
   if(siteBubble!=null)((Action)siteBubble.Tag)();
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
  async Task RunAlertTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"alert");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;await Task.Delay(500);
   string origin="https://n-sc4rfknvcljugsllxvucs6vsjn36tdcl7gitnii-0lu-script.googleusercontent.com";
   foreach(var message in new[]{"Saved! Your sheet was updated.",string.Join(" ",Enumerable.Repeat("This is a long message from the page that should wrap inside the dialog.",12))}) {
    string report=null;var timer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(900)};
    timer.Tick+=delegate {
     timer.Stop();var dialog=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w.Title=="This page says");
     if(dialog==null){report="no dialog";return;}
     var ok=VaultElements<Button>(dialog).First(b=>b.Content as string=="OK");var bottom=ok.TranslatePoint(new Point(0,ok.ActualHeight),dialog).Y;
     var text=VaultElements<TextBlock>(dialog).FirstOrDefault(b=>b.Text==message);
     report=string.Format("height {0:0}, OK bottom {1:0}, message shown {2}",dialog.ActualHeight,bottom,text!=null&&text.IsVisible);
     SaveAnyWindow(dialog,Path.Combine(dir,"alert-"+message.Length+".png"));
     if(!(bottom<=dialog.ActualHeight-10&&text!=null))report+=" clipped";dialog.DialogResult=true;
    };
    timer.Start();string ignored;
    try {PageDialog(CoreWebView2ScriptDialogKind.Alert,origin,message,null,out ignored);}catch(Exception e){report+=" "+e.Message;}
    log.Add("alert ("+message.Length+" chars): "+report);
    string shown=report??"";
    RegressionCheck("alert from a long host fits ("+message.Length+" chars)",delegate {Require(shown.StartsWith("height")&&!shown.Contains("clipped"),shown);});
   }
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
  internal List<string> navLog;
  async Task RunNavProbeTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"navprobe");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;
   navLog=new List<string>();state.AmbientYoutube=true;
   foreach(var url in new[]{"https://www.youtube.com/","https://example.com/"}) {
    navLog.Add("== first open "+url);Navigate(url,true);await Task.Delay(20000);
    string id=state.ActiveId;navLog.Add("== reopen (like a restart) "+url);DisposeView(id);await Activate(id);await Task.Delay(12000);
   }
   log.AddRange(navLog);File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
  async Task<string> DevToolsTitle() {try {return (await devToolsView.CoreWebView2.ExecuteScriptAsync("document.title")).Trim('"');}catch(Exception){return "";}}
  async Task RunDevToolsTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"devtools");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Width=1400;window.Height=880;window.Left=-6000;window.Top=-6000;
   Navigate("https://example.com/",false);
   await WaitFor(delegate {NavigationProgress p;return CurrentView()!=null&&CurrentView().CoreWebView2!=null&&navigations.TryGetValue(state.ActiveId,out p)&&!p.IsLoading;},25000);
   log.Add("debug port "+devToolsPort);
   ToggleDevTools();
   string title="";for(int i=0;i<80;i++){await Task.Delay(250);if(devToolsView!=null&&devToolsView.CoreWebView2!=null){title=await DevToolsTitle();if(title.StartsWith("DevTools - "))break;}}
   log.Add("devtools title: "+title);
   log.Add("frontend settings: "+await devToolsView.CoreWebView2.ExecuteScriptAsync("JSON.stringify(Object.keys(localStorage).filter(k=>/screencast|welcome|release|theme/i.test(k)).map(k=>k+'='+localStorage.getItem(k)))"));
   RegressionCheck("F12 docks the engine DevTools on the right",delegate {var columns=Find<Grid>("PageSurface").ColumnDefinitions;Require(Find<Grid>("DevToolsDock").IsVisible&&columns[2].ActualWidth>=280&&columns[0].ActualWidth>=320,"Dock widths "+columns[0].ActualWidth+"/"+columns[2].ActualWidth);});
   RegressionCheck("DevTools connects to the active page",delegate {Require(title.Contains("example.com"),"Title: "+title);});
   await Task.Delay(1500);
   try {using(var output=File.Create(Path.Combine(dir,"devtools.png")))await devToolsView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,output);}catch(Exception e){log.Add("capture: "+e.Message);}
   string socket=await CurrentView().CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate","{\"awaitPromise\":true,\"returnByValue\":true,\"expression\":\"new Promise(r=>{const s=new WebSocket('ws://127.0.0.1:"+devToolsPort+"/devtools/page/x');s.onopen=()=>r('open');s.onerror=()=>r('blocked');setTimeout(()=>r('timeout'),4000);})\"}");
   log.Add("page socket: "+socket);
   RegressionCheck("web pages can't open the debugging socket",delegate {Require(!socket.Contains("\"open\""),socket);});
   Navigate("https://example.org/",true);
   await WaitFor(delegate {NavigationProgress p;return CurrentView()!=null&&CurrentView().CoreWebView2!=null&&navigations.TryGetValue(state.ActiveId,out p)&&!p.IsLoading;},25000);
   for(int i=0;i<60;i++){await Task.Delay(250);title=await DevToolsTitle();if(title.Contains("example.org"))break;}
   RegressionCheck("switching tabs retargets DevTools",delegate {Require(title.Contains("example.org"),"Title: "+title);});
   RegressionCheck("DevTools page stays inside Mica",delegate {Require(state.Tabs.Count==2&&popupWindows.Count==0,"Tabs "+state.Tabs.Count+", popups "+popupWindows.Count);});
   // Detach into a Mica window, close and reopen with F12, then dock back.
   Func<string,Task<string>> waitTitle=async want=>{string t="";for(int i=0;i<80;i++){await Task.Delay(250);if(devToolsView!=null&&devToolsView.CoreWebView2!=null){t=await DevToolsTitle();if(t.Contains(want))break;}}return t;};
   SetDevToolsDetached(true);string detached=await waitTitle("example.org");log.Add("detached title: "+detached);
   RegressionCheck("Detach moves DevTools into its own Mica window",delegate {
    Require(devToolsWindow!=null&&devToolsWindow.IsVisible&&devToolsView!=null&&Window.GetWindow(devToolsView)==devToolsWindow,"not in its window");
    Require(!Find<Grid>("DevToolsDock").IsVisible,"dock still showing");Require(detached.Contains("example.org"),detached);
   });
   if(devToolsWindow!=null){devToolsWindow.UpdateLayout();var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)devToolsWindow.ActualWidth,(int)devToolsWindow.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);image.Render(devToolsWindow);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(dir,"detached-chrome.png")))png.Save(file);
    try {using(var output=File.Create(Path.Combine(dir,"detached-inspector.png")))await devToolsView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,output);}catch(Exception e){log.Add("capture: "+e.Message);}}
   ToggleDevTools();await Task.Delay(400);
   RegressionCheck("F12 closes the detached window",delegate {Require(devToolsWindow==null&&!devToolsOpen&&!Find<Grid>("DevToolsDock").IsVisible,"still open");});
   ToggleDevTools();string reopened=await waitTitle("example.org");
   RegressionCheck("F12 reopens DevTools detached",delegate {Require(devToolsWindow!=null&&reopened.Contains("example.org")&&!Find<Grid>("DevToolsDock").IsVisible,reopened);});
   var dockButton=devToolsWindow==null?null:LogicalDescendants<Button>(devToolsWindow).FirstOrDefault(b=>(b.Tag as string)=="devtools-dock");
   if(dockButton!=null)dockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
   string docked=await waitTitle("example.org");await Task.Delay(400);
   RegressionCheck("Dock to Mica brings it back to the right side",delegate {var columns=Find<Grid>("PageSurface").ColumnDefinitions;Require(devToolsWindow==null&&Find<Grid>("DevToolsDock").IsVisible&&columns[2].ActualWidth>=280&&docked.Contains("example.org"),"dock "+columns[2].ActualWidth+" "+docked);});
   ToggleDevTools();await Task.Delay(300);
   RegressionCheck("F12 again closes the dock",delegate {Require(!Find<Grid>("DevToolsDock").IsVisible&&Find<Grid>("PageSurface").ColumnDefinitions[2].ActualWidth==0,"Dock still visible");});
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
