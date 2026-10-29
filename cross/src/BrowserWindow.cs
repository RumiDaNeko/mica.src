using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using GlyphPath=Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Xilium.CefGlue.Avalonia;

namespace Mica {
 // One tab: its entry in the session (shared Core.cs model) and, once it has been shown, its engine view.
 sealed class CrossTab {
  public PageEntry Entry;public AvaloniaCefBrowser View;public Border Header;public TextBlock Label;public ContentControl Icon;
  public int LastStatus;public bool Loading;public TaskCompletionSource<bool> Waiter;
 }
 // Mica's window, laid out like the Windows build (Shell.xaml): a sidebar with the wordmark, search, the space and
 // the tabs as a list, and the page in a rounded frame with a slim toolbar. Colors and button styles are in Theme.axaml.
 sealed partial class BrowserWindow : Window {
  readonly string profile,statePath;readonly BrowserState state;
  readonly Dictionary<string,CrossTab> tabs=new Dictionary<string,CrossTab>();
  readonly StackPanel tabList=new StackPanel(),pinnedList=new StackPanel();
  readonly TextBlock tabCount=new TextBlock {HorizontalAlignment=HorizontalAlignment.Right},pinnedHint=new TextBlock {Text="Right-click a tab to keep it here",FontSize=11,Margin=new Thickness(10,0,0,0)};
  readonly TextBlock spaceName=new TextBlock {FontWeight=FontWeight.SemiBold};
  readonly Grid pages=new Grid(),layout=new Grid {Margin=new Thickness(7)},sidebar=new Grid {Margin=new Thickness(9,0,15,0)};
  readonly TextBox address=new TextBox {Watermark="A fresh start",MinWidth=150,MaxWidth=440,VerticalAlignment=VerticalAlignment.Center};
  readonly Button back,forward,reload,theme,showSidebar;
  readonly ProgressBar loading=new ProgressBar {IsIndeterminate=true,IsVisible=false};
  readonly Border notice=new Border {IsVisible=false,Padding=new Thickness(14,8),Margin=new Thickness(12,10,12,0),CornerRadius=new CornerRadius(9)};
  readonly TextBlock noticeText=new TextBlock {VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
  readonly Button noticeGames=new Button {Content="Play a game while you wait",Margin=new Thickness(12,0,0,0),Padding=new Thickness(12,6)};

  // Line icons on a 24-unit grid.
  internal static class Glyphs {
   public const string Back="M19 12H5 M11 6l-6 6 6 6",Forward="M5 12h14 M13 6l6 6-6 6",Reload="M19.5 12a7.5 7.5 0 1 1-2.2-5.3 M19.5 4.5v4.5h-4.5";
   public const string Search="M13.5 4a6.5 6.5 0 1 1 0 13a6.5 6.5 0 1 1 0-13z M8.8 15.2L4 20",Globe="M12 3a9 9 0 1 1 0 18a9 9 0 1 1 0-18z M3 12h18 M12 3c3.2 3.2 3.2 14.8 0 18 M12 3c-3.2 3.2-3.2 14.8 0 18";
   public const string Close="M6 6l12 12 M18 6L6 18",Minimize="M5 12h14",Maximize="M5.5 5.5h13v13h-13z",Menu="M4 7h16 M4 12h16 M4 17h16",Chevron="M6 9l6 6 6-6";
   public const string Plus="M12 5v14 M5 12h14",Home="M4 11l8-7 8 7 M6.5 9v11h11V9",Spaces="M5 4v16 M9.5 4v16 M13.5 5.5l4.5 14.5";
   public const string Gear="M12 9a3 3 0 1 1 0 6a3 3 0 1 1 0-6z M10.3 3h3.4l.5 2.4 1.6.9 2.3-.8 1.7 2.9-1.8 1.6v1.9l1.8 1.6-1.7 2.9-2.3-.8-1.6.9-.5 2.4h-3.4l-.5-2.4-1.6-.9-2.3.8-1.7-2.9 1.8-1.6v-1.9L4.2 8.4l1.7-2.9 2.3.8 1.6-.9z";
   public const string Person="M12 4a4 4 0 1 1 0 8a4 4 0 1 1 0-8z M4.5 20.5c1.4-3.6 4.2-5.5 7.5-5.5s6.1 1.9 7.5 5.5",Moon="M19.5 14.5A8 8 0 0 1 9.5 4.5a8 8 0 1 0 10 10z";
   public const string Sun="M12 8a4 4 0 1 1 0 8a4 4 0 1 1 0-8z M12 2.5v2 M12 19.5v2 M2.5 12h2 M19.5 12h2 M5.3 5.3l1.4 1.4 M17.3 17.3l1.4 1.4 M5.3 18.7l1.4-1.4 M17.3 6.7l1.4-1.4";
   public const string Sparkle="M12 3l1.9 5.6L19.5 10.5l-5.6 1.9L12 18l-1.9-5.6L4.5 10.5l5.6-1.9z M18.5 15.5l.7 2 2 .7-2 .7-.7 2-.7-2-2-.7 2-.7z",Game="M7 8.5h10a4 4 0 0 1 4 4v.5a3.5 3.5 0 0 1-6.3 2.1L14 14h-4l-.7 1.1A3.5 3.5 0 0 1 3 13v-.5a4 4 0 0 1 4-4z M7.5 11v3 M6 12.5h3 M15.5 12h.01 M17.5 13.5h.01";
  }
  // Drawn on the 24-unit grid, whose drawing spans about two thirds of it, so a glyph of a given size matches a
  // Segoe MDL2 glyph at that font size on Windows. The stroke stays about 1.4 pixels wide.
  internal static Viewbox Glyph(string data,double size,string tone) {
   double box=Math.Round(size*1.45);
   var p=new GlyphPath {Data=Geometry.Parse(data),Width=24,Height=24,Stretch=Stretch.None,StrokeThickness=1.4*24/box};p.Classes.Add("glyph");if(tone!=null)p.Classes.Add(tone);
   return new Viewbox {Child=p,Width=box,Height=box,Stretch=Stretch.Uniform,Tag=data,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
  }
  internal static Button IconButton(string data,string tip,double size) {
   var b=new Button {Content=Glyph(data,size,null)};b.Classes.Add("flat");b.Classes.Add("icon");if(tip!=null)ToolTip.SetTip(b,tip);return b;
  }
  static Button CaptionButton(string data,string tip) {var b=new Button {Content=Glyph(data,10,null)};b.Classes.Add("flat");b.Classes.Add("caption");ToolTip.SetTip(b,tip);return b;}
  static T Res<T>(T control,AvaloniaProperty property,string key) where T:Control {control.Bind(property,control.GetResourceObservable(key));return control;}
  static TextBlock Eyebrow(string text) {var t=new TextBlock {Text=text};t.Classes.Add("eyebrow");return t;}
  static TextBlock Muted(TextBlock t) {t.Classes.Add("muted");return t;}
  static string Mod {get {return OperatingSystem.IsMacOS()?"⌘":"Ctrl+";}}

  public BrowserWindow(string profilePath,string selfTestResult) {
   profile=profilePath;statePath=Path.Combine(profile,"session.json");
   state=StateStore.Load(statePath);
   BuiltInPages.Dark=delegate {return state.Dark;};
   Title="Mica";Width=1280;Height=840;MinWidth=760;MinHeight=520;
   Res(this,BackgroundProperty,"Backdrop");
   bool windows=OperatingSystem.IsWindows(),mac=OperatingSystem.IsMacOS();
   // As on Windows, Mica draws its own title bar: the window buttons sit in the page toolbar. macOS keeps its own
   // traffic lights over the sidebar; Linux keeps the desktop's title bar (its window managers vary too much).
   if(windows||mac) {
    ExtendClientAreaToDecorationsHint=true;ExtendClientAreaTitleBarHeightHint=-1;
    ExtendClientAreaChromeHints=windows?Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome:Avalonia.Platform.ExtendClientAreaChromeHints.PreferSystemChrome;
   }
   if(selfTestResult!=null||Program.PreviewFolder!=null) {
    // Tests and previews render off-screen: nothing appears on the desktop or the taskbar.
    WindowStartupLocation=WindowStartupLocation.Manual;Position=new PixelPoint(-6000,-6000);ShowInTaskbar=false;ShowActivated=false;
   }
   using(var icon=typeof(BrowserWindow).Assembly.GetManifestResourceStream("Mica.Brand.ico"))if(icon!=null)Icon=new WindowIcon(icon);

   back=IconButton(Glyphs.Back,"Back (Alt+Left)",15);forward=IconButton(Glyphs.Forward,"Forward (Alt+Right)",15);reload=IconButton(Glyphs.Reload,"Reload ("+Mod+"R)",15);
   theme=IconButton(Glyphs.Moon,"Switch theme",15);showSidebar=IconButton(Glyphs.Menu,"Show sidebar",15);showSidebar.IsVisible=false;

   // Sidebar: wordmark, search, space, pinned and open tabs, then the menu row.
   sidebar.RowDefinitions=new RowDefinitions("60,Auto,*,Auto");
   if(mac)sidebar.Margin=new Thickness(9,22,15,0);
   var brand=new Grid {Background=Brushes.Transparent};
   var wordmark=new TextBlock {Text="Mica.",FontSize=38,Margin=new Thickness(12,7,0,0),VerticalAlignment=VerticalAlignment.Center};Res(wordmark,TextBlock.FontFamilyProperty,"BrandFont");brand.Children.Add(wordmark);
   var hide=IconButton(Glyphs.Menu,"Hide sidebar",15);hide.HorizontalAlignment=HorizontalAlignment.Right;hide.Click+=delegate {SetSidebar(false);};brand.Children.Add(hide);
   DragArea(brand);sidebar.Children.Add(brand);

   var top=new StackPanel();Grid.SetRow(top,1);
   var searchRow=new Grid();searchRow.Children.Add(Glyph(Glyphs.Search,14,null));searchRow.Children[0].HorizontalAlignment=HorizontalAlignment.Left;
   searchRow.Children.Add(Muted(new TextBlock {Text="Search anything",Margin=new Thickness(26,0,0,0)}));
   searchRow.Children.Add(Muted(new TextBlock {Text=Mod+"L",FontSize=10,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center}));
   var search=new Button {Content=searchRow,Margin=new Thickness(0,12,0,15)};search.Classes.Add("flat");search.Classes.Add("card");ToolTip.SetTip(search,"Search or type an address ("+Mod+"L)");
   search.Click+=delegate {FocusAddress();};top.Children.Add(search);
   var spaceRow=new Grid();var books=Glyph(Glyphs.Spaces,15,"accent");books.HorizontalAlignment=HorizontalAlignment.Left;spaceRow.Children.Add(books);
   var spaceText=new StackPanel {Margin=new Thickness(27,0,18,0)};var spaceLabel=Eyebrow("SPACE");spaceLabel.FontSize=9;spaceLabel.FontWeight=FontWeight.Normal;spaceLabel.Margin=new Thickness(0,0,0,4);spaceText.Children.Add(spaceLabel);spaceText.Children.Add(spaceName);spaceRow.Children.Add(spaceText);
   var chevron=Glyph(Glyphs.Chevron,10,null);chevron.HorizontalAlignment=HorizontalAlignment.Right;spaceRow.Children.Add(chevron);
   var space=new Button {Content=spaceRow};space.Classes.Add("flat");space.Classes.Add("card");ToolTip.SetTip(space,"Choose a space");
   space.Click+=delegate {SpaceMenu().ShowAt(space);};top.Children.Add(space);
   sidebar.Children.Add(top);

   var list=new StackPanel();
   var pinnedHead=new StackPanel {Margin=new Thickness(0,22,0,0)};var pinnedLabel=Eyebrow("PINNED");pinnedLabel.Margin=new Thickness(10,0,0,10);pinnedHead.Children.Add(pinnedLabel);
   Muted(pinnedHint);pinnedHead.Children.Add(pinnedHint);pinnedHead.Children.Add(pinnedList);list.Children.Add(pinnedHead);
   list.Children.Add(Res(new Border {Height=1,Margin=new Thickness(7,14)},Border.BackgroundProperty,"Divider"));
   var tabsHead=new Grid {Margin=new Thickness(10,0,5,7)};tabsHead.Children.Add(Eyebrow("TABS"));tabCount.Classes.Add("eyebrow");tabCount.FontWeight=FontWeight.Normal;tabsHead.Children.Add(tabCount);list.Children.Add(tabsHead);
   list.Children.Add(tabList);
   var newRow=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12};newRow.Children.Add(Glyph(Glyphs.Plus,12,"muted"));newRow.Children.Add(Muted(new TextBlock {Text="New tab"}));
   var add=new Button {Content=newRow,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,5,0,0)};add.Classes.Add("flat");ToolTip.SetTip(add,"New tab ("+Mod+"T)");
   add.Click+=delegate {NewTab();};list.Children.Add(add);
   var scroll=new ScrollViewer {Content=list,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(scroll,2);sidebar.Children.Add(scroll);

   var bottom=new Grid {Margin=new Thickness(0,10,0,8)};Grid.SetRow(bottom,3);
   var menuRow=new StackPanel {Orientation=Orientation.Horizontal,Spacing=10};menuRow.Children.Add(Glyph(Glyphs.Gear,15,null));menuRow.Children.Add(new TextBlock {Text="Menu",VerticalAlignment=VerticalAlignment.Center});
   var menu=new Button {Content=menuRow,HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,10)};menu.Classes.Add("flat");ToolTip.SetTip(menu,"Browser menu");
   menu.Click+=delegate {MainMenu().ShowAt(menu);};bottom.Children.Add(menu);
   var avatar=IconButton(Glyphs.Person,"Accounts aren't in this preview yet",15);avatar.HorizontalAlignment=HorizontalAlignment.Right;avatar.Margin=new Thickness(0,0,38,0);bottom.Children.Add(avatar);
   theme.HorizontalAlignment=HorizontalAlignment.Right;theme.Click+=delegate {SetDark(!state.Dark);};bottom.Children.Add(theme);
   sidebar.Children.Add(bottom);

   // The page frame: toolbar, loading line, notices, then the page (and the Claude panel beside it).
   var frame=Res(Res(new Border {CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),ClipToBounds=true},Border.BackgroundProperty,"Canvas"),Border.BorderBrushProperty,"FrameEdge");
   Grid.SetColumn(frame,1);
   var inside=new Grid {RowDefinitions=new RowDefinitions("44,2,Auto,*")};frame.Child=inside;
   var bar=Res(new Border {BorderThickness=new Thickness(0,0,0,1),CornerRadius=new CornerRadius(11,11,0,0)},Border.BorderBrushProperty,"Line");
   var toolbar=new Grid {Background=Brushes.Transparent,Margin=new Thickness(9,0,6,0),ColumnDefinitions=new ColumnDefinitions("Auto,*,Auto")};bar.Child=toolbar;DragArea(toolbar);
   var nav=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
   showSidebar.Click+=delegate {SetSidebar(true);};
   foreach(var b in new[]{showSidebar,back,forward,reload})nav.Children.Add(b);
   back.Click+=delegate {var v=ActiveView();if(v!=null&&v.CanGoBack)v.GoBack();};
   forward.Click+=delegate {var v=ActiveView();if(v!=null&&v.CanGoForward)v.GoForward();};
   reload.Click+=delegate {Reload();};
   toolbar.Children.Add(nav);
   var center=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0),Spacing=2};
   var globe=Glyph(Glyphs.Globe,12,"muted");center.Children.Add(globe);address.Classes.Add("address");center.Children.Add(address);
   Grid.SetColumn(center,1);toolbar.Children.Add(center);
   address.KeyDown+=delegate(object s,KeyEventArgs e) {if(e.Key==Key.Enter){Navigate(address.Text);e.Handled=true;}else if(e.Key==Key.Escape){ShowAddress();var v=ActiveView();if(v!=null)v.Focus();e.Handled=true;}};
   address.GotFocus+=delegate {address.Watermark="Search or type a web address";address.MinWidth=420;globe.IsVisible=false;Dispatcher.UIThread.Post(address.SelectAll);};
   address.LostFocus+=delegate {address.Watermark="A fresh start";address.MinWidth=150;globe.IsVisible=true;ShowAddress();};
   var right=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};
   Grid.SetColumn(right,2);toolbar.Children.Add(right);
   if(windows) {
    right.Children.Add(Res(new Border {Width=1,Height=16,Margin=new Thickness(5,0,7,0)},Border.BackgroundProperty,"Line"));
    var min=CaptionButton(Glyphs.Minimize,"Minimize");min.Click+=delegate {WindowState=WindowState.Minimized;};
    var max=CaptionButton(Glyphs.Maximize,"Maximize");max.Click+=delegate {ToggleMaximized();};
    var close=CaptionButton(Glyphs.Close,"Close window");close.Click+=delegate {Close();};
    right.Children.Add(min);right.Children.Add(max);right.Children.Add(close);
   }
   inside.Children.Add(bar);
   loading.Classes.Add("loading");Grid.SetRow(loading,1);inside.Children.Add(loading);
   Res(notice,Border.BackgroundProperty,"Hover");
   noticeGames.Classes.Add("flat");Res(noticeGames,BackgroundProperty,"Panel");
   noticeGames.Click+=delegate {notice.IsVisible=false;Navigate("mica://games");};
   var noticeRow=new DockPanel();DockPanel.SetDock(noticeGames,Dock.Right);noticeRow.Children.Add(noticeGames);noticeRow.Children.Add(noticeText);notice.Child=noticeRow;
   Grid.SetRow(notice,2);inside.Children.Add(notice);
   var body=new Grid {ColumnDefinitions=new ColumnDefinitions("*,Auto")};Grid.SetRow(body,3);inside.Children.Add(body);
   // Inset by the frame's edge so the page doesn't cover it.
   pages.Margin=new Thickness(0,0,0,1);body.Children.Add(pages);

   layout.ColumnDefinitions=new ColumnDefinitions("238,*");
   layout.Children.Add(sidebar);layout.Children.Add(frame);
   claudeLoaded=CrossExtensions.IsInstalled(profile,BrowserApp.ClaudeExtensionId);
   BuildAiPanel(right,body);
   Content=layout;
   KeyDown+=OnKey;
   Closing+=delegate {Save();};
   UpdateHomeSites();
   ApplyTheme();RenderTabs();Show(state.ActiveId);
   if(selfTestResult!=null)Opened+=async delegate {await RunSelfTest(selfTestResult);};
   else if(Program.PreviewFolder!=null)Opened+=async delegate {await RunPreview(Program.PreviewFolder);};
  }

  // Empty parts of the sidebar head and the toolbar move the window, and maximize it on a double click.
  void DragArea(Panel area) {
   area.PointerPressed+=delegate(object s,PointerPressedEventArgs e) {
    if(e.Source!=area||!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
    if(e.ClickCount==2)ToggleMaximized();else BeginMoveDrag(e);
   };
  }
  void ToggleMaximized() {WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;}
  void SetSidebar(bool shown) {
   layout.ColumnDefinitions[0].Width=shown?new GridLength(238):new GridLength(0);sidebar.IsVisible=shown;showSidebar.IsVisible=!shown;
  }

  MenuFlyout SpaceMenu() {
   var flyout=new MenuFlyout();
   foreach(var name in state.Spaces) {
    var item=new MenuItem {Header=name==state.Space?name+"  ✓":name};string pick=name;
    item.Click+=delegate {SwitchSpace(pick);};flyout.Items.Add(item);
   }
   return flyout;
  }
  void SwitchSpace(string name) {
   if(name==state.Space)return;
   state.Space=name;
   var tab=state.Tabs.Where(t=>t.Space==name).OrderByDescending(t=>t.LastUsed).FirstOrDefault()??state.AddTab("mica://home");
   RenderTabs();Show(tab.Id);Save();
  }
  MenuFlyout MainMenu() {
   var flyout=new MenuFlyout();
   var items=new List<Tuple<string,Action>> {
    Tuple.Create<string,Action>("New tab\t"+Mod+"T",NewTab),
    Tuple.Create<string,Action>("Games",delegate {NewTab();Navigate("mica://games");}),
    Tuple.Create<string,Action>("Claude\t"+Mod+"Shift+Space",ToggleAi),
    Tuple.Create<string,Action>(state.Dark?"Light theme":"Dark theme",delegate {SetDark(!state.Dark);}),
   };
   foreach(var entry in items){var item=new MenuItem {Header=entry.Item1};var run=entry.Item2;item.Click+=delegate {run();};flyout.Items.Add(item);}
   flyout.Items.Add(new Separator());
   flyout.Items.Add(new MenuItem {Header="Mica "+typeof(BrowserWindow).Assembly.GetName().Version.ToString(3)+" · preview for macOS and Linux",IsEnabled=false});
   return flyout;
  }

  CrossTab Active {get {CrossTab t;return state.ActiveId!=null&&tabs.TryGetValue(state.ActiveId,out t)?t:null;}}
  AvaloniaCefBrowser ActiveView() {var t=Active;return t==null?null:t.View;}

  // The engine address for a Mica address: internal pages on their hosts, the new tab page included.
  static string EngineFor(string url) {return url=="mica://home"?BuiltInPages.Home:Navigation.EngineAddress(url);}
  static string FromEngine(string address) {
   Uri uri;
   if(string.IsNullOrEmpty(address)||address=="about:blank"||Uri.TryCreate(address,UriKind.Absolute,out uri)&&uri.Host==BuiltInPages.HomeHost)return "mica://home";
   return Navigation.FromEngine(address);
  }

  void OnKey(object sender,KeyEventArgs e) {
   bool ctrl=(e.KeyModifiers&(OperatingSystem.IsMacOS()?KeyModifiers.Meta:KeyModifiers.Control))!=0,alt=(e.KeyModifiers&KeyModifiers.Alt)!=0;
   if(ctrl&&e.Key==Key.T)NewTab();
   else if(ctrl&&e.Key==Key.W){if(Active!=null)CloseTab(Active.Entry.Id);}
   else if(ctrl&&e.Key==Key.L)FocusAddress();
   else if(e.Key==Key.F5||ctrl&&e.Key==Key.R)Reload();
   else if(alt&&e.Key==Key.Left){var v=ActiveView();if(v!=null&&v.CanGoBack)v.GoBack();}
   else if(alt&&e.Key==Key.Right){var v=ActiveView();if(v!=null&&v.CanGoForward)v.GoForward();}
   else if(ctrl&&(e.KeyModifiers&KeyModifiers.Shift)!=0&&e.Key==Key.Space)ToggleAi();
   else return;
   e.Handled=true;
  }
  void FocusAddress() {address.Focus();address.SelectAll();}

  void NewTab() {var tab=state.AddTab("mica://home");RenderTabs();Show(tab.Id);address.Focus();Save();}
  void CloseTab(string id) {
   CrossTab tab;
   if(tabs.TryGetValue(id,out tab)) {
    tabs.Remove(id);
    if(tab.View!=null){pages.Children.Remove(tab.View);tab.View.Dispose();}
   }
   var closed=state.Tabs.FirstOrDefault(t=>t.Id==id);state.RecordClosed(closed);state.CloseTab(id);
   RenderTabs();Show(state.ActiveId);Save();
  }
  void Reload() {var v=ActiveView();if(v!=null)v.Reload(false);}
  void TogglePinned(string id) {var entry=state.Tabs.FirstOrDefault(t=>t.Id==id);if(entry==null)return;entry.Pinned=!entry.Pinned;RenderTabs();Save();}

  // Tabs of the current space, in session order: pinned ones first, as on Windows.
  void RenderTabs() {
   tabList.Children.Clear();pinnedList.Children.Clear();int open=0;
   foreach(var entry in state.Tabs.Where(t=>t.Space==state.Space)) {
    CrossTab tab;if(!tabs.TryGetValue(entry.Id,out tab)){tab=MakeTab(entry);tabs[entry.Id]=tab;}
    RefreshTab(tab);
    if(entry.Pinned)pinnedList.Children.Add(tab.Header);else {tabList.Children.Add(tab.Header);open++;}
   }
   pinnedHint.IsVisible=pinnedList.Children.Count==0;
   tabCount.Text=open.ToString("00");spaceName.Text=state.Space;
   PaintTabs();
  }
  CrossTab MakeTab(PageEntry entry) {
   var tab=new CrossTab {Entry=entry};
   tab.Icon=new ContentControl {Width=15,Height=15,VerticalAlignment=VerticalAlignment.Center};
   tab.Label=new TextBlock {TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,6,0)};
   var close=CaptionButton(Glyphs.Close,"Close tab ("+Mod+"W)");close.Classes.Add("close");close.Width=24;close.Height=24;
   close.Click+=delegate(object s,Avalonia.Interactivity.RoutedEventArgs e) {e.Handled=true;CloseTab(entry.Id);};
   var row=new Grid {ColumnDefinitions=new ColumnDefinitions("Auto,*,Auto")};row.Children.Add(tab.Icon);Grid.SetColumn(tab.Label,1);row.Children.Add(tab.Label);Grid.SetColumn(close,2);row.Children.Add(close);
   tab.Header=new Border {Child=row};tab.Header.Classes.Add("tab");
   tab.Header.PointerPressed+=delegate(object s,PointerPressedEventArgs e) {
    var point=e.GetCurrentPoint(tab.Header).Properties;
    if(point.IsMiddleButtonPressed)CloseTab(entry.Id);else if(point.IsLeftButtonPressed)Show(entry.Id);
   };
   var pin=new MenuItem();var shut=new MenuItem {Header="Close tab"};
   pin.Click+=delegate {TogglePinned(entry.Id);};shut.Click+=delegate {CloseTab(entry.Id);};
   var menu=new ContextMenu();menu.Items.Add(pin);menu.Items.Add(shut);
   menu.Opening+=delegate {pin.Header=entry.Pinned?"Unpin":"Pin";};
   tab.Header.ContextMenu=menu;
   return tab;
  }
  void RefreshTab(CrossTab tab) {
   tab.Label.Text=TabTitle(tab.Entry);ToolTip.SetTip(tab.Header,TabTitle(tab.Entry)+"\n"+(tab.Entry.Url=="mica://home"?"New tab":Navigation.AddressLabel(tab.Entry.Url)));
   string url=tab.Entry.Url;string kind=url=="mica://home"?Glyphs.Home:Navigation.IsInternal(url)?Glyphs.Game:Glyphs.Globe;
   var current=tab.Icon.Content as Viewbox;
   if(current==null||(string)current.Tag!=kind)tab.Icon.Content=Glyph(kind,14,kind==Glyphs.Globe?"muted":null);
  }
  static string TabTitle(PageEntry e) {return string.IsNullOrWhiteSpace(e.Title)?Navigation.AddressLabel(e.Url):e.Title;}
  void PaintTabs() {foreach(var tab in tabs.Values)tab.Header.Classes.Set("active",tab.Entry.Id==state.ActiveId);}

  // Shows a tab, creating its engine view the first time: tabs restored from the session wait until picked.
  void Show(string id) {
   CrossTab tab;if(id==null||!tabs.TryGetValue(id,out tab))return;
   state.ActivateTab(id,DateTime.UtcNow);
   if(tab.Entry.Space!=state.Space){state.Space=tab.Entry.Space;RenderTabs();}
   if(tab.View==null) {
    tab.View=new AvaloniaCefBrowser();
    tab.View.Address=EngineFor(tab.Entry.Url);
    Wire(tab);
    pages.Children.Add(tab.View);
   }
   foreach(var t in tabs.Values)if(t.View!=null)t.View.IsVisible=t==tab;
   PaintTabs();ShowAddress();UpdateButtons();
   BindClaudePanel(false);
   Title=(tab.Entry.Url=="mica://home"?"New tab":TabTitle(tab.Entry))+" — Mica";
  }
  void ShowAddress() {var t=Active;if(t==null)return;address.Text=t.Entry.Url=="mica://home"?"":Navigation.AddressLabel(t.Entry.Url);}
  void UpdateButtons() {
   var t=Active;var v=ActiveView();back.IsEnabled=v!=null&&v.CanGoBack;forward.IsEnabled=v!=null&&v.CanGoForward;
   loading.IsVisible=t!=null&&t.Loading;
  }

  // The engine reports from its own threads; everything here moves back to the UI thread first.
  // Engine events, kept only while the self-test runs (it writes them out when a check fails).
  internal System.Collections.Concurrent.ConcurrentQueue<string> trace;
  void Trace(string line) {var t=trace;if(t!=null)t.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff")+" "+line);}
  void Wire(CrossTab tab) {
   var view=tab.View;
   view.LifeSpanHandler=new PopupsToTabs(this);
   view.LoadError+=delegate(object s,Xilium.CefGlue.Common.Events.LoadErrorEventArgs e) {Trace("error "+e.ErrorCode+" "+e.ErrorText+" "+e.FailedUrl);};
   view.AddressChanged+=delegate(object s,string a) {Trace("address "+a);};
   view.LoadEnd+=delegate(object s,Xilium.CefGlue.Common.Events.LoadEndEventArgs e) {Trace("end "+e.HttpStatusCode+" main="+e.Frame.IsMain);};
   view.LoadingStateChange+=delegate(object s,Xilium.CefGlue.Common.Events.LoadingStateChangeEventArgs e) {Trace("loading="+e.IsLoading);};
   view.AddressChanged+=delegate(object s,string a) {Dispatcher.UIThread.Post(delegate {
    tab.Entry.Url=FromEngine(a);RefreshTab(tab);
    if(tab==Active&&!address.IsFocused)ShowAddress();
   });};
   view.TitleChanged+=delegate(object s,string title) {Dispatcher.UIThread.Post(delegate {
    string internalTitle=Navigation.InternalTitle(tab.Entry.Url);
    tab.Entry.Title=internalTitle??(string.IsNullOrWhiteSpace(title)||title=="about:blank"?null:title);
    RefreshTab(tab);if(tab==Active)Title=(tab.Entry.Url=="mica://home"?"New tab":TabTitle(tab.Entry))+" — Mica";
   });};
   view.LoadStart+=delegate(object s,Xilium.CefGlue.Common.Events.LoadStartEventArgs e) {if(e.Frame.IsMain)Dispatcher.UIThread.Post(delegate {if(tab==Active)notice.IsVisible=false;});};
   view.LoadEnd+=delegate(object s,Xilium.CefGlue.Common.Events.LoadEndEventArgs e) {
    if(!e.Frame.IsMain)return;int status=e.HttpStatusCode;
    Dispatcher.UIThread.Post(delegate {tab.LastStatus=status;state.Visit(tab.Entry.Url,tab.Entry.Title);UpdateHomeSites();Save();});
   };
   view.LoadError+=delegate(object s,Xilium.CefGlue.Common.Events.LoadErrorEventArgs e) {
    if(!e.Frame.IsMain||e.ErrorCode==Xilium.CefGlue.CefErrorCode.Aborted)return;
    var code=e.ErrorCode;string text=e.ErrorText;
    Dispatcher.UIThread.Post(delegate {if(tab==Active)ShowLoadError(code,text);});
   };
   view.LoadingStateChange+=delegate(object s,Xilium.CefGlue.Common.Events.LoadingStateChangeEventArgs e) {
    bool busy=e.IsLoading;
    Dispatcher.UIThread.Post(delegate {
     tab.Loading=busy;
     if(tab==Active)UpdateButtons();
     if(!busy&&tab.Waiter!=null){var w=tab.Waiter;tab.Waiter=null;w.TrySetResult(true);}
    });
   };
  }
  // The new tab page's most visited sites (Core's TopSites, as on Windows).
  void UpdateHomeSites() {
   var sites=state.TopSites(8).Select(e=>new Dictionary<string,object> {{"t",e.Title??""},{"u",e.Url}}).ToList();
   BuiltInPages.HomeSites=new JavaScriptSerializer().Serialize(sites);
  }
  // As on Windows: no connection means the games are a click away.
  void ShowLoadError(Xilium.CefGlue.CefErrorCode code,string text) {
   bool connection=code==Xilium.CefGlue.CefErrorCode.InternetDisconnected||code==Xilium.CefGlue.CefErrorCode.NameNotResolved||code==Xilium.CefGlue.CefErrorCode.ConnectionRefused||code==Xilium.CefGlue.CefErrorCode.AddressUnreachable||code==Xilium.CefGlue.CefErrorCode.ConnectionReset;
   noticeText.Text=code==Xilium.CefGlue.CefErrorCode.InternetDisconnected?"Mica can't reach the internet. Check your Wi-Fi or cable, then try again.":"This page couldn't load ("+text+"). Check the address and your connection, then try again.";
   noticeGames.IsVisible=connection;notice.IsVisible=true;
  }
  void Notice(string text) {noticeText.Text=text;noticeGames.IsVisible=false;notice.IsVisible=true;}

  // What you typed, through the same rules as the Windows address bar (Navigation.Resolve).
  void Navigate(string input) {
   string url;
   try {url=Navigation.Resolve(input);}
   catch(ArgumentException e) {Notice(e.Message);return;}
   if(Navigation.IsLibrary(url)||Navigation.IsReading(url)){Notice("History, downloads and saved articles aren't in this preview yet.");return;}
   var tab=Active;if(tab==null)return;
   notice.IsVisible=false;
   tab.Entry.Url=url;tab.Entry.Title=Navigation.InternalTitle(url);
   tab.View.Address=EngineFor(url);
   RefreshTab(tab);ShowAddress();tab.View.Focus();
  }

  void SetDark(bool dark) {
   state.Dark=dark;ApplyTheme();
   foreach(var tab in tabs.Values)if(tab.View!=null&&BuiltInPages.IsBuiltIn(tab.View.Address))tab.View.ExecuteJavaScript(BuiltInPages.ThemeScript(dark),"",1);
   Save();
  }
  void ApplyTheme() {
   var app=Application.Current as App;if(app!=null)app.ApplyTheme(state.Dark);
   theme.Content=Glyph(state.Dark?Glyphs.Sun:Glyphs.Moon,15,null);
   PaintTabs();
  }
  void Save() {try {StateStore.Save(statePath,state);} catch(IOException) {} catch(UnauthorizedAccessException) {}}
 }
}
