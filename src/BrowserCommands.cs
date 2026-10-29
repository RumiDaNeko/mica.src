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
   if(state.RecentlyClosed.Count>0)add("Reopen closed tab","Ctrl+Shift+T","undo restore "+state.RecentlyClosed[0].Title,ReopenClosedTab);
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

  internal void ReopenClosedTab() {
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
