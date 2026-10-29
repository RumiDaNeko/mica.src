using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace Mica {
 partial class BrowserApp {
  // Fixture pages for the ways sites enter fullscreen. Served locally, no network.
  static string FullscreenFixture(Uri uri) {
   string button="<button id=go style='position:fixed;left:0;top:0;width:140px;height:140px;z-index:9'>go</button>";
   if(uri.Host=="fs-b.test")return "<!doctype html><body style='margin:0;background:#246'>"+button+"<script>go.onclick=()=>document.body.requestFullscreen()</script>";
   if(uri.Host=="fs-c.test")return "<!doctype html><title>landed</title><body>next page</body>";
   switch(uri.AbsolutePath) {
    case "/div":return "<!doctype html><body>"+button+"<div id=box style='background:red;height:50px'>box</div><script>go.onclick=()=>box.requestFullscreen()</script>";
    case "/video":return "<!doctype html><body>"+button+"<video id=v width=320 height=180 style='background:#000'></video><script>go.onclick=()=>v.requestFullscreen()</script>";
    case "/webkit":return "<!doctype html><body>"+button+"<div id=box>box</div><script>go.onclick=()=>box.webkitRequestFullscreen()</script>";
    case "/iframe":return "<!doctype html><body style='margin:0'><iframe src='https://fs-b.test/' allow='fullscreen' style='border:0;width:400px;height:300px'></iframe>";
    case "/exam":return "<!doctype html><body>"+button+"<script>window.events=[];const t0=performance.now();const note=n=>events.push(n+' @'+Math.round(performance.now()-t0)+' '+innerWidth+'x'+innerHeight+' fs='+!!document.fullscreenElement+' focus='+document.hasFocus()+' vis='+document.visibilityState);['resize','blur','focus'].forEach(n=>addEventListener(n,()=>note(n)));['fullscreenchange','visibilitychange'].forEach(n=>document.addEventListener(n,()=>note(n)));go.onclick=()=>{note('click');if(location.search=='?plain')return;document.documentElement.requestFullscreen().then(()=>note('promise resolved'),e=>note('rejected '+e))}</script>";
    case "/navigate":return "<!doctype html><body>"+button+"<script>go.onclick=()=>{document.documentElement.requestFullscreen();setTimeout(()=>location.href='https://fs-c.test/',2500)}</script>";
   }
   return "<!doctype html><title>fixture</title>";
  }
  async Task<string> FullscreenState(CoreWebView2 core) {
   var rows=((Grid)Find<Border>("ContentFrame").Child).RowDefinitions;var view=CurrentView();
   string page=view==null?"no view":await view.CoreWebView2.ExecuteScriptAsync("JSON.stringify({fe:!!(document.fullscreenElement||document.webkitFullscreenElement),w:innerWidth,h:innerHeight})");
   return string.Format("mica {0}, toolbar {1}, view {2:0}x{3:0}, window {4:0}x{5:0}, page {6}",pageFullscreen,rows[0].Height.Value,view==null?0:view.ActualWidth,view==null?0:view.ActualHeight,window.ActualWidth,window.ActualHeight,page);
  }
  string examEvents;
  async Task RunFullscreenTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"fullscreen");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Width=1280;window.Height=840;window.Left=-6000;window.Top=-6000;FullscreenBounds.KeepOffscreen=true;
   RegressionCheck("search pages show the query in the address bar",delegate {
    Require(Navigation.AddressLabel("https://www.google.com/search?q=mica+browser&sca_esv=1")=="mica browser","google");
    Require(Navigation.AddressLabel("https://www.google.com.vn/search?q=th%E1%BB%9Di+ti%E1%BA%BFt")=="thời tiết","google.com.vn");
    Require(Navigation.AddressLabel("https://www.bing.com/search?q=a%2Bb")=="a+b","bing");Require(Navigation.AddressLabel("https://duckduckgo.com/?q=x&ia=web")=="x","ddg");
    Require(Navigation.AddressLabel("https://www.google.com/maps?q=hanoi")=="https://www.google.com/maps?q=hanoi","maps is not a search page");
    Require(Navigation.AddressLabel("https://evilgoogle.com/search?q=x")=="https://evilgoogle.com/search?q=x","lookalike host");
   });
   Navigate("https://fs-a.test/",false);await WaitFor(()=>CurrentView()!=null&&CurrentView().CoreWebView2!=null,20000);
   EventHandler<CoreWebView2WebResourceRequestedEventArgs> serve=delegate(object sender,CoreWebView2WebResourceRequestedEventArgs e) {
    var uri=new Uri(e.Request.Uri);if(!uri.Host.EndsWith(".test"))return;
    e.Response=environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(FullscreenFixture(uri))),200,"OK","Content-Type: text/html");
   };
   double screenWidth=SystemParameters.PrimaryScreenWidth;
   Func<Task<CoreWebView2>> fresh=async delegate {
    var core=CurrentView().CoreWebView2;core.AddWebResourceRequestedFilter("https://*.test/*",CoreWebView2WebResourceContext.All);core.WebResourceRequested-=serve;core.WebResourceRequested+=serve;return core;
   };
   var main=await fresh();
   foreach(var path in new[]{"/div","/video","/webkit","/iframe","/navigate"}) {
    var core=CurrentView().CoreWebView2;core.Navigate("https://fs-a.test"+path);await WaitForPage();await Task.Delay(300);
    foreach(var type in new[]{"mousePressed","mouseReleased"})await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent","{\"type\":\""+type+"\",\"x\":40,\"y\":40,\"button\":\"left\",\"clickCount\":1}");
    await Task.Delay(1500);
    string inside=await FullscreenState(core);log.Add(path+" entered: "+inside);
    if(path=="/navigate"){bool wasFull=pageFullscreen;await Task.Delay(2500);string after=await FullscreenState(CurrentView().CoreWebView2);log.Add(path+" after navigation: "+after);
     RegressionCheck("leaving the page while fullscreen restores Mica",delegate {Require(wasFull,"never entered");Require(!pageFullscreen&&((Grid)Find<Border>("ContentFrame").Child).RowDefinitions[0].Height.Value==44,after);});continue;}
    string entered=inside;
    RegressionCheck(path+" fullscreen fills the window",delegate {Require(pageFullscreen&&entered.Contains("toolbar 0")&&entered.Contains("fe\\\":true"),entered);Require(Math.Abs(CurrentView().ActualWidth-window.ActualWidth)<4&&CurrentView().ActualWidth>=screenWidth-4,entered);});
    await core.ExecuteScriptAsync("(document.exitFullscreen||document.webkitExitFullscreen).call(document)");await Task.Delay(1200);
    string left=await FullscreenState(core);log.Add(path+" exited: "+left);
    RegressionCheck(path+" exit restores Mica",delegate {Require(!pageFullscreen&&left.Contains("toolbar 44"),left);});
   }
   // What an exam page (which watches for leaving fullscreen) sees.
   var exam=CurrentView().CoreWebView2;exam.Navigate("https://fs-a.test/exam");await WaitForPage();await Task.Delay(500);CurrentView().Focus();await Task.Delay(300);await exam.ExecuteScriptAsync("window.events=[]");
   foreach(var type in new[]{"mousePressed","mouseReleased"})await exam.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent","{\"type\":\""+type+"\",\"x\":40,\"y\":40,\"button\":\"left\",\"clickCount\":1}");
   await Task.Delay(3000);
   string events=await exam.ExecuteScriptAsync("JSON.stringify(window.events)");log.Add("exam events: "+events);
   examEvents=events;
   RegressionCheck("an exam page sees fullscreen only at screen size, with no blur",delegate {
    var lines=events.Split(new[]{"\\\",\\\""},StringSplitOptions.None);var change=lines.FirstOrDefault(l=>l.Contains("fullscreenchange"));
    Require(change!=null&&change.Contains(" "+(int)screenWidth+"x"),"fullscreenchange at: "+change);Require(!events.Contains("blur"),"page saw blur: "+events);
    var resolved=lines.FirstOrDefault(l=>l.Contains("promise resolved"));Require(resolved!=null&&resolved.Contains(" "+(int)screenWidth+"x"),"promise resolved at: "+resolved);
   });
   await exam.ExecuteScriptAsync("document.exitFullscreen()");await Task.Delay(1200);
   // Fullscreen, then switch tab: Mica must come back.
   var first=state.ActiveId;var core2=CurrentView().CoreWebView2;core2.Navigate("https://fs-a.test/div");await WaitForPage();await Task.Delay(500);
   foreach(var type in new[]{"mousePressed","mouseReleased"})await core2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent","{\"type\":\""+type+"\",\"x\":40,\"y\":40,\"button\":\"left\",\"clickCount\":1}");
   await Task.Delay(1200);bool wasIn=pageFullscreen;var other=state.AddTab("mica://home");await Activate(other.Id);await Task.Delay(600);
   RegressionCheck("switching tabs leaves page fullscreen",delegate {Require(wasIn&&!pageFullscreen&&!IsFullscreen,"was "+wasIn+" now "+pageFullscreen+"/"+IsFullscreen);});
   // A player in a window.open pop-up.
   await Activate(first);var opener=CurrentView().CoreWebView2;opener.Navigate("https://fs-a.test/opener");await WaitForPage();await Task.Delay(300);
   await opener.ExecuteScriptAsync("var b=document.body.appendChild(document.createElement('button'));b.style='position:fixed;left:0;top:0;width:140px;height:140px';b.onclick=()=>window.open('about:blank','player','width=500,height=400')");
   foreach(var type in new[]{"mousePressed","mouseReleased"})await opener.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent","{\"type\":\""+type+"\",\"x\":40,\"y\":40,\"button\":\"left\",\"clickCount\":1}");
   await WaitFor(()=>popupWindows.Count==1,10000).ContinueWith(x=>{});
   if(popupWindows.Count==1) {
    var popup=popupWindows[0];Microsoft.Web.WebView2.Wpf.WebView2 popupView=null;
    await WaitFor(()=>(popupView=VaultElements<Microsoft.Web.WebView2.Wpf.WebView2>(popup).FirstOrDefault())!=null&&popupView.CoreWebView2!=null,10000);
    var pc=popupView.CoreWebView2;pc.AddWebResourceRequestedFilter("https://*.test/*",CoreWebView2WebResourceContext.All);pc.WebResourceRequested+=serve;
    pc.Navigate("https://fs-b.test/");await WaitFor(()=>pc.Source.StartsWith("https://fs-b.test"),10000);await Task.Delay(800);
    double before=popup.ActualWidth;
    foreach(var type in new[]{"mousePressed","mouseReleased"})await pc.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent","{\"type\":\""+type+"\",\"x\":40,\"y\":40,\"button\":\"left\",\"clickCount\":1}");
    await Task.Delay(1500);double during=popup.ActualWidth;
    await pc.ExecuteScriptAsync("document.exitFullscreen()");await Task.Delay(1200);double after=popup.ActualWidth;
    log.Add(string.Format("pop-up width: {0:0} -> fullscreen {1:0} -> {2:0}",before,during,after));
    RegressionCheck("fullscreen in a pop-up covers the monitor",delegate {Require(during>=screenWidth-4&&Math.Abs(after-before)<4,before+" / "+during+" / "+after);});
    popup.Close();
   }else log.Add("pop-up did not open");
   FullscreenBounds.KeepOffscreen=false;
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
