using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Handlers;

namespace Mica {
 // Spotlight, the command palette, the menus and the keyboard, as on Windows (BrowserApp.cs, BrowserCommands.cs,
 // BrowserMenus.cs). Chromium draws its pages in native views that nothing can be drawn over, so while Spotlight is
 // open the page steps aside (as Windows parks it), and menus are popups, which are windows of their own.
 sealed partial class BrowserWindow {
  readonly Grid spotlightOverlay=new Grid {IsVisible=false,Background=new SolidColorBrush(Color.Parse("#55302B3D"))};
  readonly TextBox spotlightInput=new TextBox {FontSize=21,Padding=new Thickness(7,9),BorderThickness=new Thickness(0),Background=Brushes.Transparent,VerticalContentAlignment=VerticalAlignment.Center};
  readonly StackPanel spotlightRows=new StackPanel();
  readonly TextBlock spotlightHint=new TextBlock {Text="TABS, BOOKMARKS & RECENT PLACES",FontSize=10,FontWeight=FontWeight.SemiBold},spotlightEmpty=new TextBlock {Margin=new Thickness(18,22),TextWrapping=TextWrapping.Wrap,IsVisible=false};
  readonly ScrollViewer spotlightScroll=new ScrollViewer {MaxHeight=380,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
  List<SearchHit> spotlightHits=new List<SearchHit>();int spotlightIndex;bool editingAddress,tabSearch;
  readonly Dictionary<string,Action> paletteActions=new Dictionary<string,Action>();
  bool SpotlightOpen {get {return spotlightOverlay.IsVisible;}}

  void BuildSpotlight() {
   var card=Res(Res(new Border {Width=660,CornerRadius=new CornerRadius(17),BorderThickness=new Thickness(1),Padding=new Thickness(10),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,24),BoxShadow=BoxShadows.Parse("0 20 70 0 #42252032")},Border.BackgroundProperty,"Panel"),Border.BorderBrushProperty,"Line");
   var stack=new StackPanel();card.Child=stack;
   var head=new Grid {Margin=new Thickness(16,10,14,17),ColumnDefinitions=new ColumnDefinitions("32,*,40")};
   var lens=Glyph(Glyphs.Search,20,"accent");lens.HorizontalAlignment=HorizontalAlignment.Left;head.Children.Add(lens);
   spotlightInput.Classes.Add("spotlight");Grid.SetColumn(spotlightInput,1);head.Children.Add(spotlightInput);
   var esc=new Button {Content="esc",FontSize=10,Padding=new Thickness(3)};esc.Classes.Add("flat");esc.Classes.Add("muted");ToolTip.SetTip(esc,"Close Spotlight");esc.Click+=delegate {CloseSpotlight();};Grid.SetColumn(esc,2);head.Children.Add(esc);
   stack.Children.Add(head);
   stack.Children.Add(Res(new Border {Height=1},Border.BackgroundProperty,"Line"));
   var hints=new Grid {Margin=new Thickness(18,15,18,7)};Res(spotlightHint,TextBlock.ForegroundProperty,"Muted");hints.Children.Add(spotlightHint);
   var brand=Res(new TextBlock {Text="SPOTLIGHT",FontSize=10,HorizontalAlignment=HorizontalAlignment.Right},TextBlock.ForegroundProperty,"Accent");hints.Children.Add(brand);stack.Children.Add(hints);
   spotlightScroll.Content=spotlightRows;stack.Children.Add(spotlightScroll);
   Muted(spotlightEmpty);stack.Children.Add(spotlightEmpty);
   var foot=Res(new Border {BorderThickness=new Thickness(0,1,0,0),Margin=new Thickness(8,12,8,0),Padding=new Thickness(9,12,9,4)},Border.BorderBrushProperty,"Line");
   var footRow=new Grid();footRow.Children.Add(Muted(new TextBlock {Text="↑ ↓  to navigate       ↵  to open",FontSize=10}));
   var motto=Muted(new TextBlock {Text="A shortcut to anywhere.",FontStyle=FontStyle.Italic,FontSize=11,HorizontalAlignment=HorizontalAlignment.Right});Res(motto,TextBlock.FontFamilyProperty,"SerifFont");footRow.Children.Add(motto);
   foot.Child=footRow;stack.Children.Add(foot);
   spotlightOverlay.Children.Add(card);
   // A click on the dimmed space around the card closes it.
   spotlightOverlay.PointerPressed+=delegate(object s,PointerPressedEventArgs e) {if(e.Source==spotlightOverlay)CloseSpotlight();};
   spotlightInput.TextChanged+=delegate {RefreshSearch();};
   spotlightInput.KeyDown+=delegate(object s,KeyEventArgs e) {
    if(e.Key==Key.Escape)CloseSpotlight();
    else if(e.Key==Key.Enter)ExecuteSearch();
    else if(e.Key==Key.Down||e.Key==Key.Up){if(spotlightHits.Count>0)SelectHit((spotlightIndex+(e.Key==Key.Down?1:-1)+spotlightHits.Count)%spotlightHits.Count);}
    else return;
    e.Handled=true;
   };
  }
  void OpenSpotlight(bool address) {
   tabSearch=false;editingAddress=address;
   spotlightOverlay.IsVisible=true;
   // The page steps aside: Chromium's native view would cover the card.
   foreach(var t in tabs.Values)if(t.View!=null)t.View.IsVisible=false;
   aiHost.IsVisible=false;
   var tab=Active;spotlightInput.Text=address&&tab!=null&&tab.Entry.Url!="mica://home"?Navigation.AddressLabel(tab.Entry.Url):"";
   RefreshSearch();TakeKeyboard();spotlightInput.Focus();spotlightInput.SelectAll();FollowIslandZoom();
  }
  void CloseSpotlight() {
   if(!SpotlightOpen)return;
   spotlightOverlay.IsVisible=false;aiHost.IsVisible=true;
   var tab=Active;if(tab!=null&&tab.View!=null&&tab.Error==null){tab.View.IsVisible=true;tab.View.Focus();}
  }
  void OpenTabSearch() {OpenSpotlight(false);tabSearch=true;RefreshSearch();}
  // Bookmarks, listed in Spotlight as on Windows (ShowLibrary(true)).
  void ShowBookmarks() {
   OpenSpotlight(false);
   spotlightHits=state.Bookmarks.Take(50).Select(b=>new SearchHit {Kind="BOOKMARK",Title=string.IsNullOrWhiteSpace(b.Title)?b.Url:b.Title,Detail=b.Url,Url=b.Url}).ToList();
   RenderHits();spotlightHint.Text="BOOKMARKS";spotlightEmpty.Text="Bookmark a page with "+Mod+"D.";
  }
  void RefreshSearch() {
   if(!SpotlightOpen)return;
   string q=spotlightInput.Text??"";bool commands=q.TrimStart().StartsWith(">");
   spotlightHits=commands?SearchCommands(q.TrimStart()):tabSearch?Spotlight.SearchTabs(state,q,id=>{CrossTab t;return tabs.TryGetValue(id,out t)&&t.View!=null;}):Spotlight.Search(state,q);
   RenderHits();
   spotlightEmpty.Text=commands?"No action matches. Try one word, like zoom or bookmark.":tabSearch?"No tab or recent page matches. Try fewer words.":q.Contains(":")?"Use an http:// or https:// address.":"Type a website or a thought. We'll take you there.";
   spotlightHint.Text=commands?spotlightHits.Count+(spotlightHits.Count==1?" ACTION":" ACTIONS"):tabSearch?(q.Length==0?state.Tabs.Count(t=>t.Url!="mica://home")+" TABS · OPEN, RESTING AND RECENTLY CLOSED":spotlightHits.Count+" MATCHES"):q.Length==0?"TABS, BOOKMARKS & RECENT PLACES · TYPE > FOR ACTIONS":spotlightHits.Count+" PLACES TO GO";
  }
  void RenderHits() {
   spotlightRows.Children.Clear();
   for(int i=0;i<spotlightHits.Count;i++) {
    var hit=spotlightHits[i];int index=i;
    var row=new Grid {ColumnDefinitions=new ColumnDefinitions("42,*,82")};
    var tile=Res(new Border {Width=30,Height=30,CornerRadius=new CornerRadius(8),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Left},Border.BackgroundProperty,"Canvas");
    tile.Child=Glyph(HitGlyph(hit.Kind),14,"accent");row.Children.Add(tile);
    var text=new StackPanel {VerticalAlignment=VerticalAlignment.Center};
    text.Children.Add(new TextBlock {Text=hit.Title,FontSize=13,FontWeight=FontWeight.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis});
    text.Children.Add(Muted(new TextBlock {Text=hit.Detail,FontSize=11,Margin=new Thickness(0,4,0,0),TextTrimming=TextTrimming.CharacterEllipsis}));
    Grid.SetColumn(text,1);row.Children.Add(text);
    var kind=Res(new TextBlock {Text=hit.Kind,FontSize=9,FontWeight=FontWeight.SemiBold,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center},TextBlock.ForegroundProperty,"Accent");Grid.SetColumn(kind,2);row.Children.Add(kind);
    var button=new Button {Content=row,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(14,12),Margin=new Thickness(4,2),CornerRadius=new CornerRadius(10),Focusable=false};
    button.Classes.Add("flat");button.Click+=delegate {SelectHit(index);ExecuteSearch();};
    spotlightRows.Children.Add(button);
   }
   spotlightEmpty.IsVisible=spotlightHits.Count==0;spotlightScroll.IsVisible=spotlightHits.Count>0;
   SelectHit(0);
  }
  static string HitGlyph(string kind) {
   switch(kind) {
    case "TAB":case "THIS TAB":case "OPEN":case "RESTING":return Glyphs.Tabs;
    case "BOOKMARK":return Glyphs.Star;
    case "HISTORY":case "CLOSED":return Glyphs.History;
    case "SEARCH":return Glyphs.Search;
    case "ACTION":return Glyphs.Sparkle;
    default:return Glyphs.Forward;
   }
  }
  void SelectHit(int index) {
   spotlightIndex=index;
   for(int i=0;i<spotlightRows.Children.Count;i++)spotlightRows.Children[i].Classes.Set("on",i==index);
   if(index>=0&&index<spotlightRows.Children.Count)spotlightRows.Children[index].BringIntoView();
  }
  void ExecuteSearch() {
   if(!SpotlightOpen||spotlightIndex<0||spotlightIndex>=spotlightHits.Count)return;
   var hit=spotlightHits[spotlightIndex];
   Action run;
   if(hit.Kind=="ACTION"&&paletteActions.TryGetValue(hit.Url,out run)){CloseSpotlight();Dispatcher.UIThread.Post(run);return;}
   bool replace=editingAddress||(Active!=null&&Active.Entry.Url=="mica://home");
   CloseSpotlight();
   if(hit.TabId!=null&&tabs.ContainsKey(hit.TabId)){var picked=state.Tabs.FirstOrDefault(t=>t.Id==hit.TabId);if(picked!=null&&picked.Space!=state.Space){state.Space=picked.Space;RenderTabs();}Show(hit.TabId);}
   else Navigate(hit.Url,!replace);
  }

