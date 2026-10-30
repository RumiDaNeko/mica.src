using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 partial class BrowserApp {
  readonly Window window;
  readonly string profile,statePath;
  readonly bool smoke;
  readonly bool privateMode;
  readonly BrowserState state;
  readonly Dictionary<string,WebView2> views=new Dictionary<string,WebView2>();
  readonly Dictionary<string,NavigationProgress> navigations=new Dictionary<string,NavigationProgress>();
  readonly DispatcherTimer timer=new DispatcherTimer();
  Task<CoreWebView2Environment> environmentTask;
  CoreWebView2Environment environment;
  bool editingAddress,closing,sidebarHidden,tabSearch;
  IInputElement previousFocus;
  T Find<T>(string name) where T:FrameworkElement { return (T)window.FindName(name); }
  PageEntry Active { get {return state.Tabs.FirstOrDefault(t=>t.Id==state.ActiveId);} }
  bool IsSpotlight { get {return Find<Grid>("SpotlightOverlay").Visibility==Visibility.Visible;} }
  Brush Brush(string key) { return (Brush)window.Resources[key]; }
  public BrowserApp(Window w,string dataPath,bool testing,bool isPrivate=false,BrowserState initialState=null,bool runTestSuite=true) {
   window=w;profile=dataPath;smoke=testing;privateMode=isPrivate;statePath=Path.Combine(profile,"session.json");state=initialState??(privateMode?new BrowserState():StateStore.Load(statePath));if(state.Tabs.Count==0)state.AddTab("mica://home");Program.Browsers.Add(this);
   WindowChrome.SetWindowChrome(window,new WindowChrome { CaptionHeight=51,ResizeBorderThickness=new Thickness(6),CornerRadius=new CornerRadius(12),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false });
   using(var icon=Assembly.GetExecutingAssembly().GetManifestResourceStream("Mica.Brand.ico"))window.Icon=BitmapFrame.Create(icon,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
   Bind(); Find<Border>("PrivateBadge").Visibility=privateMode?Visibility.Visible:Visibility.Collapsed; InitializeContextMenus(); InitializeMenus(); InitializeDownloads(); InitializeLibrary(); InitializeMotion(); WatchSidebarScroll(); WatchHomeMenu(); InitializeDevToolsDock();InitializeAiPanel(); ScheduleEngineUpdate(); InitializeDesktopIntegration(); InitializeAvatar(); ApplyTheme(); RenderTabs();
   Find<TextBlock>("DateLabel").Text=Localizer.LongDate(DateTime.Now);
   Find<TextBlock>("Greeting").Text=privateMode?"Private browsing · history is not saved":DateTime.Now.Hour<12?"Good morning.":DateTime.Now.Hour<18?"Good afternoon.":"Good evening.";
   timer.Interval=TimeSpan.FromSeconds(15);timer.Tick+=delegate { SleepIdleTabs(false); BalanceMemory(false); var budget=KeepUnderBudget(); UpdateMemory(); };timer.Start();
   window.Loaded+=async delegate { BuildIsland();AcceptFileDrops();ApplyTextScale();if(!privateMode)OpenAutofillVault(); await Activate(state.ActiveId); UpdateMemory(); TellAboutLastCrash(); var blocker=EnsureBuiltInBlocker(); RefreshUpdatesHint(); window.Dispatcher.BeginInvoke(new Action(delegate {if(menuKey==null&&!closing)BuildMenu();}),DispatcherPriority.ApplicationIdle); PullSettingsAtLaunch(); StartSentTabs(); if(!runTestSuite)return;if(Environment.GetCommandLineArgs().Contains("--update-close-test"))await RunUpdateCloseTest();else if(Environment.GetCommandLineArgs().Contains("--store-bridge-test"))await RunStoreBridgeTest();else if(Environment.GetCommandLineArgs().Contains("--tools-test"))await RunToolsTest();else if(Environment.GetCommandLineArgs().Contains("--runtime-test"))await RunRuntimeTest();else if(Environment.GetCommandLineArgs().Contains("--brand-test"))await RunBrandTest();else if(Environment.GetCommandLineArgs().Contains("--pinned-test"))await RunPinnedTest();else if(Environment.GetCommandLineArgs().Contains("--dialogs-test"))await RunDialogTest();else if(Environment.GetCommandLineArgs().Contains("--store-test"))await RunStoreTest();else if(Environment.GetCommandLineArgs().Contains("--private-exit-test"))await RunPrivateExitTest();else if(Environment.GetCommandLineArgs().Contains("--vault-test"))await RunVaultUiTest();else if(Environment.GetCommandLineArgs().Contains("--privacy-test"))await RunPrivacyTest();else if(Environment.GetCommandLineArgs().Contains("--library-test"))await RunLibraryTest();else if(Environment.GetCommandLineArgs().Contains("--downloads-test"))await RunDownloadsTest();else if(Environment.GetCommandLineArgs().Contains("--extensions-test"))await RunExtensionTest();else if(Environment.GetCommandLineArgs().Contains("--regression-test"))await RunRegressionTest();else if(Environment.GetCommandLineArgs().Contains("--sites-test"))await RunSitesTest();else if(Environment.GetCommandLineArgs().Contains("--layout-test"))await RunLayoutTest();else if(Environment.GetCommandLineArgs().Contains("--avatar-test"))await RunAvatarTest();else if(Environment.GetCommandLineArgs().Contains("--popups-test"))await RunPopupsTest();else if(Environment.GetCommandLineArgs().Contains("--resize-test"))await RunResizeTest();else if(Environment.GetCommandLineArgs().Contains("--home-test"))await RunHomeTest();else if(Environment.GetCommandLineArgs().Contains("--motw-test"))await RunMotwTest();else if(Environment.GetCommandLineArgs().Contains("--extwin-test"))await RunExtensionWindowTest();else if(Environment.GetCommandLineArgs().Contains("--keys-test"))await RunKeysTest();else if(Environment.GetCommandLineArgs().Contains("--updates-test"))await RunUpdatesTest();else if(Environment.GetCommandLineArgs().Contains("--personalize-test"))await RunPersonalizeTest();else if(Environment.GetCommandLineArgs().Contains("--tabs-test"))await RunTabsTest();else if(Environment.GetCommandLineArgs().Contains("--bench-test"))await RunMemoryBench();else if(Environment.GetCommandLineArgs().Contains("--blocker-test"))await RunBlockerTest();else if(Environment.GetCommandLineArgs().Contains("--toolbar-test"))await RunExtensionToolbarTest();else if(Environment.GetCommandLineArgs().Contains("--islanddl-test"))await RunIslandDownloadsTest();else if(Environment.GetCommandLineArgs().Contains("--agent-test"))await RunUserAgentTest();else if(Environment.GetCommandLineArgs().Contains("--devswitch-test"))await RunDevToolsSwitchTest();else if(Environment.GetCommandLineArgs().Contains("--corners-test"))await RunCornersTest();else if(Environment.GetCommandLineArgs().Contains("--features-test"))await RunFeaturesTest();else if(Environment.GetCommandLineArgs().Contains("--more-test"))await RunMoreFeaturesTest();else if(Environment.GetCommandLineArgs().Contains("--perf-test"))await RunPerfProbe();else if(Environment.GetCommandLineArgs().Contains("--motion-test"))await RunMotionTest();else if(Environment.GetCommandLineArgs().Contains("--sandbox-test"))await RunSandboxTest();else if(Environment.GetCommandLineArgs().Contains("--devtools-test"))await RunDevToolsTest();else if(Environment.GetCommandLineArgs().Contains("--protection-test"))await RunProtectionTest();else if(Environment.GetCommandLineArgs().Contains("--notice-test"))await RunDownloadNoticeTest();else if(Environment.GetCommandLineArgs().Contains("--alert-test"))await RunAlertTest();else if(Environment.GetCommandLineArgs().Contains("--navprobe-test"))await RunNavProbeTest();else if(Environment.GetCommandLineArgs().Contains("--fullscreen-test"))await RunFullscreenTest();else if(Environment.GetCommandLineArgs().Contains("--tetris-test"))await RunTetrisTest();else if(Environment.GetCommandLineArgs().Contains("--autofill-test"))await RunAutofillTest();else if(Environment.GetCommandLineArgs().Contains("--totp-test"))await RunTotpTest();else if(Environment.GetCommandLineArgs().Contains("--navhistory-test"))await RunNavHistoryTest();else if(Environment.GetCommandLineArgs().Contains("--claudeext-test"))await RunClaudeExtProbe();else if(Environment.GetCommandLineArgs().Contains("--chatgptext-test"))await RunChatGptExtProbe();else if(Environment.GetCommandLineArgs().Contains("--aipanel-test"))await RunAiPanelTest();else if(Environment.GetCommandLineArgs().Contains("--import-test"))await RunImportTest();else if(Environment.GetCommandLineArgs().Contains("--memory-test"))await RunMemoryTest();else if(Environment.GetCommandLineArgs().Contains("--gallery-test"))await RunGalleryTest();else if(smoke)await SmokeTest(); };
   window.Closing+=delegate { if(FarewellWanted())PlayFarewell();closing=true;CloseTools();CloseVault();ClosePopups();DisposeDevTools();CloseContextMenu();timer.Stop();ShutdownDownloads();if(!Program.PreparingForUpdate){Save();PushSettingsOnClose();SyncVaultOnClose();}foreach(var view in views.Values.ToList())view.Dispose();views.Clear();Program.Browsers.Remove(this);if(privateMode)Program.SchedulePrivateCleanup(profile); };
  }
  void Click(string name,Action action) { Find<Button>(name).Click+=delegate { action(); }; }
  void Bind() {
   Click("SearchButton",delegate { OpenSpotlight(false); }); Click("HomeSearch",delegate { OpenSpotlight(false); });
   Click("AddressButton",delegate { OpenSpotlight(true); }); Click("DismissSpotlight",CloseSpotlight);
   Click("NewTabButton",NewTab); Click("BookmarkButton",Bookmark); Click("ExtensionsButton",ShowToolbarExtensions); Click("ReaderButton",ToggleReader); Click("ThemeButton",ToggleTheme); Click("MiniTheme",ToggleTheme);
   Click("SpaceButton",ShowSpaces); Click("MiniSpace",ShowSpaces); Click("MiniSearch",delegate {OpenSpotlight(false);}); Click("MiniNewTab",NewTab); Click("MiniMenu",ShowMenu);
   Click("ToggleSidebar",ToggleSidebar); Click("MinButton",delegate {window.WindowState=WindowState.Minimized;}); Click("MaxButton",Maximize); Click("CloseButton",window.Close);
   Click("InsecureButton",ContinueInsecure); Click("CertButton",ContinueDespiteCertificate);Click("GamesButton",delegate {Navigate("mica://games",true);}); Click("BackButton",delegate {var v=CurrentView();if(v!=null&&v.CanGoBack)v.GoBack();}); Click("ForwardButton",delegate {var v=CurrentView();if(v!=null&&v.CanGoForward)v.GoForward();}); WireNavHistory(); Click("ReloadButton",Reload); Click("RetryButton",Reload);
   Click("MenuButton",ShowMenu);Click("ShortcutsButton",ShowShortcuts);Click("PersonalizeButton",ShowPersonalize);
   Find<TextBox>("SpotlightInput").TextChanged+=delegate {RefreshSearch();};
   Find<Grid>("SpotlightOverlay").MouseDown+=delegate(object sender,MouseButtonEventArgs e) { if(e.OriginalSource==sender)CloseSpotlight(); };
   Find<ListBox>("SearchResults").PreviewMouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e) { var element=e.OriginalSource as DependencyObject; if(element!=null&&ItemsControl.ContainerFromElement(Find<ListBox>("SearchResults"),element) is ListBoxItem)ExecuteSearch(); };
   window.PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {var key=e.Key==Key.System?e.SystemKey:e.Key;var modifiers=Keyboard.Modifiers;if(IsShortcut(key,modifiers)){e.Handled=true;window.Dispatcher.BeginInvoke(new Action(delegate {HandleKey(key,modifiers);}));}else if(IsSpotlight&&HandleKey(key,modifiers))e.Handled=true;};
   window.Activated+=delegate {bool wasAway=Away;selfCompressed=false;inactiveSince=null;if(wasAway)BalanceMemory(false);};window.Deactivated+=delegate {inactiveSince=DateTime.UtcNow;};
   window.StateChanged+=async delegate {BalanceMemory(false);if(window.WindowState==WindowState.Minimized){await Task.Delay(1500);if(window.WindowState==WindowState.Minimized)await CompressIdleMemory(true);}};
   window.StateChanged+=delegate {Find<Button>("MaxButton").Content=window.WindowState==WindowState.Maximized?"\uE923":"\uE922";Find<Button>("MaxButton").ToolTip=window.WindowState==WindowState.Maximized?"Restore window":"Maximize";};
  }
  void Maximize() {window.WindowState=window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;}
  bool IsShortcut(Key key,ModifierKeys mods) {return (key==Key.F7&&mods==ModifierKeys.None)||((mods&ModifierKeys.Control)!=0&&new[]{Key.K,Key.L,Key.T,Key.W,Key.D,Key.R,Key.Tab,Key.H,Key.J,Key.N,Key.F,Key.P}.Contains(key))||((mods&(ModifierKeys.Control|ModifierKeys.Shift|ModifierKeys.Alt))==ModifierKeys.Control&&TabNumber(key)>0)||(key==Key.F9&&mods==ModifierKeys.None)||((mods&(ModifierKeys.Control|ModifierKeys.Shift))==(ModifierKeys.Control|ModifierKeys.Shift)&&(key==Key.B||key==Key.I||key==Key.Delete||key==Key.A||key==Key.S||key==Key.Space))||key==Key.F12||key==Key.F11||((mods&ModifierKeys.Shift)!=0&&key==Key.Escape)||key==Key.F5||((mods&ModifierKeys.Alt)!=0&&(key==Key.Left||key==Key.Right));}
  // Ctrl+1…9 pick tabs 1–9 and Ctrl+0 the 10th, in sidebar order (pinned first).
  static int TabNumber(Key key) {if(key>=Key.D1&&key<=Key.D9)return key-Key.D1+1;if(key>=Key.NumPad1&&key<=Key.NumPad9)return key-Key.NumPad1+1;return key==Key.D0||key==Key.NumPad0?10:0;}
  internal System.Collections.Generic.List<string> keyLog;
  bool HandleKey(Key key,ModifierKeys modifiers) {if(keyLog!=null){keyLog.Add(key.ToString());return true;}
   bool ctrl=(modifiers&ModifierKeys.Control)!=0,shift=(modifiers&ModifierKeys.Shift)!=0,alt=(modifiers&ModifierKeys.Alt)!=0;
   if(key==Key.F11){ToggleUserFullscreen();return true;}
   if(key==Key.F9&&!ctrl&&!shift&&!alt){ToggleReader();return true;}
   if(key==Key.F7&&!ctrl&&!shift&&!alt){CaretKey();return true;}
   if(ctrl&&key==Key.F){ShowFind();return true;}
   if(ctrl&&key==Key.P){ShowPrint();return true;}
   if(key==Key.F12||(ctrl&&shift&&key==Key.I)){ToggleDevTools();return true;}
   if(ctrl&&shift&&key==Key.Delete){ShowClearBrowsingData();return true;}
   if(ctrl&&shift&&key==Key.A){OpenTabSearch();return true;}
   if(ctrl&&shift&&key==Key.S){ToggleSplit();return true;}
   if(ctrl&&shift&&key==Key.Space){ToggleAiPanel();return true;}
   if(shift&&key==Key.Escape){ShowTaskManager();return true;}
   if(ctrl&&(key==Key.K||key==Key.L)) {OpenSpotlight(key==Key.L);return true;}
   if(ctrl&&(key==Key.H||key==Key.J)) {OpenLibraryPage(key==Key.H?"mica://history":"mica://downloads");return true;}
   if(ctrl&&shift&&key==Key.N){LaunchBrowserWindow(true);return true;}
   if(ctrl&&key==Key.N){LaunchBrowserWindow(false);return true;}
   if(ctrl&&shift&&key==Key.T){ReopenClosedTab();return true;}
   if(ctrl&&shift&&key==Key.X){TakeScreenshot("visible");return true;}
   if(ctrl&&key==Key.T) {NewTab();return true;}
   if(IsSpotlight) {
    var results=Find<ListBox>("SearchResults");
    if(key==Key.Escape) {CloseSpotlight();return true;}
    if(key==Key.Enter) {ExecuteSearch();return true;}
    if(key==Key.Down||key==Key.Up) {if(results.Items.Count>0) {results.SelectedIndex=(results.SelectedIndex+(key==Key.Down?1:-1)+results.Items.Count)%results.Items.Count;results.ScrollIntoView(results.SelectedItem);}return true;}
    return false;
   }
   if(ctrl&&key==Key.W) {if(Active!=null&&!Active.Pinned)CloseTab(Active.Id);return true;}
   if(ctrl&&key==Key.D) {Bookmark();return true;}
   if(ctrl&&shift&&(key==Key.B||key==Key.I)) {ToggleSidebar();return true;}
   if((ctrl&&key==Key.R)||key==Key.F5) {Reload();return true;}
   if(alt&&(key==Key.Left||key==Key.Right)) {var v=CurrentView();if(v!=null) {if(key==Key.Left&&v.CanGoBack)v.GoBack();if(key==Key.Right&&v.CanGoForward)v.GoForward();}return true;}
   if(ctrl&&!shift&&!alt&&TabNumber(key)>0) {var tabs=state.Tabs.Where(t=>t.Space==state.Space).OrderByDescending(t=>t.Pinned).ToList();int n=TabNumber(key);if(n<=tabs.Count)SelectTab(tabs[n-1].Id);return true;}
   if(ctrl&&key==Key.Tab) {var tabs=state.Tabs.Where(t=>t.Space==state.Space).ToList();if(tabs.Count>0) {int index=tabs.FindIndex(t=>t.Id==state.ActiveId);SelectTab(tabs[(index+(shift?-1:1)+tabs.Count)%tabs.Count].Id);}return true;}
   return false;
  }
  void ToggleSidebar() {CloseMenus();sidebarHidden=!sidebarHidden;AnimateSidebar();Find<Button>("ToggleSidebar").ToolTip=sidebarHidden?"Show sidebar (Ctrl+Shift+B)":"Hide sidebar (Ctrl+Shift+B)";}
  void NewTab() {var tab=state.AddTab("mica://home");SelectTab(tab.Id);OpenSpotlight(false);Save();}
  void SwitchSpace(string space) {state.Space=space;var tab=state.Tabs.LastOrDefault(t=>t.Space==space)??state.AddTab("mica://home");SelectTab(tab.Id);}
  void Navigate(string value,bool newTab) {
   if(IsExtensionTabRequest(value)){OpenExtensionTab(value);return;}
   string url=Navigation.ExpandKeyword(value,state.SiteKeywords);if(url==null)try {url=Navigation.Resolve(value);}catch(ArgumentException e) {Status(e.Message);return;}
   var tab=newTab||Active==null?state.AddTab(url):Active;
   WebView2 live;if((Navigation.IsWeb(url)||Navigation.IsLocalFile(url))&&views.TryGetValue(tab.Id,out live)) {tab.Url=url;CloseSpotlight();SelectTab(tab.Id);if(live.CoreWebView2!=null)live.CoreWebView2.Navigate(url);Save();return;}
   if(tab.Url!=url) {DisposeView(tab.Id);tab.Url=url;tab.Title=Navigation.InternalTitle(url)??(Navigation.IsLocalFile(url)?System.IO.Path.GetFileName(new Uri(url).LocalPath):new Uri(url).Host);}
   CloseSpotlight();SelectTab(tab.Id);Save();
  }
  async void SelectTab(string id) { HideAiBar(); await Activate(id); }
  async Task Activate(string id) {if(readerTab!=null&&readerTab!=id)CloseReader();
   var tab=state.Tabs.FirstOrDefault(t=>t.Id==id);if(tab==null||closing)return;
   if(readerTab==id&&readerView==null&&!Navigation.IsReading(tab.Url))CloseReader();
   WebView2 leavingView;if(state.ActiveId!=id&&!InSplit(state.ActiveId)&&views.TryGetValue(state.ActiveId??"",out leavingView)&&leavingView.IsVisible)await SnapshotTab(state.ActiveId,leavingView);
   HideSnapshot();
   string leaving=state.ActiveId;LeavePageFullscreenForTabSwitch();ClosePageTools();CloseContextMenu();state.ActivateTab(id,DateTime.UtcNow);
   PlaceViews(id,leaving);WakeRenderer(id);BalanceMemory(false);
   Find<Border>("ErrorPanel").Visibility=Visibility.Collapsed; Find<ProgressBar>("Loading").Visibility=Visibility.Collapsed;
   bool home=tab.Url=="mica://home",library=Navigation.IsLibrary(tab.Url);
   libraryPage.Visibility=library?Visibility.Visible:Visibility.Collapsed;
   Find<ScrollViewer>("Home").Visibility=home?Visibility.Visible:Visibility.Collapsed;
   Find<Grid>("WebHost").Visibility=home||library||IsSpotlight?Visibility.Collapsed:Visibility.Visible;
   Find<TextBlock>("AddressText").Text=home?"A fresh start":Navigation.AddressLabel(tab.Url);
   window.Title=(home?"A fresh start":tab.Title)+" — Mica"+(privateMode?" · Private":ProfileTitleSuffix);RefreshActiveTab(leaving);UpdateIslandMedia();ScrollActiveTabIntoView();UpdateNavigation();SaveSoon();if(devToolsOpen)AttachDevTools();
   if(library){RenderLibrary();AnimateArrival(libraryPage);Status("Your library, kept on this device.");return;}
   if(home){RenderHome();AnimateHome();Status("All yours. Take your time.");return;}
   if(Navigation.IsReading(tab.Url)){ShowSavedArticle(tab);return;}
   WebView2 existing;if(views.TryGetValue(id,out existing)) {if(existing.CoreWebView2!=null)FocusPageSoon(existing);return;}
   await CreateTabView(tab,false);
   if(SplitOpen)LayoutSplit();
  }
  // Creates a tab's page. warm: in the background, unseen (you hovered a resting tab), so a click shows it at once.
  async Task CreateTabView(PageEntry tab,bool warm) {
   string id=tab.Id;
   var view=new WebView2();view.DefaultBackgroundColor=System.Drawing.Color.White;views[id]=view;view.SizeChanged+=delegate {QueuePageCorners();};WatchSplitFocus(id,view);view.IsVisibleChanged+=delegate {QueuePageCorners();};view.Loaded+=delegate {QueuePageCorners();};MarkRestingTabs(true);var navigation=new NavigationProgress();navigations[id]=navigation;if(warm)view.Visibility=Visibility.Collapsed;Find<Grid>("WebHost").Children.Add(view);if(!warm)ShowSnapshotWhileLoading(id,view);
   try {
    environment=await EnsureEnvironment();
    if(closing||!IsCurrent(id,view))return;
    var controller=ViewOptions(environment,tab.Jar);
    await view.EnsureCoreWebView2Async(environment,controller);await ApplyMicaAgent(view.CoreWebView2);AttachSiteZoom(view);
    if(closing||!IsCurrent(id,view))return;
    var core=view.CoreWebView2;core.Settings.IsWebMessageEnabled=false;core.Settings.AreHostObjectsAllowed=false;core.Settings.IsGeneralAutofillEnabled=false;core.Settings.IsPasswordAutosaveEnabled=false;
    core.DownloadStarting+=delegate(object sender,CoreWebView2DownloadStartingEventArgs e) {if(closing||!IsCurrent(id,view)){e.Handled=true;e.Cancel=true;return;}downloadNavigations[id]=Path.GetFileName(e.ResultFilePath);NavigationBecameDownload(id,core);if(!HandleStoreCrxDownload(view,e))TrackDownload(view,e);};
    WatchFullscreen(id,view);ApplyTrackingPrevention(core);RegisterOwnTarget(core);if(devToolsOpen&&state.ActiveId==id)AttachDevTools();Func<bool> live=()=>IsCurrent(id,view);WatchSitePermissions(live,view,Find<Button>("AddressButton"));WatchPageDialogs(live,view);WatchLogins(live,view,core);if(AiEnabled)WatchAiSelection(core,true);WatchPeek(tab,view,core);core.IsDocumentPlayingAudioChanged+=delegate {audibleAt[id]=DateTime.UtcNow;window.Dispatcher.BeginInvoke(new Action(delegate {if(!closing){RefreshTabRow(id);UpdateIslandMedia();}}));};core.IsMutedChanged+=delegate {window.Dispatcher.BeginInvoke(new Action(delegate {if(!closing){RefreshTabRow(id);UpdateIslandMedia();}}));};
    core.ContextMenuRequested+=delegate(object sender,CoreWebView2ContextMenuRequestedEventArgs e) {ShowWebContextMenu(tab,view,e);};
    WatchCertificates(id,core);
    // An extension closing the tab (chrome.tabs.remove, windows.remove) or a page allowed to close itself.
    KeepWindowOpen(view);core.WindowCloseRequested+=delegate {window.Dispatcher.BeginInvoke(new Action(delegate {if(!closing&&IsCurrent(id,view))CloseTab(id);}));};
    core.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e) {if(!closing&&IsCurrent(id,view)&&BlockUnaskedPopup(e,core))return;if(!closing&&IsCurrent(id,view)&&TryOpenPopup(e,tab.Jar))return;e.Handled=true;if(closing||!IsCurrent(id,view))return;string uri=e.Uri;if(Navigation.IsWeb(uri)||(e.IsUserInitiated&&Navigation.IsLocalFile(uri)))window.Dispatcher.BeginInvoke(new Action(delegate {if(!closing&&IsCurrent(id,view))OpenFromTab(tab,uri);}));else if(uri.StartsWith("chrome-extension://",StringComparison.OrdinalIgnoreCase)&&IsExtensionPage(core.Source))window.Dispatcher.BeginInvoke(new Action(delegate {if(!closing)OpenExtensionPage(uri);}));else Status("Only web addresses can open in a new tab.");};
    core.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e) { if(navLog!=null)navLog.Add(DateTime.Now.ToString("ss.fff")+" start "+e.Uri+(e.IsRedirected?" (redirect)":"")+(e.IsUserInitiated?" (user)":""));if(!IsCurrent(id,view))return;if(UpgradeToHttps(id,core,e))return;if(HandleStoreInstallNavigation(core,e))return;if(!Navigation.IsWeb(e.Uri)&&e.Uri!="about:blank"&&!Navigation.IsSiteBlob(e.Uri)&&!Navigation.IsLocalFile(e.Uri)&&!IsClaudeSignInReturn(e.Uri)&&!ExternalApps.IsLaunchable(e.Uri)) {e.Cancel=true;Status("Blocked a non-web address.");return;}if(ExternalApps.IsLaunchable(e.Uri))return;navigation.Begin(e.NavigationId);certFailures.Remove(id);if(state.ActiveId==id){ClosePageTools(true);CloseContextMenu();Find<Border>("ErrorPanel").Visibility=Visibility.Collapsed;Find<Grid>("WebHost").Visibility=IsSpotlight?Visibility.Collapsed:Visibility.Visible;Find<ProgressBar>("Loading").Visibility=Visibility.Visible;SetReloadMotion(true);Status("Finding your way…");} };
    core.SourceChanged+=delegate {if(!IsCurrent(id,view))return;if(readerView==view)CloseReader();{string now=Navigation.IsWeb(core.Source)?SiteFor(core.Source):null,before;if(tabSite.TryGetValue(id,out before)&&before!=now)LeftSite(before,tab.Jar);tabSite[id]=now;}if(Navigation.IsWeb(core.Source))tab.Url=Navigation.FromEngine(core.Source);else if(Navigation.IsLocalFile(core.Source))tab.Url=core.Source;if(state.ActiveId==id)Find<TextBlock>("AddressText").Text=Navigation.AddressLabel(tab.Url);RefreshFavicon(tab,view);RefreshTabRow(id);};
    core.FaviconChanged+=delegate {RefreshFavicon(tab,view);};
    core.DocumentTitleChanged+=delegate {if(!IsCurrent(id,view))return;tab.Title=string.IsNullOrWhiteSpace(core.DocumentTitle)?(Navigation.IsLocalFile(tab.Url)?System.IO.Path.GetFileName(new Uri(tab.Url).LocalPath):new Uri(tab.Url).Host):core.DocumentTitle;RefreshTabRow(id);if(islandMediaShown==id)UpdateIslandMedia();if(state.ActiveId==id)window.Title=tab.Title+" — Mica"+(privateMode?" · Private":ProfileTitleSuffix);};
    core.HistoryChanged+=delegate {if(state.ActiveId==id)UpdateNavigation();};
    core.NavigationCompleted+=async delegate(object sender,CoreWebView2NavigationCompletedEventArgs e) {if(caretOn&&e.IsSuccess)ApplyCaret(view);
     if(navLog!=null)navLog.Add(DateTime.Now.ToString("ss.fff")+" done "+e.IsSuccess+" "+e.WebErrorStatus+" "+core.Source);
     if(!IsCurrent(id,view))return;
     string insecure=FinishHttpsUpgrade(id,e);
     var result=navigation.Complete(e.NavigationId,e.IsSuccess,e.HttpStatusCode,e.WebErrorStatus.ToString());
     if(result==NavigationResult.Ignore)return;
     if(result==NavigationResult.Ready||(result==NavigationResult.Stopped&&e.HttpStatusCode>=200&&e.HttpStatusCode<300)){if(!privateMode)state.Visit(tab.Url,tab.Title);Save();RefreshPersonalization();}
     if(state.ActiveId==id){
      Find<ProgressBar>("Loading").Visibility=Visibility.Collapsed;UpdateNavigation();
      string downloaded;bool becameDownload=downloadNavigations.TryGetValue(id,out downloaded)&&downloadNavigations.Remove(id);
      if(result==NavigationResult.Failed&&becameDownload){if(core.CanGoBack)Status("Download started: "+downloaded);else ShowDownloadNotice(downloaded);}
      else if(result==NavigationResult.Failed&&insecure!=null)ShowInsecureOffer(insecure);
      else if(result==NavigationResult.Failed&&IsCertificateError(e.WebErrorStatus))ShowCertificateOffer(id,tab.Url,e.WebErrorStatus);
      else if(result==NavigationResult.Failed){abortedTab=e.WebErrorStatus==CoreWebView2WebErrorStatus.ConnectionAborted?id:null;Status("Page couldn't load: "+e.WebErrorStatus);ShowLoadError(e.WebErrorStatus);}
      else {Find<Border>("ErrorPanel").Visibility=Visibility.Collapsed;Find<Grid>("WebHost").Visibility=IsSpotlight?Visibility.Collapsed:Visibility.Visible;Status(result==NavigationResult.Ready?"Ready when you are.":"Loading stopped. Available page content is kept.");}
     }
     RefreshFavicon(tab,view);UpdateMemory();if(e.IsSuccess){RestoreHighlights(view);await ApplyAmbient(view);await AttachStoreButton(view);}
    };
    core.ProcessFailed+=delegate(object sender,CoreWebView2ProcessFailedEventArgs e) {HandleProcessFailed(id,view,e);};
    ServeBuiltInPages(core);await PageScriptsReady(core);if(closing||LiveCore(view)!=core)return;core.Navigate(Navigation.EngineAddress(tab.Url)); if(state.ActiveId==id&&!IsSpotlight)view.Focus();RenderTabs();
   } catch(Exception e) {if(IsCurrent(id,view)) {DisposeView(id);if(state.ActiveId==id)ShowError("The web engine could not start. If using the portable package, extract the complete Runtime folder beside Mica.exe and retry.\n\n"+e.Message);}if(environmentTask!=null&&environmentTask.IsFaulted)environmentTask=null;}
  }
  System.Windows.Threading.DispatcherTimer warmTimer;
  void WarmSoon(string id,bool hovering) {
   if(warmTimer!=null)warmTimer.Stop();if(!hovering)return;
   var tab=state.Tabs.FirstOrDefault(t=>t.Id==id);if(tab==null||!IsResting(tab))return;
   warmTimer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(140)};
   warmTimer.Tick+=async delegate {warmTimer.Stop();if(!closing&&!views.ContainsKey(id)&&IsResting(tab)&&!LowOnMemory())await CreateTabView(tab,true);};warmTimer.Start();
  }
  bool IsCurrent(string id,WebView2 view) {WebView2 current;return views.TryGetValue(id,out current)&&current==view;}
  WebView2 CurrentView() {WebView2 v;return state.ActiveId!=null&&views.TryGetValue(state.ActiveId,out v)&&LiveCore(v)!=null?v:null;}
  // After the engine process dies, touching CoreWebView2 throws; treat that as "no engine".
  static CoreWebView2 LiveCore(WebView2 view) {try {return view==null?null:view.CoreWebView2;}catch(InvalidOperationException){return null;}}
  void DisposeView(string id) {WebView2 v;if(views.TryGetValue(id,out v)){ForgetPageCorners(v);ReplaceTargetWatcher(v);if(id==state.ActiveId){ClosePageTools();CloseContextMenu();}var dying=LiveCore(v);if(dying!=null)storeInstallNonces.Remove(dying);views.Remove(id);navigations.Remove(id);if(!RetainDownloadView(v)){Find<Grid>("WebHost").Children.Remove(v);v.Dispose();}MarkRestingTabs(true);}}
  void CloseTab(string id) {{var leaving=state.Tabs.FirstOrDefault(t=>t.Id==id);if(leaving!=null&&Navigation.IsWeb(leaving.Url)){string site=SiteFor(leaving.Url),jar=leaving.Jar;window.Dispatcher.BeginInvoke(new Action(delegate {LeftSite(site,jar);}));}}SplitEnded(id);snapshots.Remove(id);if(!privateMode){var closed=state.Tabs.FirstOrDefault(t=>t.Id==id);if(closed!=null)state.RecordClosed(closed);}DisposeView(id);state.CloseTab(id);SelectTab(SplitOpen&&!InSplit(state.ActiveId)?splitPanes[0]:state.ActiveId);RefreshPersonalization();}
  void Reload() {Find<Border>("ErrorPanel").Visibility=Visibility.Collapsed;var v=CurrentView();if(v!=null){Find<Grid>("WebHost").Visibility=Visibility.Visible;v.Reload();}else if(Active!=null)SelectTab(Active.Id);}
  void UpdateNavigation() {UpdateReloadMotion();UpdateSiteBadge();var v=CurrentView();Find<Button>("BackButton").IsEnabled=v!=null&&v.CanGoBack;Find<Button>("ForwardButton").IsEnabled=v!=null&&v.CanGoForward;Find<Button>("BookmarkButton").Content=Active!=null&&state.Bookmarks.Any(b=>b.Url==Active.Url)?"\uE735":"\uE734";FollowTabForClaude();}
  // A music tab goes quiet between songs or while buffering; sleeping it then would stop
  // the music and make it reload. Tabs that made sound in the last 30 minutes stay awake.
  readonly Dictionary<string,DateTime> audibleAt=new Dictionary<string,DateTime>();
  bool RecentlyAudible(string id,WebView2 view) {
   var core=LiveCore(view);if(core!=null&&core.IsDocumentPlayingAudio){audibleAt[id]=DateTime.UtcNow;return true;}
   DateTime at;return audibleAt.TryGetValue(id,out at)&&DateTime.UtcNow-at<TimeSpan.FromMinutes(30);
  }
  void ShowError(string message) {SetReloadMotion(false);Find<TextBlock>("ErrorTitle").Text="A small detour.";Find<Button>("RetryButton").Visibility=Visibility.Visible;Find<Button>("InsecureButton").Visibility=Visibility.Collapsed;Find<Button>("CertButton").Visibility=Visibility.Collapsed;Find<Button>("GamesButton").Visibility=Visibility.Collapsed;Find<TextBlock>("ErrorText").Text=message;Find<Grid>("WebHost").Visibility=Visibility.Collapsed;Find<Border>("ErrorPanel").Visibility=Visibility.Visible;Find<ProgressBar>("Loading").Visibility=Visibility.Collapsed;}
  void Status(string text) {Find<TextBlock>("StatusText").Text=text;ShowIslandMessage(text);}
  // A tab switch saves a moment later, off the frame that shows the new tab. Closing saves at once.
  System.Windows.Threading.DispatcherTimer saveTimer;
  void SaveSoon() {if(privateMode)return;if(saveTimer==null){saveTimer=new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) {Interval=TimeSpan.FromMilliseconds(600)};saveTimer.Tick+=delegate {saveTimer.Stop();if(!closing)Save();};}saveTimer.Stop();saveTimer.Start();}
  // Writing the session (JSON of every tab and the history) takes tens of milliseconds, so it happens on a
  // background thread, one save after another for all windows. The UI thread only copies the lists, so the
  // writer never sees one change under it. Closing, and tests, save at once.
  static Task saveChain=Task.FromResult(true);static readonly object saveLock=new object();internal static bool asyncSaveForTest;
  // Before Mica goes down after an error it can't recover from: every window's tabs, saved now.
  internal static void SaveAllNow() {
   Task pending;lock(saveLock)pending=saveChain;try {pending.Wait(2000);}catch(AggregateException){}
   foreach(var browser in Program.Browsers.ToArray())if(!browser.privateMode)try {StateStore.Save(browser.statePath,DetachedCopy(browser.SnapshotForSave()));}catch(Exception){}
  }
  // The first window after a crash says so once, with where the details are.
  void TellAboutLastCrash() {
   if(!CrashLog.PreviousRunCrashed||privateMode)return;CrashLog.PreviousRunCrashed=false;
   Status(CrashLog.Latest()!=null?"Mica closed unexpectedly last time. Your tabs are back; Tools › Crash reports has the details.":"Mica closed unexpectedly last time. Your tabs are back.");
  }
  void ShowCrashReports() {
   string latest=CrashLog.Latest();
   if(latest==null){Status("No crash reports. Mica hasn't crashed here.");return;}
   try {System.Diagnostics.Process.Start("explorer.exe","/select,\""+latest+"\"");}catch(Exception e){Status("Couldn't open the crash reports: "+e.Message);}
  }
  void Save() {
   if(saveTimer!=null)saveTimer.Stop();if(privateMode)return;NoteSettingsChange();
   var snapshot=DetachedCopy(SnapshotForSave());string path=statePath;
   Action<Exception> report=delegate(Exception e) {if(e is IOException||e is UnauthorizedAccessException||e is InvalidOperationException)Status("Couldn't save this session: "+e.Message);else throw e;};
   if(closing||smoke&&!asyncSaveForTest) {
    Task pending;lock(saveLock)pending=saveChain;try {pending.Wait();}catch(AggregateException){}
    try {StateStore.Save(path,snapshot);}catch(Exception e) {report(e);}return;
   }
   var dispatcher=window.Dispatcher;
   lock(saveLock)saveChain=saveChain.ContinueWith(delegate {
    try {StateStore.Save(path,snapshot);}catch(Exception e) {if(e is IOException||e is UnauthorizedAccessException||e is InvalidOperationException)dispatcher.BeginInvoke(new Action(delegate {report(e);}));}
   },TaskScheduler.Default);
  }
  internal static Task PendingSaves() {lock(saveLock)return saveChain;}
  // New lists and dictionaries holding the same entries: the writer reads these while the UI changes the real ones.
  static BrowserState DetachedCopy(BrowserState source) {
   var copy=new BrowserState();
   foreach(var property in typeof(BrowserState).GetProperties().Where(p=>p.CanRead&&p.CanWrite&&p.GetIndexParameters().Length==0)) {
    object value=property.GetValue(source,null);
    if(value is System.Collections.IList||value is System.Collections.IDictionary)value=Activator.CreateInstance(value.GetType(),value);
    property.SetValue(copy,value,null);
   }
   return copy;
  }
  void Bookmark() {var t=Active;if(t==null||!Navigation.IsWeb(t.Url))return;var found=state.Bookmarks.FirstOrDefault(b=>b.Url==t.Url);if(found!=null){state.Bookmarks.Remove(found);Status("Bookmark removed.");}else{state.Bookmarks.Add(new PageEntry {Title=t.Title,Url=t.Url,LastUsed=DateTime.UtcNow});Status("Bookmarked. Find it in Spotlight.");}UpdateNavigation();Save();}
  readonly HashSet<string> renderedTabs=new HashSet<string>();
  void OpenSpotlight(bool address) {
   tabSearch=false;   CloseMenus();bool wasVisible=IsSpotlight;editingAddress=address;if(!wasVisible)previousFocus=Keyboard.FocusedElement;ParkWebHost(true);
   // The overlay already takes every click. Disabling the browser behind it restyled every button there (a
   // whole-window redraw on open and again on close), so it only stops taking the pointer.
   Find<Grid>("AppLayout").IsHitTestVisible=false;Find<Grid>("SpotlightOverlay").Visibility=Visibility.Visible;
   AnimateSpotlightOpen(wasVisible);
   var input=Find<TextBox>("SpotlightInput");input.Text=address&&Active!=null&&Active.Url!="mica://home"?Navigation.AddressLabel(Active.Url):"";RefreshSearch();input.Focus();input.SelectAll();
  }
  // Spotlight slides the page window far off to the side rather than hiding it. Hiding tells the engine the
  // page went away (about 30 ms on the UI thread, then again on the way back); moving it is one window move.
  void ParkWebHost(bool park) {var host=Find<Grid>("WebHost");var shift=host.RenderTransform as TranslateTransform;if(shift==null){shift=new TranslateTransform();host.RenderTransform=shift;}shift.X=park?-40000:0;}
  void CloseSpotlight() {
   if(!IsSpotlight)return;
   AnimateSpotlightClose(delegate {
    Find<Grid>("SpotlightOverlay").Visibility=Visibility.Collapsed;Find<Grid>("AppLayout").IsHitTestVisible=true;ParkWebHost(false);
    if(Active!=null&&Navigation.IsWeb(Active.Url)&&Find<Border>("ErrorPanel").Visibility!=Visibility.Visible)Find<Grid>("WebHost").Visibility=Visibility.Visible;
    var view=CurrentView();if(view!=null)FocusPageSoon(view);else if(previousFocus!=null)Keyboard.Focus(previousFocus);
   });
  }
  void RefreshSearch() {var q=Find<TextBox>("SpotlightInput").Text;bool commands=q.TrimStart().StartsWith(">");var results=commands?SearchCommands(q.TrimStart()):tabSearch?TabHitsWithMemory(Spotlight.SearchTabs(state,q,id=>views.ContainsKey(id))):Spotlight.Search(state,q);var list=Find<ListBox>("SearchResults");list.ItemsSource=results;list.SelectedIndex=results.Count>0?0:-1;Find<TextBlock>("NoResults").Visibility=results.Count==0?Visibility.Visible:Visibility.Collapsed;Find<TextBlock>("NoResults").Text=commands?"No action matches. Try one word, like split or mute.":tabSearch?"No tab or recent page matches. Try fewer words.":q.Contains(":")?"Use an http:// or https:// address.":"Type a website or a thought. We'll take you there.";Find<TextBlock>("SearchHint").Text=commands?results.Count+(results.Count==1?" ACTION":" ACTIONS"):tabSearch?(q.Length==0?state.Tabs.Count(t=>t.Url!="mica://home")+" TABS · OPEN, RESTING AND RECENTLY CLOSED":results.Count+" MATCHES"):q.Length==0?"TABS, BOOKMARKS & RECENT PLACES · TYPE > FOR ACTIONS":results.Count+" PLACES TO GO";}
  void ExecuteSearch() {if(spotlightClosing||!IsSpotlight)return;var hit=Find<ListBox>("SearchResults").SelectedItem as SearchHit;if(hit==null)return;if(RunPaletteHit(hit))return;bool replace=editingAddress||(Active!=null&&Active.Url=="mica://home");CloseSpotlight();if(hit.TabId!=null){var picked=state.Tabs.FirstOrDefault(t=>t.Id==hit.TabId);if(picked!=null&&picked.Space!=state.Space)state.Space=picked.Space;SelectTab(hit.TabId);}else Navigate(hit.Url,!replace);}
  // Ctrl+Shift+A: Spotlight searching only tabs (both spaces, open and resting), recently closed ones and history.
  void OpenTabSearch() {OpenSpotlight(false);tabSearch=true;RefreshSearch();}
  void SleepIdleTabs(bool all) {
   int count=0;foreach(var tab in state.Tabs.ToArray()) {WebView2 view;if(!views.TryGetValue(tab.Id,out view)||HasDownloads(view))continue;NavigationProgress progress;if(navigations.TryGetValue(tab.Id,out progress)&&progress.IsLoading)continue;bool audio=RecentlyAudible(tab.Id,view);if(SiteMemoryMode(tab)=="never"||StateStore.InExtensionGroup(tab)||!TabLifecycle.ShouldSleep(tab,tab.Id==state.ActiveId||InSplit(tab.Id),audio,DateTime.UtcNow,all,RestAfterFor(tab,LowOnMemory())))continue;DisposeView(tab.Id);count++;}
   if(count>0){RenderTabs();UpdateMemory();Status(count+" background tab(s) put to rest. They reload when reopened.");}
   MarkRestingTabs(true);
  }
  string MemorySummary() {return MemorySummary(EngineProcessIds());}
  List<int> EngineProcessIds() {
   var ids=new List<int>();
   if(environment!=null)try {foreach(var info in environment.GetProcessInfos())ids.Add(info.ProcessId);}catch(COMException){}
   return ids;
  }
  static string MemorySummary(List<int> engine) {
   long total;using(var own=Process.GetCurrentProcess())total=own.WorkingSet64;int processes=1;
   foreach(int id in engine)try {using(var process=Process.GetProcessById(id)){total+=process.WorkingSet64;processes++;}}catch(ArgumentException){}catch(InvalidOperationException){}
   return (total/1048576.0).ToString("0")+" MB · "+processes+" processes";
  }
  // Reading other processes' working sets takes ~20 ms; keep it off the UI thread so animations don't hitch.
  bool memoryUpdating;
  async void UpdateMemory() {
   if(memoryUpdating)return;memoryUpdating=true;
   RefreshTabMemory();
   try {var ids=EngineProcessIds();string text=await Task.Run(()=>MemorySummary(ids));if(!closing){Find<TextBlock>("MemoryText").Text=text;UpdateIslandMemory(text);}}
   finally {memoryUpdating=false;}
  }
  void ApplyTheme() {
   string[] keys={"Ink","Muted","Canvas","Panel","Line","Hover","Accent"};string[] colors=state.Dark?new[]{"#F0EBF5","#B0A7BC","#24212A","#302B38","#403948","#453C52","#C8B4E0"}:new[]{"#302D38","#716B7A","#FAF9F6","#FFFFFF","#E9E5E9","#ECE7F2","#71608C"};
   for(int i=0;i<keys.Length;i++)window.Resources[keys[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));window.Resources["SideCard"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(state.Dark?"#443A50":"#60FFFFFF"));window.Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(state.Dark?"#332C40":"#DDD7E5"));Find<Viewbox>("HomeArtwork").Opacity=state.Dark?0.1:0.32;ApplyPersonalization();RenderTabs();RenderHome();if(privateMode)Find<TextBlock>("Greeting").Text="Private browsing · history is not saved";if(libraryPage!=null&&libraryPage.IsVisible)RenderLibrary();
   if(!privateMode)foreach(var browser in Program.Browsers.ToArray())if(browser.privateMode&&!browser.closing&&browser.state.Dark!=state.Dark){browser.state.Dark=state.Dark;browser.ApplyTheme();}
   ThemeGames();
  }

  void Capture(string name) {window.UpdateLayout();var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts",name)))encoder.Save(output);}
  async Task SmokeTest() {
   var log=new List<string>();string resultPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","smoke-result.txt");
   try {
    await Task.Delay(500);Capture("mica-home.png");log.Add("Home rendered: "+MemorySummary());
    if(!HandleKey(Key.K,ModifierKeys.Control)||!IsSpotlight)throw new Exception("Ctrl+K did not open Spotlight");
    Find<TextBox>("SpotlightInput").Text="example.com";if(Find<ListBox>("SearchResults").Items.Count==0)throw new Exception("URL result missing");ExecuteSearch();
    for(int i=0;i<80;i++){await Task.Delay(250);var v=CurrentView();if(v!=null&&v.CoreWebView2.DocumentTitle=="Example Domain")break;}
    var page=CurrentView();if(page==null||page.CoreWebView2.DocumentTitle!="Example Domain")throw new Exception("Web navigation failed: "+Find<TextBlock>("StatusText").Text);
    log.Add("WebView2 navigated to Example Domain: "+MemorySummary());using(var file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","web-page.png")))await page.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
    Navigate("https://example.com/?mica=2",false);for(int i=0;i<80;i++){await Task.Delay(200);if(CurrentView()!=null&&CurrentView().CoreWebView2.Source=="https://example.com/?mica=2"&&Find<ProgressBar>("Loading").Visibility!=Visibility.Visible)break;}
    if(CurrentView()==null||!CurrentView().CanGoBack)throw new Exception("Typing a new address erased same-tab history");ShowError("Test error surface before Back navigation");CurrentView().GoBack();for(int i=0;i<60;i++){await Task.Delay(200);if(Active.Url=="https://example.com/"&&Find<ProgressBar>("Loading").Visibility!=Visibility.Visible)break;}if(Active.Url!="https://example.com/")throw new Exception("Back navigation failed");if(Find<Grid>("WebHost").Visibility!=Visibility.Visible)throw new Exception("Back after error left web content hidden");log.Add("Address navigation preserves Back history and restores error surface");
    Bookmark();if(!state.Bookmarks.Any(b=>b.Url=="https://example.com/"))throw new Exception("Bookmark failed");log.Add("Bookmark saved");
    string webId=state.ActiveId;state.Space="Work";var work=state.AddTab("mica://home");await Activate(work.Id);OpenSpotlight(false);Find<TextBox>("SpotlightInput").Text="example";await Task.Delay(200);Capture("mica-spotlight.png");
    ExecuteSearch();if(state.ActiveId!=webId||state.Space!="Personal")throw new Exception("Cross-space Spotlight switch failed");log.Add("Spotlight selected tab across spaces");
    await Activate(work.Id);SleepIdleTabs(true);if(views.ContainsKey(webId))throw new Exception("Sleeping tab retained view");log.Add("Background view disposed: "+MemorySummary());
    await Activate(webId);for(int i=0;i<60;i++){await Task.Delay(200);if(CurrentView()!=null&&CurrentView().CoreWebView2.DocumentTitle=="Example Domain")break;}
    if(CurrentView()==null||CurrentView().CoreWebView2.DocumentTitle!="Example Domain")throw new Exception("Sleeping tab did not restore");log.Add("Sleeping tab restored");
    state.Dark=true;ApplyTheme();await Activate(work.Id);Capture("mica-dark.png");state.Dark=false;ApplyTheme();Save();if(StateStore.Load(statePath).Bookmarks.Count!=1)throw new Exception("Persistence failed");log.Add("Theme and session persistence passed");
    window.Width=960;window.Height=680;await Task.Delay(200);Capture("mica-compact.png");OpenSpotlight(false);Find<TextBox>("SpotlightInput").Text="example";await Task.Delay(100);Capture("mica-spotlight-compact.png");CloseSpotlight();log.Add("Compact window rendered at 960 × 680");
    log.Add("PASS");File.WriteAllLines(resultPath,log);window.Close();
   }catch(Exception e){log.Add("FAIL: "+e);File.WriteAllLines(resultPath,log);Application.Current.Shutdown(1);}
  }
 }
}






