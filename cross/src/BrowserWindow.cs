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
using Xilium.CefGlue;
using Xilium.CefGlue.Avalonia;

namespace Mica {
 // A page's engine view, with the Chromium host reachable for find, zoom, print, audio and DevTools.
 sealed class MicaView : AvaloniaCefBrowser {
  protected override Type StyleKeyOverride {get {return typeof(AvaloniaCefBrowser);}}
  internal CefBrowserHost Host {get {var b=UnderlyingBrowser;return b==null?null:b.GetHost();}}
 }
 // One tab: its entry in the session (shared Core.cs model) and, once it has been shown, its engine view.
 sealed class CrossTab {
  public PageEntry Entry;public MicaView View;public Border Header;public TextBlock Label;public ContentControl Icon;public Button Mini;
  public int LastStatus;public bool Loading;public TaskCompletionSource<bool> Waiter;
  public string FaviconUrl;public IImage Favicon;public bool Muted;public double Zoom=1;
  public Xilium.CefGlue.CefErrorCode? Error;public string ErrorText;
  public bool Playing;public DateTime AudibleAt;
 }
 // Mica's window, laid out like the Windows build (Shell.xaml): a sidebar with the wordmark, search, the space and
 // the tabs as a list (or a slim rail of icons), and the page in a rounded frame with a slim toolbar. Colors and
 // button styles are in Theme.axaml.
 sealed partial class BrowserWindow : Window {
  readonly string profile,statePath;readonly BrowserState state;
  readonly Dictionary<string,CrossTab> tabs=new Dictionary<string,CrossTab>();
  readonly StackPanel tabList=new StackPanel(),pinnedList=new StackPanel(),miniTabs=new StackPanel {Spacing=2};
  readonly TextBlock tabCount=new TextBlock {HorizontalAlignment=HorizontalAlignment.Right},pinnedHint=new TextBlock {Text="Right-click a tab to keep it here",FontSize=11,Margin=new Thickness(10,0,0,0)};
  readonly TextBlock spaceName=new TextBlock {FontWeight=FontWeight.SemiBold};
  readonly Grid root=new Grid(),pages=new Grid(),layout=new Grid {Margin=new Thickness(7)},sidebar=new Grid {Margin=new Thickness(9,0,15,0)},rail=new Grid {IsVisible=false};
  readonly Panel sidebarFull=new Grid();
  // The address as you see it in the toolbar; a click (or Ctrl+L) opens Spotlight to change it, as on Windows.
  readonly TextBlock address=new TextBlock {FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=440,VerticalAlignment=VerticalAlignment.Center};
  readonly Button addressButton=new Button {HorizontalAlignment=HorizontalAlignment.Center,MinWidth=150,MaxWidth=480,Padding=new Thickness(12,6),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0)};
  readonly ContentControl siteGlyph=new ContentControl {Margin=new Thickness(0,0,7,0),VerticalAlignment=VerticalAlignment.Center};
  readonly Border zoomBadge=new Border {IsVisible=false,CornerRadius=new CornerRadius(4),Padding=new Thickness(6,1),Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};
  readonly TextBlock zoomText=new TextBlock {FontSize=10};
  readonly Button back,forward,reload,theme,toggleSidebar,bookmark,miniTheme;
  readonly ProgressBar loading=new ProgressBar {IsIndeterminate=true,IsVisible=false};
  readonly Border notice=new Border {IsVisible=false,Padding=new Thickness(14,8),Margin=new Thickness(12,10,12,0),CornerRadius=new CornerRadius(9)};
  readonly TextBlock noticeText=new TextBlock {VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
  readonly Button noticeGames=new Button {Content="Play a game while you wait",Margin=new Thickness(12,0,0,0),Padding=new Thickness(12,6)};
  bool sidebarHidden;Button menuButton;

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
   public const string Star="M12 3.8l2.5 5.2 5.7.8-4.1 4 1 5.7-5.1-2.7-5.1 2.7 1-5.7-4.1-4 5.7-.8z",Lock="M7.5 11V8a4.5 4.5 0 0 1 9 0v3 M5.5 11h13v9.5h-13z",Info="M12 3a9 9 0 1 1 0 18a9 9 0 1 1 0-18z M12 11v5.5 M12 7.6h.01";
   public const string History="M4.5 12a7.5 7.5 0 1 0 2.2-5.3 M4.5 4.5v4.5H9 M12 8v4.5l3 2",Download="M12 4v11 M7 10.5l5 5 5-5 M5 19.5h14",Tabs="M4 6h16v13H4z M4 10h16 M9 6v4";
   public const string Find="M10.5 4a6.5 6.5 0 1 1 0 13a6.5 6.5 0 1 1 0-13z M15.2 15.2L20 20",Print="M7 9V4h10v5 M5 9h14v7h-3 M8 16H5V9 M8 13h8v7H8z",Code="M9 7l-5 5 5 5 M15 7l5 5-5 5";
   public const string Keys="M3 7h18v10H3z M6.5 10h.01 M10 10h.01 M13.5 10h.01 M17 10h.01 M7.5 14h9",Rest="M15.5 4.5A8 8 0 1 0 19.5 15a6 6 0 0 1-4-10.5z",Pin="M9 4h6l-1 5 3 3H7l3-3z M12 12v8";
   public const string Copy="M8 8h11v11H8z M5 16V5h11",Sound="M4 9.5h3.5L12 6v12l-4.5-3.5H4z M15.5 9.5a3.5 3.5 0 0 1 0 5 M18 7a7 7 0 0 1 0 10",Muted="M4 9.5h3.5L12 6v12l-4.5-3.5H4z M15.5 9.5l5 5 M20.5 9.5l-5 5";
   public const string ZoomIn="M10.5 4a6.5 6.5 0 1 1 0 13a6.5 6.5 0 1 1 0-13z M15.2 15.2L20 20 M10.5 8v5 M8 10.5h5",ZoomOut="M10.5 4a6.5 6.5 0 1 1 0 13a6.5 6.5 0 1 1 0-13z M15.2 15.2L20 20 M8 10.5h5";
   public const string Reader="M4 5.5h6.5a2 2 0 0 1 1.5.7 2 2 0 0 1 1.5-.7H20v13h-6.5a1.5 1.5 0 0 0-1.5 1.5 1.5 1.5 0 0 0-1.5-1.5H4z M12 6.2v13.3",Puzzle="M5 8h3.5a2 2 0 1 1 4 0H16v3.5a2 2 0 1 1 0 4V19H5z";
   public const string Play="M8 5.5v13l10.5-6.5z",Pause="M8 5.5v13 M16 5.5v13";
   public const string Dot="M12 8.5a3.5 3.5 0 1 1 0 7a3.5 3.5 0 1 1 0-7z";
   public const string Check="M5 12.5l4.5 4.5L19 7.5",Note="M9 17.5V6l10-2v11.5 M9 17.5a2.5 2.5 0 1 1-5 0a2.5 2.5 0 1 1 5 0z M19 15.5a2.5 2.5 0 1 1-5 0a2.5 2.5 0 1 1 5 0z";
   public const string Chat="M4.5 5h15v10.5h-9l-4.5 4v-4h-1.5z",OpenTab="M13.5 4.5h6v6 M19.5 4.5l-8 8 M17 14v5.5H4.5V7H10",ClosePane="M4 5h16v14H4z M14.5 5v14 M7 10.5l3 1.5-3 1.5";
   public const string Palette="M12 3.5a8.5 8.5 0 1 0 0 17c1.2 0 1.8-.8 1.8-1.7 0-1.3-1.1-1.6-1.1-2.7 0-1 .8-1.6 1.8-1.6h2.2a3.8 3.8 0 0 0 3.8-3.8C20.5 6.8 16.7 3.5 12 3.5z M7.5 11.5h.01 M9.5 7.5h.01 M14.5 7.5h.01";
   public const string Rename="M4 20h4l10.5-10.5-4-4L4 16z M13 7l4 4",Trash="M5 7h14 M9.5 7V4.5h5V7 M7 7l1 13h8l1-13",Work="M4 8h16v11H4z M9 8V5.5h6V8 M4 13h16",Folder="M3.5 6.5h6l2 2h9v10h-17z";
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
  static bool Mac {get {return OperatingSystem.IsMacOS();}}
  static string Mod {get {return Mac?"⌘":"Ctrl+";}}
  static string ModShift {get {return Mac?"⇧⌘":"Ctrl+Shift+";}}
  static string AltKey {get {return Mac?"⌥":"Alt+";}}

  Button spaceButton,avatarButton,railAvatar;
  // Text size: these grow with it (Personalize.cs ApplyTextScale).
  readonly LayoutTransformControl sidebarScale=new LayoutTransformControl(),toolbarScale=new LayoutTransformControl();LayoutTransformControl islandScale;Grid frameInside;
  // The window extensions' own tabs open in (ExtensionWindows).
  internal static BrowserWindow Main;
  public BrowserWindow(string profilePath,string selfTestResult) {
   if(Main==null)Main=this;
   profile=profilePath;statePath=Path.Combine(profile,"session.json");
   state=StateStore.Load(statePath);
   if(state.SiteZoom==null)state.SiteZoom=new Dictionary<string,double>();
   if(state.SiteMemory==null)state.SiteMemory=new Dictionary<string,string>();
   BuiltInPages.Dark=delegate {return state.Dark;};
   BuiltInPages.Look=HomeLook;BuiltInPages.PhotoPath=delegate {return state.HomeBackground=="photo"?HomePhotoPath:null;};BuiltInPages.Translations=Localizer.PageTable;
   Title=Program.Private?"Mica · Private":"Mica";Width=1280;Height=840;MinWidth=760;MinHeight=520;
   Res(this,BackgroundProperty,"Backdrop");
   bool windows=OperatingSystem.IsWindows();
   // As on Windows, Mica draws its own title bar: the window buttons sit in the page toolbar. macOS keeps its own
   // traffic lights over the sidebar; Linux keeps the desktop's title bar (its window managers vary too much).
   if(windows||Mac) {
    ExtendClientAreaToDecorationsHint=true;ExtendClientAreaTitleBarHeightHint=-1;
    ExtendClientAreaChromeHints=windows?Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome:Avalonia.Platform.ExtendClientAreaChromeHints.PreferSystemChrome;
   }
   if(selfTestResult!=null||Program.PreviewFolder!=null) {
    // Tests and previews render off-screen: nothing appears on the desktop or the taskbar.
    WindowStartupLocation=WindowStartupLocation.Manual;Position=new PixelPoint(-6000,-6000);ShowInTaskbar=false;ShowActivated=false;
   }
   using(var icon=typeof(BrowserWindow).Assembly.GetManifestResourceStream("Mica.Brand.ico"))if(icon!=null)Icon=new WindowIcon(icon);

   back=IconButton(Glyphs.Back,"Back ("+AltKey+"Left)",15);forward=IconButton(Glyphs.Forward,"Forward ("+AltKey+"Right)",15);reload=IconButton(Glyphs.Reload,"Reload ("+Mod+"R)",15);
   theme=IconButton(Glyphs.Moon,"Switch theme",15);miniTheme=IconButton(Glyphs.Moon,"Switch theme",15);
   toggleSidebar=IconButton(Glyphs.Menu,"Hide sidebar ("+ModShift+"B)",15);
   bookmark=IconButton(Glyphs.Star,"Bookmark this page ("+Mod+"D)",15);

   // Sidebar: wordmark, search, space, pinned and open tabs, then the menu row; or, hidden, a slim rail of icons.
   sidebar.RowDefinitions=new RowDefinitions("60,*");sidebar.ClipToBounds=true;
   if(Mac)sidebar.Margin=new Thickness(9,22,15,0);
   var brand=new Grid {Background=Brushes.Transparent};
   var wordmark=new TextBlock {Text="Mica.",FontSize=38,Margin=new Thickness(12,7,0,0),VerticalAlignment=VerticalAlignment.Center};Res(wordmark,TextBlock.FontFamilyProperty,"BrandFont");brand.Children.Add(wordmark);
   toggleSidebar.HorizontalAlignment=HorizontalAlignment.Right;toggleSidebar.Click+=delegate {ToggleSidebar();};brand.Children.Add(toggleSidebar);
   DragArea(brand);sidebar.Children.Add(brand);
   Grid.SetRow(sidebarFull,1);sidebar.Children.Add(sidebarFull);Grid.SetRow(rail,1);sidebar.Children.Add(rail);
   var full=(Grid)sidebarFull;full.RowDefinitions=new RowDefinitions("Auto,*,Auto");

   var top=new StackPanel();
   var searchRow=new Grid();searchRow.Children.Add(Glyph(Glyphs.Search,14,null));searchRow.Children[0].HorizontalAlignment=HorizontalAlignment.Left;
   searchRow.Children.Add(Muted(new TextBlock {Text="Search anything",Margin=new Thickness(26,0,0,0)}));
   searchRow.Children.Add(Muted(new TextBlock {Text=Mod+"K",FontSize=10,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center}));
   var search=new Button {Content=searchRow,Margin=new Thickness(0,12,0,15)};search.Classes.Add("flat");search.Classes.Add("card");ToolTip.SetTip(search,"Spotlight ("+Mod+"K)");
   search.Click+=delegate {OpenSpotlight(false);};top.Children.Add(search);
   var spaceRow=new Grid();var books=Glyph(Glyphs.Spaces,15,"accent");books.HorizontalAlignment=HorizontalAlignment.Left;spaceRow.Children.Add(books);
   var spaceText=new StackPanel {Margin=new Thickness(27,0,18,0)};var spaceLabel=Eyebrow("SPACE");spaceLabel.FontSize=9;spaceLabel.FontWeight=FontWeight.Normal;spaceLabel.Margin=new Thickness(0,0,0,4);spaceText.Children.Add(spaceLabel);spaceText.Children.Add(spaceName);spaceRow.Children.Add(spaceText);
   var chevron=Glyph(Glyphs.Chevron,10,null);chevron.HorizontalAlignment=HorizontalAlignment.Right;spaceRow.Children.Add(chevron);
   var space=new Button {Content=spaceRow};space.Classes.Add("flat");space.Classes.Add("card");ToolTip.SetTip(space,"Choose a space");
   spaceButton=space;space.Click+=delegate {ShowSpaces(space);};top.Children.Add(space);
   full.Children.Add(top);

   var list=new StackPanel();
   var pinnedHead=new StackPanel {Margin=new Thickness(0,22,0,0)};var pinnedLabel=Eyebrow("PINNED");pinnedLabel.Margin=new Thickness(10,0,0,10);pinnedHead.Children.Add(pinnedLabel);
   Muted(pinnedHint);pinnedHead.Children.Add(pinnedHint);pinnedHead.Children.Add(pinnedList);list.Children.Add(pinnedHead);
   list.Children.Add(Res(new Border {Height=1,Margin=new Thickness(7,14)},Border.BackgroundProperty,"Divider"));
   var tabsHead=new Grid {Margin=new Thickness(10,0,5,7)};tabsHead.Children.Add(Eyebrow("TABS"));tabCount.Classes.Add("eyebrow");tabCount.FontWeight=FontWeight.Normal;tabsHead.Children.Add(tabCount);list.Children.Add(tabsHead);
   list.Children.Add(tabList);
   var newRow=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12};newRow.Children.Add(Glyph(Glyphs.Plus,12,"muted"));newRow.Children.Add(Muted(new TextBlock {Text="New tab"}));
   var add=new Button {Content=newRow,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,5,0,0)};add.Classes.Add("flat");ToolTip.SetTip(add,"New tab ("+Mod+"T)");
   add.Click+=delegate {NewTab();};list.Children.Add(add);
   var scroll=new ScrollViewer {Content=list,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(scroll,1);full.Children.Add(scroll);

   var bottom=new Grid {Margin=new Thickness(0,10,0,8)};Grid.SetRow(bottom,2);
   var menuRow=new StackPanel {Orientation=Orientation.Horizontal,Spacing=10};menuRow.Children.Add(Glyph(Glyphs.Gear,15,null));menuRow.Children.Add(new TextBlock {Text="Menu",VerticalAlignment=VerticalAlignment.Center});
   var menu=menuButton=new Button {Content=menuRow,HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,10)};menu.Classes.Add("flat");ToolTip.SetTip(menu,"Browser menu");
   menu.Click+=delegate {ShowMenu(menu);};bottom.Children.Add(menu);
   var avatar=avatarButton=IconButton(Glyphs.Person,"Account and profiles",15);avatar.HorizontalAlignment=HorizontalAlignment.Right;avatar.Margin=new Thickness(0,0,38,0);avatar.Click+=delegate {ShowAccount(avatar);};bottom.Children.Add(avatar);
   avatar.IsVisible=!Program.Private;
   theme.HorizontalAlignment=HorizontalAlignment.Right;theme.Click+=delegate {SetDark(!state.Dark);};bottom.Children.Add(theme);
   full.Children.Add(bottom);
   BuildRail();

