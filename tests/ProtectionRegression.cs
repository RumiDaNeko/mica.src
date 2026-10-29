using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace Mica {
 partial class BrowserApp {
  async Task WaitForPage(int milliseconds=25000) {
   await WaitFor(delegate {NavigationProgress p;return CurrentView()!=null&&CurrentView().CoreWebView2!=null&&navigations.TryGetValue(state.ActiveId,out p)&&!p.IsLoading;},milliseconds);
  }
  async Task RunProtectionTest() {
   string dir=Path.Combine(profile,"protection");Directory.CreateDirectory(dir);
   var steps=RunProtectionSteps();
   if(await Task.WhenAny(steps,Task.Delay(200000))!=steps)regressionLog.Add("TIMEOUT after the last line above");
   else if(steps.IsFaulted)regressionLog.Add("ERROR "+steps.Exception.InnerException);
   regressionLog.Add(regressionFailures==0&&steps.Status==TaskStatus.RanToCompletion?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),regressionLog);window.Close();
  }
  async Task RunProtectionSteps() {
   var log=regressionLog;string dir=Path.Combine(profile,"protection");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;
   var latest=await EngineUpdater.Latest();log.Add("newest engine on Microsoft's page: "+(latest==null?"none":latest.Item1+" "+latest.Item2));
   RegressionCheck("engine feed lists x64 Fixed Version builds",delegate {Require(latest!=null&&latest.Item1.Major>=150&&latest.Item2.StartsWith("https://msedge.sf.dl.delivery.mp.microsoft.com/"),"Feed: "+latest);});
   string runtime=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Runtime","msedgewebview2.exe"),fake=Path.Combine(dir,"fake.exe");File.WriteAllText(fake,"MZ not signed");
   RegressionCheck("only Microsoft-signed engines are accepted",delegate {Require(EngineUpdater.IsMicrosoftSigned(runtime),"Bundled engine rejected");Require(!EngineUpdater.IsMicrosoftSigned(fake),"Unsigned file accepted");});
   string cab=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..",".deps","webview2-fixed-x64.cab")),engines=Path.Combine(Path.GetTempPath(),"mica-engine-test-"+Guid.NewGuid().ToString("N"));
   if(File.Exists(cab)) {
    log.Add("installing "+cab);var version=EngineUpdater.ParseVersion(BundledRuntime.BundledVersion(Path.GetDirectoryName(runtime)));
    string installed=await Task.Run(()=>EngineUpdater.InstallCab(cab,version,engines));
    RegressionCheck("a downloaded engine installs and is picked up",delegate {var list=EngineUpdater.Installed(engines);Require(list.Count==1&&list[0].Item1==version&&list[0].Item2==installed,"Installed: "+string.Join(", ",list.Select(i=>i.Item1+" "+i.Item2)));});
    File.Copy(fake,Path.Combine(dir,"fake.cab"));
    RegressionCheck("a tampered download is refused",delegate {try {EngineUpdater.InstallCab(Path.Combine(dir,"fake.cab"),new Version(999,0,0,0),engines);throw new Exception("Accepted");}catch(InvalidOperationException){}Require(!Directory.Exists(Path.Combine(engines,"999.0.0.0")),"Folder created");});
    try {Directory.Delete(engines,true);}catch(Exception e){log.Add("cleanup: "+e.Message);}
   }else log.Add("no local CAB to test install");
   RegressionCheck("local and intranet addresses keep http",delegate {
    foreach(var keep in new[]{"http://localhost/","http://192.168.0.1/","http://10.1.2.3/","http://router/","http://printer.local/","http://example.com:8080/","http://[::1]/"})Require(KeepsHttp(new Uri(keep)),keep+" would be upgraded");
    foreach(var upgrade in new[]{"http://example.com/","http://8.8.8.8/","http://news.ycombinator.com/item?id=1"})Require(!KeepsHttp(new Uri(upgrade)),upgrade+" would stay on http");
    Require(HttpsAddress(new Uri("http://example.com/a?b=1#c"))=="https://example.com/a?b=1#c","Upgrade keeps path and query");
    Require(SiteOf("abc.neverssl.com")=="neverssl.com"&&SiteOf("www.bbc.co.uk")=="bbc.co.uk"&&SiteOf("vnexpress.net")=="vnexpress.net"&&SiteOf("a.b.com.vn")=="b.com.vn","Site grouping");
   });
   Navigate("http://example.com/",false);await WaitFor(delegate {return CurrentView()!=null&&CurrentView().CoreWebView2!=null&&CurrentView().CoreWebView2.Source.StartsWith("https://");},25000).ContinueWith(x=>{});await WaitForPage();
   string source=CurrentView().CoreWebView2.Source;
   RegressionCheck("http links open over HTTPS",delegate {Require(source.StartsWith("https://example.com"),"Loaded "+source);});
   log.Add("engine in use "+environment.BrowserVersionString+" (chosen "+BundledRuntime.ChosenVersion+")");

   CurrentView().CoreWebView2.Navigate("http://neverssl.com/");
   bool offered=false;
   for(int i=0;i<120;i++){await Task.Delay(250);if(Find<Button>("InsecureButton").IsVisible){offered=true;break;}NavigationProgress p;if(CurrentView()!=null&&navigations.TryGetValue(state.ActiveId,out p)&&!p.IsLoading&&CurrentView().CoreWebView2.Source.StartsWith("http://neverssl.com"))break;}
   log.Add("neverssl: "+(offered?"offered insecure fallback":"fell back after redirect")+" · "+Find<TextBlock>("ErrorText").Text);
   if(offered){await Task.Delay(400);SaveWindowImage(Path.Combine(dir,"insecure-offer.png"));ContinueInsecure();await Task.Delay(500);await WaitForPage();}
   source=CurrentView().CoreWebView2.Source;
   RegressionCheck("sites without HTTPS still open after a warning",delegate {Require(source.StartsWith("http://")&&new Uri(source).Host.EndsWith("neverssl.com"),"Loaded "+source);});

   var core=CurrentView().CoreWebView2;
   RegressionCheck("tracker blocking is Balanced by default",delegate {Require(core.Profile.PreferredTrackingPreventionLevel==CoreWebView2TrackingPreventionLevel.Balanced,"Level "+core.Profile.PreferredTrackingPreventionLevel);});
   ToggleTrackerBlocking();
   RegressionCheck("tracker blocking can be made Strict",delegate {Require(state.StrictTracking&&core.Profile.PreferredTrackingPreventionLevel==CoreWebView2TrackingPreventionLevel.Strict,"Level "+core.Profile.PreferredTrackingPreventionLevel);});
   ToggleTrackerBlocking();

   Navigate("https://example.com/",false);await WaitForPage();core=CurrentView().CoreWebView2;
   await core.ExecuteScriptAsync("document.cookie='mica_probe=1; max-age=3600; path=/'");
   int before=(await core.CookieManager.GetCookiesAsync("https://example.com/")).Count;
   ShowClearBrowsingData();await Task.Delay(700);if(clearDataWindow!=null){SaveAnyWindow(clearDataWindow,Path.Combine(dir,"clear-data.png"));clearDataWindow.Close();}
   string result=await ClearBrowsingData(new List<string>{"history","cookies","cache"},DateTime.MinValue);
   int after=(await core.CookieManager.GetCookiesAsync("https://example.com/")).Count;
   RegressionCheck("clear browsing data removes cookies and history",delegate {Require(before>0&&after==0,"Cookies "+before+" -> "+after+" ("+result+")");Require(state.History.Count==0,"History left: "+state.History.Count);});

   string crashed=state.ActiveId;
   var crash=core.CallDevToolsProtocolMethodAsync("Page.crash","{}"); // never answers: the page is gone
   await WaitFor(delegate {return Find<Border>("ErrorPanel").IsVisible;},10000).ContinueWith(t=>{});
   string crashText=Find<TextBlock>("ErrorText").Text;
   RegressionCheck("a crashed page doesn't take the browser down",delegate {Require(Find<Border>("ErrorPanel").IsVisible&&crashText.Contains("crashed"),"Error: "+crashText);});
   Reload();await WaitForPage();

   uint engine=CurrentView().CoreWebView2.BrowserProcessId;
   try {using(var process=Process.GetProcessById((int)engine))process.Kill();}catch(Exception e){log.Add("kill: "+e.Message);}
   await Task.Delay(1500);
   try {await WaitForPage(30000);}catch(Exception){}
   uint restarted=CurrentView()!=null?CurrentView().CoreWebView2.BrowserProcessId:0;
   RegressionCheck("the engine restarts and the tab comes back",delegate {Require(restarted!=0&&restarted!=engine,"Engine "+engine+" -> "+restarted+", status: "+Find<TextBlock>("StatusText").Text);});
  }
 }
}
