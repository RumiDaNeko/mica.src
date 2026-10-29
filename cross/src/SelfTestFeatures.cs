using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;

namespace Mica {
 // Self-test checks for the features ported from the Windows build: Spotlight and the command palette, bookmarks,
 // zoom, find, the library pages, downloads, the error page and resting tabs.
 sealed partial class BrowserWindow {
  async Task Spotlights() {
   OpenSpotlight(false);
   Check("Spotlight opens and the page steps aside",SpotlightOpen&&(Active.View==null||!Active.View.IsVisible),null);
   spotlightInput.Text="snake";await Task.Delay(100);
   Check("Spotlight finds a page",spotlightHits.Count>0&&spotlightHits.Any(h=>(h.Url??"").Contains("snake")),string.Join(", ",spotlightHits.Select(h=>h.Kind+" "+h.Url)));
   spotlightInput.Text="> theme";await Task.Delay(100);
   bool darkBefore=state.Dark;
   Check("command palette lists actions",spotlightHits.Count>0&&spotlightHits[0].Kind=="ACTION",string.Join(", ",spotlightHits.Select(h=>h.Title)));
   ExecuteSearch();await Task.Delay(200);
   Check("command palette runs an action",state.Dark!=darkBefore&&!SpotlightOpen,"dark="+state.Dark);
   SetDark(darkBefore);
   Check("Ctrl+K is a shortcut inside pages",IsShortcut(Key.K,Mac?KeyModifiers.Meta:KeyModifiers.Control)&&FromVirtualKey(0x4B)==Key.K&&!IsShortcut(Key.K,KeyModifiers.None),null);
   HandleKey(Key.K,Mac?KeyModifiers.Meta:KeyModifiers.Control);
   Check("Ctrl+K opens Spotlight",SpotlightOpen,null);
   HandleKey(Key.Escape,KeyModifiers.None);CloseSpotlight();
   Check("Spotlight closes and the page comes back",!SpotlightOpen&&(Active.View==null||Active.View.IsVisible||Active.Error!=null),null);
  }

