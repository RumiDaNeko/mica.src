using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace Mica {
 partial class BrowserApp {
  Popup spacesPopup,menuPopup;
  async Task<CoreWebView2Environment> EnsureEnvironment() {
   if(environmentTask==null)environmentTask=CoreWebView2Environment.CreateAsync(BundledRuntime.Folder(),EngineFolder,new CoreWebView2EnvironmentOptions {ScrollBarStyle=CoreWebView2ScrollbarStyle.FluentOverlay,AreBrowserExtensionsEnabled=!privateMode,AdditionalBrowserArguments=DevToolsArguments(devToolsPort=DevToolsPort(EngineFolder,DockedDevToolsWanted))+EngineMemoryArguments+QuietDebuggerArgument});
   try {return environment=await environmentTask;}catch {environmentTask=null;throw;}
  }
  void InitializeMenus() {
   spacesPopup=CreatePopup("SpacesPopup",226);menuPopup=CreatePopup("BrowserMenuPopup",280);
   sitesPopup=CreatePopup("SitesPopup",250,false);sitesPopup.StaysOpen=true;sitesPopup.HorizontalOffset=14;sitesPopup.VerticalOffset=-9;
   menuPopup.Closed+=delegate {CloseSites();};window.Deactivated+=delegate {if(sitesPopup.IsOpen)CloseMenus();};
   ((Border)sitesPopup.Child).PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {if(e.Key==Key.Left||e.Key==Key.Escape){var back=submenuRow;CloseSites();if(back!=null)back.Focus();e.Handled=true;}};
   spacesPopup.Opened+=delegate {Tween((RotateTransform)Find<TextBlock>("SpaceChevron").RenderTransform,RotateTransform.AngleProperty,180,180,null);};
   spacesPopup.Closed+=delegate {Tween((RotateTransform)Find<TextBlock>("SpaceChevron").RenderTransform,RotateTransform.AngleProperty,0,180,null);};
   // Popup auto-dismiss runs on mouse-down, before Button.Click on mouse-up.
   // Consume a press on the open popup's trigger so it cannot reopen on release.
   PreProcessInputEventHandler togglePress=delegate(object sender,PreProcessInputEventArgs e) {
    var mouse=e.StagingItem.Input as MouseButtonEventArgs;
    if(mouse==null||mouse.RoutedEvent!=Mouse.PreviewMouseDownEvent)return;
    if(sitesPopup!=null&&sitesPopup.IsOpen) {
     Func<Popup,bool> inside=delegate(Popup p) {var card=(FrameworkElement)p.Child;var at=mouse.GetPosition(card);return new Rect(0,0,card.ActualWidth,card.ActualHeight).Contains(at);};
     var trigger=menuPopup.PlacementTarget as FrameworkElement;
     bool onTrigger=trigger!=null&&trigger.IsVisible&&new Rect(0,0,trigger.ActualWidth,trigger.ActualHeight).Contains(mouse.GetPosition(trigger));
     // A press on the menu button falls through to the toggle below, which closes the menu without reopening it.
     if(!inside(menuPopup)&&!inside(sitesPopup)){if(onTrigger)CloseSites();else CloseMenus();}
    }
    if(mouse.ChangedButton!=MouseButton.Left||mouse.ButtonState!=MouseButtonState.Pressed)return;
    foreach(var popup in new[]{spacesPopup,menuPopup,accountPopup}) {
     if(popup==null)continue;
     var trigger=popup.PlacementTarget as FrameworkElement;
     if(!popup.IsOpen||trigger==null||!trigger.IsVisible)continue;
     var point=mouse.GetPosition(trigger);
     if(!new Rect(0,0,trigger.ActualWidth,trigger.ActualHeight).Contains(point))continue;
     e.Cancel();popup.IsOpen=false;trigger.Focus();break;
    }
   };
   InputManager.Current.PreProcessInput+=togglePress;
   window.Closed+=delegate {InputManager.Current.PreProcessInput-=togglePress;};
   window.LocationChanged+=delegate {CloseMenus();};window.SizeChanged+=delegate {CloseMenus();};
  }
  Popup CreatePopup(string name,double width,bool focusFirst=true) {
   // The card animates itself; a popup fade on top would redraw the layered window twice per frame.
   var popup=new Popup {AllowsTransparency=true,StaysOpen=false,Placement=PlacementMode.Right,HorizontalOffset=8,PopupAnimation=PopupAnimation.None};
   var card=new Border {Width=width,CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),Padding=new Thickness(8),Resources=window.Resources};Scaled(card);
   card.SetResourceReference(Border.BackgroundProperty,"PopupPanel");card.SetResourceReference(Border.BorderBrushProperty,"PopupLine");
   card.Child=new ScrollViewer {Content=new StackPanel(),MaxHeight=Math.Max(240,SystemParameters.WorkArea.Height-100),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};popup.Child=card;Find<Grid>("Root").Children.Add(popup);window.RegisterName(name,popup);
   popup.Opened+=delegate {AnimatePopupArrival(card);if(!focusFirst)return;var first=((StackPanel)((ScrollViewer)card.Child).Content).Children.OfType<Button>().FirstOrDefault();if(first!=null)first.Focus();};
   card.PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {if(e.Key==Key.Escape){popup.IsOpen=false;e.Handled=true;}};
   return popup;
  }
  void CloseMenus() {CloseContextMenu();CloseSites();if(spacesPopup!=null)spacesPopup.IsOpen=false;if(menuPopup!=null)menuPopup.IsOpen=false;if(accountPopup!=null)accountPopup.IsOpen=false;}
  // Submenus (Sites, Privacy, Tools): one popup beside the main menu that opens on hover (or Right / Enter from the
  // keyboard) and shows the rows of whichever group is pointed at. While it is open the main menu stops
  // light-dismissing, so a click in the submenu doesn't close the menu under it; clicks elsewhere close both through
  // the press hook.
  Popup sitesPopup;Button sitesRow,submenuRow;
  readonly System.Collections.Generic.Dictionary<Button,StackPanel> submenus=new System.Collections.Generic.Dictionary<Button,StackPanel>();
  void OpenSites(bool focus) {OpenSubmenu(sitesRow,focus);}
  void OpenSubmenu(Button row,bool focus) {
   if(row==null||!menuPopup.IsOpen||!submenus.ContainsKey(row))return;
   if(submenuRow!=row) {
    if(sitesPopup.IsOpen)sitesPopup.IsOpen=false;if(submenuRow!=null)submenuRow.ClearValue(Button.BackgroundProperty);
    ((ScrollViewer)((Border)sitesPopup.Child).Child).Content=submenus[row];submenuRow=row;
   }
   if(!sitesPopup.IsOpen){menuPopup.StaysOpen=true;sitesPopup.PlacementTarget=row;sitesPopup.IsOpen=true;row.SetResourceReference(Button.BackgroundProperty,"Hover");}
   if(focus)window.Dispatcher.BeginInvoke(new Action(delegate {var first=PopupButtons(sitesPopup).FirstOrDefault();if(first!=null)first.Focus();}),System.Windows.Threading.DispatcherPriority.Loaded);
  }
  void CloseSites() {
   if(sitesPopup==null||!sitesPopup.IsOpen)return;
   sitesPopup.IsOpen=false;if(submenuRow!=null)submenuRow.ClearValue(Button.BackgroundProperty);
   menuPopup.StaysOpen=false;
  }
  // A main-menu row that opens a submenu; the returned panel takes the submenu's rows.
  StackPanel SubmenuRow(StackPanel body,string icon,string title,string tag,out Button row) {
   var opener=MenuCommand(body,sitesPopup,icon,title,"›",null);opener.Tag=tag;row=opener;
   var chevron=MenuHint(opener);chevron.FontSize=16;chevron.Margin=new Thickness(10,-3,2,0);
   opener.Click+=delegate {OpenSubmenu(opener,true);};
   opener.MouseEnter+=delegate {OpenSubmenu(opener,false);};
   opener.PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {if(e.Key==Key.Right){OpenSubmenu(opener,true);e.Handled=true;}};
   var content=new StackPanel();submenus[opener]=content;return content;
  }
  System.Collections.Generic.IEnumerable<Button> PopupButtons(Popup popup) {return ((StackPanel)((ScrollViewer)((Border)popup.Child).Child).Content).Children.OfType<Button>();}
  StackPanel PopupBody(Popup popup) {var body=(StackPanel)((ScrollViewer)((Border)popup.Child).Child).Content;body.Children.Clear();return body;}
  void MenuHeading(StackPanel body,string label) {var text=new TextBlock {Text=label,FontSize=10,FontWeight=FontWeights.SemiBold,Margin=new Thickness(12,9,12,9)};text.SetResourceReference(TextBlock.ForegroundProperty,"Muted");body.Children.Add(text);}
  void MenuDivider(StackPanel body) {var line=new Border {Height=1,Margin=new Thickness(10,7,10,7)};line.SetResourceReference(Border.BackgroundProperty,"Line");body.Children.Add(line);}
  Button MenuCommand(StackPanel body,Popup popup,string icon,string title,string hint,Action action) {
   var button=new Button {Style=(Style)window.FindResource(typeof(Button)),HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(12,11,12,11),Margin=new Thickness(0,1,0,1)};
   var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(28)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
   var symbol=new TextBlock {Text=icon,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=14,VerticalAlignment=VerticalAlignment.Center};symbol.SetResourceReference(TextBlock.ForegroundProperty,"Accent");row.Children.Add(symbol);
   var name=new TextBlock {Text=title,FontSize=12,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(name,1);row.Children.Add(name);
   var shortcut=new TextBlock {Text=hint,FontSize=10,Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Center};shortcut.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Grid.SetColumn(shortcut,2);row.Children.Add(shortcut);
   button.Content=row;if(action!=null)button.Click+=delegate {popup.IsOpen=false;action();};System.Windows.Automation.AutomationProperties.SetName(button,title);body.Children.Add(button);return button;
  }
  void ShowSpaces() {
   if(spacesPopup.IsOpen){spacesPopup.IsOpen=false;return;}CloseMenus();var body=PopupBody(spacesPopup);MenuHeading(body,"SWITCH SPACE");
   foreach(string name in state.Spaces) {string space=name;int count=state.Tabs.Count(t=>t.Space==space);var item=MenuCommand(body,spacesPopup,SpaceIcon(space),space,(state.Space==space?"✓  ":"")+count+" tabs",delegate {SwitchSpace(space);});item.Tag="space:"+space;if(state.Space==space)item.SetResourceReference(Button.BackgroundProperty,"Hover");}
   MenuDivider(body);
   if(state.Spaces.Count<BrowserState.MaxSpaces)MenuCommand(body,spacesPopup,"\uE710","New space","",ShowNewSpace).Tag="space-new";
   if(state.Space!="Personal") {string here=state.Space;MenuCommand(body,spacesPopup,"\uE8AC","Rename "+here,"",delegate {ShowNewSpace(here);}).Tag="space-rename";MenuCommand(body,spacesPopup,"\uE74D","Remove "+here,"Tabs move to Personal",delegate {RemoveSpace(here);}).Tag="space-remove";}var help=new TextBlock {Text="Separate tabs for each part of your day.",FontSize=10,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12,4,12,10)};help.SetResourceReference(TextBlock.ForegroundProperty,"Muted");body.Children.Add(help);
   spacesPopup.Placement=sidebarHidden?PlacementMode.Right:PlacementMode.Bottom;spacesPopup.HorizontalOffset=sidebarHidden?8:0;spacesPopup.VerticalOffset=sidebarHidden?0:8;spacesPopup.PlacementTarget=Find<Button>(sidebarHidden?"MiniSpace":"SpaceButton");spacesPopup.IsOpen=true;
  }
  static string SpaceIcon(string space) {return space=="Personal"?"\uE77B":space=="Work"?"\uE821":"\uE8F1";}
  void ShowNewSpace() {ShowNewSpace(null);}
  // Names a new space, or renames one (Personal keeps its name).
  Window spaceNameWindow;
  void ShowNewSpace(string renaming) {
   if(spaceNameWindow!=null){spaceNameWindow.Activate();return;}
   StackPanel body,actions;var dialog=CreateMicaDialog(window,renaming==null?"New space":"Rename space",420,300,out body,out actions);spaceNameWindow=dialog;dialog.Closed+=delegate {spaceNameWindow=null;};
   body.Children.Add(DialogText("A space keeps its own set of tabs in this window, for another part of your day.",12,"Muted"));
   SectionLabel(body,"NAME");
   var name=new TextBox {Padding=new Thickness(8,6,8,6),Tag="space-name",MaxLength=BrowserState.MaxSpaceName,Text=renaming??""};System.Windows.Automation.AutomationProperties.SetName(name,"Space name");body.Children.Add(name);
   var problem=DialogText("",11,"Muted");problem.Margin=new Thickness(0,8,0,0);body.Children.Add(problem);
   var create=new Button {Content=renaming==null?"Create":"Rename",Padding=new Thickness(18,9,18,9),IsDefault=true,Tag="space-create"};create.SetResourceReference(Button.BackgroundProperty,"Hover");actions.Children.Add(create);
   create.Click+=delegate {
    string title=(name.Text??"").Trim();if(renaming==title){dialog.Close();return;}
    string why=state.SpaceNameProblem(title,renaming);if(why!=null){problem.Text=why;name.Focus();return;}
    // Windows of one profile share their spaces.
    foreach(var browser in SpaceWindows()){if(renaming==null)browser.state.AddSpace(title);else browser.state.RenameSpace(renaming,title);if(browser!=this)browser.RenderTabs();}
    dialog.Close();if(renaming==null)SwitchSpace(title);else RenderTabs();Save();
   };
   dialog.Show();name.Focus();name.SelectAll();
  }
  System.Collections.Generic.List<BrowserApp> SpaceWindows() {var list=Program.Browsers.Where(b=>!b.closing&&!b.privateMode&&b.profile==profile&&b!=this).ToList();list.Insert(0,this);return list;}
  void RemoveSpace(string space) {int moved=0;foreach(var browser in SpaceWindows()){int here=browser.state.RemoveSpace(space);if(browser==this)moved=here;else {browser.SwitchSpace(browser.state.Space);}}SwitchSpace(state.Space);Save();Status(moved==0?"Removed "+space:"Removed "+space+". Its "+moved+" tabs are in Personal now.");}
  void ToggleTheme() {bool dark=!state.Dark;foreach(var browser in Program.Browsers.ToArray())if(!browser.closing){browser.state.Dark=dark;browser.ApplyTheme();browser.Save();browser.AnimateHome();}}
  // Building the menu's rows costs a few frames, so the built menu is kept and only its live hints are refreshed.
  string menuKey;TextBlock ambientHint,updatesHintText,aiHint;
  string MenuKey() {return TrackingLevel()+Localizer.Language;}
  static TextBlock MenuHint(Button command) {return (TextBlock)((Grid)command.Content).Children[2];}
  void ShowMenu() {
   if(menuPopup.IsOpen){menuPopup.IsOpen=false;return;}CloseMenus();
   if(MenuKey()!=menuKey)BuildMenu();
   else {if(resetTrackerRow!=null)resetTrackerRow();ambientHint.Text=state.AmbientYoutube?"On":"Off";if(aiHint!=null)aiHint.Text=state.AiPanel?"On":"Off";updatesHintText.Text=UpdatesHint();if(adsHint!=null)adsHint.Text=AdsHint();UpdateZoomBadge();}
   menuPopup.PlacementTarget=Find<Button>(sidebarHidden?"MiniMenu":"MenuButton");menuPopup.IsOpen=true;RefreshUpdatesHint();
  }
  void BuildMenu() {
   menuKey=MenuKey();var body=PopupBody(menuPopup);
   MenuCommand(body,menuPopup,"\uE81C","History","Ctrl+H",delegate {OpenLibraryPage("mica://history");});
   MenuCommand(body,menuPopup,"\uE896","Downloads","Ctrl+J",delegate {OpenLibraryPage("mica://downloads");});
   MenuCommand(body,menuPopup,"\uE71E","Search tabs","Ctrl+Shift+A",OpenTabSearch);
   splitHint=MenuHint(MenuCommand(body,menuPopup,"\uE7F4","Split view",SplitOpen?"On":"Off",ToggleSplit));
   MenuCommand(body,menuPopup,"\uE734","Bookmarks","",delegate {ShowLibrary(true);});
   MenuCommand(body,menuPopup,"\uE72E","Passwords & sync","",ShowVault);
   MenuCommand(body,menuPopup,"\uE727","New private window","Ctrl+Shift+N",delegate {LaunchBrowserWindow(true);});
   profilesHint=MenuHint(MenuCommand(body,menuPopup,"\uE77B","Profiles",CurrentProfile==null?"":CurrentProfile.Name,ShowProfiles));
   ZoomMenuRow(body);
   MenuDivider(body);
   submenus.Clear();submenuRow=null;
   var sites=SubmenuRow(body,"\uE774","Sites","menu-sites",out sitesRow);
   MenuCommand(sites,menuPopup,"\uE721","Find in page","Ctrl+F",ShowFind);
   MenuCommand(sites,menuPopup,"\uE749","Print","Ctrl+P",ShowPrint);
   MenuCommand(sites,menuPopup,"\uE88A","Cast media","",ShowCast);
   ambientHint=MenuHint(MenuCommand(sites,menuPopup,"\uE706","YouTube ambient",state.AmbientYoutube?"On":"Off",ToggleAmbient));
   MenuCommand(sites,menuPopup,"\uE9E9","Site settings","",ShowSiteCard);
   MenuCommand(sites,menuPopup,"\uE72E","Site permissions","",ShowSitePermissions);
   MenuCommand(sites,menuPopup,"\uE943","Developer tools","F12",ToggleDevTools);
   dockHint=MenuHint(MenuCommand(sites,menuPopup,"\uE8A0","Dock DevTools",state.DockedDevTools?"On":"Off",ToggleDockedDevTools));
   adsHint=MenuHint(MenuCommand(sites,menuPopup,"\uEA18","Ads on this site",AdsHint(),ToggleAdsHere));
   Button privacyRow,toolsRow;
   var privacy=SubmenuRow(body,"\uE8D7","Privacy","menu-privacy",out privacyRow);
   MenuCommand(privacy,menuPopup,"\uE8F8","Ad blocking","",ShowAdBlocking);
   MenuCommand(privacy,menuPopup,"\uEA86","Extensions","",ShowExtensions);
   MenuCommand(privacy,menuPopup,"\uE74D","Clear browsing data","Ctrl+Shift+Del",ShowClearBrowsingData);
   TrackerRow(body);
   var tools=SubmenuRow(body,"\uE90F","Tools","menu-tools",out toolsRow);
   MenuCommand(tools,menuPopup,"\uE9D9","Task manager","Shift+Esc",ShowTaskManager);
   if(!privateMode)MenuCommand(tools,menuPopup,"\uE8B5","Import from another browser","",ShowImport);
   if(!privateMode){aiHint=MenuHint(MenuCommand(tools,menuPopup,"\uE8F2","AI side panel",state.AiPanel?"On":"Off",ShowAiSettings));}
   MenuCommand(tools,menuPopup,"\uE823","Rest background tabs","",delegate {SleepIdleTabs(true);});
   MenuCommand(tools,menuPopup,"\uE765","Keyboard shortcuts","",ShowShortcuts);
   MenuCommand(tools,menuPopup,"\uEBE8","Crash reports","",ShowCrashReports);
   MenuDivider(body);
   updatesHintText=MenuHint(MenuCommand(body,menuPopup,"\uE895","Updates",UpdatesHint(),ShowUpdates));
   MenuCommand(body,menuPopup,"\uE790","Personalize","",ShowPersonalize);
   var languageRow=MenuCommand(body,menuPopup,"\uE8C1","Language",LanguageHint(),delegate {SwitchLanguage(Localizer.Vietnamese?"en":"vi");});
   languageRow.Tag="menu-language";
   // Pointing at any other row folds the submenu away.
   foreach(UIElement other in body.Children)if(!(other is Button&&submenus.ContainsKey((Button)other)))other.MouseEnter+=delegate {CloseSites();};
  }
  void ShowLibrary(bool bookmarks) {
   if(!bookmarks){OpenLibraryPage("mica://history");return;}
   OpenSpotlight(false);var entries=bookmarks?state.Bookmarks:state.History;var results=entries.Take(50).Select(b=>new SearchHit {Kind=bookmarks?"BOOKMARK":"HISTORY",Title=b.Title,Detail=b.Url,Url=b.Url}).ToList();
   Find<ListBox>("SearchResults").ItemsSource=results;Find<ListBox>("SearchResults").SelectedIndex=results.Count>0?0:-1;Find<TextBlock>("SearchHint").Text=bookmarks?"BOOKMARKS":"RECENT HISTORY";Find<TextBlock>("NoResults").Visibility=results.Count==0?Visibility.Visible:Visibility.Collapsed;Find<TextBlock>("NoResults").Text=bookmarks?"Bookmark a page with Ctrl+D.":"Your visited pages will appear here.";
  }
 }
}
