using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Mica {
 partial class BrowserApp {
  // The bundled uBlock Origin Lite: added on first start, blocking a well-known ad script, with the lists and sites chosen in Ad blocking.
  async Task RunBlockerTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"blocker");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;
   try {
    Navigate("https://example.com/",false);await WaitForPage();
    await WaitFor(()=>blockerId!=null&&networkBlockerId!=null,30000).ContinueWith(t=>{});
    var target=await EnsureExtensionProfile();var all=await target.GetBrowserExtensionsAsync();var added=all.FirstOrDefault(e=>e.Id==blockerId);
    log.Add("extensions: "+string.Join(", ",all.Select(e=>e.Name+(e.IsEnabled?"":" (off)"))));
    RegressionCheck("uBlock Origin Lite and Mica blocker are added on first start",delegate {Require(added!=null&&added.IsEnabled,"id "+blockerId);Require(all.Any(e=>e.Id==networkBlockerId&&e.IsEnabled),"no Mica blocker");});
    // Its rulesets register shortly after install; try a real ad <script> load for up to 20 s.
    string result="";
    for(int i=0;i<10&&result!="\"blocked\"";i++) {
     await Task.Delay(2000);Navigate("https://example.com/",false);await WaitForPage();
     string raw=await CurrentView().CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate","{\"awaitPromise\":true,\"returnByValue\":true,\"expression\":\"new Promise(r=>{const go=()=>{if(!document.documentElement||document.readyState==='loading')return setTimeout(go,100);const s=document.createElement('script');s.src='https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js';s.onload=()=>r('loaded');s.onerror=()=>r('blocked');(document.head||document.documentElement).appendChild(s);};go();})\"}");
     result=raw.Contains("\"blocked\"")?"\"blocked\"":raw;log.Add("try "+(i+1)+": "+raw);
    }
    log.Add("ad script: "+result);
    foreach(var probe in new[]{"https://www.google-analytics.com/analytics.js","https://securepubads.g.doubleclick.net/tag/js/gpt.js"}) {
     string outcome=await CurrentView().CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate","{\"awaitPromise\":true,\"returnByValue\":true,\"expression\":\"fetch('"+probe+"?x='+Math.random(),{mode:'no-cors'}).then(()=>'loaded',()=>'blocked')\"}");
     RegressionCheck("trackers are blocked: "+new Uri(probe).Host,delegate {Require(outcome.Contains("\"blocked\""),outcome);});
    }    RegressionCheck("an ad script is blocked",delegate {Require(result=="\"blocked\"",result);});
    // The site card counts what was blocked on the page, asked of Mica blocker without watching the traffic.
    {int blocked=-1;for(int i=0;i<6&&blocked<1;i++){blocked=await BlockedOnPage(CurrentView());if(blocked<1)await Task.Delay(500);}
     log.Add("blocked on this page: "+blocked);
     RegressionCheck("the page knows how many ads and trackers were blocked",delegate {Require(blocked>=2,"count "+blocked);});
     ShowSiteCard();await WaitFor(()=>siteCard!=null&&siteCard.IsOpen,3000).ContinueWith(t=>{});await Task.Delay(1500);
     var note=siteCard==null?null:LogicalDescendants<System.Windows.Controls.TextBlock>(siteCard.Child).FirstOrDefault(b=>(b.Tag as string)=="site-blocked");
     RegressionCheck("the site card shows the count",delegate {Require(note!=null&&note.Text.Contains("blocked on this page"),note==null?"no note":"\""+note.Text+"\"");});
     if(siteCard!=null)siteCard.IsOpen=false;
     Navigate("https://example.com/?fresh",false);await WaitForPage();await Task.Delay(500);int fresh=-1;for(int i=0;i<4&&fresh<0;i++){fresh=await BlockedOnPage(CurrentView());if(fresh<0)await Task.Delay(500);}log.Add("after a fresh load: "+fresh);
     RegressionCheck("a new page starts its own count",delegate {Require(fresh>=0&&fresh<blocked,"fresh "+fresh+", before "+blocked);});}
    RenderExtensions(all.ToList());
    RegressionCheck("the built-in blockers don't show in Extensions",delegate {Require(!displayedExtensions.Any(e=>e.Id==blockerId||e.Id==networkBlockerId),string.Join(", ",displayedExtensions.Select(e=>e.Name)));});
    // Excluding a site lets everything load there, in both blockers.
    var config=LoadBlockerConfig();config.Excluded.Add(SiteFor("https://www.Example.com/page"));
    log.Add("applied: "+await ApplyBlocker(config));
    Navigate("https://example.com/",false);await WaitForPage();await Task.Delay(1500);
    string allowed=await CurrentView().CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate","{\"awaitPromise\":true,\"returnByValue\":true,\"expression\":\"fetch('https://www.google-analytics.com/analytics.js?x='+Math.random(),{mode:'no-cors'}).then(()=>'loaded',()=>'blocked')\"}");
    RegressionCheck("an excluded site loads trackers",delegate {Require(allowed.Contains("\"loaded\""),allowed);});
    string modes=await RunInExtension(blockerId,"dashboard.html","chrome.runtime.sendMessage({what:'getFilteringModeDetails'}).then(d=>JSON.stringify(d.none))");CloseBlockerControl();
    RegressionCheck("uBlock Origin Lite doesn't filter the excluded site",delegate {Require(modes!=null&&modes.Contains("example.com"),modes);});
    config.Excluded.Clear();config.Lists.Remove("easyprivacy");await ApplyBlocker(config);
    RegressionCheck("lists and sites are saved",delegate {var saved=LoadBlockerConfig();Require(saved.Excluded.Count==0&&!saved.Lists.Contains("easyprivacy")&&saved.Lists.Contains("easylist"),File.ReadAllText(BlockerConfigPath));});
    config.Enabled=false;await ApplyBlocker(config);var off=await target.GetBrowserExtensionsAsync();
    RegressionCheck("turning ad blocking off turns both blockers off",delegate {Require(off.Where(e=>builtInIds.Contains(e.Id)).All(e=>!e.IsEnabled),string.Join(", ",off.Select(e=>e.Name+(e.IsEnabled?"":" (off)"))));});
    config.Enabled=true;config.Lists.Add("easyprivacy");await ApplyBlocker(config);
    // A removed copy comes back on the next start, keeping the chosen lists.
    if(added!=null){await added.RemoveAsync();blockerChecked=false;blockerId=null;await EnsureBuiltInBlocker();}
    var after=await target.GetBrowserExtensionsAsync();
    RegressionCheck("a removed blocker is added again",delegate {Require(blockerId!=null&&after.Any(e=>e.Id==blockerId&&e.IsEnabled),"id "+blockerId);});
    ShowAdBlocking();var dialog=adBlockingWindow;dialog.Left=-5000;dialog.Top=-5000;await Task.Delay(600);
    RegressionCheck("Ad blocking lists every list",delegate {int boxes=LogicalDescendants<System.Windows.Controls.CheckBox>(dialog).Count();Require(boxes==BlockerLists().Count,boxes+" of "+BlockerLists().Count);});
    CaptureElement((FrameworkElement)dialog.Content,"ad-blocking.png");dialog.Close();
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
