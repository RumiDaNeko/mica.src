using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Mica {
 // --self-test <result file>: drives the shell in its off-screen window with a throwaway profile and writes
 // one PASS/FAIL/SKIP line per check. The exit code is the number of failures.
 sealed partial class BrowserWindow {
  readonly List<string> results=new List<string>();
  int failures;
  void Check(string name,bool ok,string detail) {results.Add((ok?"PASS ":"FAIL ")+name+(ok||detail==null?"":": "+detail));if(!ok)failures++;}
  void Skip(string name,string why) {results.Add("SKIP "+name+": "+why);}

  // Runs an action that starts a load and waits until the tab stops loading.
  async Task<bool> Load(CrossTab tab,Action start,int seconds) {
   Trace("-- start load");
   var waiter=new TaskCompletionSource<bool>();tab.Waiter=waiter;start();
   var done=await Task.WhenAny(waiter.Task,Task.Delay(seconds*1000));
   if(done!=waiter.Task){tab.Waiter=null;return false;}
   await Task.Delay(150);return true;
  }
  Task<string> Js(CrossTab tab,string code) {return Js(tab.View,code);}
  async Task<string> Js(Xilium.CefGlue.Avalonia.AvaloniaCefBrowser view,string code) {
   try {return await view.EvaluateJavaScript<string>("return "+code,(string)null,"about:blank",1,TimeSpan.FromSeconds(10));}
   catch(Exception e) {return "error: "+e.GetType().Name+" "+e.Message;}
  }
  async Task Page(string name,CrossTab tab,Action start,string url,string title) {
   bool loaded=await Load(tab,start,20);
   string shown=await Js(tab,"document.title");
   Check(name,loaded&&tab.Entry.Url==url&&shown==title,"loaded="+loaded+" url="+tab.Entry.Url+" title="+shown);
  }

  async Task RunSelfTest(string resultFile) {
   trace=new System.Collections.Concurrent.ConcurrentQueue<string>();
   try {
    var first=Active;
    var ready=new TaskCompletionSource<bool>();
    if(first.View.IsBrowserInitialized)ready.TrySetResult(true);else first.View.BrowserInitialized+=delegate {ready.TrySetResult(true);};
    Check("engine starts",await Task.WhenAny(ready.Task,Task.Delay(30000))==ready.Task,"browser never initialized");
    Check("starts on a new tab",state.Tabs.Count==1&&first.Entry.Url=="mica://home"&&address.Text=="","tabs="+state.Tabs.Count+" url="+first.Entry.Url);

    await Page("games hub from the address bar",first,delegate {Navigate("mica://games");},"mica://games","Games");
    Check("address bar shows mica://games",address.Text=="mica://games",address.Text);
    await Page("link to a game",first,delegate {first.View.ExecuteJavaScript("document.querySelector('a[href=\"https://games.mica.invalid/snake\"]').click()","",1);},"mica://snake","Snake");
    await Page("games hub again",first,delegate {Navigate("mica://games");},"mica://games","Games");
    // Chromium skips history entries a script added without a user gesture, so the history used below is typed.
    await Page("snake from the address bar",first,delegate {Navigate("mica://snake");},"mica://snake","Snake");
    await Page("tetris host",first,delegate {Navigate("mica://tetris");},"mica://tetris","Tetris");
    Check("served as UTF-8 HTML",await Js(first,"document.contentType+' '+document.characterSet")=="text/html UTF-8",null);

    await Page("back",first,delegate {first.View.GoBack();},"mica://snake","Snake");
    await Page("back again",first,delegate {first.View.GoBack();},"mica://games","Games");
    Check("forward enabled after back",forward.IsEnabled,null);
    await Page("forward",first,delegate {first.View.GoForward();},"mica://snake","Snake");
    first.View.ExecuteJavaScript("window.micaMarker='kept'","",1);await Task.Delay(200);
    Check("marker set before reload",await Js(first,"String(window.micaMarker)")=="kept",null);
    await Page("reload",first,Reload,"mica://snake","Snake");
    Check("reload made a fresh page",await Js(first,"String(window.micaMarker)")=="undefined",null);

    bool missing=await Load(first,delegate {Navigate("https://games.mica.invalid/nope");},20);
    Check("unknown game is 404",missing&&first.LastStatus==404,"status="+first.LastStatus);

    await Page("game again",first,delegate {Navigate("mica://2048");},"mica://2048","2048");
    SetDark(true);await Task.Delay(300);
    Check("theme flips an open game",await Js(first,"document.documentElement.className")=="dark",null);
    await Page("reload while dark",first,Reload,"mica://2048","2048");
    Check("game arrives dark",await Js(first,"document.documentElement.className")=="dark",null);

    NewTab();var second=Active;
    Check("new tab",state.Tabs.Count==2&&second!=first&&address.Text=="","tabs="+state.Tabs.Count);
    bool web=await Load(second,delegate {Navigate("example.com");},25);
    string webTitle=web?await Js(second,"document.title"):"";
    if(web&&webTitle=="Example Domain")Check("web page",second.Entry.Url=="https://example.com/",second.Entry.Url);
    else Skip("web page","example.com didn't load (offline?) title="+webTitle);
    // Extensions run by Chromium itself (CEF Chrome style): the built-in blocker stops an ad script, and Claude for
    // Chrome runs in the AI panel, pointed at the tab in front.
    if(web) {
     string ad=await EvalAsync(second.View,"(async()=>{try{await fetch('https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js',{mode:'no-cors',cache:'no-store'});return 'loaded';}catch(e){return 'blocked';}})()",15);
     string plain=await EvalAsync(second.View,"(async()=>{try{await fetch('https://example.com/',{mode:'no-cors',cache:'no-store'});return 'loaded';}catch(e){return 'blocked';}})()",15);
     Check("ad blocker stops an ad script",ad=="blocked"&&plain=="loaded","ad="+ad+" plain="+plain);
    } else Skip("ad blocker","needs the network");
    if(Program.InstallErrors.Count>0)Check("Claude for Chrome installs",false,string.Join("; ",Program.InstallErrors));
    else if(!claudeLoaded)Skip("Claude for Chrome","not installed; run with --install-extension "+BrowserApp.ClaudeExtensionId);
    else if(!web)Skip("Claude for Chrome","needs a web page to bind to");
    else {
     ToggleAi();
     for(int i=0;i<100&&!(aiView.IsBrowserInitialized&&(aiView.Address??"").Contains("tabId="));i++)await Task.Delay(200);
     await Task.Delay(1500);
     Check("Claude panel binds to the tab in front",(aiView.Address??"").StartsWith(Claude.PanelUrl+"?tabId=")&&aiBoundTab==second.Entry.Id,aiView.Address);
     Check("Claude panel runs as the extension",await Js(aiView,"chrome.runtime.id")==BrowserApp.ClaudeExtensionId,null);
     Check("panel shim: no tab of its own",await EvalAsync(aiView,"(async()=>String(await chrome.tabs.getCurrent()))()",15)=="undefined",null);
     string tabs=await EvalAsync(aiView,"(async()=>(await chrome.tabs.query({})).map(t=>t.url).join(' '))()",15);
     Check("Claude sees Mica's tabs, not its panel",tabs.Contains("https://example.com/")&&!tabs.Contains("chrome-extension://"),tabs);
     // How Claude reads and acts on a page: the debugger API on the tab, here reading the page's title through it.
     string driven=await EvalAsync(aiView,"(async()=>{const t=(await chrome.tabs.query({})).find(x=>x.url==='https://example.com/');const d={tabId:t.id};await chrome.debugger.attach(d,'1.3');try{const r=await chrome.debugger.sendCommand(d,'Runtime.evaluate',{expression:'document.title',returnByValue:true});return r.result.value;}finally{await chrome.debugger.detach(d);}})()",15);
     Check("Claude's debugger reaches the page",driven=="Example Domain",driven);
     ToggleAi();
    }
    Show(first.Entry.Id);
    Check("switching tabs swaps the address",address.Text=="mica://2048"&&first.View.IsVisible&&!second.View.IsVisible,address.Text);
    // Files are allowed since Core.cs learned local files; Chrome's own pages and other schemes are not.
    foreach(var scheme in new[]{"chrome://settings","ftp://example.com/"}) {
     notice.IsVisible=false;string rejected=null;Navigate(scheme);if(notice.IsVisible)rejected=noticeText.Text;
     Check("refuses "+scheme,rejected!=null&&first.Entry.Url=="mica://2048",rejected+" url="+first.Entry.Url);
    }

    try {
     var shot=new RenderTargetBitmap(new PixelSize((int)Bounds.Width,(int)Bounds.Height),new Vector(96,96));shot.Render(this);
     shot.Save(Path.ChangeExtension(resultFile,".png"));
    } catch(Exception e) {Skip("chrome screenshot",e.Message);}

    CloseTab(second.Entry.Id);
    Check("close tab",state.Tabs.Count==1&&Active==first,"tabs="+state.Tabs.Count);
    var saved=StateStore.Load(statePath);
    Check("session saved through Core.cs",saved.Tabs.Count==1&&saved.Tabs[0].Url=="mica://2048"&&saved.Dark&&saved.RecentlyClosed.Count==(web?1:0),"tabs="+saved.Tabs.Count+" dark="+saved.Dark+" closed="+saved.RecentlyClosed.Count);
    Check("dates keep the Windows format",File.ReadAllText(statePath).Contains("\"\\/Date("),null);
   } catch(Exception e) {Check("self-test ran",false,e.ToString());}
   results.Add(failures==0?"ALL PASSED":failures+" FAILED");
   if(failures>0){results.Add("--- engine events");results.AddRange(trace);}
   File.WriteAllLines(resultFile,results);
   Program.ExitCode=failures;
   Close();
  }
 }
}
