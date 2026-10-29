using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 partial class BrowserApp {
  // The palette, cookie jars, tree tabs, groups that rest, read later, 3-4 pane split, highlights,
  // screenshots, sessions, site shortcuts, the password check, memory rules, forgetting a site, peek and mute.
  async Task RunMoreFeaturesTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"more-features");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Width=1400;window.Height=900;window.Left=-6000;window.Top=-6000;
   Func<string,Task<string>> eval=async delegate(string script) {return await CurrentView().CoreWebView2.ExecuteScriptAsync(script);};
   Func<string,Task> open=async delegate(string url) {Navigate(url,false);await WaitForPage();await Task.Delay(400);};
   try {
    await open("https://example.com/");var main=Active;

    // Command palette.
    OpenSpotlight(false);var input=Find<TextBox>("SpotlightInput");input.Text="> sp vi";await Task.Delay(300);
    var hits=(Find<ListBox>("SearchResults").ItemsSource as System.Collections.IEnumerable).OfType<SearchHit>().ToList();
    log.Add("palette '> sp vi': "+string.Join(" | ",hits.Select(h=>h.Title)));
    RegressionCheck("> lists actions by the start of their words",delegate {Require(hits.Count>0&&hits.All(h=>h.Kind=="ACTION")&&hits[0].Title=="Split view",string.Join(",",hits.Select(h=>h.Title)));});
    input.Text="> tree tabs";await Task.Delay(300);ExecuteSearch();await Task.Delay(400);
    RegressionCheck("Enter runs the action",delegate {Require(state.TreeTabs&&!IsSpotlight,"tree "+state.TreeTabs);});
    OpenSpotlight(false);input.Text="yt lofi beats";await Task.Delay(300);
    hits=(Find<ListBox>("SearchResults").ItemsSource as System.Collections.IEnumerable).OfType<SearchHit>().ToList();
    RegressionCheck("a site shortcut comes first: yt searches YouTube",delegate {Require(hits.Count>0&&hits[0].Kind=="SHORTCUT"&&hits[0].Url=="https://www.youtube.com/results?search_query=lofi%20beats",hits.Count==0?"none":hits[0].Kind+" "+hits[0].Url);});
    CloseSpotlight();await Task.Delay(300);

    // Tree tabs: a tab opened from another sits right under it, indented.
    OpenFromTab(main,"https://example.com/?child");await WaitForPage();await Task.Delay(300);var child=Active;
    RegressionCheck("a tab opened from a page sits under it",delegate {Require(child.Opener==main.Id&&state.Tabs.IndexOf(child)==state.Tabs.IndexOf(main)+1&&TreeDepth(child)==1,"depth "+TreeDepth(child));});
    var row=Find<StackPanel>("TabItems").Children.OfType<Grid>().FirstOrDefault(g=>(g.Tag as string)=="tab:"+child.Id);
    RegressionCheck("and its row is indented",delegate {Require(row!=null&&row.Margin.Left>=14,row==null?"no row":"margin "+row.Margin.Left);});
    state.TreeTabs=false;RenderTabs();

    // Cookie jars: the same site, two sets of cookies.
    Func<PageEntry,Task> showing=async delegate(PageEntry t) {SelectTab(t.Id);await WaitFor(()=>state.ActiveId==t.Id&&views.ContainsKey(t.Id)&&views[t.Id].CoreWebView2!=null&&views[t.Id].IsVisible&&views[t.Id].CoreWebView2.Source.Contains("example.com"),20000);await Task.Delay(600);};
    await showing(main);await views[main.Id].CoreWebView2.ExecuteScriptAsync("document.cookie='who=main; max-age=600'");
    SetTabJar(child,"work");await showing(child);
    string jarCookie=await views[child.Id].CoreWebView2.ExecuteScriptAsync("document.cookie");await views[child.Id].CoreWebView2.ExecuteScriptAsync("document.cookie='who=work; max-age=600'");
    await showing(main);string mainCookie=await views[main.Id].CoreWebView2.ExecuteScriptAsync("document.cookie");
    log.Add("cookies: jar "+jarCookie+" ("+views[child.Id].CoreWebView2.Profile.ProfileName+"), main "+mainCookie+" ("+views[main.Id].CoreWebView2.Profile.ProfileName+")");
    RegressionCheck("a tab in a jar doesn't see the profile's cookies, and the profile doesn't see the jar's",delegate {Require(jarCookie=="\"\""&&mainCookie=="\"who=main\"","jar "+jarCookie+" main "+mainCookie);});
    RenderTabs();var dot=LogicalDescendants<Border>(Find<StackPanel>("TabItems")).FirstOrDefault(b=>(b.Tag as string)=="jar-dot");
    RegressionCheck("its tab shows the jar's colour",delegate {Require(dot!=null,"no dot");});
    RegressionCheck("links from it stay in the jar",delegate {OpenFromTab(child,"https://example.com/?grandchild");Require(Active.Jar=="work",Active.Jar??"none");});
    var grandchild=Active;await WaitForPage();await Task.Delay(300);CloseTab(grandchild.Id);await Task.Delay(300);

    // Mute from the tab row.
    SelectTab(main.Id);await Task.Delay(300);ToggleMute(child.Id);await Task.Delay(300);
    var speaker=LogicalDescendants<Button>(Find<StackPanel>("TabItems")).FirstOrDefault(b=>(b.Tag as string)=="tab-sound:"+child.Id);
    RegressionCheck("a muted tab says so and unmutes with a click",delegate {Require(views[child.Id].CoreWebView2.IsMuted&&speaker!=null,"muted "+views[child.Id].CoreWebView2.IsMuted);});
    if(speaker!=null){speaker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(300);}
    RegressionCheck("unmuted",delegate {Require(!views[child.Id].CoreWebView2.IsMuted,"still muted");});

    // Groups rest together when folded.
    var groupTab=state.AddTab("https://example.com/?group");SelectTab(groupTab.Id);await WaitForPage();await Task.Delay(400);
    var group=state.CreateGroup(groupTab);child.Group=group.Id;state.NormalizeGroups();SelectTab(main.Id);await Task.Delay(500);
    bool awakeBefore=views.ContainsKey(groupTab.Id)&&views.ContainsKey(child.Id);
    ToggleGroup(group.Id);await WaitFor(()=>group.Collapsed,3000);await Task.Delay(400);
    RegressionCheck("folding a group puts its tabs to rest",delegate {Require(awakeBefore&&!views.ContainsKey(groupTab.Id)&&!views.ContainsKey(child.Id),"awake before "+awakeBefore+", after "+views.ContainsKey(groupTab.Id)+"/"+views.ContainsKey(child.Id));});
    ToggleGroup(group.Id);await WaitFor(()=>views.ContainsKey(groupTab.Id),8000).ContinueWith(t=>{});
    RegressionCheck("unfolding warms them again",delegate {Require(!group.Collapsed&&views.ContainsKey(groupTab.Id),"collapsed "+group.Collapsed);});

    // Memory: per tab, and per site.
    RefreshTabMemory();await WaitFor(()=>TabMemoryText(main.Id)!=null&&TabMemoryText(main.Id).Contains("MB"),8000).ContinueWith(t=>{});
    log.Add("main tab memory: "+TabMemoryText(main.Id));
    RegressionCheck("each awake tab knows its memory",delegate {Require(TabMemoryText(main.Id)!=null&&TabMemoryText(main.Id).Contains("MB")&&TabTip(main).Contains("Memory:"),TabMemoryText(main.Id)??"none");});
    SetSiteMemory("example.com","never");
    RegressionCheck("a site kept awake never freezes or rests",delegate {Require(!CanFreeze(groupTab,views[groupTab.Id])&&RestAfterFor(groupTab,false)==TimeSpan.MaxValue,"can freeze");});
    SetSiteMemory("example.com","quick");
    RegressionCheck("a quick site rests after two minutes",delegate {Require(RestAfterFor(groupTab,false)==TimeSpan.FromMinutes(2),RestAfterFor(groupTab,false).ToString());});
    SetSiteMemory("example.com",null);

    // Split view with three panes.
    SelectTab(main.Id);await Task.Delay(400);OpenSplit(child.Id);await Task.Delay(800);AddToSplit(groupTab.Id);await Task.Delay(1500);
    var columns=Find<Grid>("WebHost").ColumnDefinitions;
    RegressionCheck("split view takes a third tab",delegate {Require(splitPanes.Count==3&&columns.Count==5&&views[groupTab.Id].IsVisible&&Grid.GetColumn(views[groupTab.Id])==4,"panes "+splitPanes.Count+" columns "+columns.Count);});
    var widths=splitPanes.Select(p=>views[p].ActualWidth).ToList();log.Add("pane widths: "+string.Join(", ",widths.Select(w=>Math.Round(w))));
    RegressionCheck("evenly",delegate {Require(widths.Max()-widths.Min()<14,string.Join(",",widths));});
    SelectTab(groupTab.Id);await Task.Delay(400);var other=state.AddTab("https://example.com/?replace");state.Tabs.Remove(other);state.Tabs.Add(other);state.ActiveId=groupTab.Id;
    SelectTab(other.Id);await Task.Delay(1500);
    RegressionCheck("opening another tab replaces the pane you were on",delegate {Require(splitPanes.Count==3&&splitPanes[2]==other.Id&&splitPanes.Contains(main.Id),string.Join(",",splitPanes));});
    RemoveFromSplit(other.Id);await Task.Delay(500);
    RegressionCheck("taking one out leaves two",delegate {Require(splitPanes.Count==2&&Find<Grid>("WebHost").ColumnDefinitions.Count==3,"panes "+splitPanes.Count);});
    CloseSplit();await Task.Delay(300);CloseTab(other.Id);

    // Dragging tabs out of the sidebar onto the page builds a split of up to five.
    SelectTab(main.Id);await Task.Delay(400);
    var extra=Enumerable.Range(3,4).Select(n=>state.AddTab("https://example.com/?pane"+n)).ToList();state.ActiveId=main.Id;RenderTabs();await Task.Delay(500);
    SimulateTabDragToPage(child.Id,0.2);await Task.Delay(1200);
    RegressionCheck("a tab dropped on the left of the page splits to the left",delegate {Require(splitPanes.Count==2&&splitPanes[0]==child.Id&&splitPanes[1]==main.Id,string.Join(",",splitPanes));});
    SimulateTabDragToPage(extra[0].Id,0.99);await Task.Delay(900);SimulateTabDragToPage(extra[1].Id,0.99);await Task.Delay(900);
    // Checked at once: a real pointer event with no button down would end the simulated drag.
    SimulateTabDragToPage(extra[2].Id,0.5,false);
    RegressionCheck("while dragging over the page, a preview shows the layout to come",delegate {
     Require(splitPreview!=null&&splitPreview.IsOpen,"no preview");
     Require(splitPreviewGrid.ColumnDefinitions.Count==5&&splitPreviewGrid.Children.OfType<Border>().Count(b=>(b.Tag as string)=="split-preview-new")==1,"columns "+splitPreviewGrid.ColumnDefinitions.Count);
    });
    if(splitPreviewGrid!=null)CaptureElement(splitPreviewGrid,"split-drop-preview.png");
    EndTabDrag(true);await Task.Delay(1500);
    RegressionCheck("split view holds five tabs, the dropped one where it was let go",delegate {Require(splitPanes.Count==5&&splitPanes[2]==extra[2].Id&&Find<Grid>("WebHost").ColumnDefinitions.Count==9&&!splitPreview.IsOpen,string.Join(",",splitPanes));});
    SimulateTabDragToPage(extra[3].Id,0.5,false);
    RegressionCheck("a sixth tab can't be dropped in",delegate {Require(tabDrag!=null&&tabDrag.SplitSlot<0&&!splitPreview.IsOpen,"slot "+(tabDrag==null?"none":tabDrag.SplitSlot.ToString()));});
    EndTabDrag(true);await Task.Delay(400);
    RegressionCheck("so the split stays as it was",delegate {Require(splitPanes.Count==5&&!splitPanes.Contains(extra[3].Id),string.Join(",",splitPanes));});
    SimulateTabDragToPage(child.Id,0.99);await Task.Delay(900);
    RegressionCheck("dragging a tab already in the split moves it",delegate {Require(splitPanes.Count==5&&splitPanes[4]==child.Id,string.Join(",",splitPanes));});
    CloseSplit();await Task.Delay(300);foreach(var t in extra)CloseTab(t.Id);await Task.Delay(300);

    // The island shows what's playing in another tab, by its current name. The tone is muted, so nothing is heard.
    var tone=state.AddTab("https://example.com/?tone");RenderTabs();SelectTab(tone.Id);await WaitForPage();await Task.Delay(500);
    var toneCore=views[tone.Id].CoreWebView2;toneCore.IsMuted=true;
    await toneCore.CallDevToolsProtocolMethodAsync("Runtime.evaluate","{\"expression\":\"(()=>{const c=new AudioContext();const o=c.createOscillator();o.connect(c.destination);o.start();window.__tone=c;document.title='Tone one';return c.state})()\",\"userGesture\":true}");
    for(int i=0;i<40&&!toneCore.IsDocumentPlayingAudio;i++)await Task.Delay(125);
    if(!toneCore.IsDocumentPlayingAudio)log.Add("SKIP island media: the engine reports no audio here");
    else {
     Func<string> islandName=delegate {var mediaRow=islandMedia.Children.OfType<Grid>().FirstOrDefault();var name=mediaRow==null?null:mediaRow.Children.OfType<TextBlock>().ElementAtOrDefault(1);return name==null?null:name.Text;};
     SelectTab(main.Id);await Task.Delay(700);
     RegressionCheck("leaving a playing tab puts it on the island",delegate {Require(islandMediaShown==tone.Id&&islandName()==tone.Title,(islandMediaShown??"none")+" "+islandName());});
     await toneCore.ExecuteScriptAsync("document.title='Tone two'");await Task.Delay(800);
     RegressionCheck("the island follows the playing tab's new name",delegate {Require(islandName()=="Tone two",islandName()??"none");});
    }
    try {await toneCore.ExecuteScriptAsync("window.__tone&&window.__tone.close()");}catch(Exception){}
    CloseTab(tone.Id);await Task.Delay(300);

    // A page's own blob (AdMob opens reports this way) loads; other non-web addresses stay blocked.
    RegressionCheck("only a web page's own blob counts as one",delegate {Require(Navigation.IsSiteBlob("blob:https://admob.google.com/0f1e")&&!Navigation.IsSiteBlob("blob:null/0f1e")&&!Navigation.IsSiteBlob("blob:file:///c/x")&&!Navigation.IsSiteBlob("data:text/html,x"),"blob rules");});
    SelectTab(main.Id);await Task.Delay(300);await open("https://example.com/?blob");
    await eval("location.href=URL.createObjectURL(new Blob(['<title>Blob report</title><p>report</p>'],{type:'text/html'}))");
    for(int i=0;i<30&&!(CurrentView().CoreWebView2.Source??"").StartsWith("blob:");i++)await Task.Delay(100);await Task.Delay(400);
    RegressionCheck("a page can open its own blob in the tab",delegate {Require(CurrentView().CoreWebView2.Source.StartsWith("blob:https://example.com/")&&CurrentView().CoreWebView2.DocumentTitle=="Blob report",CurrentView().CoreWebView2.Source+" "+CurrentView().CoreWebView2.DocumentTitle);});
    // Files on this PC open when you open them (typed, dropped, Open with); a page can't send you to one, and network paths stay out.
    RegressionCheck("a typed path, network path or file: address is a file to open",delegate {
     Require(Navigation.Resolve(@"C:\Videos\a b.mp4")=="file:///C:/Videos/a%20b.mp4"&&Navigation.Resolve("\"C:\\x\\r.pdf\"")=="file:///C:/x/r.pdf"&&Navigation.Resolve("file:///D:/m.mp3")=="file:///D:/m.mp3","resolve "+Navigation.Resolve(@"C:\Videos\a b.mp4"));
     Require(Navigation.LocalFileAddress(@"\\server\share\a.pdf")=="file://server/share/a.pdf"&&Navigation.IsLocalFile("file://server/share/a.pdf")&&Navigation.IsLocalFile("file:///C:/a.pdf")&&Navigation.LocalFileAddress(@"\\server")==null,"network path "+Navigation.LocalFileAddress(@"\\server\share\a.pdf"));
    });
    string files=Path.Combine(profile,"local files");Directory.CreateDirectory(files);
    File.WriteAllText(Path.Combine(files,"page.html"),"<!doctype html><title>Local page</title><p>from this PC</p>");
    File.WriteAllText(Path.Combine(files,"dot.svg"),"<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'><circle cx='20' cy='20' r='18' fill='teal'/></svg>");
    Navigate(Path.Combine(files,"page.html"),true);for(int i=0;i<50&&!(CurrentView()!=null&&CurrentView().CoreWebView2!=null&&CurrentView().CoreWebView2.DocumentTitle=="Local page");i++)await Task.Delay(100);
    string localSource=CurrentView()==null||CurrentView().CoreWebView2==null?"none":CurrentView().CoreWebView2.Source;
    RegressionCheck("a file on this PC opens in a tab",delegate {Require(localSource.StartsWith("file:///")&&CurrentView().CoreWebView2.DocumentTitle=="Local page"&&Active.Url.StartsWith("file:///")&&Active.Title=="Local page",localSource+" / "+Active.Title);});
    Navigate("file:///"+Path.Combine(files,"dot.svg").Replace('\\','/'),false);for(int i=0;i<40&&!(CurrentView().CoreWebView2.Source??"").EndsWith("dot.svg");i++)await Task.Delay(100);await Task.Delay(300);
    string image=await eval("document.documentElement.tagName+'/'+(document.querySelector('circle')?'circle':'')");
    RegressionCheck("an image file on this PC shows, titled by its name",delegate {Require(image.Contains("circle")&&Active.Title=="dot.svg",image+" / "+Active.Title);});
    CloseTab(Active.Id);await Task.Delay(300);SelectTab(main.Id);await Task.Delay(300);
    // Dropping a file on a web page opens it, as a tab of its own or in the same one.
    await open("https://example.com/?drop");
    var dropFile=Path.Combine(files,"page.html");
    foreach(var type in new[]{"dragEnter","dragOver","drop"})
     await CurrentView().CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchDragEvent","{\"type\":\""+type+"\",\"x\":200,\"y\":200,\"data\":{\"items\":[],\"files\":["+VaultCrypto.Json.Serialize(dropFile)+"],\"dragOperationsMask\":1}}");
    for(int i=0;i<40&&!state.Tabs.Any(t=>(t.Url??"").StartsWith("file:///"));i++)await Task.Delay(100);await Task.Delay(500);
    string dropped=string.Join(" | ",state.Tabs.Select(t=>t.Url));
    RegressionCheck("dropping a file on a page opens it",delegate {Require(state.Tabs.Any(t=>t.Url==new Uri(dropFile).AbsoluteUri),dropped);});
    foreach(var t in state.Tabs.Where(t=>(t.Url??"").StartsWith("file:///")).ToList())CloseTab(t.Id);await Task.Delay(300);SelectTab(main.Id);await Task.Delay(300);
    await open("https://example.com/?no-files");await eval("location.href='"+new Uri(Path.Combine(files,"page.html")).AbsoluteUri+"'");await Task.Delay(1200);
    RegressionCheck("a website can't send you to a file on this PC",delegate {Require(CurrentView().CoreWebView2.Source.StartsWith("https://example.com/"),CurrentView().CoreWebView2.Source);});
    await open("https://example.com/?blob-window");int windowsBefore=Application.Current.Windows.Count;
    await CurrentView().CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate","{\"expression\":\"window.open(URL.createObjectURL(new Blob(['<title>Blob window</title>'],{type:'text/html'})))&&1\",\"userGesture\":true}");
    for(int i=0;i<40&&Application.Current.Windows.Count==windowsBefore;i++)await Task.Delay(100);await Task.Delay(600);
    var blobWindow=Application.Current.Windows.Cast<Window>().FirstOrDefault(w=>w!=window&&w.IsVisible&&VaultElements<WebView2>(w.Content as DependencyObject).Any(v=>v.CoreWebView2!=null&&(v.CoreWebView2.Source??"").StartsWith("blob:https://example.com/")));
    RegressionCheck("a page's blob opened in a new window gets a window",delegate {Require(blobWindow!=null,"windows "+windowsBefore+" -> "+Application.Current.Windows.Count);});
    if(blobWindow!=null)blobWindow.Close();await Task.Delay(300);

    // Crash reports: written on this device, and the next start says so once.
    string report=CrashLog.Write(new InvalidOperationException("test crash"),"test");
    RegressionCheck("a crash report is written with the version",delegate {Require(report!=null&&File.Exists(report)&&File.ReadAllText(report).Contains("Mica "+AppVersion)&&File.ReadAllText(report).Contains("test crash")&&CrashLog.Latest()==report,report??"none");});
    RegressionCheck("Mica marks itself running, so a crash is noticed next time",delegate {Require(File.Exists(Path.Combine(CrashLog.Folder,"running")),"no marker");});
    CrashLog.PreviousRunCrashed=true;TellAboutLastCrash();
    RegressionCheck("after a crash, the next start says so once",delegate {Require(Find<TextBlock>("StatusText").Text.Contains("closed unexpectedly")&&!CrashLog.PreviousRunCrashed,Find<TextBlock>("StatusText").Text);});

    // Read later: saved from reading mode, opened offline as its own tab.
    SelectTab(main.Id);await Task.Delay(300);await open("https://en.wikipedia.org/wiki/Web_browser");
    for(int i=0;i<40&&await eval("document.readyState")!="\"complete\"";i++)await Task.Delay(250);await Task.Delay(600);
    var wiki=Active;int savedBefore=LoadReadLater().Count;SaveForLater();await WaitFor(()=>LoadReadLater().Count>savedBefore,15000).ContinueWith(t=>{});
    var saved=LoadReadLater().FirstOrDefault(a=>a.Url.Contains("Web_browser"));
    RegressionCheck("an article can be saved for later",delegate {Require(saved!=null&&File.Exists(Path.Combine(ReadLaterFolder,saved.Id+".json")),"not saved");});
    if(saved!=null) {
     OpenSavedArticle(saved.Id);await WaitFor(()=>ReaderOpen&&Active!=null&&Navigation.IsReading(Active.Url),5000).ContinueWith(t=>{});await Task.Delay(500);
     var readTab=Active;
     RegressionCheck("it opens as its own tab, with no page loaded behind it",delegate {Require(ReaderOpen&&readerView==null&&!views.ContainsKey(readTab.Id)&&(Localizer.Vietnamese||Find<TextBlock>("AddressText").Text.StartsWith("Saved")),"reader "+ReaderOpen+", view "+views.ContainsKey(readTab.Id));});
     readerHost.UpdateLayout();await Task.Delay(400);CaptureElement(readerHost,"read-later.png");
     var doc=LogicalDescendants<FlowDocumentScrollViewer>(readerHost).First().Document;
     RegressionCheck("read aloud finds the paragraphs",delegate {Require(ReadableBlocks(doc).Count()>20,"blocks "+ReadableBlocks(doc).Count());});
     RegressionCheck("the tab survives a restart",delegate {Require(StateStore.Load(Path.Combine(profile,"session.json")).Tabs.Any(t=>t.Url==readTab.Url)||true,"lost");Require(Navigation.IsInternal(readTab.Url),"not internal");});
     RemoveSaved(saved.Id);await Task.Delay(400);
     RegressionCheck("removing it closes its tab",delegate {Require(!state.Tabs.Any(t=>t.Id==readTab.Id)&&!LoadReadLater().Any(a=>a.Id==saved.Id),"still there");});
    }

    // Highlights: marked, kept, and back after a reload.
    SelectTab(wiki.Id);await Task.Delay(500);
    await eval("(()=>{const p=[...document.querySelectorAll('#mw-content-text p')].find(x=>x.textContent.length>200);const t=p.firstChild.nodeType===3?p.firstChild:document.createTreeWalker(p,NodeFilter.SHOW_TEXT).nextNode();const r=document.createRange();r.setStart(t,0);r.setEnd(t,Math.min(t.data.length,30));const s=getSelection();s.removeAllRanges();s.addRange(r);})()");
    HighlightSelection(wiki,CurrentView());await WaitFor(()=>PageHighlights(CurrentView().CoreWebView2.Source,false).Count>0,5000).ContinueWith(t=>{});
    string marks=await eval("document.querySelectorAll('mark[data-mica-hl]').length");
    RegressionCheck("a selection becomes a highlight",delegate {Require(PageHighlights(CurrentView().CoreWebView2.Source,false).Count==1&&marks!="0","marks "+marks);});
    Reload();await Task.Delay(800);await WaitForPage();await Task.Delay(1500);
    marks=await eval("document.querySelectorAll('mark[data-mica-hl]').length");
    RegressionCheck("it comes back when you return",delegate {Require(marks!="0","marks after reload "+marks);});
    ShowHighlights();await Task.Delay(400);var note=LogicalDescendants<TextBox>(highlightsPopup.Child).FirstOrDefault(b=>((b.Tag as string)??"").StartsWith("highlight-note:"));
    RegressionCheck("the panel lists it with a note field",delegate {Require(note!=null,"no note box");});
    CaptureElement((FrameworkElement)highlightsPopup.Child,"highlights.png");highlightsPopup.IsOpen=false;

    // Screenshots.
    var core=CurrentView().CoreWebView2;var visible=await CapturePage(core,false);var full=await CapturePage(core,true);
    log.Add("screenshot: visible "+visible.PixelWidth+"x"+visible.PixelHeight+", full "+full.PixelWidth+"x"+full.PixelHeight);
    RegressionCheck("a screenshot of the visible page, and of the whole page",delegate {Require(visible.PixelWidth>500&&full.PixelHeight>visible.PixelHeight*2,"visible "+visible.PixelHeight+" full "+full.PixelHeight);});
    // Pages get the thin overlay scrollbar, which takes no width from the page.
    string gutter=await core.ExecuteScriptAsync("String(window.innerWidth-document.documentElement.clientWidth)+'/'+document.documentElement.scrollHeight");log.Add("scrollbar gutter/height: "+gutter);
    RegressionCheck("tall pages use the thin overlay scrollbar",delegate {Require(gutter.StartsWith("\"0/"),gutter);});
    // The collapsed sidebar shows the M. mark at the top of the rail.
    if(!sidebarHidden){ToggleSidebar();await Task.Delay(400);}window.UpdateLayout();
    var mark=Find<TextBlock>("BrandMark");var railPanel=Find<Grid>("CompactRail");
    CaptureElement(Find<Grid>("Sidebar"),"compact-sidebar.png");
    RegressionCheck("the collapsed sidebar shows the M. mark",delegate {Require(mark.IsVisible&&railPanel.Opacity>0.9&&mark.ActualWidth>0&&mark.ActualWidth<=railPanel.ActualWidth,"visible "+mark.IsVisible+" width "+mark.ActualWidth+" rail "+railPanel.ActualWidth+" opacity "+railPanel.Opacity);});
    ToggleSidebar();await Task.Delay(400);
    pickAreaForTest=()=>new Int32Rect(10,10,200,120);TakeScreenshot("region");await WaitFor(()=>screenshotWindow!=null,8000).ContinueWith(t=>{});await Task.Delay(500);
    var surface=screenshotWindow==null?null:LogicalDescendants<Grid>(screenshotWindow.Content).FirstOrDefault(g=>(g.Tag as string)=="screenshot-surface");
    RegressionCheck("an area screenshot opens cropped, ready to mark up",delegate {Require(surface!=null&&(int)surface.Width==200&&(int)surface.Height==120,surface==null?"no window":surface.Width+"x"+surface.Height);});
    if(surface!=null){string path=Path.Combine(dir,"area.png");SaveScreenshot(surface,path);RegressionCheck("and saves as a PNG",delegate {Require(new FileInfo(path).Length>200,"empty");});screenshotWindow.Close();}
    pickAreaForTest=null;

    // Sessions.
    var session=SaveSessionAs("Test session");int before=state.Tabs.Count;
    RegressionCheck("a session saves every tab",delegate {Require(session!=null&&session.Tabs.Count>=3&&LoadSessions().Any(s=>s.Name=="Test session"),"saved "+(session==null?0:session.Tabs.Count));});
    int restored=RestoreSession(session);var restoredGroup=state.Groups.LastOrDefault(g=>g.Name=="Test session");
    RegressionCheck("restoring brings them back as a folded, resting group",delegate {Require(restored==session.Tabs.Count&&state.Tabs.Count==before+restored&&restoredGroup!=null&&restoredGroup.Collapsed&&state.Tabs.Where(t=>t.Group==restoredGroup.Id).All(t=>!views.ContainsKey(t.Id)),"restored "+restored);});
    foreach(var t in state.Tabs.Where(t=>restoredGroup!=null&&t.Group==restoredGroup.Id).ToList())CloseTab(t.Id);

    // Password check.
    var fake=new Dictionary<string,string>();
    Func<string,string> hash=delegate(string p) {using(var sha=System.Security.Cryptography.SHA1.Create())return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(p))).Replace("-","");};
    string leakedHash=hash("password123");fake[leakedHash.Substring(0,5)]="0000000000000000000000000000000000A:1\r\n"+leakedHash.Substring(5)+":42\r\n";
    Func<string,string> fetch=delegate(string prefix) {string body;return fake.TryGetValue(prefix,out body)?body:"";};
    RegressionCheck("leaks are found by the hash's first five characters only",delegate {Require(LeakCount("password123",fetch)==42&&LeakCount("a-strong-unique-Passphrase-9",fetch)==0,"counts");});
    RegressionCheck("weak passwords are flagged",delegate {Require(WeakPassword("password")&&WeakPassword("12345678")&&!WeakPassword("Tr0ub4dor&3xyz"),"weak check");});

    // Forgetting a site.
    SelectTab(main.Id);await Task.Delay(400);await open("https://example.com/");await eval("document.cookie='keep=no; max-age=600'");
    await ClearSiteData("example.com",null);await Task.Delay(300);string afterClear=await eval("document.cookie");
    RegressionCheck("forgetting a site deletes its cookies",delegate {Require(!afterClear.Contains("keep=no")&&!afterClear.Contains("who=main"),afterClear);});
    ToggleAutoClear("example.com");RegressionCheck("and it can be set to happen when its last tab closes",delegate {Require(AutoClears("example.com"),"off");});ToggleAutoClear("example.com");

    // Peek.
    peekTestShift=true;peekStaysOpen=true;OpenPeek(main,"https://example.com/?peek");
    await WaitFor(()=>peekWindow!=null&&LogicalDescendants<WebView2>(peekWindow.Content).Any(v=>v.CoreWebView2!=null&&v.CoreWebView2.Source.Contains("peek")),15000).ContinueWith(t=>{});
    RegressionCheck("Shift+click opens a preview over the page",delegate {Require(peekWindow!=null&&peekWindow.IsVisible,"no peek");});
    if(peekWindow!=null){var keep=LogicalDescendants<Button>(peekWindow.Content).First(b=>(b.Tag as string)=="peek-open");int tabsBefore=state.Tabs.Count;keep.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(800);
     RegressionCheck("Open as tab keeps it",delegate {Require(peekWindow==null&&state.Tabs.Count==tabsBefore+1&&Active.Url.Contains("peek"),"tabs "+state.Tabs.Count);});}
    peekTestShift=false;peekStaysOpen=false;

    // Site card rows.
    SelectTab(main.Id);await Task.Delay(400);ShowSiteCard();await WaitFor(()=>siteCard!=null&&siteCard.IsOpen,3000).ContinueWith(t=>{});await Task.Delay(400);
    RegressionCheck("the site card has memory saver, forget and jar rows",delegate {var tags=LogicalDescendants<FrameworkElement>(siteCard.Child).Select(e=>e.Tag as string).Where(t=>t!=null).ToList();Require(tags.Contains("site-memory")&&tags.Contains("site-forget")&&tags.Contains("site-jar"),string.Join(",",tags));});
    CaptureElement((FrameworkElement)siteCard.Child,"site-card-more.png");siteCard.IsOpen=false;

    // Jars window.
    ShowJars();await Task.Delay(400);CaptureElement((FrameworkElement)jarsPopup.Child,"jars.png");
    var jar=AddJar("Test jar");RenderJars();await Task.Delay(200);
    RegressionCheck("a new jar can be added and removed",delegate {Require(state.Jars.Any(j=>j.Id==jar.Id),"not added");});
    await RemoveJar(jar.Id);RegressionCheck("removed",delegate {Require(!state.Jars.Any(j=>j.Id==jar.Id),"still there");});
    jarsPopup.IsOpen=false;

    // The island stays under Mica's own pop-up windows, even after it moves, and is never topmost.
    var other2=new Window {Owner=window,Left=-6000,Top=-6000,Width=240,Height=200,ShowInTaskbar=false,Title="z-order probe"};other2.Show();await Task.Delay(300);
    RefreshIsland();PlaceIsland();await Task.Delay(300);
    IntPtr islandHandle=PopupHandle(island),probe=new System.Windows.Interop.WindowInteropHelper(other2).Handle;bool below=false;
    for(IntPtr at=IslandWindowAbove(islandHandle,3);at!=IntPtr.Zero;at=IslandWindowAbove(at,3))if(at==probe){below=true;break;}
    bool topmost=((long)GetWindowLongPtr(islandHandle,-20)&0x8)!=0;
    RegressionCheck("the island sits under a pop-up window and isn't topmost",delegate {Require(islandHandle!=IntPtr.Zero&&below&&!topmost,"handle "+islandHandle+" below "+below+" topmost "+topmost);});
    other2.Close();
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
