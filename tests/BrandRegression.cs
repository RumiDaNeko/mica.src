using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Mica {
 partial class BrowserApp {
  // Sites see Micarium, not Edge: in the user agent, the brands a page reads, and the requests it makes.
  async Task RunUserAgentTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"agent");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;
   try {
    RegressionCheck("the Edge token is taken out of a user agent",delegate {string plain=PlainUserAgent("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36 Edg/156.0.4314.42");Require(plain=="Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/156.0.0.0 Safari/537.36",plain);});
    Navigate("https://example.com/",false);await WaitForPage();
    var core=CurrentView().CoreWebView2;string typed=null,clicked=null,fetched=null;
    // The headers as sent on the wire.
    await core.CallDevToolsProtocolMethodAsync("Network.enable","{}");
    core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSentExtraInfo").DevToolsProtocolEventReceived+=delegate(object sender,CoreWebView2DevToolsProtocolEventReceivedEventArgs e) {
     string sent=e.ParameterObjectAsJson;if(sent.Contains("\"/?typed\""))typed=sent;else if(sent.Contains("\"/?clicked\""))clicked=sent;else if(sent.Contains("\"/?sub\""))fetched=sent;
    };
    Navigate("https://example.com/?typed",false);await WaitForPage();await Task.Delay(500);
    string seen=await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate","{\"awaitPromise\":true,\"returnByValue\":true,\"expression\":\"navigator.userAgentData.getHighEntropyValues(['fullVersionList']).then(v=>navigator.userAgent+' || '+JSON.stringify(navigator.userAgentData.brands)+' || '+JSON.stringify(v.fullVersionList))\"}");
    await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate","{\"awaitPromise\":true,\"expression\":\"fetch('/?sub',{cache:'no-store'}).then(r=>r.status)\"}");await WaitFor(()=>fetched!=null,5000).ContinueWith(t=>{});
    await core.ExecuteScriptAsync("location.href='/?clicked'");await WaitFor(()=>clicked!=null,8000).ContinueWith(t=>{});await WaitForPage();
    Func<string,string> hints=delegate(string sent) {if(sent==null)return "none";int at=sent.IndexOf("\"sec-ch-ua\":");return at<0?"no sec-ch-ua":sent.Substring(at,Math.Min(140,sent.Length-at));};
    log.Add("page: "+seen);log.Add("typed address: "+hints(typed));log.Add("link: "+hints(clicked));log.Add("fetch: "+hints(fetched));
    RegressionCheck("the page sees Micarium, not Edge, in its user agent and brands",delegate {Require(!seen.Contains("Edg")&&seen.Contains("Micarium/"+AppVersion)&&System.Text.RegularExpressions.Regex.Matches(seen,"Micarium").Count>=3,seen);});
    RegressionCheck("a page's own requests send Micarium, not Edge",delegate {Require(fetched!=null&&!fetched.Contains("Edg")&&fetched.Contains("Micarium"),hints(fetched));});
    RegressionCheck("links send Micarium, not Edge, in their client hints",delegate {Require(clicked!=null&&!clicked.Contains("Edg")&&clicked.Contains("Micarium"),hints(clicked));});
    // Known gap: a load Mica itself starts (typed address, reload) still carries the engine's own sec-ch-ua.
    RegressionCheck("page loads send the Micarium user agent",delegate {Require(typed!=null&&typed.Contains("Micarium/"+AppVersion)&&!typed.Contains("Edg/"),typed??"none");});
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