  async Task NewFeatures(CrossTab first,CrossTab web) {
   Show(web.Entry.Id);
   await Task.Delay(1500);
   Check("page corners rounded where they meet the frame",OperatingSystem.IsWindows()?cornerShapes.Values.Any(v=>v.Contains("LR")):true,string.Join(", ",cornerShapes.Values));
   Bookmark();
   Check("bookmark this page",state.Bookmarks.Any(b=>b.Url=="https://example.com/"),"bookmarks="+state.Bookmarks.Count);
   ShowBookmarks();
   Check("bookmarks listed in Spotlight",spotlightHits.Any(h=>h.Kind=="BOOKMARK"&&h.Url=="https://example.com/"),null);
   CloseSpotlight();

   ZoomStep(1);await Task.Delay(800);
   string ratio=await Js(web,"String(Math.round(devicePixelRatio*100))");
   double baseRatio=1;try {baseRatio=RenderScaling;} catch(Exception) {}
   Check("zoom in, remembered for the site",Math.Abs(web.Zoom-1.1)<0.001&&state.SiteZoom.ContainsKey("example.com")&&zoomBadge.IsVisible&&ratio==Math.Round(110*baseRatio).ToString(),"zoom="+web.Zoom+" ratio="+ratio);
   ZoomStep(0);await Task.Delay(300);
   Check("zoom reset",Math.Abs(web.Zoom-1)<0.001&&!state.SiteZoom.ContainsKey("example.com")&&!zoomBadge.IsVisible,null);

   // Zoom in the island (BrowserIslandZoom.cs): only for the user's own changes, gone after a few seconds.
   state.SiteZoom["example.com"]=1.1;ApplySiteZoom(web);await Task.Delay(200);
   bool restored=!IslandHasZoom;
   UserZoom(1);await Task.Delay(200);
   Func<string,Control> zoomPart=tag=>islandZoom.Children.OfType<Grid>().SelectMany(g=>g.Children).FirstOrDefault(c=>(c.Tag as string)==tag);
   Check("zoom shows in the island, not when Mica restores it",restored&&IslandHasZoom&&islandZoomLevel.Text=="125%"&&Math.Abs(web.Zoom-1.25)<0.001&&zoomPart("island-zoom-in")!=null,"restored="+restored+" level="+(islandZoomLevel==null?"-":islandZoomLevel.Text));
   var zoomOut=zoomPart("island-zoom-out") as Button;if(zoomOut!=null)zoomOut.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));await Task.Delay(200);
   string afterOut=islandZoomLevel.Text;
   var zoomReset=zoomPart("island-zoom-reset") as Button;if(zoomReset!=null)zoomReset.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));await Task.Delay(200);
   Check("the island's − and reset step the zoom; 100% isn't kept",afterOut=="110%"&&islandZoomLevel.Text=="100%"&&Math.Abs(web.Zoom-1)<0.001&&!state.SiteZoom.ContainsKey("example.com")&&!islandAway,afterOut+" then "+islandZoomLevel.Text);
   await Task.Delay(4000);
   bool lingered=!IslandHasZoom;
   ZoomStep(1);await Task.Delay(200);bool menuQuiet=!IslandHasZoom;ZoomStep(0);await Task.Delay(200);
   Check("the island's zoom leaves after a moment; the menu's zoom doesn't bring it up",lingered&&menuQuiet,"lingered="+lingered+" menu="+menuQuiet);
   await Js(web,"(dispatchEvent(new WheelEvent('wheel',{deltaY:-100,ctrlKey:true,cancelable:true})),'1')");await Task.Delay(400);
   Check("Ctrl+wheel zooms through Mica and shows in the island",Math.Abs(web.Zoom-1.1)<0.001&&IslandHasZoom&&islandZoomLevel.Text=="110%","zoom="+web.Zoom);
   ZoomStep(0);HideIslandZoom();await Task.Delay(200);

   var names=CrossExtensions.Folders(profile).Select(f=>{string n,v;ManifestName(f,out n,out v);return n;}).ToList();
   Check("extensions card names what loaded",names.Count>=2&&names.All(n=>!n.StartsWith("__MSG")),string.Join(", ",names));
   lastStatus="";ToggleReader();for(int i=0;i<50&&lastStatus.Length==0;i++)await Task.Delay(100);await Task.Delay(300);
   string reading=await EvalAsync(web.View,"Promise.resolve(String(!!document.getElementById('mica-reader')))",5);
   Check("reading mode opens over the page (the island says so)",reading=="true"&&lastStatus.StartsWith("Reading mode")&&islandMessage.Text==lastStatus,reading+" / "+lastStatus);
   ToggleReader();await Task.Delay(600);
   Check("reading mode closes",await EvalAsync(web.View,"Promise.resolve(String(!!document.getElementById('mica-reader')))",5)=="false",null);
   Check("the island is up, showing memory",island.IsOpen&&islandMemory.Text.Contains("MB"),"open="+island.IsOpen+" text="+islandMemory.Text+" state="+WindowState+" visible="+IsVisible+" full="+islandFullscreen+" inactive="+islandInactive);
   state.SiteMemory["example.com"]="never";
   Show(first.Entry.Id);RestIdleTabs(true);await Task.Delay(1500);
   Check("a site kept awake doesn't rest",web.View!=null,null);
   state.SiteMemory.Remove("example.com");Show(web.Entry.Id);
   string memory=MemorySummary();
   Check("task manager measures Mica and Chromium",memory.Contains(" MB in use")&&!memory.StartsWith("0 MB"),memory);
   ShowFind();findInput.Text="domain";
   for(int i=0;i<30&&findCount.Text=="0 / 0";i++)await Task.Delay(100);
   Check("find in page counts matches",findCount.Text.EndsWith(" / 2")||findCount.Text.EndsWith(" / 1")||findCount.Text.EndsWith(" / 3"),findCount.Text);
   CloseFind();

   // A download made by the page lands in the (test) Downloads folder and shows in the library.
   web.View.ExecuteJavaScript("const a=document.createElement('a');a.href=URL.createObjectURL(new Blob(['mica'],{type:'text/plain'}));a.download='mica-test.txt';document.body.append(a);a.click();","",1);
   for(int i=0;i<60&&!downloads.Any(d=>d.Status=="Completed");i++)await Task.Delay(100);
   var done=downloads.FirstOrDefault(d=>d.Status=="Completed");
   Check("download rides in the island",islandDownloads.Children.Count>0,null);
   Check("download saved",done!=null&&File.Exists(done.FilePath)&&File.ReadAllText(done.FilePath)=="mica",done==null?"none":done.Status+" "+done.FilePath);
   await ChromiumUiProbe(web);

   Show(first.Entry.Id);
   await Page("history page",first,delegate {Navigate("mica://history");},"mica://history","History");
   string rows=await Js(first,"document.querySelectorAll('.card').length+' '+document.body.innerText.includes('example.com')");
   Check("history lists the visit",rows.EndsWith(" true")&&!rows.StartsWith("0 "),rows);
   string guarded=await EvalAsync(first.View,"fetch('/data').then(r=>String(r.status))",10);
   Check("library refuses calls without its key",guarded=="404",guarded);
   await Page("downloads page",first,delegate {Navigate("mica://downloads");},"mica://downloads","Downloads");
   string files=await Js(first,"document.body.innerText.includes('mica-test.txt')+''");
   Check("downloads page lists the file",files=="true",files);

   // Resting: a background tab gives its page back and wakes when picked.
   Show(first.Entry.Id);RestIdleTabs(true);
   for(int i=0;i<40&&web.View!=null;i++)await Task.Delay(100);
   Check("background tab rests",web.View==null,null);
   Show(web.Entry.Id);
   Check("rested tab wakes when picked",web.View!=null,null);
   await Load(web,delegate {},20);

   Show(first.Entry.Id);
   bool failed=await Load(first,delegate {Navigate("https://mica-nowhere.invalid/");},20);
   Show(first.Entry.Id);
   Check("error page for a site that isn't there",first.Error!=null&&errorPanel.IsVisible&&!first.View.IsVisible,"error="+first.Error+" panel="+errorPanel.IsVisible);
   await Page("leaves the error page",first,delegate {Navigate("mica://2048");},"mica://2048","2048");
   Check("error page gone",!errorPanel.IsVisible&&first.View.IsVisible,null);
  }
  // What Claude Code's tools do through Claude for Chrome: make a window for the session, group a tab in it, name
  // and colour the group, open another tab into it, navigate, screenshot, upload a file and close a tab. Run from the
  // extension's own page (the panel), which reaches the same browser APIs as its worker.
  async Task ClaudeTools() {
   int before=state.Tabs.Count;var had=new System.Collections.Generic.HashSet<string>(state.Tabs.Select(t=>t.Id));
   string upload=Path.Combine(profile,"claude-upload.txt");File.WriteAllText(upload,"hello from Mica");
   string probe=ClaudeToolsProbe.Replace("__UPLOAD__",new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(upload));
   string answer=await EvalAsync(aiView,probe,120)??await Js(aiView,"JSON.stringify(self.__claudeOut||{})");Trace("claude tools: "+answer);
   System.Collections.Generic.Dictionary<string,string> found;
   try {found=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,string>>(answer);}catch(Exception){found=new System.Collections.Generic.Dictionary<string,string>();}
   Func<string,string> api=k=>found!=null&&found.ContainsKey(k)?found[k]:"missing";Func<string,bool> ok=k=>api(k)!="missing"&&!api(k).StartsWith("ERR",StringComparison.Ordinal);
   Check("Claude makes a window for its session",ok("windows.create")&&api("windows.create").Contains("\"tab\":"),api("windows.create"));
   Check("Claude groups a tab and names the group",api("tabGroups.get")=="\"Claude session|blue\""&&api("tabs.query group")=="true",api("tabs.group")+" "+api("tabGroups.get")+" "+api("tabs.query group"));
   Check("Claude opens a tab into its group",api("tabs.group second")=="2",api("tabs.create in window")+" "+api("tabs.group second"));
   Check("Claude's tabs open as Mica tabs",api("as mica tabs")=="true",api("as mica tabs")+" tabs "+before+" -> "+state.Tabs.Count);
   Check("Claude navigates its tab",api("tabs.update navigate").Contains("example.com"),api("tabs.update navigate"));
   Check("Claude screenshots its tab",ok("screenshot")&&api("screenshot")!="0",api("screenshot"));
   Check("Claude uploads a file into the page",api("setFileInputFiles")=="\"claude-upload.txt\"",api("setFileInputFiles"));
   await Task.Delay(1000);
   Check("Claude closes its tab and Mica stays open",api("tabs.remove")=="\"closed\""&&IsVisible,api("tabs.remove")+" visible="+IsVisible);
   foreach(var extra in state.Tabs.Where(t=>!had.Contains(t.Id)).Select(t=>t.Id).ToList())CloseTab(extra);
   state.RecentlyClosed.Clear();
  }
  const string ClaudeToolsProbe=@"(async()=>{const out=self.__claudeOut={};