  // The command palette: Spotlight with ">" in front runs an action ("> zoom in", "> dark"). Only actions that
  // make sense right now are listed.
  sealed class PaletteCommand {public string Title,Detail,Words;public Action Run;}
  List<PaletteCommand> PaletteCommands() {
   var list=new List<PaletteCommand>();var tab=Active;bool web=tab!=null&&Navigation.IsWeb(tab.Entry.Url)&&tab.View!=null;
   Action<string,string,string,Action> add=delegate(string title,string detail,string words,Action run) {list.Add(new PaletteCommand {Title=title,Detail=detail??"",Words=words??"",Run=run});};
   add("New tab",Mod+"T","open",NewTab);
   if(state.RecentlyClosed.Count>0)add("Reopen closed tab",ModShift+"T","undo restore "+state.RecentlyClosed[0].Title,ReopenClosedTab);
   if(tab!=null&&tab.Entry.Url!="mica://home") {
    if(!tab.Entry.Pinned)add("Close tab",Mod+"W","",delegate {CloseTab(tab.Entry.Id);});
    add(tab.Entry.Pinned?"Unpin tab":"Pin tab","","keep",delegate {TogglePinned(tab.Entry.Id);});
    if(web)add("Duplicate tab","","copy clone",delegate {DuplicateTab(tab);});
   }
   if(web) {
    add(tab.Muted?"Unmute tab":"Mute tab","","sound audio silence",delegate {ToggleMute(tab);});
    add("Reload",Mod+"R","refresh",Reload);
    add(state.Bookmarks.Any(b=>b.Url==tab.Entry.Url)?"Remove bookmark":"Bookmark this page",Mod+"D","favorite star",Bookmark);
    add("Copy page address","","url link clipboard",delegate {CopyAddress(tab);});
    add("Zoom in",Mod+"Plus","bigger larger",delegate {UserZoom(1);});
    add("Zoom out",Mod+"Minus","smaller",delegate {UserZoom(-1);});
    if(Math.Abs(tab.Zoom-1)>0.001)add("Reset zoom",Mod+"0",Math.Round(tab.Zoom*100)+"%",delegate {UserZoom(0);});
    add("Find in page",Mod+"F","search",ShowFind);
    add("Reading mode","F9","reader article",ToggleReader);
    add("Site settings","","permissions cookies memory "+SiteOf(tab.Entry.Url),ShowSiteCard);
    add("Print",Mod+"P","pdf",Print);
    add("Developer tools","F12","devtools inspect",ShowDevTools);
    foreach(var other in state.Spaces.Where(s=>s!=tab.Entry.Space)){string target=other;add("Move tab to "+target,"","space",delegate {MoveTabToSpace(tab.Entry.Id,target);});}
   }
   add("History",Mod+(Mac?"Y":"H"),"",delegate {OpenLibraryPage("mica://history");});
   add("Downloads",Mod+(Mac?"⌥L":"J"),"",delegate {OpenLibraryPage("mica://downloads");});
   add("Bookmarks","","favorites",ShowBookmarks);
   add("Search tabs",ModShift+"A","find tab",OpenTabSearch);
   add("Rest background tabs","","sleep memory free",delegate {RestIdleTabs(true);});
   add("Task manager","Shift+Esc","memory processes",ShowTaskManager);
   add("scrubnote: record page animations",ScrubnoteKey,"animation timing frames scrub motion",delegate {_=ToggleScrubnote();});
   add("Extensions","","addons chrome web store",ShowExtensions);
   add("Personalize","","colour color frame",ShowPersonalize);
   add("New private window",ModShift+"N","incognito inprivate",NewPrivateWindow);
   add(state.Dark?"Light theme":"Dark theme","","mode appearance",delegate {SetDark(!state.Dark);});
   add(sidebarHidden?"Show sidebar":"Hide sidebar",ModShift+"B","tabs panel",ToggleSidebar);
   foreach(var name in state.Spaces.Where(s=>s!=state.Space)){string space=name;add("Switch to "+space,"","space",delegate {SwitchSpace(space);});}
   if(state.Spaces.Count<BrowserState.MaxSpaces)add("New space","","space create add",delegate {ShowSpaceName(null);});
   add(aiOpen?"Close the AI side panel":"Open the AI side panel",ModShift+"Space","ai chat assistant claude chatgpt gemini",ToggleAi);
   add("Games","","play tetris snake 2048 minesweeper breakout sudoku memory offline",delegate {Navigate("mica://games",true);});
   foreach(var game in new[]{"Tetris","Snake","2048","Minesweeper","Breakout","Sudoku","Memory"}){string name=game;add("Play "+name,"","game",delegate {Navigate("mica://"+name.ToLowerInvariant(),true);});}
   add("Clear browsing data",ModShift+(Mac?"⌫":"Del"),"history cookies cache",ShowClearData);
   add("Keyboard shortcuts","","keys help",ShowShortcuts);
   return list;
  }
  // "> zo in" matches "Zoom in": every word must start a word of the title or its keywords.
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

