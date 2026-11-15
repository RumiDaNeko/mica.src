using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // The command palette: Spotlight with ">" in front runs an action instead of opening a page
 // ("> mute", "> split", "> jar work"). Only actions that make sense right now are listed.
 partial class BrowserApp {
  sealed class PaletteCommand {public string Title,Detail,Words;public Action Run;}
  readonly Dictionary<string,Action> paletteActions=new Dictionary<string,Action>();

  List<PaletteCommand> PaletteCommands() {
   var list=new List<PaletteCommand>();var tab=Active;bool web=tab!=null&&Navigation.IsWeb(tab.Url);
   WebView2 view=CurrentView();var core=web&&view!=null?LiveCore(view):null;string site=web?SiteFor(tab.Url):null;
   Action<string,string,string,Action> add=delegate(string title,string detail,string words,Action run) {list.Add(new PaletteCommand {Title=title,Detail=detail??"",Words=words??"",Run=run});};

   add("New tab","Ctrl+T","open",NewTab);
   add("New window","Ctrl+N","open",delegate {LaunchBrowserWindow(false);});
   add("New private window","Ctrl+Shift+N","incognito inprivate",delegate {LaunchBrowserWindow(true);});
   if(lastClosedBatch!=null)add("Bring back "+lastClosedBatch.Tabs.Count+" closed tabs","Ctrl+Shift+T","undo reopen restore clear",delegate {UndoCloseTabs();});
   else if(state.RecentlyClosed.Count>0)add("Reopen closed tab","Ctrl+Shift+T","undo restore "+state.RecentlyClosed[0].Title,ReopenClosedTab);
   if(ClosableInSpace().Count>0)add("Clear tabs in "+state.Space,"","close all tabs space session",ClearSpaceTabs);
   if(tab!=null&&ClosableInSpace().Any(t=>t.Id!=tab.Id))add("Close other tabs","","all except this",delegate {CloseOtherTabs(tab);});
   if(DuplicateCount()>0)add("Close duplicate tabs","","same dedupe twice",CloseDuplicateTabs);
   if(tab!=null&&tab.Url!="mica://home") {
    if(!tab.Pinned)add("Close tab","Ctrl+W","",delegate {CloseTab(tab.Id);});
    add(tab.Pinned?"Unpin tab":"Pin tab","","keep",delegate {tab.Pinned=!tab.Pinned;animateTabMoves=true;state.NormalizeGroups();RenderTabs();Save();});
    if(web)add("Duplicate tab","","copy clone",DuplicateTab);
   }
   if(core!=null) {
    bool muted=false;try {muted=core.IsMuted;}catch(Exception){}
    add(muted?"Unmute tab":"Mute tab","","sound audio silence",delegate {ToggleMute(tab.Id);});
    add("Reload","Ctrl+R","refresh",Reload);
    add(state.Bookmarks.Any(b=>b.Url==tab.Url)?"Remove bookmark":"Bookmark this page","Ctrl+D","favorite star",Bookmark);
    add("Copy page address","","url link clipboard",CopyAddress);
    add(SplitOpen?"End split view":"Split view","Ctrl+Shift+S","side by side",ToggleSplit);
    if(SplitOpen&&splitPanes.Count<MaxPanes)add("Add a tab to the split","","pane three four",delegate {AddToSplit(null);});
    add(ReaderOpen?"Leave reading mode":"Reading mode","F9","reader article",ToggleReader);
    add("Save for later","","read later reading list offline",SaveForLater);
    add("Translate page to "+LanguageName(UiLanguage),"","translation language",delegate {TranslatePage(UiLanguage);});
    if(UiLanguage!="en")add("Translate page to English","","translation language",delegate {TranslatePage("en");});
    add("Highlights and notes","","annotate mark",ShowHighlights);
    add("Zoom in","Ctrl+Plus","bigger larger",delegate {ZoomView(view,NextZoom(view.ZoomFactor,1));ShowIslandZoom(view);});
    add("Zoom out","Ctrl+Minus","smaller",delegate {ZoomView(view,NextZoom(view.ZoomFactor,-1));ShowIslandZoom(view);});
    if(Math.Abs(view.ZoomFactor-PageBase)>0.001)add("Reset zoom","Ctrl+0",Math.Round(PageBase*100)+"%",delegate {ZoomView(view,PageBase);ShowIslandZoom(view);});
    add("Site settings","","permissions cookies "+site,ShowSiteCard);
    add("Ads on this site: "+(AdsHint()=="Blocked"?"allow":"block"),"","adblock ublock",ToggleAdsHere);
    string mode=SiteMemoryMode(tab);
    if(mode!="never")add("Keep "+site+" awake","","memory saver never rest music",delegate {SetSiteMemory(site,"never");});
    if(mode!="quick")add("Rest "+site+" quickly","","memory saver sleep",delegate {SetSiteMemory(site,"quick");});
    if(mode!=null)add("Usual memory saver for "+site,"","memory",delegate {SetSiteMemory(site,null);});
    if(!privateMode)add(AutoClears(site)?"Stop forgetting "+site:"Forget "+site+" when I close it","","auto clear cookies delete",delegate {ToggleAutoClear(site);});
    add("Screenshot of what's visible","Ctrl+Shift+X","capture image",delegate {TakeScreenshot("visible");});
    add("Screenshot of the whole page","","capture full page image",delegate {TakeScreenshot("full");});
    add("Screenshot of an area","","capture region select",delegate {TakeScreenshot("region");});
    add("Picture-in-picture","","video float pip",PictureInPicture);
    add("Find in page","Ctrl+F","search",ShowFind);
    add("Print","Ctrl+P","pdf",ShowPrint);
    add("Developer tools","F12","devtools inspect",ToggleDevTools);
    if(!privateMode)add("Send tab to a device","","phone laptop share",ShowSendTab);
    if(!privateMode) {
     foreach(var jar in state.Jars)if(tab.Jar!=jar.Id){var j=jar;add("Move tab to the "+j.Name+" jar","","cookie jar container account",delegate {SetTabJar(tab,j.Id);});}
     if(tab.Jar!=null)add("Move tab out of its jar","","cookie jar container",delegate {SetTabJar(tab,null);});
    }
   }
   if(!privateMode)foreach(var jar in state.Jars){var j=jar;add("New tab in the "+j.Name+" jar","","cookie jar container account",delegate {NewTabInJar(j.Id);});}
   if(!privateMode)add("Cookie jars…","","container manage",ShowJars);
   add("Read later","","saved articles reading list",ShowReadLater);
   add("History","Ctrl+H","",delegate {OpenLibraryPage("mica://history");});
   add("Downloads","Ctrl+J","",delegate {OpenLibraryPage("mica://downloads");});
   add("Bookmarks","","favorites",delegate {ShowLibrary(true);});
   add("Search tabs","Ctrl+Shift+A","find tab",OpenTabSearch);
   add("Passwords & sync","","vault logins",ShowVault);
   if(!privateMode)add("Import from another browser","","import chrome edge firefox brave opera bookmarks passwords extensions",ShowImport);
   if(!privateMode)add("Check my passwords","","breach leak weak reused security",ShowPasswordCheck);
   add("Ad blocking","","ublock lists filters",ShowAdBlocking);
   add("Extensions","","addons",ShowExtensions);
   add("Profiles","","account person",ShowProfiles);
   foreach(var other in privateMode?new List<ProfileInfo>():LoadProfiles())if(other.Id!=ProfileId){var p=other;add("Open profile: "+p.Name,"","window",delegate {OpenProfile(p.Id);});}
   if(!privateMode){add("Save this session…","","snapshot windows tabs",SaveSession);add("Restore a session…","","snapshot open",ShowSessions);}
   add("Site shortcuts…","","keywords search engines yt",ShowKeywords);
   add(state.TreeTabs?"Turn off tree tabs":"Tree tabs","","indent nested opener vertical",ToggleTreeTabs);
   if(state.Groups.Any(g=>!g.Collapsed))add("Fold all groups","","collapse rest",FoldAllGroups);
   add("Rest background tabs","","sleep memory free",delegate {SleepIdleTabs(true);});
   add("Task manager","Shift+Esc","memory processes",ShowTaskManager);
   add("scrubnote: record page animations","Alt+Shift+S","animation timing frames scrub motion",ToggleScrubnote);
   add(state.Dark?"Light theme":"Dark theme","","mode appearance",ToggleTheme);
   add("Personalize","","colour color background",ShowPersonalize);
   add(sidebarHidden?"Show sidebar":"Hide sidebar","Ctrl+Shift+B","tabs panel",ToggleSidebar);
   foreach(var name in state.Spaces.Where(s=>s!=state.Space)){string space=name;add("Switch to "+space,"","space",delegate {SwitchSpace(space);});}
   add("New space","","space create add",ShowNewSpace);
   if(!privateMode){if(state.AiPanel)add(aiOpen?"Close AI panel":"Open AI panel","Ctrl+Shift+Space","ai chat chatgpt claude gemini copilot",ToggleAiPanel);add(state.AiPanel?"Turn off AI side panel":"Turn on AI side panel","","ai chat assistant chatgpt claude gemini copilot",delegate {SetAiEnabled(!state.AiPanel);});add("AI side panel settings","","ai chat assistant chatgpt claude gemini copilot perplexity choose",ShowAiSettings);}
   add("Games","","play tetris snake 2048 minesweeper breakout sudoku memory offline",delegate {Navigate("mica://games",true);});
   foreach(var game in new[]{"Tetris","Snake","2048","Minesweeper","Breakout","Sudoku","Memory"}){string name=game;add("Play "+name,"","game",delegate {Navigate("mica://"+name.ToLowerInvariant(),true);});}
   add("Clear browsing data","Ctrl+Shift+Del","history cookies cache",ShowClearBrowsingData);
   add("Keyboard shortcuts","","keys help",ShowShortcuts);
   return list;
  }
  // "> sp vi" matches "Split view": every word must start a word of the title or its keywords.
  List<SearchHit> SearchCommands(string query) {
   var words=query.TrimStart('>').Trim().ToLowerInvariant().Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
   paletteActions.Clear();var hits=new List<SearchHit>();int n=0;
   foreach(var command in PaletteCommands()) {
    var haystack=(command.Title+" "+command.Words).ToLowerInvariant().Split(new[]{' ','-','…',':','\''},StringSplitOptions.RemoveEmptyEntries);
    if(!words.All(w=>haystack.Any(h=>h.StartsWith(w,StringComparison.Ordinal))))continue;
    string key="mica-command:"+(n++);paletteActions[key]=command.Run;
    hits.Add(new SearchHit {Kind="ACTION",Title=command.Title,Detail=command.Detail.Length>0?command.Detail:"Press Enter",Url=key});
    if(hits.Count>=30)break;
   }
   return hits;
  }
  bool RunPaletteHit(SearchHit hit) {
   Action run;if(hit==null||hit.Kind!="ACTION"||!paletteActions.TryGetValue(hit.Url,out run))return false;
   CloseSpotlight();window.Dispatcher.BeginInvoke(new Action(delegate {if(!closing)run();}));return true;
  }

  // ---- Closing many tabs at once: one pass, one render, and one undo that puts every tab back in its place ----
  sealed class ClosedBatch {public readonly List<PageEntry> Tabs=new List<PageEntry>();public readonly List<int> Places=new List<int>();public readonly List<TabGroup> Groups=new List<TabGroup>();public string ActiveId,AddedHome;}
  ClosedBatch lastClosedBatch;
  System.Windows.Threading.DispatcherTimer clearUndoTimer;
  List<PageEntry> ClosableInSpace() {return state.Tabs.Where(t=>t.Space==state.Space&&!t.Pinned).ToList();}
  internal int CloseTabs(IEnumerable<string> ids,string activate=null) {
   var set=new HashSet<string>(ids);var batch=new ClosedBatch {ActiveId=state.ActiveId};
   for(int i=0;i<state.Tabs.Count;i++){var t=state.Tabs[i];if(!set.Contains(t.Id))continue;batch.Tabs.Add(t);batch.Places.Add(i);var g=state.FindGroup(t.Group);if(g!=null&&!batch.Groups.Contains(g))batch.Groups.Add(g);}
   if(batch.Tabs.Count==0)return 0;
   var sites=new HashSet<Tuple<string,string>>();
   foreach(var t in batch.Tabs){if(Navigation.IsWeb(t.Url))sites.Add(Tuple.Create(SiteFor(t.Url),t.Jar));SplitEnded(t.Id);snapshots.Remove(t.Id);if(!privateMode)state.RecordClosed(t);DisposeView(t.Id);}
   foreach(var s in sites){var site=s;window.Dispatcher.BeginInvoke(new Action(delegate {LeftSite(site.Item1,site.Item2);}));}
   state.Tabs.RemoveAll(t=>set.Contains(t.Id));state.NormalizeGroups();
   // The space stays where you are: an emptied space gets a home tab instead of jumping to another space.
   if(!state.Tabs.Any(t=>t.Space==state.Space))batch.AddedHome=state.AddTab("mica://home").Id;
   else if(activate!=null&&state.Tabs.Any(t=>t.Id==activate))state.ActiveId=activate;
   else if(set.Contains(state.ActiveId)) {
    // The nearest tab that stays, below the one you were on first, as closing a single tab does.
    int place=batch.Places[batch.Tabs.FindIndex(t=>t.Id==batch.ActiveId)],was=place-batch.Places.Count(p=>p<place);
    var left=state.Tabs.Select((t,i)=>new {t,i}).Where(x=>x.t.Space==state.Space).ToList();
    state.ActiveId=(left.FirstOrDefault(x=>x.i>=was&&!x.t.Pinned)??left.LastOrDefault(x=>x.i<was&&!x.t.Pinned)??left.Last()).t.Id;
   }
   lastClosedBatch=batch;animateTabMoves=true;
   SelectTab(SplitOpen&&!InSplit(state.ActiveId)?splitPanes[0]:state.ActiveId);RefreshPersonalization();Save();
   return batch.Tabs.Count;
  }
  internal bool UndoCloseTabs() {
   var batch=lastClosedBatch;if(batch==null)return false;lastClosedBatch=null;ShowClearButton(false);
   foreach(var g in batch.Groups)if(state.FindGroup(g.Id)==null)state.Groups.Add(g);
   for(int i=0;i<batch.Tabs.Count;i++){var t=batch.Tabs[i];if(state.Tabs.Any(x=>x.Id==t.Id))continue;state.Tabs.Insert(Math.Min(batch.Places[i],state.Tabs.Count),t);state.RecentlyClosed.RemoveAll(c=>c.Url==t.Url);}
   var home=batch.AddedHome==null?null:state.Tabs.FirstOrDefault(t=>t.Id==batch.AddedHome&&t.Url=="mica://home");
   if(home!=null){DisposeView(home.Id);state.Tabs.Remove(home);}
   state.NormalizeGroups();if(state.Tabs.Any(t=>t.Id==batch.ActiveId))state.ActiveId=batch.ActiveId;else if(home!=null)state.ActiveId=batch.Tabs[batch.Tabs.Count-1].Id;
   animateTabMoves=true;SelectTab(state.ActiveId);MarkRestingTabs(false);Save();
   Status("Brought back "+batch.Tabs.Count+(batch.Tabs.Count==1?" tab.":" tabs."));return true;
  }
  void ClosedMany(int n) {if(n==0)return;Status("Closed "+n+(n==1?" tab":" tabs")+". Ctrl+Shift+T brings them back.");ShowClearButton(true);}
  // Clears this space: pinned tabs and other spaces stay. For a few seconds the button offers Undo.
  internal void ClearSpaceTabs() {
   var tabs=ClosableInSpace();if(tabs.Count==0){Status("No tabs to clear: pinned tabs stay.");return;}
   ClosedMany(CloseTabs(tabs.Select(t=>t.Id)));
  }
  internal void CloseOtherTabs(PageEntry keep) {ClosedMany(CloseTabs(ClosableInSpace().Where(t=>t.Id!=keep.Id).Select(t=>t.Id),keep.Id));}
  internal void CloseTabsBelow(PageEntry from) {
   var tabs=ClosableInSpace();int at=tabs.FindIndex(t=>t.Id==from.Id);
   ClosedMany(CloseTabs(tabs.Skip(at<0?tabs.Count:at+1).Select(t=>t.Id)));
  }
  // The same address open twice: keeps the one you're on (or a pinned one, or the first) and closes the rest.
  internal void CloseDuplicateTabs() {
   var close=new List<string>();
   foreach(var same in state.Tabs.Where(t=>t.Space==state.Space&&(Navigation.IsWeb(t.Url)||t.Url=="mica://home")).GroupBy(t=>t.Url.TrimEnd('/'))) {
    var keep=same.FirstOrDefault(t=>t.Id==state.ActiveId)??same.FirstOrDefault(t=>t.Pinned)??same.First();
    close.AddRange(same.Where(t=>t!=keep&&!t.Pinned).Select(t=>t.Id));
   }
   if(close.Count==0){Status("No duplicate tabs here.");return;}
   ClosedMany(CloseTabs(close));
  }
  int DuplicateCount() {return state.Tabs.Where(t=>t.Space==state.Space&&(Navigation.IsWeb(t.Url)||t.Url=="mica://home")).GroupBy(t=>t.Url.TrimEnd('/')).Sum(g=>g.Count(t=>!t.Pinned)-(g.Any(t=>t.Pinned)?0:1));}
  void ShowClearButton(bool undo) {
   var button=Find<System.Windows.Controls.Button>("ClearTabsButton");if(button==null)return;
   button.Content=undo?"Undo":"Clear";button.Tag=undo?"undo":"clear";button.ToolTip=undo?"Bring the closed tabs back (Ctrl+Shift+T)":"Close every tab in this space. Pinned tabs stay.";
   if(clearUndoTimer==null){clearUndoTimer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(10)};clearUndoTimer.Tick+=delegate {clearUndoTimer.Stop();ShowClearButton(false);};}
   clearUndoTimer.Stop();if(undo)clearUndoTimer.Start();
  }
  void InitializeClearButton() {
   var button=Find<System.Windows.Controls.Button>("ClearTabsButton");if(button==null)return;
   button.Click+=delegate {if((string)button.Tag=="undo")UndoCloseTabs();else ClearSpaceTabs();};
   LazyMenu(button,delegate {
    var menu=CreateContextMenu();
    menu.Items.Add(ContextCommand("Clear tabs in "+state.Space,"","",ClearSpaceTabs));
    var dupes=ContextCommand("Close duplicate tabs","","",CloseDuplicateTabs);dupes.IsEnabled=DuplicateCount()>0;menu.Items.Add(dupes);
    if(state.Groups.Any(g=>!g.Collapsed))menu.Items.Add(ContextCommand("Fold all groups","","",FoldAllGroups));
    menu.Items.Add(ContextCommand("Rest background tabs","","",delegate {SleepIdleTabs(true);}));
    if(!privateMode)menu.Items.Add(ContextCommand("Save this session…","","",SaveSession));
    if(lastClosedBatch!=null){menu.Items.Add(ContextSeparator());menu.Items.Add(ContextCommand("Bring back "+lastClosedBatch.Tabs.Count+" closed tabs","","Ctrl+Shift+T",delegate {UndoCloseTabs();}));}
    return menu;
   });
  }

  internal void ReopenClosedTab() {
   if(UndoCloseTabs())return;
   if(state.RecentlyClosed.Count==0){Status("No closed tabs to bring back.");return;}
   var last=state.RecentlyClosed[0];state.RecentlyClosed.RemoveAt(0);Navigate(last.Url,true);
  }
  internal void DuplicateTab() {var tab=Active;if(tab==null||!Navigation.IsWeb(tab.Url))return;OpenFromTab(tab,tab.Url);}
  internal void CopyAddress() {var tab=Active;if(tab==null||!Navigation.IsWeb(tab.Url))return;try {Clipboard.SetText(tab.Url);Status("Address copied.");}catch(Exception){Status("Couldn't copy the address.");}}
  internal void FoldAllGroups() {
   foreach(var group in state.Groups.Where(g=>!g.Collapsed).ToList()){group.Collapsed=true;RestGroup(group.Id);}
   Save();RenderTabs();
  }
  static string UiLanguage {get {var name=CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;return string.IsNullOrEmpty(name)||name=="iv"?"en":name;}}
  static string LanguageName(string code) {try {return new CultureInfo(code).NativeName.Split('(')[0].Trim();}catch(Exception){return code;}}
 }
}
