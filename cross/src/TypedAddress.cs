using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Mica {
 // As on Windows (BrowserTypedAddress.cs): javascript: typed in the address bar runs on the page you're on and a
 // data: page opens, each once you've said yes; mailto:, ftp:// and app links go to the app the system has for them.
 sealed partial class BrowserWindow {
  void ConfirmTyped(string title,string message,string accept,Action yes) {
   StackPanel body,actions;var popup=MicaDialog(title,420,out body,out actions);
   var detail=DialogText(message,13,"Muted");detail.LineHeight=22;body.Children.Add(detail);
   var cancel=DialogButton("Cancel",false);cancel.Margin=new Avalonia.Thickness(0,0,8,0);cancel.Click+=delegate {popup.IsOpen=false;};actions.Children.Add(cancel);
   var ok=DialogButton(accept,true);ok.Click+=delegate {popup.IsOpen=false;yes();};actions.Children.Add(ok);
   popup.IsOpen=true;Dispatcher.UIThread.Post(delegate {cancel.Focus();},DispatcherPriority.Background);
  }
  void RunTypedScript(string url) {
   var tab=Active;
   if(tab==null||tab.View==null||!tab.View.IsBrowserInitialized||!(Navigation.IsWeb(tab.Entry.Url)||Navigation.IsLocalFile(tab.Entry.Url))){Status("javascript: runs on a web page. Open one first.");return;}
   string site=Navigation.IsWeb(tab.Entry.Url)?new Uri(tab.Entry.Url).Host:"this file";
   ConfirmTyped("Run JavaScript on "+site+"?","It runs with your sign-in on "+site+" and can read or change anything there. Scammers ask people to paste code like this to take over their accounts. Run it only if you wrote it or trust where it came from.","Run it",delegate {
    if(tab.View==null)return;
    string code;try {code=Uri.UnescapeDataString(url.Substring("javascript:".Length));}catch(UriFormatException){code=url.Substring("javascript:".Length);}
    tab.View.ExecuteJavaScript(code,"",1);Status("Ran the JavaScript on "+site+".");
   });
  }
  void OpenAppLink(string url) {
   try {
    var start=new ProcessStartInfo(Mac?"open":OperatingSystem.IsWindows()?url:"xdg-open") {UseShellExecute=OperatingSystem.IsWindows()};
    if(!OperatingSystem.IsWindows())start.ArgumentList.Add(url);
    var app=Process.Start(start);string scheme=url.Substring(0,url.IndexOf(':'));
    // open and xdg-open fail fast when nothing handles the scheme.
    if(app!=null&&!OperatingSystem.IsWindows())_=System.Threading.Tasks.Task.Run(delegate {
     if(app.WaitForExit(5000)&&app.ExitCode!=0)Dispatcher.UIThread.Post(delegate {Status("No app on this computer opens "+scheme+": links.");});
    });
    Status("Opening "+scheme+": in its app.");
   } catch(Exception) {Status("No app on this computer opens that link.");}
  }
 }
}
