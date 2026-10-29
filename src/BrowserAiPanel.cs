using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // The AI side panel: a chat site (ChatGPT, Claude…) beside the page, off until you turn it on. It opens
 // the provider's own website in your profile, so there are no keys and nothing for Mica to keep.
 // When the window has room it grows outward so the page keeps its width; maximized, full screen or
 // snapped, it docks inside. Closed, it freezes like a background tab and rests after a few minutes.
 partial class BrowserApp {
  internal static readonly string[][] AiProviders={
   new[]{"chatgpt","ChatGPT","https://chatgpt.com/"},
   new[]{"claude","Claude","https://claude.ai/new"},
   new[]{"gemini","Gemini","https://gemini.google.com/app"},
   new[]{"copilot","Copilot","https://copilot.microsoft.com/"},
   new[]{"perplexity","Perplexity","https://www.perplexity.ai/"}};
  const double AiWidth=400;
  static readonly TimeSpan AiFreezeAfter=TimeSpan.FromSeconds(15),AiRestAfter=TimeSpan.FromMinutes(5);
  static string Glyph(int code) {return ((char)code).ToString();}

  internal bool AiEnabled {get {return state.AiPanel&&!privateMode;}}
  internal bool aiOpen;internal WebView2 aiView;internal double aiGrown;internal DateTime aiClosedAt;internal Action<CoreWebView2> aiViewCreatedForTest;
  double aiShift,aiGrownLeft;bool aiCreating;string aiLoaded;DispatcherTimer aiRest;
  Border aiDock;Grid aiHost;WrapPanel aiPills;StackPanel aiCaptionSlot;TextBlock aiTitle,aiSite,aiEmpty;Button aiButton;

  string AiProviderId() {return state.AiProvider=="custom"&&AiCustomAddress()!=null?"custom":AiProviders.Any(p=>p[0]==state.AiProvider)?state.AiProvider:"chatgpt";}
  string AiCustomAddress() {Uri uri;return !string.IsNullOrEmpty(state.AiCustomUrl)&&Uri.TryCreate(state.AiCustomUrl,UriKind.Absolute,out uri)&&uri.Scheme=="https"?uri.AbsoluteUri:null;}
  string AiAddress() {string id=AiProviderId();if(id=="custom")return AiCustomAddress();return AiProviders.First(p=>p[0]==id)[2];}
  string AiName() {string id=AiProviderId();if(id=="custom"){Uri uri=new Uri(AiCustomAddress());return uri.Host.StartsWith("www.")?uri.Host.Substring(4):uri.Host;}return AiProviders.First(p=>p[0]==id)[1];}

  void InitializeAiPanel() {
   // The toolbar button, shown only while the panel is turned on.
   var reader=Find<Button>("ReaderButton");var row=(StackPanel)reader.Parent;
   aiButton=new Button {Style=(Style)window.FindResource("Icon"),Content=Glyph(0xE8F2),ToolTip="AI side panel (Ctrl+Shift+Space)",Tag="ai-button"};
   System.Windows.Automation.AutomationProperties.SetName(aiButton,"AI side panel");aiButton.Click+=delegate {ToggleAiPanel();};row.Children.Insert(0,aiButton);
   window.StateChanged+=delegate {if(aiOpen)window.Dispatcher.BeginInvoke(new Action(FitAiPanel),DispatcherPriority.Background);};
   window.Closed+=delegate {DisposeAiView();HideAiBar();};
   window.Deactivated+=delegate {HideAiBar();};
   ApplyAiSetting();
  }
  // The setting itself: the button, the toolbar's room for it, and the selection script on open pages.
  void ApplyAiSetting() {
   if(aiButton==null)return;bool on=AiEnabled;
   aiButton.Visibility=on?Visibility.Visible:Visibility.Collapsed;
   var columns=Find<Grid>("Toolbar").ColumnDefinitions;columns[2].Width=new GridLength(218+(on?32:0));
   if(!on){if(aiOpen)SetAiOpen(false);DisposeAiView();HideAiBar();}
   foreach(var pair in views.ToList()){var core=LiveCore(pair.Value);if(core!=null)WatchAiSelection(core,on);}
  }
  internal void SetAiEnabled(bool on) {
   foreach(var browser in Program.Browsers.Where(b=>!b.closing&&!b.privateMode&&b.profile==profile).ToList()){browser.state.AiPanel=on;browser.ApplyAiSetting();browser.Save();}
   if(aiHint!=null)aiHint.Text=on?"On":"Off";if(aiSettingsSwitch!=null)aiSettingsSwitch.Content=on?"On":"Off";
   Status(on?"AI side panel is on. Press Ctrl+Shift+Space or the chat button to open it.":"AI side panel is off.");
  }
  void ToggleAiPanel() {
   if(privateMode){Status("The AI side panel isn't available in private windows.");return;}
   if(!AiEnabled){ShowAiSettings();return;}
   SetAiOpen(!aiOpen);
  }
  internal void SetAiOpen(bool open) {
   if(open==aiOpen||(open&&!AiEnabled))return;aiOpen=open;EnsureAiDock();HideAiBar();
   // The chat loads once the slide is over: starting a web view mid-slide would make the slide stutter.
   if(open){if(aiRest!=null)aiRest.Stop();RevealAi(true,delegate {if(aiOpen)EnsureAiView();});}
   else {RevealAi(false,null);aiClosedAt=DateTime.UtcNow;RestAiLater();var page=CurrentView();if(page!=null&&Find<Grid>("WebHost").IsVisible)page.Focus();}
   if(aiButton!=null){if(open)aiButton.SetResourceReference(Button.BackgroundProperty,"Hover");else aiButton.ClearValue(Button.BackgroundProperty);}
  }
  // A card of its own beside the page, like the page itself. A page in full screen (a video) hides it;
  // Mica's own full screen (F11) keeps it, edge to edge.
  internal void LayoutAi() {
   if(aiDock==null)return;FinishAiSlide();aiDock.ClearValue(FrameworkElement.WidthProperty);aiDock.ClearValue(FrameworkElement.HorizontalAlignmentProperty);var columns=Find<Grid>("AppLayout").ColumnDefinitions;bool show=aiOpen&&!pageFullscreen;
   columns[2].Width=new GridLength(show?AiWidth:0);aiDock.Visibility=show?Visibility.Visible:Visibility.Collapsed;
   aiDock.Margin=new Thickness(fullscreenApplied?0:7,0,0,0);aiDock.CornerRadius=new CornerRadius(fullscreenApplied?0:12);aiDock.BorderThickness=fullscreenApplied?new Thickness(1,0,0,0):new Thickness(1);
   MoveCaptionButtons(show);if(show){RefreshAiHeader();ShapeAiLater();}
  }
  // The window's own buttons belong at its top-right corner, so while the panel is open they move into its header.
  List<FrameworkElement> captionParts;double captionWidth;
  void MoveCaptionButtons(bool intoPanel) {
   if(captionParts==null){var min=Find<Button>("MinButton");var row=(StackPanel)min.Parent;int at=row.Children.IndexOf(min);captionParts=row.Children.Cast<FrameworkElement>().Skip(Math.Max(0,at-1)).ToList();}
   var home=Find<Button>("ReaderButton").Parent as StackPanel;bool moved=aiCaptionSlot.Children.Count>0;if(moved==intoPanel)return;
   if(intoPanel)captionWidth=captionParts.Sum(e=>e.ActualWidth+e.Margin.Left+e.Margin.Right);
   foreach(var part in captionParts){((Panel)part.Parent).Children.Remove(part);(intoPanel?aiCaptionSlot:home).Children.Add(part);}
   Find<Grid>("Toolbar").ColumnDefinitions[2].Width=new GridLength(218+(AiEnabled?32:0)-(intoPanel?captionWidth:0));
  }
  // Opening and closing slide: the window grows outward with the panel (or the page makes room for it when
  // the window can't grow) and shrinks back the same way. The card keeps its full width while it slides,
  // so the chat site isn't laid out again on every frame. Without animations it just appears.
  Action aiSlideEnd;
  // A slide still running when something else changes the layout lands where it was going first.
  void FinishAiSlide() {var end=aiSlideEnd;aiSlideEnd=null;if(end!=null)end();}
  void RevealAi(bool open,Action done) {
   FinishAiSlide();var columns=Find<Grid>("AppLayout").ColumnDefinitions;double from=columns[2].ActualWidth;
   double left=window.Left,width=window.ActualWidth,shift=0,grow=0;
   if(open){var plan=PlanAiGrowth();aiGrown=0;aiShift=0;if(plan!=null){shift=left-plan.Value.Left;grow=AiWidth;aiShift=shift;aiGrown=AiWidth;aiGrownLeft=plan.Value.Left;}}
   else {
    if(aiGrown>0&&!(window.WindowState==WindowState.Normal&&Snapped())){grow=-Math.Min(aiGrown,width-window.MinWidth);if(window.WindowState==WindowState.Normal&&aiShift>0&&Math.Abs(window.Left-aiGrownLeft)<1)shift=-aiShift;}
    aiGrown=0;
   }
   if(window.WindowState!=WindowState.Normal){grow=0;shift=0;}
   LayoutAi();bool show=aiOpen&&!pageFullscreen;
   double to=show?AiWidth:0;
   if(!SystemParameters.ClientAreaAnimation||Math.Abs(to-from)<1){columns[2].Width=new GridLength(to);if(grow!=0||shift!=0){window.Left=left-shift;window.Width=width+grow;}if(done!=null)done();return;}
   // Slide from where it is: the card stays visible (and full width) until it has slid away.
   aiDock.Visibility=Visibility.Visible;aiDock.Width=AiWidth-aiDock.Margin.Left;aiDock.HorizontalAlignment=HorizontalAlignment.Left;
   var clock=System.Diagnostics.Stopwatch.StartNew();EventHandler frame=null;
   Action<double> place=delegate(double eased) {columns[2].Width=new GridLength(from+(to-from)*eased);if(grow!=0||shift!=0){window.Left=left-shift*eased;window.Width=width+grow*eased;}};
   Action end=null;end=delegate {
    if(aiSlideEnd==end)aiSlideEnd=null;CompositionTarget.Rendering-=frame;place(1);
    aiDock.ClearValue(FrameworkElement.WidthProperty);aiDock.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
    if(!show)aiDock.Visibility=Visibility.Collapsed;else ShapeAiLater();
    if(done!=null)done();
   };
   frame=delegate {
    if(closing){CompositionTarget.Rendering-=frame;return;}
    double t=Math.Min(1,clock.Elapsed.TotalMilliseconds/AiSlideMilliseconds);
    if(t<1)place(1-Math.Pow(1-t,3));else end();
   };
   aiSlideEnd=end;place(0);CompositionTarget.Rendering+=frame;
  }
  const double AiSlideMilliseconds=240;
  // Going maximized or back while the panel is open: grow outward again when there's room.
  void FitAiPanel() {if(!aiOpen||aiGrown>0)return;GrowForAi();LayoutAi();}

  void EnsureAiDock() {
   if(aiDock!=null)return;var layout=Find<Grid>("AppLayout");
   while(layout.ColumnDefinitions.Count<3)layout.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(0)});
   aiDock=new Border {Visibility=Visibility.Collapsed,Tag="ai-dock",BorderBrush=new SolidColorBrush(Color.FromArgb(0x30,255,255,255))};Grid.SetColumn(aiDock,2);
   aiDock.SetResourceReference(Border.BackgroundProperty,"Canvas");
   var grid=new Grid();aiDock.Child=grid;
   grid.RowDefinitions.Add(new RowDefinition {Height=new GridLength(44)});grid.RowDefinitions.Add(new RowDefinition());
   // The header lines up with the page's toolbar: which AI, then choose another, open in a tab and close.
   var head=new Border {BorderThickness=new Thickness(0,0,0,1)};head.SetResourceReference(Border.BorderBrushProperty,"Line");grid.Children.Add(head);
   var headGrid=new Grid {Margin=new Thickness(14,0,6,0),VerticalAlignment=VerticalAlignment.Center};head.Child=headGrid;
   var label=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};headGrid.Children.Add(label);
   var mark=DialogText(Glyph(0xE8F2),14,"Accent");mark.FontFamily=new FontFamily("Segoe MDL2 Assets");mark.VerticalAlignment=VerticalAlignment.Center;mark.Margin=new Thickness(0,1,9,0);label.Children.Add(mark);
   aiTitle=DialogText(AiName(),13,"Ink");aiTitle.FontWeight=FontWeights.SemiBold;aiTitle.VerticalAlignment=VerticalAlignment.Center;label.Children.Add(aiTitle);
   aiSite=DialogText("",11,"Muted");aiSite.VerticalAlignment=VerticalAlignment.Center;aiSite.Margin=new Thickness(8,1,0,0);label.Children.Add(aiSite);
   var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};headGrid.Children.Add(buttons);
   Func<int,string,Action,Button> tool=delegate(int glyph,string tip,Action act) {var b=new Button {Style=(Style)window.FindResource("Icon"),Content=Glyph(glyph),ToolTip=tip};System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b,true);b.Click+=delegate {act();};buttons.Children.Add(b);return b;};
   tool(0xE713,"Choose the AI",ShowAiSettings).Tag="ai-settings-button";
   tool(0xE8A7,"Open in a tab",delegate {var core=aiView==null?null:LiveCore(aiView);string at=core!=null&&Navigation.IsWeb(core.Source)?core.Source:AiAddress();SetAiOpen(false);Navigate(at,true);});
   tool(0xE89F,"Close the panel (Ctrl+Shift+Space)",delegate {SetAiOpen(false);}).Tag="ai-close";
   aiCaptionSlot=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};buttons.Children.Add(aiCaptionSlot);
   var body=new Grid();Grid.SetRow(body,1);grid.Children.Add(body);
   aiEmpty=DialogText("Opening…",12,"Muted");aiEmpty.HorizontalAlignment=HorizontalAlignment.Center;aiEmpty.VerticalAlignment=VerticalAlignment.Center;aiEmpty.TextWrapping=TextWrapping.Wrap;aiEmpty.Margin=new Thickness(24);body.Children.Add(aiEmpty);
   aiHost=new Grid();body.Children.Add(aiHost);aiHost.SizeChanged+=delegate {ShapeAiLater();};
   layout.Children.Add(aiDock);RefreshAiHeader();
  }
  static ControlTemplate aiPillTemplate;
  Button AiPill(string text,string tag,Action act) {
   if(aiPillTemplate==null)aiPillTemplate=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(
    "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>"+
    "<Border x:Name='Surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='12' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>"+
    "<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
   var pill=new Button {Content=text,Template=aiPillTemplate,Padding=new Thickness(9,3,9,4),Margin=new Thickness(0,0,5,6),FontSize=12,Tag=tag,Cursor=System.Windows.Input.Cursors.Hand};
   pill.Click+=delegate {act();};return pill;
  }
  // The panel's settings: on or off, and which chat it opens, every choice in sight, one click each.
  internal Window aiSettingsWindow;internal Button aiSettingsSwitch;
  internal void ShowAiSettings() {
   CloseMenus();if(aiSettingsWindow!=null){aiSettingsWindow.Activate();return;}
   StackPanel body,actions;var dialog=CreateMicaDialog(window,"AI side panel",460,400,out body,out actions);aiSettingsWindow=dialog;
   dialog.Closed+=delegate {aiSettingsWindow=null;aiPills=null;aiSettingsSwitch=null;aiExtensionNote=null;aiExtensionGet=null;aiExtensionRemove=null;};
   body.Children.Add(DialogText("A chat site beside the page, opened with your profile. Nothing on a page goes to it unless you ask.",12,"Muted"));
   var row=new Grid {Margin=new Thickness(12,9,12,9)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
   var name=DialogText("Show the panel",12,"Ink");name.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(name);
   aiSettingsSwitch=new Button {Content=state.AiPanel?"On":"Off",Padding=new Thickness(14,5,14,5),Tag="ai-settings-switch"};Grid.SetColumn(aiSettingsSwitch,1);row.Children.Add(aiSettingsSwitch);
   aiSettingsSwitch.Click+=delegate {SetAiEnabled(!state.AiPanel);};
   body.Children.Add(Card(row));
   SectionLabel(body,"CHAT");
   aiPills=new WrapPanel {Margin=new Thickness(0,2,0,0)};body.Children.Add(aiPills);
   foreach(var p in AiProviders){string id=p[0];aiPills.Children.Add(AiPill(p[1],"ai-provider:"+id,delegate {ChooseAiProvider(id,null);}));}
   aiPills.Children.Add(AiPill("Your own…","ai-provider:custom",ShowAiCustom));
   // An AI with its own extension can use it instead of the site, so it can work on the page too. The extension
   // isn't listed with the others, so it is got and removed here.
   aiExtensionNote=DialogText("",11,"Muted");aiExtensionNote.Margin=new Thickness(2,10,2,0);aiExtensionNote.TextWrapping=TextWrapping.Wrap;aiExtensionNote.Tag="ai-claude-extension";body.Children.Add(aiExtensionNote);
   var extensionActions=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,6,0,0)};body.Children.Add(extensionActions);
   aiExtensionGet=new Button {Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,0),Visibility=Visibility.Collapsed,Tag="ai-claude-extension-get"};aiExtensionGet.SetResourceReference(Button.BackgroundProperty,"Hover");extensionActions.Children.Add(aiExtensionGet);
   aiExtensionGet.Click+=delegate {var ai=AiExtensionFor(AiProviderId());if(ai==null)return;dialog.Close();Navigate(ai.Store,true);};
   aiExtensionRemove=new Button {Content="Remove the extension",Padding=new Thickness(12,6,12,6),Visibility=Visibility.Collapsed,Tag="ai-extension-remove"};aiExtensionRemove.SetResourceReference(Button.BackgroundProperty,"Hover");extensionActions.Children.Add(aiExtensionRemove);
   aiExtensionRemove.Click+=delegate {RemoveAiExtension(AiExtensionFor(AiProviderId()));};
   RefreshAiExtensionNote();
   var keys=DialogText("Ctrl+Shift+Space opens and closes it.",11,"Muted");keys.Margin=new Thickness(2,10,2,0);body.Children.Add(keys);
   var done=new Button {Content="Done",Padding=new Thickness(18,9,18,9),IsDefault=true,IsCancel=true};done.SetResourceReference(Button.BackgroundProperty,"Hover");done.Click+=delegate {dialog.Close();};actions.Children.Add(done);
   RefreshAiHeader();dialog.Show();
  }
  // The chosen AI's name and site, and its pill filled in with the accent.
  void RefreshAiHeader() {
   string id=AiProviderId();
   if(aiTitle!=null){aiTitle.Text=AiName();Uri uri;aiSite.Text=aiExtension!=null&&aiExtension.Provider==id?aiExtension.Name:id!="custom"&&Uri.TryCreate(AiAddress(),UriKind.Absolute,out uri)?(uri.Host.StartsWith("www.")?uri.Host.Substring(4):uri.Host):"";}
   if(aiPills==null)return;
   var accent=window.TryFindResource("Accent") as SolidColorBrush;Color tint=accent!=null?accent.Color:Colors.SlateBlue;
   foreach(var pill in aiPills.Children.OfType<Button>()) {
    bool on=(string)pill.Tag=="ai-provider:"+id;
    if(on){pill.Background=new SolidColorBrush(Color.FromArgb(0x26,tint.R,tint.G,tint.B));pill.BorderBrush=new SolidColorBrush(Color.FromArgb(0x70,tint.R,tint.G,tint.B));pill.SetResourceReference(Control.ForegroundProperty,"Accent");}
    else {pill.Background=Brushes.Transparent;pill.SetResourceReference(Control.BorderBrushProperty,"Line");pill.SetResourceReference(Control.ForegroundProperty,"Muted");}
    pill.Content=(string)pill.Tag=="ai-provider:custom"&&on?AiName():(string)pill.Tag=="ai-provider:custom"?"Your own…":pill.Content;
   }
  }
  // The chat is a native window WPF can't clip, so its bottom corners are rounded to the card's like a page's.
  void ShapeAiLater() {window.Dispatcher.BeginInvoke(new Action(delegate {if(aiView!=null&&aiDock!=null&&!closing)ShapePage(aiView,aiDock);}),DispatcherPriority.Render);}
  internal void ChooseAiProvider(string id,string custom) {
   foreach(var browser in Program.Browsers.Where(b=>!b.closing&&!b.privateMode&&b.profile==profile).ToList()) {
    browser.state.AiProvider=id;if(custom!=null)browser.state.AiCustomUrl=custom;
    browser.RefreshAiHeader();browser.RefreshAiExtensionNote();
    if(browser.aiOpen)browser.EnsureAiView();else browser.DisposeAiView();browser.Save();
   }
  }
  void ShowAiCustom() {
   StackPanel body,actions;var dialog=CreateMicaDialog(window,"Your own AI address",440,280,out body,out actions);
   body.Children.Add(DialogText("Any chat site you like, opened in the panel with your profile. It must start with https://.",12,"Muted"));
   SectionLabel(body,"ADDRESS");
   var box=new TextBox {Padding=new Thickness(8,6,8,6),Text=state.AiCustomUrl??"https://",Tag="ai-custom"};body.Children.Add(box);
   var problem=DialogText("",11,"Muted");problem.Margin=new Thickness(0,8,0,0);body.Children.Add(problem);
   var use=new Button {Content="Use it",Padding=new Thickness(18,9,18,9),IsDefault=true};use.SetResourceReference(Button.BackgroundProperty,"Hover");actions.Children.Add(use);
   use.Click+=delegate {Uri uri;string text=(box.Text??"").Trim();if(!Uri.TryCreate(text,UriKind.Absolute,out uri)||uri.Scheme!="https"||string.IsNullOrEmpty(uri.Host)){problem.Text="Enter a full address starting with https://";box.Focus();return;}dialog.Close();ChooseAiProvider("custom",uri.AbsoluteUri);};
   dialog.Show();box.Focus();box.SelectAll();
  }

  async void EnsureAiView() {
   if(aiCreating)return;aiCreating=true;string address;try {address=await AiPanelAddress();}finally {aiCreating=false;}
   if(closing||!aiOpen)return;
   if(aiView!=null) {
    var live=LiveCore(aiView);if(live==null)return;
    try {if(live.IsSuspended)live.Resume();live.MemoryUsageTargetLevel=CoreWebView2MemoryUsageTargetLevel.Normal;}catch(Exception){}
    if(aiLoaded!=address){aiLoaded=address;aiEmpty.Text="Opening "+AiName()+"…";live.Navigate(address);}
    else FollowTabForClaude();
    return;
   }
   if(aiCreating)return;aiCreating=true;aiEmpty.Text="Opening "+AiName()+"…";
   var view=new WebView2 {DefaultBackgroundColor=System.Drawing.Color.Transparent,Visibility=Visibility.Hidden};
   try {
    aiHost.Children.Add(view);var engine=await EnsureEnvironment();await view.EnsureCoreWebView2Async(engine,ViewOptions(engine));
    if(closing||!AiEnabled){aiHost.Children.Remove(view);view.Dispose();return;}
    var core=view.CoreWebView2;await ApplyMicaAgent(core);RegisterOwnTarget(core);ApplyTrackingPrevention(core);
    core.Settings.AreHostObjectsAllowed=false;core.Settings.IsWebMessageEnabled=false;core.Settings.IsGeneralAutofillEnabled=false;await ShimClaudeSidePanel(core);
    // Links and pop-ups from the chat open as ordinary tabs.
    core.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e) {e.Handled=true;string uri=e.Uri;if(Navigation.IsWeb(uri))window.Dispatcher.BeginInvoke(new Action(delegate {if(!closing)Navigate(uri,true);}));};
    core.DownloadStarting+=delegate(object sender,CoreWebView2DownloadStartingEventArgs e) {TrackDownload(view,e);};
    // Shown as soon as the page starts drawing: chat apps keep loading in the background and may not
    // reach DOMContentLoaded, let alone "load", for a long time. Only a real failure (not a redirect or a cancelled load) hides it again.
    Action show=delegate {if(aiView!=view)return;view.Visibility=Visibility.Visible;aiEmpty.Text="";ShapeAiLater();};
    core.ContentLoading+=delegate {show();};core.DOMContentLoaded+=delegate {show();if(aiView==view&&claudeExtensionActive&&IsClaudePanel(core.Source)&&core.Source.IndexOf("tabId=",StringComparison.Ordinal)<0)BindClaudePanel();};
    core.NavigationCompleted+=delegate(object sender,CoreWebView2NavigationCompletedEventArgs e) {
     if(aiView!=view)return;
     if(e.IsSuccess||e.WebErrorStatus==CoreWebView2WebErrorStatus.OperationCanceled){if(e.IsSuccess)show();return;}
     view.Visibility=Visibility.Hidden;aiEmpty.Text=AiName()+" couldn't load. Check your connection, or open it in a tab.";
    };
    core.ProcessFailed+=delegate {if(aiView==view)window.Dispatcher.BeginInvoke(new Action(DisposeAiView));};
    aiView=view;aiLoaded=address;
    if(aiViewCreatedForTest!=null)aiViewCreatedForTest(core);
    core.Navigate(address);
   }catch(Exception e){aiHost.Children.Remove(view);try {view.Dispose();}catch(Exception){}aiEmpty.Text="Couldn't open the AI panel: "+e.Message;}
   finally {aiCreating=false;}
  }
  internal void DisposeAiView() {
   if(aiRest!=null)aiRest.Stop();var view=aiView;if(view==null)return;aiView=null;aiLoaded=null;
   ForgetPageCorners(view);if(aiHost!=null)aiHost.Children.Remove(view);try {view.Dispose();}catch(Exception){}
   if(aiEmpty!=null)aiEmpty.Text="Opening…";
  }
  // Closed: low memory and frozen after a few seconds (instant to bring back), gone after five minutes.
  void RestAiLater() {
   if(aiView==null)return;
   if(aiRest==null){aiRest=new DispatcherTimer {Interval=AiFreezeAfter};aiRest.Tick+=delegate {AiRestTick();};}
   aiRest.Stop();aiRest.Start();
  }
  internal async void AiRestTick() {
   if(aiOpen||aiView==null){if(aiRest!=null)aiRest.Stop();return;}
   if(DateTime.UtcNow-aiClosedAt>=AiRestAfter){DisposeAiView();return;}
   var core=LiveCore(aiView);if(core==null)return;
   try {core.MemoryUsageTargetLevel=CoreWebView2MemoryUsageTargetLevel.Low;if(!core.IsSuspended&&!aiView.IsVisible)await core.TrySuspendAsync();}catch(Exception){}
   if(aiRest!=null)aiRest.Interval=TimeSpan.FromSeconds(30);
  }

  // Growing outward: the window gets wider by the panel's width, on its own monitor, sliding left if
  // the right edge is too close. Too narrow a screen, maximized, snapped or full screen: it docks inside.
  internal static Rect? AiGrowPlan(Rect frame,Rect area,double panel) {
   if(frame.Width+panel>area.Width)return null;
   double left=frame.Right+panel<=area.Right?frame.Left:Math.Max(area.Left,area.Right-frame.Width-panel);
   return new Rect(left,frame.Top,frame.Width+panel,frame.Height);
  }
  [DllImport("user32.dll")] static extern bool IsWindowArranged(IntPtr hwnd);
  bool Snapped() {try {return IsWindowArranged(new WindowInteropHelper(window).Handle);}catch(EntryPointNotFoundException){return false;}}
  [StructLayout(LayoutKind.Sequential)] struct AiMonitorRect {public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)] struct AiMonitorInfo {public int Size;public AiMonitorRect Monitor,Work;public uint Flags;}
  [DllImport("user32.dll",EntryPoint="MonitorFromWindow")] static extern IntPtr AiMonitorFromWindow(IntPtr hwnd,uint flags);
  [DllImport("user32.dll",EntryPoint="GetMonitorInfoW")] static extern bool AiGetMonitorInfo(IntPtr monitor,ref AiMonitorInfo info);
  Rect WorkArea() {
   var info=new AiMonitorInfo {Size=Marshal.SizeOf(typeof(AiMonitorInfo))};
   if(!AiGetMonitorInfo(AiMonitorFromWindow(new WindowInteropHelper(window).Handle,2),ref info))return SystemParameters.WorkArea;
   var source=PresentationSource.FromVisual(window);var toDip=source!=null&&source.CompositionTarget!=null?source.CompositionTarget.TransformFromDevice:Matrix.Identity;
   return new Rect(toDip.Transform(new Point(info.Work.Left,info.Work.Top)),toDip.Transform(new Point(info.Work.Right,info.Work.Bottom)));
  }
  Rect? PlanAiGrowth() {
   if(window.WindowState!=WindowState.Normal||fullscreenApplied||Snapped())return null;
   return AiGrowPlan(new Rect(window.Left,window.Top,window.ActualWidth,window.ActualHeight),WorkArea(),AiWidth);
  }
  void GrowForAi() {
   aiGrown=0;aiShift=0;var plan=PlanAiGrowth();if(plan==null)return;
   aiShift=window.Left-plan.Value.Left;window.Left=plan.Value.Left;window.Width=plan.Value.Width;aiGrown=AiWidth;aiGrownLeft=window.Left;
  }
  // Ask AI about selected text: a small bar over the selection and the same in the right-click menu.
  // Nothing leaves the page unless you pick one; it never shows in private windows, on password
  // fields or on tabs in the Banking cookie jar.
  const string AskAiScript=@"(()=>{if(window.top!==window||!window.chrome||!chrome.webview)return;let t=0,shown=false;
