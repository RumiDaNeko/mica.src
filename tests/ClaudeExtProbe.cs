using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace Mica {
 partial class BrowserApp {
  // With Claude chosen and Claude for Chrome installed, the AI panel shows the extension's own panel for the tab
  // in front. Needs the store package (MICA_CLAUDE_CRX); skipped without it.
  async Task RunClaudeExtProbe() {
   var log=regressionLog;string dir=Path.Combine(profile,"claudeext");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;
   var flush=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(2)};flush.Tick+=delegate {try {File.WriteAllLines(Path.Combine(dir,"result.txt"),log.Concat(new[]{"(still running)"}));}catch(Exception){}};flush.Start();
   try {
    // The store package, or an unpacked copy of it (MICA_CLAUDE_DIR, e.g. copied from another profile).
    string crx=Environment.GetEnvironmentVariable("MICA_CLAUDE_CRX"),unpacked=Environment.GetEnvironmentVariable("MICA_CLAUDE_DIR");
    bool haveCrx=!string.IsNullOrEmpty(crx)&&File.Exists(crx),haveDir=!string.IsNullOrEmpty(unpacked)&&File.Exists(Path.Combine(unpacked??"","manifest.json"));
    if(!haveCrx&&!haveDir){log.Add("no Claude for Chrome package; skipped");await Task.Delay(1500);}
    else {
     PreparedExtension prepared;
     if(haveCrx)prepared=ExtensionPackage.PrepareBytes(File.ReadAllBytes(crx),ClaudeExtensionId,Path.Combine(profile,"StoreExtensions"));
     else {
      string copy=Path.Combine(profile,"StoreExtensions","package-"+Guid.NewGuid().ToString("N"));
      foreach(var file in Directory.GetFiles(unpacked,"*",SearchOption.AllDirectories)){string to=Path.Combine(copy,file.Substring(unpacked.TrimEnd('\\','/').Length+1));Directory.CreateDirectory(Path.GetDirectoryName(to));File.Copy(file,to);}
      prepared=new PreparedExtension {Folder=copy,Version="unpacked"};
     }
     var installed=await (await EnsureExtensionProfile()).AddBrowserExtensionAsync(prepared.Folder);
     RegressionCheck("the store package keeps its store id",delegate {Require(installed.Id==ClaudeExtensionId,installed.Id);});
     Navigate("https://example.com/",false);await WaitFor(()=>CurrentView()!=null&&CurrentView().CoreWebView2!=null,20000);await WaitForPage().ContinueWith(t=>{});
     // Claude Code drives the extension from its worker, with no panel open and often with Mica in the background.
     LoadExtensionRecords();RememberExtension(ClaudeExtensionId,prepared.Folder,"Chrome Web Store",prepared.Version);
     if(aiExtensionsRepaired!=null)await aiExtensionsRepaired;
     aiPatchChecked.Remove(ClaudeExtensionId);await ClaudeExtensionInstalled();await Task.Delay(3000);
     await RunClaudeWorkerProbe(log);
     SetAiEnabled(true);ChooseAiProvider("claude",null);SetAiOpen(true);
     Func<string> panel=()=>{var core=aiView==null?null:LiveCore(aiView);return core==null?"":core.Source;};
     // Installing opens the extension's sign-in page as a tab; the panel follows whichever tab is in front.
     await WaitFor(()=>panel().Contains("tabId=")&&(claudeBoundTab==state.ActiveId||claudeOpenedTabs.Contains(state.ActiveId??""))&&!claudeBinding,25000).ContinueWith(t=>{});await Task.Delay(1500);
     await WaitFor(()=>panel().Contains("tabId=")&&(claudeBoundTab==state.ActiveId||claudeOpenedTabs.Contains(state.ActiveId??""))&&!claudeBinding,10000).ContinueWith(t=>{});
     string first=panel();log.Add("panel: "+first+" for "+(Active==null?"":Active.Url));
     // The sign-in tab the extension opened is Claude's own, so the panel may stay with the page it was opened on.
     RegressionCheck("the AI panel shows Claude for Chrome for the tab in front",delegate {Require(first.StartsWith(ClaudePanelPage+"?tabId=",StringComparison.Ordinal)&&(claudeBoundTab==state.ActiveId||claudeOpenedTabs.Contains(state.ActiveId)),first);});
     // The extension only runs in a side panel, where it has no tab of its own.
     string current=await LiveCore(aiView).CallDevToolsProtocolMethodAsync("Runtime.evaluate","{\"expression\":\"chrome.tabs.getCurrent().then(t=>t===undefined?'side panel':'tab '+t.id)\",\"awaitPromise\":true,\"returnByValue\":true}");
     log.Add("panel context: "+current);
     RegressionCheck("the panel counts as a side panel, not a tab",delegate {Require(current.Contains("side panel"),current);});
     log.Add("before apis: "+panel()+" bound "+claudeBoundTab+" active "+state.ActiveId+" binding "+claudeBinding);
     string apis="";
     try {await LiveCore(aiView).ExecuteScriptAsync("window.__probeDone=false;"+ClaudeToolApiProbe+";0");
      for(int i=0;i<160;i++){await Task.Delay(250);var core=LiveCore(aiView);if(core==null)break;string done=await core.ExecuteScriptAsync("window.__probeDone===true?window.__probe:null");if(done!="null"){apis="{\"result\":{\"value\":"+done+"}}";break;}if(i%8==0)log.Add("probe so far: "+await core.ExecuteScriptAsync("window.__probe||null")+" panel "+panel());}
     }catch(Exception e){log.Add("apis failed: "+e.Message+"; panel now "+panel()+" bound "+claudeBoundTab+" active "+state.ActiveId);throw;}
     log.Add("tool apis: "+apis);
     // Claude's tools group the tabs they work in, look for a normal window, and open tabs of their own.
     var found=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,string>>(new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,System.Collections.Generic.Dictionary<string,object>>>(apis)["result"]["value"] as string);
     Func<string,string> api=k=>found.ContainsKey(k)?found[k]:"missing";
     RegressionCheck("the extension can group the tab it works in",delegate {Require(!api("tabs.group").StartsWith("ERR")&&api("tabGroups.update")=="\"Claude\""&&api("tabGroups.get")=="\"Claude\""&&api("tabs.query group")=="true"&&api("tabs.get groupId")=="true"&&api("tabs.ungroup")=="-1",api("tabs.group")+" "+api("tabGroups.get")+" "+api("tabs.query group")+" "+api("tabs.ungroup"));});
     RegressionCheck("the extension finds a normal window and not the panel",delegate {Require(api("windows.normal")=="\"normal\""&&api("panel hidden")=="true",api("windows.normal")+" "+api("panel hidden"));});
     RegressionCheck("a tab the extension opens stays usable",delegate {Require(api("tabs.create").Contains("example.net"),api("tabs.create"));});
     RegressionCheck("the extension can control the page",delegate {Require(!api("debugger.attach").StartsWith("ERR")&&api("debugger.Page.captureScreenshot")!="missing"&&!api("debugger.Page.captureScreenshot").StartsWith("ERR")&&!api("debugger.Input.dispatchMouseEvent").StartsWith("ERR"),api("debugger.attach")+" "+api("debugger.Page.captureScreenshot"));});
     // claude.ai's page inside the panel must count as the side panel when it asks the extension (only a side panel
     // is offered open_options), as an extension installed before Mica patched it must once fixed in place.
     log.Add("asking from a claude.ai frame");string hostInfo=await AskFromClaudeFrame();log.Add("host info from the panel: "+hostInfo);
     RegressionCheck("claude.ai inside the panel reaches the extension as its side panel",delegate {Require(hostInfo.Contains("open_options"),hostInfo);});
     LoadExtensionRecords();RememberExtension(ClaudeExtensionId,prepared.Folder,"Chrome Web Store",prepared.Version);
     string worker=Path.Combine(prepared.Folder,"service-worker-loader.js");File.WriteAllLines(worker,File.ReadAllLines(worker).Where(l=>!l.Contains("mica-sidepanel")));File.Delete(Path.Combine(prepared.Folder,"mica-sidepanel.js"));
     var claude=(await (await EnsureExtensionProfile()).GetBrowserExtensionsAsync()).First(e=>e.Id==ClaudeExtensionId);await claude.EnableAsync(false);await claude.EnableAsync(true);await ReopenClaudePanel();
     string unpatched=await AskFromClaudeFrame();log.Add("unpatched: "+unpatched);
     aiPatchChecked.Remove(ClaudeExtensionId);await ClaudeExtensionInstalled();await WaitFor(()=>aiView!=null&&LiveCore(aiView)!=null&&LiveCore(aiView).Source.Contains("tabId="),25000).ContinueWith(t=>{});
     string repaired=await AskFromClaudeFrame();log.Add("after repair: "+repaired);
     RegressionCheck("an extension installed before the fix is repaired in place",delegate {Require(!unpatched.Contains("open_options")&&repaired.Contains("open_options"),unpatched+" / "+repaired);});
     // claude.ai's panel page talks to the extension through chrome.runtime (externally_connectable).
     var site=CurrentView().CoreWebView2;site.Navigate("https://claude.ai/");await Task.Delay(8000);
     string reach=await site.CallDevToolsProtocolMethodAsync("Runtime.evaluate","{\"expression\":\"(async()=>{const r=self.chrome&&chrome.runtime;const o={ua:navigator.userAgent,runtime:typeof r,sendMessage:typeof (r&&r.sendMessage),connect:typeof (r&&r.connect),url:location.href};try{o.reply=await Promise.race([new Promise((ok,no)=>{try{chrome.runtime.sendMessage('"+ClaudeExtensionId+"',{type:'ping'},x=>ok(JSON.stringify(x)+' '+(chrome.runtime.lastError?chrome.runtime.lastError.message:'')))}catch(e){no(e)}}),new Promise(ok=>setTimeout(()=>ok('timeout'),4000))])}catch(e){o.error=String(e)}return JSON.stringify(o)})()\",\"awaitPromise\":true,\"returnByValue\":true}");
     log.Add("claude.ai reach: "+reach);
     RegressionCheck("the panel names the extension",delegate {Require(aiSite.Text=="Claude for Chrome",aiSite.Text);});
     Navigate("https://example.org/",true);await WaitFor(()=>panel()!=first&&panel().Contains("tabId="),20000).ContinueWith(t=>{});
     string second=panel();log.Add("after switching tabs: "+second);
     RegressionCheck("switching tabs points the panel at the new tab",delegate {Require(second!=first&&second.StartsWith(ClaudePanelPage+"?tabId=",StringComparison.Ordinal),second);});
     // What Claude reads as "this page": the bound tab's address, after going to another site in the same tab,
     // and after the tab's page is made again (a rested tab coming back).
     Func<Task<string>> panelSees=async()=>{var core=LiveCore(aiView);if(core==null)return "no panel";string r=await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate","{\"expression\":\"(async()=>{try{const t=await chrome.tabs.get(Number(new URL(location.href).searchParams.get('tabId')));return t.url;}catch(e){return 'ERR '+e.message;}})()\",\"awaitPromise\":true,\"returnByValue\":true}");var m=System.Text.RegularExpressions.Regex.Match(r,"\"value\":\"([^\"]*)\"");return m.Success?m.Groups[1].Value:r;};
     CurrentView().CoreWebView2.Navigate("https://www.iana.org/help/example-domains");await Task.Delay(6000);
     string moved=await panelSees();log.Add("after another site in the same tab: panel "+panel()+" sees "+moved+", page "+LiveCore(CurrentView()).Source);
     RegressionCheck("after going to another site, the panel sees the new address",delegate {Require(moved.Contains("iana.org"),moved);});
     string restedId=state.ActiveId;DisposeView(restedId);SelectTab(restedId);await WaitFor(()=>CurrentView()!=null&&LiveCore(CurrentView())!=null&&Navigation.IsWeb(LiveCore(CurrentView()).Source),20000).ContinueWith(t=>{});await Task.Delay(5000);
     string remade=await panelSees();log.Add("after the tab's page is made again: panel "+panel()+" sees "+remade+", page "+(CurrentView()==null?"":LiveCore(CurrentView()).Source));
     RegressionCheck("after a rested tab comes back, the panel follows its new page",delegate {Require(remade.Contains("iana.org"),remade);});
     ShowExtensions();await Task.Delay(2500);bool listed=displayedExtensions.Any(e=>e.Id==ClaudeExtensionId);if(activeExtensionsWindow!=null)activeExtensionsWindow.Close();
     RegressionCheck("the extensions page leaves the AI extension out",delegate {Require(!listed,"listed");});
     ShowAiSettings();await Task.Delay(1500);
     var note=aiSettingsWindow==null?null:LogicalDescendants<TextBlock>(aiSettingsWindow.Content as DependencyObject).FirstOrDefault(b=>(b.Tag as string)=="ai-claude-extension");
     RegressionCheck("settings say Claude uses the extension",delegate {Require(note!=null&&note.Text.Contains("Claude for Chrome extension"),note==null?"no note":note.Text);});
     if(aiSettingsWindow!=null)aiSettingsWindow.Close();
     // Signing in returns the tab to the extension's address; Mica must not block it.
     var tab=CurrentView().CoreWebView2;bool cancelled=false;
     tab.NavigationStarting+=delegate(object s,CoreWebView2NavigationStartingEventArgs e) {if(IsClaudeSignInReturn(e.Uri))window.Dispatcher.BeginInvoke(new Action(delegate {cancelled=e.Cancel;}));};
     tab.Navigate("chrome-extension://"+ClaudeExtensionId+"/oauth_callback.html?code=test&state=test");await Task.Delay(1500);
     string status=Find<TextBlock>("StatusText").Text;
     RegressionCheck("the sign-in return isn't blocked",delegate {Require(!cancelled&&status!="Blocked a non-web address.","status "+status);});
     var shot=await CapturePage(LiveCore(aiView),false);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shot));
     using(var output=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","claude-extension-panel.png")))encoder.Save(output);
    }
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
  // What the extension's browser tools call, tried from inside the extension against the tab the panel is bound to.
  const string ClaudeToolApiProbe=@"(async()=>{const out={};const tabId=Number(new URL(location.href).searchParams.get('tabId'));
const run=async(name,fn,ms=5000)=>{try{const v=await Promise.race([fn(),new Promise((_,no)=>setTimeout(()=>no(new Error('timeout')),ms))]);out[name]=v===undefined?'ok':JSON.stringify(v).slice(0,160);}catch(e){out[name]='ERR '+(e&&e.message||e);}window.__probe=JSON.stringify(out);};
await run('tabs.get',async()=>{const t=await chrome.tabs.get(tabId);return {id:t.id,win:t.windowId,url:t.url,active:t.active,group:t.groupId};});
await run('tabs.query active',async()=>(await chrome.tabs.query({active:true,currentWindow:true})).map(t=>t.id));
await run('tabs.query all',async()=>(await chrome.tabs.query({})).map(t=>t.id+':'+t.windowId+':'+(t.url||'').slice(0,30)));
await run('windows.getAll',async()=>(await chrome.windows.getAll()).map(w=>w.id+':'+w.type+':'+w.focused));
await run('windows.getLastFocused',async()=>{const w=await chrome.windows.getLastFocused();return w.id;});
await run('tabGroups.query',async()=>(await chrome.tabGroups.query({})).length);
let group;await run('tabs.group',async()=>{group=await chrome.tabs.group({tabIds:[tabId]});return group;});
await run('tabGroups.update',async()=>{if(group===undefined)throw new Error('no group');const g=await chrome.tabGroups.update(group,{title:'Claude',color:'orange'});return g.title;});
await run('tabGroups.get',async()=>(await chrome.tabGroups.get(group)).title);
await run('tabs.query group',async()=>(await chrome.tabs.query({groupId:group})).map(t=>t.id).join()===String(tabId));
await run('tabs.get groupId',async()=>(await chrome.tabs.get(tabId)).groupId===group);
await run('windows.normal',async()=>{const w=await chrome.windows.getLastFocused({windowTypes:['normal']});return w.type;});
await run('panel hidden',async()=>(await chrome.tabs.query({})).every(t=>!t.url.includes('sidepanel.html')));
await run('tabs.ungroup',async()=>{await chrome.tabs.ungroup([tabId]);return (await chrome.tabs.get(tabId)).groupId;});
await run('tabs.create',async()=>{const t=await chrome.tabs.create({url:'https://example.net/'});await new Promise(r=>setTimeout(r,1500));const again=await chrome.tabs.get(t.id);return again.url;},20000);
await run('debugger.attach',async()=>{await chrome.debugger.attach({tabId},'1.3');});
await run('debugger.Runtime.evaluate',async()=>(await chrome.debugger.sendCommand({tabId},'Runtime.evaluate',{expression:'document.title',returnByValue:true})).result.value);
await run('debugger.Page.captureScreenshot',async()=>(await chrome.debugger.sendCommand({tabId},'Page.captureScreenshot',{format:'png'})).data.length);
await run('debugger.Input.dispatchMouseEvent',async()=>{await chrome.debugger.sendCommand({tabId},'Input.dispatchMouseEvent',{type:'mouseMoved',x:5,y:5});});
await run('debugger.detach',async()=>{await chrome.debugger.detach({tabId});});
await run('scripting.executeScript',async()=>(await chrome.scripting.executeScript({target:{tabId},func:()=>location.href}))[0].result);
await run('tabs.captureVisibleTab',async()=>(await chrome.tabs.captureVisibleTab()).length);
await run('tabs.sendMessage',async()=>await chrome.tabs.sendMessage(tabId,{type:'mica-ping'}));
await run('webNavigation.getAllFrames',async()=>(await chrome.webNavigation.getAllFrames({tabId})).length);
await run('offscreen',async()=>typeof chrome.offscreen.createDocument);
await run('connectNative',async()=>await new Promise(ok=>{const port=chrome.runtime.connectNative('com.microsoft.browsercore');let done=false;port.onDisconnect.addListener(()=>{if(!done){done=true;ok('disconnected: '+(chrome.runtime.lastError?chrome.runtime.lastError.message:'no error'));}});setTimeout(()=>{if(!done){done=true;try{port.disconnect();}catch(e){}ok('connected');}},2500);}));
window.__probeDone=true;return JSON.stringify(out);})()";
  async Task ReopenClaudePanel() {
   DisposeAiView();SetAiOpen(false);await Task.Delay(500);SetAiOpen(true);
   await WaitFor(()=>aiView!=null&&LiveCore(aiView)!=null&&LiveCore(aiView).Source.Contains("tabId="),25000).ContinueWith(t=>{});
  }
  // Opens claude.ai in a frame of the panel page and asks the extension what claude.ai's panel page asks.
  async Task<string> AskFromClaudeFrame() {
   var panelCore=LiveCore(aiView);CoreWebView2Frame frame=null;
   EventHandler<CoreWebView2FrameCreatedEventArgs> created=delegate(object s,CoreWebView2FrameCreatedEventArgs e) {frame=e.Frame;};
   panelCore.FrameCreated+=created;
   await panelCore.ExecuteScriptAsync("(()=>{const f=document.createElement('iframe');f.src='https://claude.ai/cic/new?surface=cic_sidepanel';document.body.appendChild(f);})()");
   await WaitFor(()=>frame!=null,10000).ContinueWith(t=>{});panelCore.FrameCreated-=created;
   if(frame==null)return "no frame";regressionLog.Add("frame created");
   await Task.Delay(7000);
   await frame.ExecuteScriptAsync("window.__mica='waiting';try{chrome.runtime.sendMessage('"+ClaudeExtensionId+"',{type:'get_sidepanel_host_info'},r=>{window.__mica=JSON.stringify(r===undefined?(chrome.runtime.lastError||{}).message||'no reply':r)})}catch(e){window.__mica='error '+e+' at '+location.href}");
   regressionLog.Add("frame at "+await frame.ExecuteScriptAsync("location.href+' runtime:'+typeof (self.chrome&&chrome.runtime)"));
   string answer="";for(int i=0;i<40;i++){await Task.Delay(250);answer=await frame.ExecuteScriptAsync("window.__mica");if(answer!="\"waiting\"")break;}
   return answer;
  }
  // What Claude Code's tools do through the extension's worker: find or make a window for the session's group,
  // group a tab in it, open another, navigate, screenshot and upload a file.
  async Task RunClaudeWorkerProbe(System.Collections.Generic.List<string> log) {
   var ser=new System.Web.Script.Serialization.JavaScriptSerializer();
   string upload=Path.Combine(profile,"claude-upload.txt");File.WriteAllText(upload,"hello from Mica");
   string script=ClaudeWorkerProbe.Replace("__UPLOAD__",ser.Serialize(upload));
   string answer=await EvaluateInExtensionWorker(ClaudeExtensionId,script,90000);log.Add("worker apis: "+answer);
   System.Collections.Generic.Dictionary<string,string> found;
   try {found=ser.Deserialize<System.Collections.Generic.Dictionary<string,string>>(answer);}catch(Exception){found=new System.Collections.Generic.Dictionary<string,string>();}
   Func<string,string> api=k=>found.ContainsKey(k)?found[k]:"missing";Func<string,bool> ok=k=>api(k)!="missing"&&!api(k).StartsWith("ERR",StringComparison.Ordinal);
   RegressionCheck("the worker finds or makes a normal window for a session",delegate {Require(ok("windows.getLastFocused normal")||ok("windows.create")&&api("windows.create").Contains("\"tab\":"),api("windows.getLastFocused normal")+" / "+api("windows.create"));});
   RegressionCheck("the worker groups a tab, names and colors the group",delegate {Require(ok("tabs.group")&&api("tabGroups.get")=="\"Claude session|blue\""&&api("tabs.query group")=="true",api("tabs.group")+" "+api("tabGroups.get")+" "+api("tabs.query group"));});
   RegressionCheck("a tab opened into the group's window joins it",delegate {Require(ok("tabs.create in window")&&api("tabs.group second")=="2",api("tabs.create in window")+" "+api("tabs.group second"));});
   RegressionCheck("the worker navigates its tab",delegate {Require(api("tabs.update navigate").Contains("example.com"),api("tabs.update navigate"));});
   RegressionCheck("the worker screenshots its tab",delegate {Require(ok("debugger.attach")&&ok("screenshot")&&api("screenshot")!="0",api("debugger.attach")+" "+api("screenshot"));});
   RegressionCheck("the worker uploads a file into the page",delegate {Require(api("setFileInputFiles")=="\"claude-upload.txt\"",api("setFileInputFiles"));});
   RegressionCheck("the worker closes its tab, in Mica too",delegate {Require(api("tabs.remove")=="\"closed\""&&api("group after remove")=="1"&&state.Tabs.Count(t=>t.Url.Contains("google.com"))==0,api("tabs.remove")+" "+api("group after remove")+" "+string.Join(",",state.Tabs.Select(t=>t.Url)));});
   await Task.Delay(1500);
   RegressionCheck("Mica shows the session's group",delegate {Require(state.Tabs.Any(t=>state.FindGroup(t.Group)!=null&&state.FindGroup(t.Group).Name=="Claude session"&&state.FindGroup(t.Group).Color=="#4A7FD9"),string.Join(",",state.Tabs.Select(t=>t.Url+"="+t.Group)));});
  }
  // Runs a script in an extension's service worker over DevTools and returns its string result.
  async Task<string> EvaluateInExtensionWorker(string id,string expression,int timeout) {
   var core=LiveCore(CurrentView());var ser=new System.Web.Script.Serialization.JavaScriptSerializer();
   string target=null;
   for(int i=0;i<40&&target==null;i++) {
    var all=ser.Deserialize<System.Collections.Generic.Dictionary<string,object>>(await core.CallDevToolsProtocolMethodAsync("Target.getTargets","{}"));
    foreach(System.Collections.Generic.Dictionary<string,object> info in (System.Collections.ArrayList)all["targetInfos"])
     if((string)info["type"]=="service_worker"&&((string)info["url"]).StartsWith("chrome-extension://"+id+"/",StringComparison.Ordinal))target=(string)info["targetId"];
    if(target==null)await Task.Delay(250);
   }
   if(target==null)return "{\"error\":\"no worker\"}";
   var attached=ser.Deserialize<System.Collections.Generic.Dictionary<string,object>>(await core.CallDevToolsProtocolMethodAsync("Target.attachToTarget",ser.Serialize(new {targetId=target,flatten=true})));
   string session=(string)attached["sessionId"];
   var call=core.CallDevToolsProtocolMethodForSessionAsync(session,"Runtime.evaluate",ser.Serialize(new {expression,awaitPromise=true,returnByValue=true}));
   if(await Task.WhenAny(call,Task.Delay(timeout))!=call)return "{\"error\":\"timeout\"}";
   var result=ser.Deserialize<System.Collections.Generic.Dictionary<string,object>>(await call);
   try {await core.CallDevToolsProtocolMethodAsync("Target.detachFromTarget",ser.Serialize(new {sessionId=session}));}catch(Exception){}
   var value=((System.Collections.Generic.Dictionary<string,object>)result["result"]);object text;
   return value.TryGetValue("value",out text)&&text is string?(string)text:ser.Serialize(result);
  }
  const string ClaudeWorkerProbe=@"(async()=>{const out={};