   // The page frame: toolbar, loading line, notices and find, then the page (and the Claude panel beside it).
   var frame=Res(Res(new Border {CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),ClipToBounds=true},Border.BackgroundProperty,"Canvas"),Border.BorderBrushProperty,"FrameEdge");
   Grid.SetColumn(frame,1);
   var inside=frameInside=new Grid {RowDefinitions=new RowDefinitions("44,2,Auto,Auto,*")};frame.Child=inside;
   var bar=Res(new Border {BorderThickness=new Thickness(0,0,0,1),CornerRadius=new CornerRadius(11,11,0,0)},Border.BorderBrushProperty,"Line");
   var toolbar=new Grid {Background=Brushes.Transparent,Margin=new Thickness(9,0,6,0),ColumnDefinitions=new ColumnDefinitions("Auto,*,Auto")};bar.Child=toolbar;DragArea(toolbar);
   var nav=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
   foreach(var b in new[]{back,forward,reload})nav.Children.Add(b);
   back.Click+=delegate {GoBack();};forward.Click+=delegate {GoForward();};reload.Click+=delegate {Reload();};
   toolbar.Children.Add(nav);
   var addressRow=new StackPanel {Orientation=Orientation.Horizontal};
   addressRow.Children.Add(siteGlyph);Res(address,TextBlock.ForegroundProperty,"Muted");addressRow.Children.Add(address);
   Res(zoomBadge,Border.BackgroundProperty,"Hover");zoomBadge.Child=zoomText;addressRow.Children.Add(zoomBadge);
   addressButton.Content=addressRow;addressButton.Classes.Add("flat");ToolTip.SetTip(addressButton,"Edit address ("+Mod+"L)");
   addressButton.Click+=delegate {OpenSpotlight(true);};
   Grid.SetColumn(addressButton,1);toolbar.Children.Add(addressButton);
   var right=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};
   Grid.SetColumn(right,2);toolbar.Children.Add(right);
   bookmark.Click+=delegate {Bookmark();};right.Children.Add(bookmark);
   addressRow.Children.Insert(1,privateBadge);
   if(windows) {
    var line=Res(new Border {Width=1,Height=16,Margin=new Thickness(5,0,7,0)},Border.BackgroundProperty,"Line");right.Children.Add(line);
    var min=CaptionButton(Glyphs.Minimize,"Minimize");min.Click+=delegate {WindowState=WindowState.Minimized;};
    var max=CaptionButton(Glyphs.Maximize,"Maximize");max.Click+=delegate {ToggleMaximized();};
    var close=CaptionButton(Glyphs.Close,"Close window");close.Click+=delegate {Close();};
    right.Children.Add(min);right.Children.Add(max);right.Children.Add(close);
    captionParts.AddRange(new Control[]{line,min,max,close});
   }
   toolbarScale.Child=bar;inside.Children.Add(toolbarScale);
   loading.Classes.Add("loading");Grid.SetRow(loading,1);inside.Children.Add(loading);
   Res(notice,Border.BackgroundProperty,"Hover");
   noticeGames.Classes.Add("flat");Res(noticeGames,BackgroundProperty,"Panel");
   noticeGames.Click+=delegate {notice.IsVisible=false;Navigate("mica://games");};
   var noticeRow=new DockPanel();DockPanel.SetDock(noticeGames,Dock.Right);noticeRow.Children.Add(noticeGames);noticeRow.Children.Add(noticeText);notice.Child=noticeRow;
   Grid.SetRow(notice,2);inside.Children.Add(notice);
   BuildFindBar();Grid.SetRow(findBar,3);inside.Children.Add(findBar);
   var body=new Grid {ColumnDefinitions=new ColumnDefinitions("*,Auto")};Grid.SetRow(body,4);inside.Children.Add(body);
   // Inset by the frame's edge so the page doesn't cover it.
   pages.Margin=new Thickness(0,0,0,1);body.Children.Add(pages);
   BuildErrorPanel();body.Children.Add(errorPanel);

   layout.ColumnDefinitions=new ColumnDefinitions("238,*");
   sidebarScale.Child=sidebar;layout.Children.Add(sidebarScale);layout.Children.Add(frame);WatchCorners(frame);
   BuildAiPanel(right,layout);
   BuildExtras(right);
   root.Children.Add(layout);
   BuildSpotlight();root.Children.Add(spotlightOverlay);
   BuildIsland();
   Content=root;
   AddHandler(KeyDownEvent,OnKey,Avalonia.Interactivity.RoutingStrategies.Tunnel);
   Closing+=delegate {Save();SaveDownloads();};
   LoadDownloads();
   UpdateHomeSites();UpdateLibrary();
   ApplyTheme();ApplyTextScale();RenderTabs();Show(state.ActiveId);
   StartResting();
   if(selfTestResult!=null)Opened+=async delegate {await RunSelfTest(selfTestResult);};
   else if(Program.PreviewFolder!=null)Opened+=async delegate {if(Program.Shots)await RunShots(Program.PreviewFolder);else await RunPreview(Program.PreviewFolder);};
  }

  // The slim rail the sidebar folds into (Ctrl+Shift+B), as on Windows: search, space, a column of tab icons,
  // then account, theme and the menu.
  void BuildRail() {
   rail.RowDefinitions=new RowDefinitions("Auto,*,Auto");
   var head=new StackPanel();
   var mark=new TextBlock {Text="M.",FontSize=34,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,2),IsHitTestVisible=false};Res(mark,TextBlock.FontFamilyProperty,"BrandFont");head.Children.Add(mark);
   var search=IconButton(Glyphs.Search,"Spotlight ("+Mod+"K)",15);search.HorizontalAlignment=HorizontalAlignment.Center;search.Margin=new Thickness(0,6,0,10);Res(search,BackgroundProperty,"SideCard");search.Click+=delegate {OpenSpotlight(false);};head.Children.Add(search);
   var space=IconButton(Glyphs.Spaces,"Choose a space",15);space.HorizontalAlignment=HorizontalAlignment.Center;space.Margin=new Thickness(0,0,0,14);space.Click+=delegate {ShowSpaces(space);};head.Children.Add(space);
   head.Children.Add(Res(new Border {Height=1,Margin=new Thickness(7,0,7,10)},Border.BackgroundProperty,"Line"));
   rail.Children.Add(head);
   var column=new StackPanel();column.Children.Add(miniTabs);
   var add=IconButton(Glyphs.Plus,"New tab ("+Mod+"T)",15);add.HorizontalAlignment=HorizontalAlignment.Center;add.Margin=new Thickness(0,6,0,0);add.Click+=delegate {NewTab();};column.Children.Add(add);
   var scroll=new ScrollViewer {Content=column,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);rail.Children.Add(scroll);
   var foot=new StackPanel {Margin=new Thickness(0,8),Spacing=8,HorizontalAlignment=HorizontalAlignment.Center};Grid.SetRow(foot,2);
   var avatar=railAvatar=IconButton(Glyphs.Person,"Account and profiles",15);avatar.Click+=delegate {ShowAccount(avatar);};foot.Children.Add(avatar);
   avatar.IsVisible=!Program.Private;
   miniTheme.Click+=delegate {SetDark(!state.Dark);};foot.Children.Add(miniTheme);
   var menu=IconButton(Glyphs.Gear,"Browser menu",15);menu.Click+=delegate {ShowMenu(menu);};foot.Children.Add(menu);
   rail.Children.Add(foot);
  }

  // Empty parts of the sidebar head and the toolbar move the window, and maximize it on a double click.
  void DragArea(Panel area) {
   area.PointerPressed+=delegate(object s,PointerPressedEventArgs e) {
    if(e.Source!=area||!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
    if(e.ClickCount==2)ToggleMaximized();else BeginMoveDrag(e);
   };
  }
  void ToggleMaximized() {WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;}
  void ToggleSidebar() {SetSidebar(sidebarHidden);}
  void SetSidebar(bool shown) {
   sidebarHidden=!shown;
   layout.ColumnDefinitions[0].Width=new GridLength((shown?238:64)*TextScale);
   sidebar.Margin=shown?new Thickness(9,Mac?22:0,15,0):new Thickness(0,Mac?22:0,0,0);
   sidebarFull.IsVisible=shown;rail.IsVisible=!shown;
   ((Control)((Grid)toggleSidebar.Parent).Children[0]).IsVisible=shown;
   toggleSidebar.HorizontalAlignment=shown?HorizontalAlignment.Right:HorizontalAlignment.Center;
   ToolTip.SetTip(toggleSidebar,(shown?"Hide":"Show")+" sidebar ("+ModShift+"B)");
  }

  CrossTab Active {get {CrossTab t;return state.ActiveId!=null&&tabs.TryGetValue(state.ActiveId,out t)?t:null;}}
  MicaView ActiveView() {var t=Active;return t==null?null:t.View;}

  // The engine address for a Mica address: internal pages on their hosts, the new tab page and library included.
  static string EngineFor(string url) {return url=="mica://home"?BuiltInPages.Home:Navigation.IsLibrary(url)?BuiltInPages.LibraryAddress(url):Navigation.EngineAddress(url);}
  static string FromEngine(string address) {
   Uri uri;
   if(string.IsNullOrEmpty(address)||address=="about:blank"||Uri.TryCreate(address,UriKind.Absolute,out uri)&&uri.Host==BuiltInPages.HomeHost)return "mica://home";
   string library=BuiltInPages.LibraryPage(address);if(library!=null)return library;
   return Navigation.FromEngine(address);
  }

  void FocusAddress() {OpenSpotlight(true);}

  void NewTab() {var tab=state.AddTab("mica://home");RenderTabs();Show(tab.Id);Save();}
  void CloseTab(string id) {
   CrossTab tab;
   if(tabs.TryGetValue(id,out tab)) {
    tabs.Remove(id);
    if(tab.View!=null){pages.Children.Remove(tab.View);tab.View.Dispose();}
   }
   var closed=state.Tabs.FirstOrDefault(t=>t.Id==id);state.RecordClosed(closed);state.CloseTab(id);
   RenderTabs();Show(state.ActiveId);Save();
  }
  void Reload() {var t=Active;if(t==null||t.View==null)return;if(t.Error!=null){HideError(t);t.View.Address=EngineFor(t.Entry.Url);return;}t.View.Reload(false);}
  void GoBack() {var v=ActiveView();if(v!=null&&v.CanGoBack){HideError(Active);v.GoBack();}}
  void GoForward() {var v=ActiveView();if(v!=null&&v.CanGoForward){HideError(Active);v.GoForward();}}
  void TogglePinned(string id) {var entry=state.Tabs.FirstOrDefault(t=>t.Id==id);if(entry==null)return;entry.Pinned=!entry.Pinned;RenderTabs();Save();}

  // Tabs of the current space, in session order: pinned ones first, as on Windows.
  void RenderTabs() {
   tabList.Children.Clear();pinnedList.Children.Clear();miniTabs.Children.Clear();int open=0;
   foreach(var entry in state.Tabs.Where(t=>t.Space==state.Space).OrderByDescending(t=>t.Pinned)) {
    CrossTab tab;if(!tabs.TryGetValue(entry.Id,out tab)){tab=MakeTab(entry);tabs[entry.Id]=tab;}
    RefreshTab(tab);
    if(entry.Pinned)pinnedList.Children.Add(tab.Header);else {tabList.Children.Add(tab.Header);open++;}
    miniTabs.Children.Add(tab.Mini);
   }
   pinnedHint.IsVisible=pinnedList.Children.Count==0;
   tabCount.Text=open.ToString("00");spaceName.Text=state.Space;
   PaintTabs();
  }
  CrossTab MakeTab(PageEntry entry) {
   var tab=new CrossTab {Entry=entry};
   tab.Icon=new ContentControl {Width=16,Height=16,VerticalAlignment=VerticalAlignment.Center};
   tab.Label=new TextBlock {TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,6,0)};
   var close=CaptionButton(Glyphs.Close,"Close tab ("+Mod+"W)");close.Classes.Add("close");close.Width=24;close.Height=24;
   close.Click+=delegate(object s,Avalonia.Interactivity.RoutedEventArgs e) {e.Handled=true;CloseTab(entry.Id);};
   var row=new Grid {ColumnDefinitions=new ColumnDefinitions("Auto,*,Auto")};row.Children.Add(tab.Icon);Grid.SetColumn(tab.Label,1);row.Children.Add(tab.Label);Grid.SetColumn(close,2);row.Children.Add(close);
   tab.Header=new Border {Child=row};tab.Header.Classes.Add("tab");
   tab.Header.PointerPressed+=delegate(object s,PointerPressedEventArgs e) {
    var point=e.GetCurrentPoint(tab.Header).Properties;
    if(point.IsMiddleButtonPressed)CloseTab(entry.Id);else if(point.IsLeftButtonPressed)Show(entry.Id);
   };
   tab.Header.ContextMenu=TabMenu(tab);
   tab.Mini=new Button {Width=40,Height=36,Padding=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Center};tab.Mini.Classes.Add("flat");
   tab.Mini.Click+=delegate {Show(entry.Id);};tab.Mini.ContextMenu=TabMenu(tab);
   return tab;
  }
  ContextMenu TabMenu(CrossTab tab) {
   var entry=tab.Entry;
   var pin=new MenuItem();var mute=new MenuItem();var dup=new MenuItem {Header="Duplicate"};var copy=new MenuItem {Header="Copy address"};
   var move=new MenuItem {Header="Move to space"};var shut=new MenuItem {Header="Close tab"};var others=new MenuItem {Header="Close other tabs"};
   pin.Click+=delegate {TogglePinned(entry.Id);};mute.Click+=delegate {ToggleMute(tab);};dup.Click+=delegate {DuplicateTab(tab);};copy.Click+=delegate {CopyAddress(tab);};
   shut.Click+=delegate {CloseTab(entry.Id);};others.Click+=delegate {CloseOtherTabs(entry.Id);};
   var menu=new ContextMenu();foreach(var item in new[]{pin,mute,dup,copy,move})menu.Items.Add(item);menu.Items.Add(new Separator());menu.Items.Add(shut);menu.Items.Add(others);
   menu.Opening+=delegate {
    pin.Header=entry.Pinned?"Unpin":"Pin";mute.Header=tab.Muted?"Unmute":"Mute";mute.IsEnabled=tab.View!=null;
    bool web=Navigation.IsWeb(entry.Url);dup.IsEnabled=web;copy.IsEnabled=web;
    move.Items.Clear();
    foreach(var name in state.Spaces.Where(s=>s!=entry.Space)){string target=name;var item=new MenuItem {Header=target};item.Click+=delegate {MoveTabToSpace(entry.Id,target);};move.Items.Add(item);}
    move.IsEnabled=move.Items.Count>0;
   };
   return menu;
  }
  void CloseOtherTabs(string keep) {foreach(var t in state.Tabs.Where(t=>t.Space==state.Space&&t.Id!=keep&&!t.Pinned).Select(t=>t.Id).ToList())CloseTab(t);Show(keep);}
  void MoveTabToSpace(string id,string space) {
   var entry=state.Tabs.FirstOrDefault(t=>t.Id==id);if(entry==null)return;
   entry.Space=space;
   if(state.ActiveId==id){var next=state.Tabs.Where(t=>t.Space==state.Space).OrderByDescending(t=>t.LastUsed).FirstOrDefault()??state.AddTab("mica://home");RenderTabs();Show(next.Id);}
   else RenderTabs();
   Status("Moved to "+space+".");Save();
  }
  void RefreshTab(CrossTab tab) {
   string title=TabTitle(tab.Entry);
   tab.Label.Text=title;ToolTip.SetTip(tab.Header,title+"\n"+(tab.Entry.Url=="mica://home"?"New tab":Navigation.AddressLabel(tab.Entry.Url)));ToolTip.SetTip(tab.Mini,title);
   tab.Header.Opacity=tab.View==null&&tab.Entry.Id!=state.ActiveId?0.62:1;tab.Mini.Opacity=tab.Header.Opacity;
   tab.Icon.Content=TabIcon(tab,16);tab.Mini.Content=TabIcon(tab,18);
  }
  // The site's own icon once the page named one; a muted speaker for a muted tab; else a glyph for the kind of page.
  Control TabIcon(CrossTab tab,double size) {
   if(tab.Muted)return Glyph(Glyphs.Muted,size-3,"muted");
   string url=tab.Entry.Url;
   if(tab.Favicon!=null&&Navigation.IsWeb(url))return new Image {Source=tab.Favicon,Width=size,Height=size};
   string kind=url=="mica://home"?Glyphs.Home:url=="mica://history"?Glyphs.History:url=="mica://downloads"?Glyphs.Download:Navigation.IsInternal(url)?Glyphs.Game:Glyphs.Globe;
   return Glyph(kind,size-3,kind==Glyphs.Globe?"muted":null);
  }
  static string TabTitle(PageEntry e) {return string.IsNullOrWhiteSpace(e.Title)?Navigation.AddressLabel(e.Url):e.Title;}
  void PaintTabs() {foreach(var tab in tabs.Values){bool on=tab.Entry.Id==state.ActiveId;tab.Header.Classes.Set("active",on);tab.Mini.Classes.Set("on",on);}}

  // Shows a tab, creating its engine view the first time: tabs restored from the session (or put to rest) wait
  // until picked.
  void Show(string id) {
   CrossTab tab;if(id==null||!tabs.TryGetValue(id,out tab))return;
   string leaving=state.ActiveId;
   state.ActivateTab(id,DateTime.UtcNow);
   if(tab.Entry.Space!=state.Space){state.Space=tab.Entry.Space;RenderTabs();}
   if(leaving!=id)CloseFind();
   if(tab.View==null)Wake(tab);
   foreach(var t in tabs.Values)if(t.View!=null)t.View.IsVisible=t==tab&&t.Error==null&&!SpotlightOpen;
   ShowError(tab);
   PaintTabs();ShowAddress();UpdateButtons();RefreshTab(tab);
   UpdateIslandMedia();PlaceIsland();QueueCorners();
   BindClaudePanel(false);
   Title=(tab.Entry.Url=="mica://home"?"A fresh start":TabTitle(tab.Entry))+" — Mica"+(Program.Private?" · Private":"");
  }
  void Wake(CrossTab tab) {
   tab.View=new MicaView();
   tab.View.Address=EngineFor(tab.Entry.Url);
   Wire(tab);
   pages.Children.Add(tab.View);
  }
  void ShowAddress() {
   var t=Active;if(t==null)return;bool home=t.Entry.Url=="mica://home";
   address.Text=home?"A fresh start":Navigation.AddressLabel(t.Entry.Url);
   Uri uri;bool secure=Uri.TryCreate(t.Entry.Url,UriKind.Absolute,out uri)&&uri.Scheme=="https";
   string kind=home||Navigation.IsInternal(t.Entry.Url)?Glyphs.Search:secure?Glyphs.Lock:Glyphs.Info;
   var current=siteGlyph.Content as Viewbox;if(current==null||(string)current.Tag!=kind)siteGlyph.Content=Glyph(kind,11,"muted");
   ToolTip.SetTip(siteGlyph,home||Navigation.IsInternal(t.Entry.Url)?null:secure?"Connection is secure":"Not secure");
   bool marked=Navigation.IsWeb(t.Entry.Url)&&state.Bookmarks.Any(b=>b.Url==t.Entry.Url);
   bookmark.IsEnabled=Navigation.IsWeb(t.Entry.Url);
   var star=Glyph(Glyphs.Star,15,marked?"accent":null);if(marked)Res((GlyphPath)star.Child,GlyphPath.FillProperty,"Accent");bookmark.Content=star;
   ToolTip.SetTip(bookmark,(marked?"Remove bookmark":"Bookmark this page")+" ("+Mod+"D)");
   UpdateZoomBadge();
  }
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
   view.KeyboardHandler=new PageKeys(this);
   view.DisplayHandler=new PageDisplay(this,tab);
   view.DownloadHandler=downloadHandler;
   // Chromium's own prompts, dialogs and bubbles never show (ChromiumUi.cs).
   view.PermissionHandler=new SitePermissions(this,()=>tabs.ContainsKey(tab.Entry.Id)&&tab.View==view);view.JSDialogHandler=new PageDialogs(this,tab);view.CommandHandler=ChromiumUi.CommandHandler;
   view.LoadError+=delegate(object s,Xilium.CefGlue.Common.Events.LoadErrorEventArgs e) {Trace("error "+e.ErrorCode+" "+e.ErrorText+" "+e.FailedUrl);};
   view.AddressChanged+=delegate(object s,string a) {Trace("address "+a);};
   view.LoadEnd+=delegate(object s,Xilium.CefGlue.Common.Events.LoadEndEventArgs e) {Trace("end "+e.HttpStatusCode+" main="+e.Frame.IsMain);};
   view.LoadingStateChange+=delegate(object s,Xilium.CefGlue.Common.Events.LoadingStateChangeEventArgs e) {Trace("loading="+e.IsLoading);};
   view.AddressChanged+=delegate(object s,string a) {Dispatcher.UIThread.Post(delegate {
    string before=tab.Entry.Url;
    tab.Entry.Url=FromEngine(a);
    if(SiteOf(before)!=SiteOf(tab.Entry.Url)){tab.Favicon=null;tab.FaviconUrl=null;ApplySiteZoom(tab);}
    RefreshTab(tab);
    if(tab==Active)ShowAddress();
   });};
   view.TitleChanged+=delegate(object s,string title) {Dispatcher.UIThread.Post(delegate {
    string internalTitle=Navigation.InternalTitle(tab.Entry.Url);
    tab.Entry.Title=internalTitle??(string.IsNullOrWhiteSpace(title)||title=="about:blank"?null:title);
    RefreshTab(tab);if(tab==Active)Title=(tab.Entry.Url=="mica://home"?"A fresh start":TabTitle(tab.Entry))+" — Mica";
   });};
   // Mica's scrollbar on every page, as on Windows, from the start so the default one never shows.
   view.LoadStart+=delegate(object s,Xilium.CefGlue.Common.Events.LoadStartEventArgs e) {if(Navigation.IsWeb(FromEngine(e.Frame.Url)))e.Frame.ExecuteJavaScript(ScrollbarScript,"",1);};
   view.LoadStart+=delegate(object s,Xilium.CefGlue.Common.Events.LoadStartEventArgs e) {if(e.Frame.IsMain)Dispatcher.UIThread.Post(delegate {if(tab==Active)notice.IsVisible=false;});};
   view.LoadEnd+=delegate(object s,Xilium.CefGlue.Common.Events.LoadEndEventArgs e) {
    if(!e.Frame.IsMain)return;int status=e.HttpStatusCode;
    if(Navigation.IsWeb(FromEngine(e.Frame.Url))){e.Frame.ExecuteJavaScript(MediaScript,"",1);e.Frame.ExecuteJavaScript(WheelZoomScript,"",1);if(AiEnabled)e.Frame.ExecuteJavaScript(AskAiScript,"",1);}
    Dispatcher.UIThread.Post(delegate {tab.LastStatus=status;if(!Program.Private)state.Visit(tab.Entry.Url,tab.Entry.Title);if(tab==Active){readerButton.Classes.Set("on",false);BindClaudePanel(false);}UpdateHomeSites();UpdateLibrary();Save();});
   };
   view.LoadError+=delegate(object s,Xilium.CefGlue.Common.Events.LoadErrorEventArgs e) {
    if(!e.Frame.IsMain||e.ErrorCode==Xilium.CefGlue.CefErrorCode.Aborted)return;
    var code=e.ErrorCode;string text=e.ErrorText,failed=e.FailedUrl;
    Dispatcher.UIThread.Post(delegate {LoadFailed(tab,code,text,failed);});
   };
   view.LoadingStateChange+=delegate(object s,Xilium.CefGlue.Common.Events.LoadingStateChangeEventArgs e) {
    bool busy=e.IsLoading;
    Dispatcher.UIThread.Post(delegate {
     tab.Loading=busy;
     if(tab==Active)UpdateButtons();
     if(!busy&&tab.Waiter!=null){var w=tab.Waiter;tab.Waiter=null;w.TrySetResult(true);}
    });
   };
   view.BrowserInitialized+=delegate {Dispatcher.UIThread.Post(delegate {ApplySiteZoom(tab);if(tab.Muted){var host=view.Host;if(host!=null)host.SetAudioMuted(true);}});};
  }
  // The new tab page's most visited sites and the places you closed lately (Core's TopSites, as on Windows).
  void UpdateHomeSites() {
   var sites=state.TopSites(8).Select(e=>new Dictionary<string,object> {{"t",e.Title??""},{"u",e.Url}}).ToList();
   var recent=state.RecentlyClosed.Where(e=>Navigation.IsWeb(e.Url)).Take(3).Select(e=>new Dictionary<string,object> {{"t",e.Title??""},{"u",e.Url}}).ToList();
   var json=new JavaScriptSerializer();
   BuiltInPages.HomeSites=json.Serialize(sites);BuiltInPages.HomeRecent=json.Serialize(recent);
  }
  void Notice(string text) {noticeText.Text=text;noticeGames.IsVisible=false;notice.IsVisible=true;}
  // A short word about what just happened: the island says it, as on Windows.
  void Status(string text) {ShowIslandMessage(text);}

  // What you typed, through the same rules as the Windows address bar (Navigation.Resolve), site keywords included.
  void Navigate(string input) {Navigate(input,false);}
  void Navigate(string input,bool newTab) {
   string url=Navigation.ExpandKeyword(input,state.SiteKeywords);
   if(url==null)try {url=Navigation.Resolve(input);}
   catch(ArgumentException e) {Notice(e.Message);return;}
   if(Navigation.IsReading(url)){Notice("Saved articles aren't in this version yet.");return;}
   if(newTab||Active==null){var created=state.AddTab(url);RenderTabs();Show(created.Id);}
   var tab=Active;if(tab==null)return;
   notice.IsVisible=false;HideError(tab);
   tab.Entry.Url=url;tab.Entry.Title=Navigation.InternalTitle(url);
   if(tab.View==null)Wake(tab);else tab.View.Address=EngineFor(url);
   RefreshTab(tab);ShowAddress();tab.View.Focus();Save();
  }

  void SetDark(bool dark) {
   state.Dark=dark;ApplyTheme();
   foreach(var tab in tabs.Values)if(tab.View!=null&&BuiltInPages.IsBuiltIn(tab.View.Address))tab.View.ExecuteJavaScript(BuiltInPages.ThemeScript(dark),"",1);
   Save();
  }
  void ApplyTheme() {
   var app=Application.Current as App;if(app!=null)app.ApplyTheme(state.Dark);
   theme.Content=Glyph(state.Dark?Glyphs.Sun:Glyphs.Moon,15,null);miniTheme.Content=Glyph(state.Dark?Glyphs.Sun:Glyphs.Moon,15,null);
   PaintTabs();
  }
  void Save() {if(Program.Private)return;try {StateStore.Save(statePath,state);} catch(IOException) {} catch(UnauthorizedAccessException) {}}
 }
}