const post=m=>{try{chrome.webview.postMessage(m)}catch(e){}};
const hide=()=>{clearTimeout(t);if(shown){shown=false;post({mica:'aiSelection',text:''});}};
const report=()=>{clearTimeout(t);t=setTimeout(()=>{const s=getSelection();const a=document.activeElement;
 if(!s||s.isCollapsed||a&&a.tagName==='INPUT'&&a.type==='password'){hide();return;}
 const text=String(s).trim();if(text.length<2){hide();return;}let r;try{r=s.getRangeAt(0).getBoundingClientRect();}catch(e){return;}
 shown=true;post({mica:'aiSelection',text:text.slice(0,4000),x:r.left,y:r.top,w:r.width,h:r.height});},160);};
addEventListener('mouseup',e=>{if(e.button===0)report();},true);
addEventListener('keyup',e=>{if(e.key==='Shift'||e.shiftKey||(e.ctrlKey&&(e.key==='a'||e.key==='A')))report();},true);
addEventListener('mousedown',hide,true);addEventListener('scroll',hide,{capture:true,passive:true});addEventListener('blur',hide);})()";
  readonly System.Runtime.CompilerServices.ConditionalWeakTable<CoreWebView2,string> aiScripts=new System.Runtime.CompilerServices.ConditionalWeakTable<CoreWebView2,string>();
  readonly System.Runtime.CompilerServices.ConditionalWeakTable<CoreWebView2,object> aiWatched=new System.Runtime.CompilerServices.ConditionalWeakTable<CoreWebView2,object>();
  // Turned on, the script goes on every open page (now and on each new document); off, it's removed.
  async void WatchAiSelection(CoreWebView2 core,bool on) {
   if(privateMode)return;string id;
   try {
    if(!on){if(aiScripts.TryGetValue(core,out id)){aiScripts.Remove(core);core.RemoveScriptToExecuteOnDocumentCreated(id);}return;}
    object seen;if(!aiWatched.TryGetValue(core,out seen)){aiWatched.Add(core,new object());core.WebMessageReceived+=delegate(object sender,CoreWebView2WebMessageReceivedEventArgs e) {AiSelectionMessage(core,e);};}
    if(aiScripts.TryGetValue(core,out id))return;aiScripts.Add(core,"");
    core.Settings.IsWebMessageEnabled=true;string added=await core.AddScriptToExecuteOnDocumentCreatedAsync(AskAiScript);
    string now;if(aiScripts.TryGetValue(core,out now)&&now==""){aiScripts.Remove(core);aiScripts.Add(core,added);}else core.RemoveScriptToExecuteOnDocumentCreated(added);
    if(Navigation.IsWeb(core.Source))await core.ExecuteScriptAsync(AskAiScript);
   }catch(Exception){} // The page may be closing.
  }
  bool AiAllowedOn(PageEntry tab) {return AiEnabled&&tab!=null&&tab.Jar!="banking"&&Navigation.IsWeb(tab.Url);}
  internal string aiSelectionNote;
  void AiSelectionMessage(CoreWebView2 core,CoreWebView2WebMessageReceivedEventArgs e) {
   aiSelectionNote="message";if(closing||!AiEnabled)return;
   Dictionary<string,object> message;try {message=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(e.WebMessageAsJson);}catch(Exception){return;}
   object kind;if(message==null||!message.TryGetValue("mica",out kind)||(kind as string)!="aiSelection")return;
   var view=CurrentView();if(view==null||LiveCore(view)!=core||!AiAllowedOn(Active)){aiSelectionNote="not this page: "+(view==null?"no view":LiveCore(view)!=core?"other core":"not allowed "+(Active==null?"":Active.Url));HideAiBar();return;}aiSelectionNote="shown";
   object text;message.TryGetValue("text",out text);var selected=text as string;if(string.IsNullOrEmpty(selected)){HideAiBar();return;}
   Func<string,double> number=delegate(string key) {object v;return message.TryGetValue(key,out v)&&v!=null?Convert.ToDouble(v):0;};
   double zoom=1;try {zoom=view.ZoomFactor;}catch(Exception){}
   ShowAiBar(view,selected,new Rect(number("x")*zoom,number("y")*zoom,number("w")*zoom,number("h")*zoom));
  }
  internal Popup aiBar;internal string aiBarText;
  void ShowAiBar(WebView2 view,string text,Rect selection) {
   HideAiBar();aiBarText=text;
   var row=new StackPanel {Orientation=Orientation.Horizontal};
   var card=new Border {CornerRadius=new CornerRadius(10),Padding=new Thickness(4),BorderThickness=new Thickness(1),Child=row,Tag="ai-bar"};card.SetResourceReference(Border.BackgroundProperty,"Panel");card.SetResourceReference(Border.BorderBrushProperty,"Line");
   foreach(var action in AiActions()) {
    string key=action[0];var label=new StackPanel {Orientation=Orientation.Horizontal};
    var icon=DialogText(Glyph(Convert.ToInt32(action[2],16)),11,"Accent");icon.FontFamily=new FontFamily("Segoe MDL2 Assets");icon.VerticalAlignment=VerticalAlignment.Center;icon.Margin=new Thickness(0,0,6,0);label.Children.Add(icon);
    var name=DialogText(action[1],12,"Ink");name.VerticalAlignment=VerticalAlignment.Center;label.Children.Add(name);
    var button=new Button {Content=label,Padding=new Thickness(9,5,9,5),Tag="ai-bar:"+key,Focusable=false};button.Click+=delegate {var chosen=aiBarText;HideAiBar();AskAi(key,chosen);};row.Children.Add(button);
   }
   card.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
   double x=Math.Max(4,Math.Min(view.ActualWidth-card.DesiredSize.Width-4,selection.X+selection.Width/2-card.DesiredSize.Width/2));
   double y=selection.Y-card.DesiredSize.Height-8;if(y<4)y=Math.Min(view.ActualHeight-card.DesiredSize.Height-4,selection.Bottom+8);
   aiBar=new Popup {Child=card,AllowsTransparency=true,PlacementTarget=view,Placement=PlacementMode.Relative,HorizontalOffset=x,VerticalOffset=y,StaysOpen=true,Focusable=false};
   aiBar.IsOpen=true;AnimateArrival(card);
  }
  internal void HideAiBar() {if(aiBar==null)return;aiBar.IsOpen=false;aiBar=null;}
  // key, label, glyph.
  static string[][] AiActions() {return new[]{new[]{"explain","Explain","E946"},new[]{"summarize","Summarize","E8FD"},new[]{"translate","Translate","E8C1"},new[]{"ask","Ask…","E8F2"}};}
  void AddAiMenuItems(ContextMenu menu,PageEntry tab,string selection) {
   if(!AiAllowedOn(tab)||string.IsNullOrWhiteSpace(selection))return;string text=selection.Trim();
   string shown=text.Length>24?text.Substring(0,24).TrimEnd()+"…":text;
   var ask=ContextCommand("Ask AI about “"+shown+"”",Glyph(0xE8F2),"",null);ask.Tag="ai-menu";
   foreach(var action in AiActions()){string key=action[0];ask.Items.Add(ContextCommand(action[1],Glyph(Convert.ToInt32(action[2],16)),"",delegate {AskAi(key,text);}));}
   menu.Items.Insert(0,ContextSeparator());menu.Items.Insert(0,ask);
  }
  internal string AiPrompt(string action,string text) {
   bool vi=Localizer.Vietnamese;
   if(action=="explain")return (vi?"Giải thích đoạn này:":"Explain this:")+"\n\n"+text;
   if(action=="summarize")return (vi?"Tóm tắt đoạn này:":"Summarize this:")+"\n\n"+text;
   if(action=="translate")return (vi?"Dịch đoạn này sang tiếng Việt:":"Translate this into English:")+"\n\n"+text;
   return "“"+text+"”\n\n";
  }
  // The text is typed into the provider's own chat box in the panel, the way you would, and sent with
  // Enter unless you chose Ask… (then the cursor waits after it for your question). If the box can't
  // be found, the text goes to the clipboard instead.
  const string AiFocusScript=@"(()=>{const pick=['#prompt-textarea','div.ProseMirror[contenteditable=""true""]','rich-textarea [contenteditable=""true""]','.ql-editor[contenteditable=""true""]','textarea#userInput','#ask-input','textarea','[contenteditable=""true""]'];