  // Keyboard focus may be inside a page (Chromium's own native view); Mica's controls need it back first.
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr hwnd);
  void TakeKeyboard() {
   var view=ActiveView();if(view!=null){var host=view.Host;if(host!=null)host.SetFocus(false);}
   if(OperatingSystem.IsWindows()){var handle=TryGetPlatformHandle();if(handle!=null)SetFocus(handle.Handle);}
   Activate();
  }

  // Shortcuts, the same set as on Windows (BrowserApp.HandleKey); Cmd takes Ctrl's place on a Mac.
  void OnKey(object sender,KeyEventArgs e) {if(HandleKey(e.Key,e.KeyModifiers))e.Handled=true;}
  static bool Primary(KeyModifiers m) {return (m&(Mac?KeyModifiers.Meta:KeyModifiers.Control))!=0;}
  static int TabNumber(Key key) {if(key>=Key.D1&&key<=Key.D9)return key-Key.D1+1;if(key>=Key.NumPad1&&key<=Key.NumPad9)return key-Key.NumPad1+1;return 0;}
  // Whether a key is one of Mica's, decided without touching any state (it is asked from the engine's thread).
  static bool IsShortcut(Key key,KeyModifiers mods) {
   bool ctrl=Primary(mods),shift=(mods&KeyModifiers.Shift)!=0,alt=(mods&KeyModifiers.Alt)!=0;
   if(key==Key.F5||key==Key.F12||key==Key.F11||(key==Key.F9&&!ctrl&&!alt))return true;
   if(shift&&!ctrl&&!alt&&key==Key.Escape)return true;
   if(alt&&!ctrl&&(key==Key.Left||key==Key.Right))return true;
   if(Mac&&ctrl&&!shift&&(key==Key.OemOpenBrackets||key==Key.OemCloseBrackets))return true;
   if(alt&&shift&&!ctrl&&key==Key.S)return true;
   if(!ctrl)return false;
   if(shift)return key==Key.N||key==Key.T||key==Key.A||key==Key.B||key==Key.Space||key==Key.Delete||key==Key.Back||key==Key.I||key==Key.Tab||key==Key.J;
   return key==Key.K||key==Key.L||key==Key.T||key==Key.W||key==Key.D||key==Key.R||key==Key.Tab||key==Key.H||key==Key.J||key==Key.Y||key==Key.F||key==Key.P||key==Key.G
    ||key==Key.OemPlus||key==Key.Add||key==Key.OemMinus||key==Key.Subtract||key==Key.D0||key==Key.NumPad0||TabNumber(key)>0||(Mac&&alt&&key==Key.L)||(Mac&&alt&&key==Key.I);
  }
  bool HandleKey(Key key,KeyModifiers mods) {
   bool ctrl=Primary(mods),shift=(mods&KeyModifiers.Shift)!=0,alt=(mods&KeyModifiers.Alt)!=0;
   if(SpotlightOpen&&!(ctrl&&(key==Key.K||key==Key.L||key==Key.T)))return false;
   if(!IsShortcut(key,mods))return false;
   if(key==Key.F9){ToggleReader();return true;}
   if(shift&&!ctrl&&key==Key.Escape){ShowTaskManager();return true;}
   if(ctrl&&shift&&key==Key.N){NewPrivateWindow();return true;}
   if(key==Key.F11){WindowState=WindowState==WindowState.FullScreen?WindowState.Normal:WindowState.FullScreen;return true;}
   if(key==Key.F12||(ctrl&&shift&&key==Key.I)||(Mac&&ctrl&&alt&&key==Key.I)){ShowDevTools();return true;}
   if(ctrl&&shift&&(key==Key.Delete||key==Key.Back)){ShowClearData();return true;}
   if(ctrl&&shift&&key==Key.A){OpenTabSearch();return true;}
   if(ctrl&&shift&&key==Key.Space){ToggleAi();return true;}
   if(ctrl&&shift&&key==Key.B){ToggleSidebar();return true;}
   if(ctrl&&shift&&key==Key.T){ReopenClosedTab();return true;}
   if(alt&&shift&&!ctrl&&key==Key.S){_=ToggleScrubnote();return true;}
   if(ctrl&&shift&&key==Key.J||(Mac&&ctrl&&alt&&key==Key.L)||(!Mac&&ctrl&&key==Key.J)){OpenLibraryPage("mica://downloads");return true;}
   if(ctrl&&(key==Key.K||key==Key.L)){OpenSpotlight(key==Key.L);return true;}
   if(ctrl&&(Mac?key==Key.Y:key==Key.H)){OpenLibraryPage("mica://history");return true;}
   if(ctrl&&key==Key.T){NewTab();OpenSpotlight(false);return true;}
   if(ctrl&&key==Key.W){if(Active!=null&&!Active.Entry.Pinned)CloseTab(Active.Entry.Id);return true;}
   if(ctrl&&key==Key.D){Bookmark();return true;}
   if(ctrl&&key==Key.F){ShowFind();return true;}
   if(ctrl&&key==Key.G){FindNext(!shift);return true;}
   if(ctrl&&key==Key.P){Print();return true;}
   if(ctrl&&(key==Key.OemPlus||key==Key.Add)){UserZoom(1);return true;}
   if(ctrl&&(key==Key.OemMinus||key==Key.Subtract)){UserZoom(-1);return true;}
   if(ctrl&&(key==Key.D0||key==Key.NumPad0)){UserZoom(0);return true;}
   if(key==Key.F5||(ctrl&&key==Key.R)){Reload();return true;}
   if(alt&&key==Key.Left||Mac&&ctrl&&key==Key.OemOpenBrackets){GoBack();return true;}
   if(alt&&key==Key.Right||Mac&&ctrl&&key==Key.OemCloseBrackets){GoForward();return true;}
   if(ctrl&&TabNumber(key)>0){var list=state.Tabs.Where(t=>t.Space==state.Space).OrderByDescending(t=>t.Pinned).ToList();int n=TabNumber(key);if(n==9&&list.Count>0)n=list.Count;if(n<=list.Count)Show(list[n-1].Id);return true;}
   if(ctrl&&key==Key.Tab){var list=state.Tabs.Where(t=>t.Space==state.Space).OrderByDescending(t=>t.Pinned).ToList();if(list.Count>0){int i=list.FindIndex(t=>t.Id==state.ActiveId);Show(list[(i+(shift?-1:1)+list.Count)%list.Count].Id);}return true;}
   return false;
  }
  // Keys pressed inside a page reach Mica first, so the shortcuts work there too, as in Chrome.
  sealed class PageKeys : KeyboardHandler {
   readonly BrowserWindow window;
   public PageKeys(BrowserWindow w) {window=w;}
   protected override bool OnPreKeyEvent(CefBrowser browser,CefKeyEvent keyEvent,IntPtr os_event,out bool isKeyboardShortcut) {
    isKeyboardShortcut=false;
    if(keyEvent.EventType!=CefKeyEventType.RawKeyDown&&keyEvent.EventType!=CefKeyEventType.KeyDown)return false;
    var key=FromVirtualKey(keyEvent.WindowsKeyCode);if(key==Key.None)return false;
    var flags=keyEvent.Modifiers;var mods=KeyModifiers.None;
    if((flags&CefEventFlags.ControlDown)!=0)mods|=KeyModifiers.Control;
    if((flags&CefEventFlags.ShiftDown)!=0)mods|=KeyModifiers.Shift;
    if((flags&CefEventFlags.AltDown)!=0)mods|=KeyModifiers.Alt;
    if((flags&CefEventFlags.CommandDown)!=0)mods|=KeyModifiers.Meta;
    if(!IsShortcut(key,mods))return false;
    Dispatcher.UIThread.Post(delegate {window.HandleKey(key,mods);});
    return true;
   }
  }
  // Chromium reports keys as Windows virtual-key codes on every OS.
  static Key FromVirtualKey(int vk) {
   if(vk>=0x41&&vk<=0x5A)return Key.A+(vk-0x41);
   if(vk>=0x30&&vk<=0x39)return Key.D0+(vk-0x30);
   if(vk>=0x60&&vk<=0x69)return Key.NumPad0+(vk-0x60);
   if(vk>=0x70&&vk<=0x7B)return Key.F1+(vk-0x70);
   switch(vk) {
    case 0x08:return Key.Back;case 0x09:return Key.Tab;case 0x20:return Key.Space;case 0x25:return Key.Left;case 0x27:return Key.Right;
    case 0x2E:return Key.Delete;case 0x6B:return Key.Add;case 0x6D:return Key.Subtract;case 0xBB:return Key.OemPlus;case 0xBD:return Key.OemMinus;
    case 0xDB:return Key.OemOpenBrackets;case 0xDD:return Key.OemCloseBrackets;
   }
   return Key.None;
  }