const run=async(name,fn,ms=15000)=>{try{const v=await Promise.race([fn(),new Promise((_,no)=>setTimeout(()=>no(new Error('timeout')),ms))]);out[name]=v===undefined?'ok':JSON.stringify(v).slice(0,200);}catch(e){out[name]='ERR '+(e&&e.message||e);}};
const settle=async id=>{for(let i=0;i<60;i++){const t=await chrome.tabs.get(id);if(t.status==='complete'&&t.url&&!t.url.startsWith('about:'))return t;await new Promise(r=>setTimeout(r,250));}return chrome.tabs.get(id);};
let windowId,tabId,group,second;
await run('windows.getLastFocused normal',async()=>{const w=await chrome.windows.getLastFocused({windowTypes:['normal']});windowId=w.id;return {id:w.id,type:w.type};});
await run('windows.create',async()=>{const w=await chrome.windows.create({url:'chrome://newtab/',focused:false,type:'normal'});if(windowId===undefined)windowId=w.id;tabId=w.tabs[0].id;return {id:w.id,tab:tabId,type:w.type};},30000);
await run('tabs.group',async()=>{const{windowId:w}=await chrome.tabs.get(tabId);group=await chrome.tabs.group({tabIds:[tabId],createProperties:{windowId:w}});return group;});
await run('tabGroups.update',async()=>{const g=await chrome.tabGroups.update(group,{title:'Claude session',color:chrome.tabGroups.Color.BLUE});return g.title;});
await run('tabGroups.get',async()=>{const g=await chrome.tabGroups.get(group);return g.title+'|'+g.color;});
await run('tabs.query group',async()=>(await chrome.tabs.query({groupId:group})).map(t=>t.id).join()===String(tabId));
await run('tabs.create in window',async()=>{const t=await chrome.tabs.create({windowId,url:'chrome://newtab/',active:false});second=t.id;return t.id;},30000);
await run('tabs.group second',async()=>{await chrome.tabs.group({tabIds:[second],groupId:group});return (await chrome.tabs.query({groupId:group})).length;});
await run('tabs.update navigate',async()=>{await chrome.tabs.update(tabId,{url:'https://example.com/'});return (await settle(tabId)).url;},30000);
await run('debugger.attach',async()=>{await chrome.debugger.attach({tabId},'1.3');});
await run('screenshot',async()=>(await chrome.debugger.sendCommand({tabId},'Page.captureScreenshot',{format:'png'})).data.length);
await run('setFileInputFiles',async()=>{
 await chrome.debugger.sendCommand({tabId},'Runtime.evaluate',{expression:""(()=>{const i=document.createElement('input');i.type='file';i.id='mica-upload';document.body.appendChild(i);})()""});
 const doc=await chrome.debugger.sendCommand({tabId},'DOM.getDocument',{});
 const node=await chrome.debugger.sendCommand({tabId},'DOM.querySelector',{nodeId:doc.root.nodeId,selector:'#mica-upload'});
 await chrome.debugger.sendCommand({tabId},'DOM.setFileInputFiles',{nodeId:node.nodeId,files:[__UPLOAD__]});
 return (await chrome.debugger.sendCommand({tabId},'Runtime.evaluate',{expression:""document.getElementById('mica-upload').files[0].name"",returnByValue:true})).result.value;});
await run('debugger.detach',async()=>{await chrome.debugger.detach({tabId});});
await run('tabs.remove',async()=>{await chrome.tabs.remove(second);await new Promise(r=>setTimeout(r,1500));try{await chrome.tabs.get(second);return 'still there';}catch(e){return 'closed';}});
await run('group after remove',async()=>(await chrome.tabs.query({groupId:group})).length);
return JSON.stringify(out);})()";
 }
}