const run=async(name,fn,ms=20000)=>{try{const v=await Promise.race([fn(),new Promise((_,no)=>setTimeout(()=>no(new Error('timeout')),ms))]);out[name]=v===undefined?'ok':JSON.stringify(v).slice(0,200);}catch(e){out[name]='ERR '+(e&&e.message||e);}};
const settle=async id=>{for(let i=0;i<60;i++){const t=await chrome.tabs.get(id);if(t.status==='complete'&&t.url&&!t.url.startsWith('about:'))return t;await new Promise(r=>setTimeout(r,250));}return chrome.tabs.get(id);};
const before=(await chrome.windows.getAll({windowTypes:['normal','popup','app','devtools']})).length;
let windowId,tabId,group,second;
await run('windows.create',async()=>{const w=await chrome.windows.create({url:'chrome://newtab/',focused:false,type:'normal'});windowId=w.id;tabId=w.tabs[0].id;return {id:w.id,tab:tabId};},30000);
await run('tabs.group',async()=>{const{windowId:w}=await chrome.tabs.get(tabId);group=await chrome.tabs.group({tabIds:[tabId],createProperties:{windowId:w}});return group;});
await run('tabGroups.update',async()=>(await chrome.tabGroups.update(group,{title:'Claude session',color:chrome.tabGroups.Color.BLUE})).title);
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
await run('as mica tabs',async()=>(await chrome.windows.getAll({windowTypes:['normal','popup','app','devtools']})).length-before<=2);
await run('tabs.remove',async()=>{await chrome.tabs.remove(second);await new Promise(r=>setTimeout(r,1500));try{await chrome.tabs.get(second);return 'still there';}catch(e){return 'closed';}});
return JSON.stringify(out);})()";

  // Mica never shows Chromium's own UI: no bubble, prompt or dialog window of Chromium's appears (on Windows they are
  // top-level windows of Mica's process), and Mica's own card or dialog takes its place.
  async Task ChromiumUiProbe(CrossTab web) {
   Func<string> chromium=()=>{var seen=ChromiumWindows.Visible();return seen==null?null:string.Join(", ",seen);};
   bool measured=chromium()!=null;if(!measured)Skip("no Chromium windows","only measurable on Windows");
   await Task.Delay(1200);
   if(measured)Check("a download shows no Chromium bubble",chromium()=="",chromium());
   UserZoom(1);await Task.Delay(1200);
   if(measured)Check("zooming shows no Chromium bubble",chromium()=="",chromium());
   ZoomStep(0);HideIslandZoom();
   // A page dialog is Mica's, and the page gets its answer.
   web.View.ExecuteJavaScript("setTimeout(()=>{window.__said=confirm('Keep going?')},0)","",1);
   for(int i=0;i<30&&openDialog==null;i++)await Task.Delay(100);
   bool ours=openDialog!=null;string seenNow=measured?chromium():"";
   if(openDialog!=null){foreach(var b in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(openDialog.Child))if(b is Button&&(string)((Button)b).Tag=="dialog-ok"){((Button)b).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));break;}}
   await Task.Delay(300);string said=await Js(web,"String(window.__said)");
   Check("a page's confirm() is Mica's dialog and gets its answer",ours&&said=="true"&&seenNow=="",ours+" said="+said+" chromium="+seenNow);
   // Permissions ask through Mica's card.
   foreach(var probe in new[]{new[]{"camera","navigator.mediaDevices.getUserMedia({video:true}).then(()=>'yes',e=>e.name)"},new[]{"location","new Promise(r=>navigator.geolocation.getCurrentPosition(()=>r('yes'),e=>r('code '+e.code)))"},new[]{"notifications","Notification.requestPermission()"}}) {
    web.View.ExecuteJavaScript("window.__asked=null;Promise.resolve("+probe[1]+").then(v=>window.__asked=String(v))","",1);
    for(int i=0;i<40&&sitePrompt==null;i++)await Task.Delay(100);
    string card=sitePromptText;seenNow=measured?chromium():"";
    await Task.Delay(750);AnswerSitePrompt(false);
    string asked="null";for(int i=0;i<30&&asked=="null";i++){await Task.Delay(100);asked=await Js(web,"String(window.__asked)");}
    Check("asking for "+probe[0]+" shows Mica's card, and Block blocks",card!=null&&card.StartsWith("example.com wants to")&&seenNow==""&&asked!="yes"&&asked!="granted"&&asked!="null"&&sitePrompt==null,"card="+card+" answer="+asked+" chromium="+seenNow);
   }
  }
 }
}