for(const s of pick)for(const e of document.querySelectorAll(s)){const r=e.getBoundingClientRect();if(r.width<40||r.height<10||e.disabled||e.readOnly)continue;e.focus();
 try{if(e.isContentEditable){const g=getSelection();g.selectAllChildren(e);g.collapseToEnd();}else{e.selectionStart=e.selectionEnd=e.value.length;}}catch(x){}
 const a=document.activeElement;return a===e||e.contains(a)?'ok':'nofocus';}
return 'none';})()";
  internal string lastAiResult;
  internal async void AskAi(string action,string text) {
   text=(text??"").Trim();if(text.Length==0||!AiEnabled)return;if(text.Length>4000)text=text.Substring(0,4000);
   string prompt=AiPrompt(action,text);bool send=action!="ask";lastAiResult="waiting";
   SetAiOpen(true);if(!aiOpen){lastAiResult="closed";return;}
   string found="none";
   for(int i=0;i<50&&!closing;i++) {
    var core=aiView==null?null:LiveCore(aiView);
    if(core!=null&&aiView.IsVisible){try {aiView.Focus();found=await core.ExecuteScriptAsync(AiFocusScript);found=found.Trim('"');}catch(Exception){found="none";}if(found=="ok")break;}
    await Task.Delay(300);
   }
   var live=aiView==null?null:LiveCore(aiView);
   if(found!="ok"||live==null) {
    try {Clipboard.SetText(prompt);}catch(Exception){}
    lastAiResult="clipboard";Status("Copied for "+AiName()+". Click its chat box and press Ctrl+V.");return;
   }
   var json=new JavaScriptSerializer();
   try {
    await live.CallDevToolsProtocolMethodAsync("Input.insertText",json.Serialize(new Dictionary<string,object>{{"text",prompt}}));
    if(send) {
     await Task.Delay(200);
     foreach(var type in new[]{"rawKeyDown","char","keyUp"})
      await live.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",json.Serialize(new Dictionary<string,object>{{"type",type},{"key","Enter"},{"code","Enter"},{"windowsVirtualKeyCode",13},{"text",type=="char"?"\r":""}}));
    }
    lastAiResult=send?"sent":"typed";
   }catch(Exception){try {Clipboard.SetText(prompt);}catch(Exception){}lastAiResult="clipboard";Status("Copied for "+AiName()+". Click its chat box and press Ctrl+V.");}
  }
 }
}
