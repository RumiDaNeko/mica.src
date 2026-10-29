using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

   var names=CrossExtensions.Folders(profile).Select(f=>{string n,v;ManifestName(f,out n,out v);return n;}).ToList();
   Check("extensions card names what loaded",names.Count>=2&&names.All(n=>!n.StartsWith("__MSG")),string.Join(", ",names));
   lastStatus="";ToggleReader();for(int i=0;i<50&&lastStatus.Length==0;i++)await Task.Delay(100);await Task.Delay(300);
   string reading=await EvalAsync(web.View,"Promise.resolve(String(!!document.getElementById('mica-reader')))",5);
   Check("reading mode opens over the page (the island says so)",reading=="true"&&lastStatus.StartsWith("Reading mode")&&islandMessage.Text==lastStatus,reading+" / "+lastStatus);
   ToggleReader();await Task.Delay(600);
   Check("reading mode closes",await EvalAsync(web.View,"Promise.resolve(String(!!document.getElementById('mica-reader')))",5)=="false",null);
   Check("the island is up, showing memory",island.IsOpen&&islandMemory.Text.Contains("MB"),"open="+island.IsOpen+" text="+islandMemory.Text);
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
 }
}