  // Menus: a card of rows with an icon, a name and a hint, as on Windows (BrowserMenus.MenuCommand).
  Popup openMenu;
  Popup MenuCard(Control anchor,double width,PlacementMode placement,Action<StackPanel,Popup> fill) {
   if(openMenu!=null&&openMenu.IsOpen){bool same=openMenu.PlacementTarget==anchor;openMenu.IsOpen=false;if(same)return null;}
   var body=new StackPanel();
   var card=Res(Res(new Border {Width=width,CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),Padding=new Thickness(8),Child=new ScrollViewer {Content=body,MaxHeight=Math.Max(240,Bounds.Height-40),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},Margin=new Thickness(0,0,12,12),BoxShadow=BoxShadows.Parse("0 5 18 0 #30252032")},Border.BackgroundProperty,"Panel"),Border.BorderBrushProperty,"Line");
   var popup=new Popup {Child=card,PlacementTarget=anchor,IsLightDismissEnabled=true,Placement=sidebarHidden&&placement!=PlacementMode.Center?PlacementMode.Right:placement,HorizontalOffset=sidebarHidden?8:0,VerticalOffset=placement==PlacementMode.Bottom&&!sidebarHidden?8:0};
   card.KeyDown+=delegate(object s,KeyEventArgs e) {if(e.Key==Key.Escape){popup.IsOpen=false;e.Handled=true;}};
   popup.Closed+=delegate {root.Children.Remove(popup);if(openMenu==popup)openMenu=null;};
   fill(body,popup);
   root.Children.Add(popup);openMenu=popup;popup.IsOpen=true;
   return popup;
  }
  static void MenuHeading(StackPanel body,string label) {var t=Eyebrow(label);t.Margin=new Thickness(12,9,12,9);body.Children.Add(t);}
  static void MenuDivider(StackPanel body) {body.Children.Add(Res(new Border {Height=1,Margin=new Thickness(10,7)},Border.BackgroundProperty,"Line"));}
  static Button MenuCommand(StackPanel body,Popup popup,string icon,string title,string hint,Action action) {
   var row=new Grid {ColumnDefinitions=new ColumnDefinitions("28,*,Auto")};
   var symbol=Glyph(icon,14,"accent");symbol.HorizontalAlignment=HorizontalAlignment.Left;row.Children.Add(symbol);
   var name=new TextBlock {Text=title,FontSize=12,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(name,1);row.Children.Add(name);
   var shortcut=Muted(new TextBlock {Text=hint,FontSize=10,Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Center});Grid.SetColumn(shortcut,2);row.Children.Add(shortcut);
   var button=new Button {Content=row,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(12,11),Margin=new Thickness(0,1)};button.Classes.Add("flat");
   if(action!=null)button.Click+=delegate {popup.IsOpen=false;Dispatcher.UIThread.Post(action);};
   body.Children.Add(button);return button;
  }
  void ShowMenu(Control anchor) {
   MenuCard(anchor,280,PlacementMode.Top,delegate(StackPanel body,Popup popup) {
    MenuCommand(body,popup,Glyphs.History,"History",Mod+(Mac?"Y":"H"),delegate {OpenLibraryPage("mica://history");});
    MenuCommand(body,popup,Glyphs.Download,"Downloads",Mac?"⌥⌘L":"Ctrl+J",delegate {OpenLibraryPage("mica://downloads");});
    MenuCommand(body,popup,Glyphs.Tabs,"Search tabs",ModShift+"A",OpenTabSearch);
    MenuCommand(body,popup,Glyphs.Star,"Bookmarks","",ShowBookmarks);
    MenuCommand(body,popup,Glyphs.Lock,"New private window",ModShift+"N",NewPrivateWindow);
    if(!Program.Private){var current=CurrentProfile;MenuCommand(body,popup,Glyphs.Person,"Profiles",current==null?"":current.Name,delegate {ShowAccount(sidebarHidden?railAvatar:avatarButton);});}
    ZoomRow(body);
    MenuDivider(body);
    MenuHeading(body,"SITES");
    MenuCommand(body,popup,Glyphs.Find,"Find in page",Mod+"F",ShowFind);
    MenuCommand(body,popup,Glyphs.Reader,"Reading mode","F9",ToggleReader);
    MenuCommand(body,popup,Glyphs.Print,"Print",Mod+"P",Print);
    MenuCommand(body,popup,Glyphs.Info,"Site settings","",ShowSiteCard);
    MenuCommand(body,popup,Glyphs.Code,"Developer tools","F12",ShowDevTools);
    MenuHeading(body,"PRIVACY");
    MenuCommand(body,popup,Glyphs.Puzzle,"Extensions","",ShowExtensions);
    MenuCommand(body,popup,Glyphs.Trash,"Clear browsing data",ModShift+(Mac?"⌫":"Del"),ShowClearData);
    MenuHeading(body,"TOOLS");
    MenuCommand(body,popup,Glyphs.Gear,"Task manager","Shift+Esc",ShowTaskManager);
    MenuCommand(body,popup,Glyphs.Play,"scrubnote",ScrubnoteKey,delegate {_=ToggleScrubnote();});
    MenuCommand(body,popup,Glyphs.Chat,"AI side panel",ModShift+"Space",ToggleAi);
    MenuCommand(body,popup,Glyphs.Rest,"Rest background tabs","",delegate {RestIdleTabs(true);});
    MenuCommand(body,popup,Glyphs.Game,"Games","",delegate {Navigate("mica://games",true);});
    MenuCommand(body,popup,Glyphs.Keys,"Keyboard shortcuts","",ShowShortcuts);
    MenuDivider(body);
    MenuCommand(body,popup,Glyphs.Palette,"Personalize","",ShowPersonalize);
    // Its own name, so it can be found in either language.
    MenuCommand(body,popup,Glyphs.Globe,"Language",Localizer.Vietnamese?"Tiếng Việt":"English",delegate {SwitchLanguage(Localizer.Vietnamese?"en":"vi");});
    var about=Muted(new TextBlock {Text="Mica "+typeof(BrowserWindow).Assembly.GetName().Version.ToString(3)+" · Chromium "+CefRuntime.ChromeVersion,FontSize=10,Margin=new Thickness(12,8,12,6)});
    body.Children.Add(about);
   });
  }
  // − 100% + in the menu, as on Windows (ZoomMenuRow).
  void ZoomRow(StackPanel body) {
   var row=new Grid {ColumnDefinitions=new ColumnDefinitions("28,*,Auto"),Margin=new Thickness(12,4,4,4)};
   var symbol=Glyph(Glyphs.ZoomIn,14,"accent");symbol.HorizontalAlignment=HorizontalAlignment.Left;row.Children.Add(symbol);
   var name=new TextBlock {Text="Zoom",FontSize=12,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(name,1);row.Children.Add(name);
   var controls=new StackPanel {Orientation=Orientation.Horizontal};Grid.SetColumn(controls,2);row.Children.Add(controls);
   var tab=Active;var level=Muted(new TextBlock {Text=tab==null?"100%":Math.Round(tab.Zoom*100)+"%",FontSize=11,Width=44,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
   var less=CaptionButton(Glyphs.Minimize,"Zoom out ("+Mod+"Minus)");var more=CaptionButton(Glyphs.Plus,"Zoom in ("+Mod+"Plus)");
   less.Click+=delegate {ZoomStep(-1);var t=Active;if(t!=null)level.Text=Math.Round(t.Zoom*100)+"%";};
   more.Click+=delegate {ZoomStep(1);var t=Active;if(t!=null)level.Text=Math.Round(t.Zoom*100)+"%";};
   bool web=tab!=null&&Navigation.IsWeb(tab.Entry.Url);less.IsEnabled=web;more.IsEnabled=web;
   controls.Children.Add(less);controls.Children.Add(level);controls.Children.Add(more);
   body.Children.Add(row);
  }

  // Spaces: switch, make, rename and remove, as on Windows (ShowSpaces).
  void ShowSpaces(Control anchor) {
   MenuCard(anchor,226,PlacementMode.Bottom,delegate(StackPanel body,Popup popup) {
    MenuHeading(body,"SWITCH SPACE");
    foreach(string name in state.Spaces) {
     string space=name;int count=state.Tabs.Count(t=>t.Space==space);
     var item=MenuCommand(body,popup,space=="Personal"?Glyphs.Person:space=="Work"?Glyphs.Work:Glyphs.Spaces,space,(state.Space==space?"✓  ":"")+count+(count==1?" tab":" tabs"),delegate {SwitchSpace(space);});
     if(state.Space==space)item.Classes.Add("on");
    }
    MenuDivider(body);
    if(state.Spaces.Count<BrowserState.MaxSpaces)MenuCommand(body,popup,Glyphs.Plus,"New space","",delegate {ShowSpaceName(null);});
    if(state.Space!="Personal") {
     string here=state.Space;
     MenuCommand(body,popup,Glyphs.Rename,"Rename "+here,"",delegate {ShowSpaceName(here);});
     MenuCommand(body,popup,Glyphs.Trash,"Remove "+here,"Tabs move to Personal",delegate {RemoveSpace(here);});
    }
    body.Children.Add(Muted(new TextBlock {Text="Separate tabs for each part of your day.",FontSize=10,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12,4,12,10)}));
   });
  }
  void SwitchSpace(string name) {
   if(name==state.Space)return;
   state.Space=name;
   var tab=state.Tabs.Where(t=>t.Space==name).OrderByDescending(t=>t.LastUsed).FirstOrDefault()??state.AddTab("mica://home");
   RenderTabs();Show(tab.Id);Save();
  }
  // Names a new space, or renames one: a small card under the space button.
  void ShowSpaceName(string renaming) {
   Control anchor=sidebarHidden?(Control)rail:sidebarFull;
   var input=new TextBox {Text=renaming??"",MaxLength=BrowserState.MaxSpaceName,Watermark="Space name",Margin=new Thickness(12,0,12,8)};
   input.Classes.Add("field");
   var problem=Muted(new TextBlock {FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12,0,12,8)});
   Popup card=null;
   Action commit=delegate {
    string title=(input.Text??"").Trim();if(title==renaming){card.IsOpen=false;return;}
    string why=state.SpaceNameProblem(title,renaming);if(why!=null){problem.Text=why;return;}
    if(renaming==null)state.AddSpace(title);else state.RenameSpace(renaming,title);
    card.IsOpen=false;if(renaming==null)SwitchSpace(title);else RenderTabs();Save();
   };
   card=MenuCard(anchor,260,PlacementMode.Center,delegate(StackPanel body,Popup popup) {
    MenuHeading(body,renaming==null?"NEW SPACE":"RENAME SPACE");
    body.Children.Add(Muted(new TextBlock {Text="A space keeps its own set of tabs, for another part of your day.",FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12,0,12,10)}));
    body.Children.Add(input);body.Children.Add(problem);
    var go=new Button {Content=renaming==null?"Create":"Rename",Margin=new Thickness(12,0,12,6),Padding=new Thickness(18,8)};go.Classes.Add("flat");go.Classes.Add("on");go.Click+=delegate {commit();};body.Children.Add(go);
   });
   if(card==null)return;
   input.KeyDown+=delegate(object s,KeyEventArgs e) {if(e.Key==Key.Enter){commit();e.Handled=true;}};
   TakeKeyboard();Dispatcher.UIThread.Post(delegate {input.Focus();input.SelectAll();},DispatcherPriority.Loaded);
  }
  void RemoveSpace(string space) {
   int moved=state.RemoveSpace(space);
   var tab=state.Tabs.Where(t=>t.Space==state.Space).OrderByDescending(t=>t.LastUsed).FirstOrDefault()??state.AddTab("mica://home");
   RenderTabs();Show(tab.Id);Save();
   Status(moved==0?"Removed "+space+".":"Removed "+space+". Its "+moved+" tabs are in Personal now.");
  }
  // Accounts and sync are the Windows build's; here the account button says where things stand.
 }
}
