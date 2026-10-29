using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace Mica {
 partial class BrowserApp {
  async Task RunRuntimeTest() {
   var log=new System.Collections.Generic.List<string>();int failed=0;
   try {
    // A lite install has no engine of its own and runs on Windows' WebView2 runtime instead.
    string folder=BundledRuntime.Folder();bool windowsEngine=folder==null&&BundledRuntime.EvergreenMode;Require(folder!=null||windowsEngine,"Test requires the packaged runtime");
    using(var server=new RegressionServer()) {
     Navigate(server.Url+"ok",false);await WaitFor(()=>CurrentView()!=null&&CurrentView().CoreWebView2.DocumentTitle=="Regression OK",45000);
     var core=CurrentView().CoreWebView2;string path;
     using(var process=Process.GetProcessById((int)core.BrowserProcessId))path=process.MainModule.FileName;
     if(windowsEngine)Require(path.IndexOf("\\EdgeWebView\\Application\\",StringComparison.OrdinalIgnoreCase)>=0,"Lite install didn't use Windows' engine: "+path);
     else Require(Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Browser used an installed engine: "+path);
     log.Add((windowsEngine?"PASS running browser process comes from Windows' WebView2 runtime: ":"PASS running browser process comes from packaged Runtime: ")+path);
     log.Add("Runtime version: "+(await EnsureEnvironment()).BrowserVersionString);
     Require(await core.ExecuteScriptAsync("1+2")=="3","Bundled renderer script execution failed");
     log.Add("PASS bundled engine navigates and executes JavaScript");
    }
   }catch(Exception e){failed++;log.Add("FAIL "+e);log.Add(Find<System.Windows.Controls.TextBlock>("ErrorText").Text);}
   log.Add(failed==0?"PASS":"FAIL");File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","runtime-result.txt"),log);Application.Current.Shutdown(failed);
  }
 }
}
