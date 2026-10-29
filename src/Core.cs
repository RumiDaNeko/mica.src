using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
namespace Mica {
 public enum NavigationResult { Ignore, Ready, Stopped, Failed }
 public class NavigationProgress {
  public ulong CurrentId {get; private set;}
  public bool IsLoading {get; private set;}
  public void Begin(ulong id) { CurrentId=id;IsLoading=true; }
  public NavigationResult Complete(ulong id,bool success,int httpStatus,string error) {
   if(id!=CurrentId)return NavigationResult.Ignore;
   IsLoading=false;
   if(success)return NavigationResult.Ready;
   if(error=="OperationCanceled"||error=="ValidAuthenticationCredentialsRequired"||error=="ValidProxyAuthenticationRequired")return NavigationResult.Stopped;
   if(error=="ConnectionAborted"&&httpStatus>=200&&httpStatus<300)return NavigationResult.Stopped;
   return NavigationResult.Failed;
  }
 }
 public class PageEntry { public string Id {get;set;} public string Title {get;set;} public string Url {get;set;} public string Space {get;set;} public bool Pinned {get;set;} public DateTime LastUsed {get;set;} public int VisitCount {get;set;} public string Group {get;set;}
  // The cookie jar the tab lives in (null = the profile's own) and the tab it was opened from (tree tabs).
  public string Jar {get;set;} public string Opener {get;set;} }
 // A separate set of cookies and site data inside one profile: two accounts on one site in two tabs.
 public class CookieJar { public string Id {get;set;} public string Name {get;set;} public string Color {get;set;} }
 // A named, coloured run of tabs in the sidebar; its tabs sit together and the group can fold away.
 public class TabGroup { public string Id {get;set;} public string Name {get;set;} public string Color {get;set;} public bool Collapsed {get;set;} }
 public class SearchHit { public string Kind {get;set;} public string Title {get;set;} public string Detail {get;set;} public string Url {get;set;} public string TabId {get;set;} }
 public class BrowserState {
  public List<PageEntry> Tabs {get;set;} public List<PageEntry> Bookmarks {get;set;} public List<PageEntry> History {get;set;} public string ActiveId {get;set;} public string Space {get;set;} public bool Dark {get;set;}
  public bool AmbientYoutube {get;set;}
  // Mica's text size: 1 (default), 1.15 or 1.3.
  public double TextScale {get;set;}
  public bool StrictTracking {get;set;}
  public bool DockedDevTools {get;set;}
  // Zoom chosen per site (host without www.), 1 = 100%; sites at 100% aren't kept.
  public Dictionary<string,double> SiteZoom {get;set;}
  // Reading mode: text size in px (0 = default 18) and serif type.
  public int ReaderSize {get;set;} public bool ReaderSerif {get;set;}
  // 0 Off, 1 Basic, 2 Balanced, 3 Strict; null = from StrictTracking (older sessions).
  public int? TrackingLevel {get;set;}
  // Personalize: frame colour (#RRGGBB, null = theme default), home background (null, a preset name or "photo") and frosted-glass strength 0–100.
  public string FrameColor {get;set;} public string HomeBackground {get;set;} public int? HomeGlass {get;set;}
  public List<PageEntry> RecentlyClosed {get;set;}
  public List<TabGroup> Groups {get;set;}
  public List<CookieJar> Jars {get;set;}
  // Address-bar shortcuts: keyword -> address with %s for the words ("yt" -> youtube search).
  public Dictionary<string,string> SiteKeywords {get;set;}
  // Memory saver per site: "never" (stays awake) or "quick" (rests soon after you leave).
  public Dictionary<string,string> SiteMemory {get;set;}
  // Sites whose cookies and data are forgotten when their last tab closes.
  public List<string> AutoClearSites {get;set;}
  // Tabs opened from a page sit indented under it.
  public bool TreeTabs {get;set;}
  // The animation as a window closes: stars (default, null), fade, drop, shatter or off.
  public string CloseEffect {get;set;}
  // The AI side panel: off unless turned on; which chat site it opens (an id, or "custom" with an https address).
  public bool AiPanel {get;set;} public string AiProvider {get;set;} public string AiCustomUrl {get;set;}
  // Spaces in menu order: Personal is always first; Work and any you make follow.
  public List<string> Spaces {get;set;}
  public const int MaxSpaces=12,MaxSpaceName=30;
  public string KnownSpace(string name) {return Spaces!=null&&name!=null&&Spaces.Contains(name)?name:"Personal";}
  // Null when the name is fine, else why not.
  public string SpaceNameProblem(string name,string renaming) {
   name=(name??"").Trim();if(name.Length==0)return "Give it a name, like School or Side project.";if(name.Length>MaxSpaceName)return "Keep the name under "+MaxSpaceName+" characters.";
   if(Spaces.Any(s=>s!=renaming&&string.Equals(s,name,StringComparison.OrdinalIgnoreCase)))return "There is already a space called "+name+".";
   if(renaming==null&&Spaces.Count>=MaxSpaces)return "You can have up to "+MaxSpaces+" spaces.";return null;
  }
  public void AddSpace(string name) {name=name.Trim();if(SpaceNameProblem(name,null)==null)Spaces.Add(name);}
  public void RenameSpace(string from,string to) {
   to=to.Trim();int at=Spaces.IndexOf(from);if(at<=0||SpaceNameProblem(to,from)!=null)return;Spaces[at]=to;
   foreach(var t in Tabs.Concat(RecentlyClosed))if(t.Space==from)t.Space=to;if(Space==from)Space=to;
  }
  // Removing a space keeps its tabs: they move to Personal.
  public int RemoveSpace(string name) {
   if(name=="Personal"||!Spaces.Remove(name))return 0;int moved=0;
   foreach(var t in Tabs)if(t.Space==name){t.Space="Personal";t.Group=null;moved++;}foreach(var t in RecentlyClosed)if(t.Space==name)t.Space="Personal";
   if(Space==name)Space="Personal";NormalizeGroups();return moved;
  }
  public BrowserState() { Spaces=new List<string> {"Personal","Work"}; Tabs=new List<PageEntry>(); Bookmarks=new List<PageEntry>(); History=new List<PageEntry>(); RecentlyClosed=new List<PageEntry>(); Groups=new List<TabGroup>(); Jars=StateStore.DefaultJars(); SiteKeywords=StateStore.DefaultKeywords(); SiteMemory=new Dictionary<string,string>(); AutoClearSites=new List<string>(); Space="Personal"; }
  public void ActivateTab(string id,DateTime now) { var next=Tabs.FirstOrDefault(t=>t.Id==id);if(next==null)return;var previous=Tabs.FirstOrDefault(t=>t.Id==ActiveId);if(previous!=null)previous.LastUsed=now;ActiveId=id;Space=next.Space;next.LastUsed=now; }
  public PageEntry AddTab(string url) { var t=new PageEntry { Id=Guid.NewGuid().ToString("N"),Title=Navigation.InternalTitle(url)??(Navigation.IsLocalFile(url)?System.IO.Path.GetFileName(new Uri(url).LocalPath):new Uri(url).Host),Url=url,Space=Space,LastUsed=DateTime.UtcNow }; Tabs.Add(t); ActivateTab(t.Id,DateTime.UtcNow); return t; }
  public void CloseTab(string id) { Tabs.RemoveAll(t=>t.Id==id); NormalizeGroups(); if(Tabs.Count==0)AddTab("mica://home"); else if(ActiveId==id) { var t=Tabs.LastOrDefault(x=>x.Space==Space)??Tabs.Last(); ActiveId=t.Id; Space=t.Space; } }
  // A page can send itself through huge URLs; history keeps only ordinary ones, so the session stays small.
  public const int MaxHistoryUrl=8192;
  public void Visit(string url,string title) { if(!Navigation.IsWeb(url)||url.Length>MaxHistoryUrl)return;if(title!=null&&title.Length>1024)title=title.Substring(0,1024);var previous=History.FirstOrDefault(t=>t.Url==url);int count=previous==null?1:(int)Math.Min(int.MaxValue,(long)Math.Max(1,previous.VisitCount)+1); History.RemoveAll(t=>t.Url==url); History.Insert(0,new PageEntry { Url=url,Title=title,LastUsed=DateTime.UtcNow,VisitCount=count }); if(History.Count>500)History.RemoveRange(500,History.Count-500); }
  public void RecordClosed(PageEntry tab) {
   if(tab==null||!Navigation.IsWeb(tab.Url))return;
   RecentlyClosed.RemoveAll(t=>t.Url==tab.Url);RecentlyClosed.Insert(0,new PageEntry {Url=tab.Url,Title=tab.Title,LastUsed=DateTime.UtcNow});
   if(RecentlyClosed.Count>8)RecentlyClosed.RemoveRange(8,RecentlyClosed.Count-8);
  }
  public static readonly string[] GroupColors={"#4A7FD9","#D9534F","#E0A526","#3E9B5F","#D45C9B","#8B62D9","#2E9FB5","#E07B35","#8A8A93"};
  public TabGroup FindGroup(string id) {return id==null?null:Groups.FirstOrDefault(g=>g.Id==id);}
  // A new group takes the first colour no other group is using.
  public TabGroup CreateGroup(PageEntry first) {
   var used=new HashSet<string>(Groups.Select(g=>g.Color));
   var group=new TabGroup {Id=Guid.NewGuid().ToString("N"),Name="",Color=GroupColors.FirstOrDefault(c=>!used.Contains(c))??GroupColors[Groups.Count%GroupColors.Length]};
   Groups.Add(group);if(first!=null){first.Pinned=false;first.Group=group.Id;}NormalizeGroups();return group;
  }
  // Moves a tab next to another (before it, or after it), taking on the pinned state and group it lands in.
  // With no anchor it goes to the end of its space.
  public void MoveTab(string id,string anchorId,bool after,bool pinned,string group) {
   var tab=Tabs.FirstOrDefault(t=>t.Id==id);if(tab==null||id==anchorId)return;
   Tabs.Remove(tab);tab.Pinned=pinned;tab.Group=pinned?null:group;
   int at=anchorId==null?-1:Tabs.FindIndex(t=>t.Id==anchorId);
   if(at<0)Tabs.Add(tab);else Tabs.Insert(after?at+1:at,tab);
   NormalizeGroups();
  }
  // Moves a whole group as one block next to a tab outside it.
  public void MoveGroup(string groupId,string anchorId,bool after) {
   var members=Tabs.Where(t=>t.Group==groupId).ToList();if(members.Count==0||members.Any(t=>t.Id==anchorId))return;
   foreach(var m in members)Tabs.Remove(m);
   int at=anchorId==null?-1:Tabs.FindIndex(t=>t.Id==anchorId);
   if(at<0)Tabs.AddRange(members);else Tabs.InsertRange(after?at+1:at,members);
   NormalizeGroups();
  }
  public void Ungroup(string groupId) {foreach(var t in Tabs.Where(t=>t.Group==groupId))t.Group=null;NormalizeGroups();}
  // Keeps the rules: pinned tabs aren't grouped, a group's tabs sit together where its first tab is,
  // and a group with no tabs left is gone.
  public void NormalizeGroups() {
   foreach(var t in Tabs)if(t.Pinned||FindGroup(t.Group)==null)t.Group=null;
   var ordered=new List<PageEntry>();var placed=new HashSet<string>();
   foreach(var t in Tabs) {
    if(t.Group==null){ordered.Add(t);continue;}
    if(!placed.Add(t.Group))continue;
    ordered.AddRange(Tabs.Where(x=>x.Group==t.Group));
   }
   Tabs.Clear();Tabs.AddRange(ordered);
   Groups.RemoveAll(g=>!Tabs.Any(t=>t.Group==g.Id));
  }
  // Rank sites by all visits to their origin; return the most recently used page.
  public List<PageEntry> TopSites(int count) {
   return History.Where(t=>t!=null&&Navigation.IsWeb(t.Url)).GroupBy(t=>new Uri(t.Url).GetLeftPart(UriPartial.Authority),StringComparer.OrdinalIgnoreCase)
    .Select(group=>new {Entry=group.OrderByDescending(t=>t.LastUsed).First(),Visits=group.Sum(t=>(long)Math.Max(1,t.VisitCount))})
    .OrderByDescending(site=>site.Visits).ThenByDescending(site=>site.Entry.LastUsed).ThenBy(site=>site.Entry.Url,StringComparer.Ordinal).Take(Math.Max(0,count)).Select(site=>site.Entry).ToList();
  }
 }
 public static class TabLifecycle {
  // Background tabs freeze within seconds (cheap to bring back); resting drops the page and
  // costs a reload, so it waits 15 minutes, or 5 when the PC is short of memory.
  public static TimeSpan RestAfter(bool lowMemory) {return TimeSpan.FromMinutes(lowMemory?5:15);}
  public static bool ShouldSleep(PageEntry tab,bool active,bool audio,DateTime now,bool force) {return ShouldSleep(tab,active,audio,now,force,RestAfter(false));}
  public static bool ShouldSleep(PageEntry tab,bool active,bool audio,DateTime now,bool force,TimeSpan after) {return !active&&!audio&&(force||now-tab.LastUsed>=after);}
 }
 public static class Navigation {
  // A search results page shows its query in the address bar, like Chrome.
  public static string SearchQuery(string value) {
   Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||!IsWeb(value))return null;
   string host=uri.Host.ToLowerInvariant();if(host.StartsWith("www."))host=host.Substring(4);
   bool google=Regex.IsMatch(host,@"^google\.(com|[a-z]{2}|co\.[a-z]{2}|com\.[a-z]{2})$")&&uri.AbsolutePath=="/search",bing=host=="bing.com"&&uri.AbsolutePath=="/search",duck=host=="duckduckgo.com"&&(uri.AbsolutePath=="/"||uri.AbsolutePath=="");
   if(!google&&!bing&&!duck)return null;
   foreach(var pair in uri.Query.TrimStart('?').Split('&')) {
    int split=pair.IndexOf('=');if(split<=0||pair.Substring(0,split)!="q")continue;
    string query=Uri.UnescapeDataString(pair.Substring(split+1).Replace('+',' ')).Trim();
    // A query that reads like an address would pass for one in the address bar, so the real URL shows instead.
    if(query.Length==0||LooksLikeAddress(query))return null;return query;
   }
   return null;
  }
  static bool LooksLikeAddress(string query) {return query.Contains("://")||query.Contains("/")||Regex.IsMatch(query,@"^[^\s]+\.[a-z]{2,}(:\d+)?$",RegexOptions.IgnoreCase);}
  // The bar cuts text at its end, so what it shows always ends in the real site: no user@ part, and a long
  // host is cut from the left ("…google.com.evil.example") with the path dropped.
  public static string AddressLabel(string value) {
   var query=SearchQuery(value);if(query!=null)return query;Uri uri;
   if(value==null||!IsWeb(value)||!Uri.TryCreate(value,UriKind.Absolute,out uri))return value;
   string host=uri.IdnHost,shown=uri.Scheme+"://"+host+(uri.IsDefaultPort?"":":"+uri.Port);
   if(host.Length>48){var labels=host.Split('.');string tail=labels[labels.Length-1];for(int i=labels.Length-2;i>=0&&tail.Length+labels[i].Length+1<=48;i--)tail=labels[i]+"."+tail;return uri.Scheme+"://…"+tail+(uri.IsDefaultPort?"":":"+uri.Port)+(uri.PathAndQuery.Length>1?"/…":"");}
   return string.IsNullOrEmpty(uri.UserInfo)?value:shown+uri.PathAndQuery+uri.Fragment;
  }
  public static bool IsInternal(string value) {return value=="mica://home"||value==Tetris||Game(value)!=null||IsLibrary(value)||IsReading(value);}
  // A saved article, read offline: mica://read/<id>.
  public static bool IsReading(string value) {return value!=null&&value.StartsWith("mica://read/",StringComparison.Ordinal)&&value.Length==12+32;}
  // Built-in games run in a web view, served by Mica itself from a host that never resolves, so they work offline.
  public const string Tetris="mica://tetris",TetrisHost="tetris.mica.invalid",GamesHost="games.mica.invalid";
  // The other games and the page that lists them all: address, title, path on GamesHost, page inside Mica.
  public static readonly string[][] Games={new[]{"mica://games","Games","","GameHub.html"},new[]{"mica://snake","Snake","snake","Snake.html"},new[]{"mica://2048","2048","2048","Game2048.html"},new[]{"mica://minesweeper","Minesweeper","minesweeper","Minesweeper.html"},new[]{"mica://breakout","Breakout","breakout","Breakout.html"},new[]{"mica://sudoku","Sudoku","sudoku","Sudoku.html"},new[]{"mica://memory","Memory","memory","Memory.html"},new[]{"mica://flappy","Flappy","flappy","Flappy.html"},new[]{"mica://pong","Pong","pong","Pong.html"},new[]{"mica://invaders","Invaders","invaders","Invaders.html"},new[]{"mica://connect4","Connect Four","connect4","ConnectFour.html"}};
  public static string[] Game(string value) {foreach(var game in Games)if(game[0]==value)return game;return null;}
  public static string EngineAddress(string value) {if(value==Tetris)return "https://"+TetrisHost+"/";var game=Game(value);return game!=null?"https://"+GamesHost+"/"+game[2]:value;}
  public static string FromEngine(string value) {
   Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.Scheme!="https")return value;if(uri.Host==TetrisHost)return Tetris;
   if(uri.Host==GamesHost)foreach(var game in Games)if(uri.AbsolutePath.Trim('/')==game[2])return game[0];return value;
  }
  public static bool IsLibrary(string value) {return value=="mica://history"||value=="mica://downloads";}
  public static string InternalTitle(string value) {return value=="mica://home"?"New tab":value=="mica://history"?"History":value=="mica://downloads"?"Downloads":value==Tetris?"Tetris":Game(value)!=null?Game(value)[1]:null;}
  public static bool IsWeb(string value) { Uri uri; return Uri.TryCreate(value,UriKind.Absolute,out uri)&&(uri.Scheme=="https"||uri.Scheme=="http")&&!string.IsNullOrEmpty(uri.Host); }
  // blob:https://site/… is data a web page made itself (a report, an export, a preview); it carries that
  // page's origin, so opening it is as safe as the page. Other non-web schemes stay blocked.
  // A file on this PC (file:///C:/…) or on a network share (file://server/share/…) that you open: typed,
  // dropped on a page, or handed to Mica by Windows. Pages can't send you to one (the engine refuses
  // web-to-file navigation), so only you decide which share Mica (and your Windows sign-in) reaches.
  public static bool IsLocalFile(string value) { Uri uri; return Uri.TryCreate(value,UriKind.Absolute,out uri)&&uri.IsFile; }
  // "C:\Videos\a.mp4", "\\server\share\a.pdf", "\"C:\a b.pdf\"" or "file:///C:/a.pdf" as a file: address; null for anything else.
  public static string LocalFileAddress(string input) {
   var q=(input??"").Trim().Trim('"');if(q.Length<3||q.Length>4096)return null;
   if(Regex.IsMatch(q,@"^([A-Za-z]:[\\/]|\\\\[^\\/]+[\\/][^\\/]+)")){try {var file=new Uri(System.IO.Path.GetFullPath(q)).AbsoluteUri;return IsLocalFile(file)?file:null;}catch(Exception){return null;}}
   return q.StartsWith("file:",StringComparison.OrdinalIgnoreCase)&&IsLocalFile(q)?new Uri(q).AbsoluteUri:null;
  }
  public static bool IsSiteBlob(string value) { return value!=null&&value.StartsWith("blob:",StringComparison.OrdinalIgnoreCase)&&IsWeb(value.Substring(5)); }
  // "yt cats" with keywords {yt: ...%s}: the address with the rest of the words in it; null when the first word isn't a keyword.
  public static string ExpandKeyword(string input,Dictionary<string,string> keywords) {
   var q=(input??"").Trim();int space=q.IndexOf(' ');if(keywords==null||space<1)return null;string words=q.Substring(space+1).Trim(),template;
   if(words.Length==0||!keywords.TryGetValue(q.Substring(0,space).ToLowerInvariant(),out template)||template==null||!template.Contains("%s"))return null;
   string url=template.Replace("%s",Uri.EscapeDataString(words));return IsWeb(url)?url:null;
  }
  public static string Resolve(string input) {
   var q=(input??"").Trim(); if(q.Length==0)return "mica://home";if(IsInternal(q.ToLowerInvariant().TrimEnd('/')))return q.ToLowerInvariant().TrimEnd('/');
   if(Regex.IsMatch(q,@"^(localhost|127\.0\.0\.1|\[::1\])(:\d+)?([/?#].*)?$",RegexOptions.IgnoreCase))return new Uri("http://"+q).AbsoluteUri;
   if(!q.Any(char.IsWhiteSpace)&&Regex.IsMatch(q,@"^([\p{L}\d-]+\.)+[\p{L}\d-]+(:\d+)?([/?#].*)?$")) { Uri uri; if(Uri.TryCreate("https://"+q,UriKind.Absolute,out uri))return uri.AbsoluteUri; }
   var local=LocalFileAddress(q);if(local!=null)return local;
   if(Regex.IsMatch(q,@"^[a-z][a-z0-9+.-]*:",RegexOptions.IgnoreCase)) { if(!IsWeb(q))throw new ArgumentException("Only web addresses and files on this PC can be opened."); return new Uri(q).AbsoluteUri; }
   return "https://www.google.com/search?q="+Uri.EscapeDataString(q);
  }
 }
 public static class Spotlight {
  public static List<SearchHit> Search(BrowserState state,string query) {
   var q=(query??"").Trim(); var hits=new List<SearchHit>(); var seen=new HashSet<string>(StringComparer.Ordinal);
   Add(hits,seen,state.Tabs.OrderByDescending(t=>t.LastUsed),q,"TAB");
   Add(hits,seen,state.Bookmarks,q,"BOOKMARK"); Add(hits,seen,state.History,q,"HISTORY");
   hits=hits.Take(7).ToList();
   // A site keyword ("yt cats") goes first: it's what you meant.
   string keyword=Navigation.ExpandKeyword(q,state.SiteKeywords);if(keyword!=null){hits.RemoveAll(h=>h.Url==keyword);hits.Insert(0,new SearchHit {Kind="SHORTCUT",Title="Search "+new Uri(keyword).Host.Replace("www.","")+" for “"+q.Substring(q.IndexOf(' ')+1).Trim()+"”",Detail="Press Enter · keyword "+q.Split(' ')[0],Url=keyword});}
   if(q.Length>0) { try { var url=Navigation.Resolve(q);
    // Enter does what you typed, like Chrome: a search, or the address. Only the start of a site's address
    // ("git" for github.com) keeps that site on top; a word that merely appears in a title doesn't.
    var typed=hits.FirstOrDefault(h=>h.Url==url);if(typed!=null)hits.Remove(typed);
    else typed=new SearchHit { Kind=url.StartsWith("https://www.google.com/search?q=")?"SEARCH":"OPEN",Title=url.StartsWith("https://www.google.com/search?q=")?"Search the web for “"+q+"”": "Go to “"+q+"”",Detail="Press Enter to open",Url=url };
    bool keepTop=keyword!=null||hits.Count>0&&typed.Kind=="SEARCH"&&StartsSiteAddress(hits[0].Url,q);
    hits.Insert(keepTop?1:0,typed);
   } catch(ArgumentException) { } }
   return hits;
  }
  static bool StartsSiteAddress(string url,string q) {
   Uri uri;if(q.Any(char.IsWhiteSpace)||!Uri.TryCreate(url,UriKind.Absolute,out uri)||string.IsNullOrEmpty(uri.Host))return false;
   string host=uri.Host.ToLowerInvariant(),typed=q.ToLowerInvariant();foreach(var prefix in new[]{"https://","http://"})if(typed.StartsWith(prefix))typed=typed.Substring(prefix.Length);
   if(host.StartsWith("www.")&&!typed.StartsWith("www."))host=host.Substring(4);
   return (host+uri.PathAndQuery).StartsWith(typed,StringComparison.Ordinal);
  }
  // Tab search (Ctrl+Shift+A): every tab in both spaces, open ones marked apart from resting ones, then tabs
  // closed lately and history. Each word typed must appear in the title or address, in any order.
  public static List<SearchHit> SearchTabs(BrowserState state,string query,Func<string,bool> awake) {
   var words=(query??"").Trim().Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);var hits=new List<SearchHit>();var seen=new HashSet<string>(StringComparer.Ordinal);
   Func<PageEntry,bool> matches=delegate(PageEntry e) {string text=(e.Title??"")+" "+e.Url;return words.All(w=>text.IndexOf(w,StringComparison.OrdinalIgnoreCase)>=0);};
   foreach(var t in state.Tabs.OrderByDescending(t=>t.Id==state.ActiveId).ThenByDescending(t=>t.LastUsed)) {
    if(t.Url=="mica://home"||!matches(t))continue;seen.Add(t.Url);
    hits.Add(new SearchHit {Kind=t.Id==state.ActiveId?"THIS TAB":awake(t.Id)?"OPEN":"RESTING",Title=string.IsNullOrWhiteSpace(t.Title)?t.Url:t.Title,Detail=t.Space+" · "+Navigation.AddressLabel(t.Url),Url=t.Url,TabId=t.Id});
   }
   foreach(var pair in new[]{Tuple.Create("CLOSED",(IEnumerable<PageEntry>)(state.RecentlyClosed??new List<PageEntry>())),Tuple.Create("HISTORY",(IEnumerable<PageEntry>)state.History)})
    foreach(var e in pair.Item2) {
     if(hits.Count>=14)break;if(!Navigation.IsWeb(e.Url)||!matches(e)||!seen.Add(e.Url))continue;
     hits.Add(new SearchHit {Kind=pair.Item1,Title=string.IsNullOrWhiteSpace(e.Title)?e.Url:e.Title,Detail=Navigation.AddressLabel(e.Url),Url=e.Url});
    }
   return hits;
  }
  static void Add(List<SearchHit> hits,HashSet<string> seen,IEnumerable<PageEntry> entries,string q,string kind) {
   foreach(var e in entries) { if(e.Url=="mica://home"||(!Navigation.IsWeb(e.Url)&&!Navigation.IsLibrary(e.Url)))continue; if(q.Length>0&&((e.Title??"")+" "+e.Url).IndexOf(q,StringComparison.OrdinalIgnoreCase)<0)continue; if(!seen.Add(e.Url))continue;
    hits.Add(new SearchHit { Kind=kind,Title=string.IsNullOrWhiteSpace(e.Title)?e.Url:e.Title,Detail=(kind=="TAB"?e.Space+" · ":"")+e.Url,Url=e.Url,TabId=kind=="TAB"?e.Id:null }); }
  }
 }
 public static class StateStore {
  // The default 2 MB cap would make a big session fail to save, or load as empty and then be overwritten.
  static JavaScriptSerializer Serializer() {return new JavaScriptSerializer {MaxJsonLength=int.MaxValue};}
  public static List<CookieJar> DefaultJars() {return new List<CookieJar> {new CookieJar {Id="work",Name="Work",Color="#4A7FD9"},new CookieJar {Id="shopping",Name="Shopping",Color="#E0A526"},new CookieJar {Id="banking",Name="Banking",Color="#3E9B5F"},new CookieJar {Id="social",Name="Social",Color="#D45C9B"}};}
  public static Dictionary<string,string> DefaultKeywords() {return new Dictionary<string,string> {{"yt","https://www.youtube.com/results?search_query=%s"},{"gh","https://github.com/search?q=%s"},{"w","https://en.wikipedia.org/w/index.php?search=%s"},{"maps","https://www.google.com/maps/search/%s"},{"g","https://www.google.com/search?q=%s"},{"so","https://stackoverflow.com/search?q=%s"},{"r","https://www.reddit.com/search/?q=%s"},{"amz","https://www.amazon.com/s?k=%s"}};}
  public static void Save(string path,BrowserState state) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); var temp=path+".tmp"; File.WriteAllText(temp,Serializer().Serialize(state)); if(File.Exists(path))File.Replace(temp,path,null); else File.Move(temp,path); }
  // Groups an extension makes (Claude's tools keep their tabs in one) belong to its session: they aren't kept for the
  // next start, and their tabs don't rest while the extension works in them.
  public const string ExtensionGroupPrefix="ext-";
  public static bool InExtensionGroup(PageEntry tab) {return tab.Group!=null&&tab.Group.StartsWith(ExtensionGroupPrefix,StringComparison.Ordinal);}
  public static BrowserState Load(string path) {
   BrowserState s=null; try { if(File.Exists(path))s=Serializer().Deserialize<BrowserState>(File.ReadAllText(path)); } catch(Exception e) { if(!(e is IOException||e is ArgumentException||e is InvalidOperationException))throw; }
   if(s==null)s=new BrowserState(); if(s.Tabs==null)s.Tabs=new List<PageEntry>(); if(s.History==null)s.History=new List<PageEntry>(); if(s.Bookmarks==null)s.Bookmarks=new List<PageEntry>();
   if(s.SiteZoom==null)s.SiteZoom=new Dictionary<string,double>();
   if(s.RecentlyClosed==null)s.RecentlyClosed=new List<PageEntry>();s.RecentlyClosed=s.RecentlyClosed.Where(t=>t!=null&&Navigation.IsWeb(t.Url)).GroupBy(t=>t.Url).Select(g=>g.First()).Take(8).ToList();
   s.Spaces=(s.Spaces??new List<string> {"Personal","Work"}).Where(n=>n!=null).Select(n=>n.Trim()).Where(n=>n.Length>0&&n.Length<=BrowserState.MaxSpaceName).Distinct(StringComparer.OrdinalIgnoreCase).Where(n=>n!="Personal").Take(BrowserState.MaxSpaces-1).ToList();s.Spaces.Insert(0,"Personal");
   s.Space=s.KnownSpace(s.Space);
   s.Tabs.RemoveAll(t=>t==null||(!Navigation.IsInternal(t.Url)&&!Navigation.IsWeb(t.Url)));
   s.Bookmarks.RemoveAll(t=>t==null||!Navigation.IsWeb(t.Url)); s.History.RemoveAll(t=>t==null||!Navigation.IsWeb(t.Url)||t.Url.Length>BrowserState.MaxHistoryUrl); s.History=s.History.Take(500).ToList();
   foreach(var entry in s.History)entry.VisitCount=Math.Max(1,entry.VisitCount);
   var ids=new HashSet<string>(); foreach(var t in s.Tabs) { if(string.IsNullOrEmpty(t.Id)||!ids.Add(t.Id)) { t.Id=Guid.NewGuid().ToString("N"); ids.Add(t.Id); } t.Space=s.KnownSpace(t.Space); }
   if(s.Jars==null||s.Jars.Count==0)s.Jars=DefaultJars();s.Jars.RemoveAll(j=>j==null||string.IsNullOrEmpty(j.Id));
   if(s.SiteKeywords==null)s.SiteKeywords=DefaultKeywords();if(s.SiteMemory==null)s.SiteMemory=new Dictionary<string,string>();if(s.AutoClearSites==null)s.AutoClearSites=new List<string>();
   foreach(var t in s.Tabs)if(t!=null&&t.Jar!=null&&!s.Jars.Any(j=>j.Id==t.Jar))t.Jar=null;
   if(s.Groups==null)s.Groups=new List<TabGroup>();s.Groups.RemoveAll(g=>g==null||string.IsNullOrEmpty(g.Id)||g.Id.StartsWith(ExtensionGroupPrefix,StringComparison.Ordinal));s.NormalizeGroups();
   if(s.Tabs.Count==0)s.AddTab("mica://home"); if(!s.Tabs.Any(t=>t.Id==s.ActiveId))s.ActiveId=s.Tabs[0].Id; s.Space=s.Tabs.First(t=>t.Id==s.ActiveId).Space; return s;
  }
 }
}
