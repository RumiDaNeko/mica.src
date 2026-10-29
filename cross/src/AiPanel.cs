using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Xilium.CefGlue;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common.Handlers;

namespace Mica {
 // The AI side panel, as on Windows (BrowserAiPanel.cs, BrowserClaudeExtension.cs): a chat site (ChatGPT, Claude…)
 // beside the page, off until you turn it on. It opens the provider's own website in your profile, or, with the
 // provider's Chrome extension installed, that extension's side panel page pointed at the tab in front, so it can read
 // and act on the page. Chromium here is Chrome itself (CEF Chrome style), so the extension runs as in Chrome.
 // It is a card of its own beside the page card. When the window has room it grows outward so the page keeps its
 // width; maximized or full screen it docks inside. Closed, it rests after a while.
 sealed partial class BrowserWindow {
  internal static readonly string[][] AiProviders={
   new[]{"chatgpt","ChatGPT","https://chatgpt.com/"},
   new[]{"claude","Claude","https://claude.ai/new"},
   new[]{"gemini","Gemini","https://gemini.google.com/app"},
   new[]{"copilot","Copilot","https://copilot.microsoft.com/"},
   new[]{"perplexity","Perplexity","https://www.perplexity.ai/"}};
  const double AiWidth=400,AiSlideMilliseconds=240;
  static readonly TimeSpan AiFreezeAfter=TimeSpan.FromSeconds(15),AiRestAfter=TimeSpan.FromMinutes(5);
  static AiExtension Claude {get {return BrowserApp.AiExtensionById(BrowserApp.ClaudeExtensionId);}}

  readonly Border aiDock=new Border {IsVisible=false,CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),Margin=new Thickness(7,0,0,0),ClipToBounds=true};
  readonly Grid aiHost=new Grid();
  readonly StackPanel aiCaptionSlot=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
  readonly TextBlock aiTitle=new TextBlock {FontSize=13,FontWeight=FontWeight.SemiBold,VerticalAlignment=VerticalAlignment.Center},aiSite=new TextBlock {FontSize=11,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,1,0,0)};
  readonly TextBlock aiEmpty=new TextBlock {Text="Opening…",FontSize=12,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(24),TextAlignment=TextAlignment.Center};
  Button aiToggle;
  internal bool aiOpen;
  MicaView aiView,aiBoundView;string aiLoaded,aiBoundTab;bool aiBinding;AiExtension aiExtension;
  double aiGrown,aiShift;DateTime aiClosedAt;DispatcherTimer aiRest,aiSlide;Action aiSlideEnd;
  // The window's own buttons (and the line before them): they move into the panel's header while it's open.
  readonly List<Control> captionParts=new List<Control>();StackPanel captionHome;
  // Extensions load when the engine starts: these are the ones running this run.
  readonly HashSet<string> extensionsLoaded=new HashSet<string>();
  bool claudeLoaded {get {return extensionsLoaded.Contains(BrowserApp.ClaudeExtensionId);}}

  bool AiEnabled {get {return state.AiPanel&&!Program.Private;}}
  string AiProviderId() {return state.AiProvider=="custom"&&AiCustomAddress()!=null?"custom":AiProviders.Any(p=>p[0]==state.AiProvider)?state.AiProvider:"chatgpt";}
  string AiCustomAddress() {Uri uri;return !string.IsNullOrEmpty(state.AiCustomUrl)&&Uri.TryCreate(state.AiCustomUrl,UriKind.Absolute,out uri)&&uri.Scheme=="https"?uri.AbsoluteUri:null;}
  string AiAddress() {string id=AiProviderId();if(id=="custom")return AiCustomAddress();return AiProviders.First(p=>p[0]==id)[2];}
  string AiName() {string id=AiProviderId();if(id=="custom"){var uri=new Uri(AiCustomAddress());return uri.Host.StartsWith("www.")?uri.Host.Substring(4):uri.Host;}return AiProviders.First(p=>p[0]==id)[1];}
  AiExtension AiExtensionRunning() {var ai=Program.Private?null:BrowserApp.AiExtensionFor(AiProviderId());return ai!=null&&extensionsLoaded.Contains(ai.Id)?ai:null;}

  void BuildAiPanel(StackPanel toolbarRight,Grid layout) {
   foreach(var ai in BrowserApp.AiExtensions)if(CrossExtensions.IsInstalled(profile,ai.Id))extensionsLoaded.Add(ai.Id);
   // The toolbar button, shown only while the panel is turned on.
   aiToggle=IconButton(Glyphs.Chat,"AI side panel ("+ModShift+"Space)",15);aiToggle.Click+=delegate {ToggleAi();};
   toolbarRight.Children.Insert(0,aiToggle);
   captionHome=toolbarRight;
   Res(Res(aiDock,Border.BackgroundProperty,"Canvas"),Border.BorderBrushProperty,"FrameEdge");
   var grid=new Grid {RowDefinitions=new RowDefinitions("44,*")};aiDock.Child=grid;
   // The header lines up with the page's toolbar: which AI, then choose another, open in a tab and close.
   var head=Res(new Border {BorderThickness=new Thickness(0,0,0,1),Background=Brushes.Transparent},Border.BorderBrushProperty,"Line");grid.Children.Add(head);
   var headGrid=new Grid {Margin=new Thickness(14,0,6,0),VerticalAlignment=VerticalAlignment.Center,ColumnDefinitions=new ColumnDefinitions("*,Auto")};head.Child=headGrid;
   var label=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,ClipToBounds=true};headGrid.Children.Add(label);
   var mark=Glyph(Glyphs.Chat,14,"accent");mark.Margin=new Thickness(0,1,9,0);label.Children.Add(mark);
   Res(aiTitle,TextBlock.ForegroundProperty,"Ink");label.Children.Add(aiTitle);
   Res(aiSite,TextBlock.ForegroundProperty,"Muted");label.Children.Add(aiSite);
   var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(buttons,1);headGrid.Children.Add(buttons);
   Func<string,string,Action,Button> tool=delegate(string glyph,string tip,Action act) {var b=IconButton(glyph,tip,15);b.Click+=delegate {act();};buttons.Children.Add(b);return b;};
   tool(Glyphs.Gear,"Choose the AI",ShowAiSettings);
   tool(Glyphs.OpenTab,"Open in a tab",delegate {string at=aiView!=null&&Navigation.IsWeb(aiView.Address)?aiView.Address:AiAddress();SetAiOpen(false);Navigate(at,true);});
   tool(Glyphs.ClosePane,"Close the panel ("+ModShift+"Space)",delegate {SetAiOpen(false);});
   buttons.Children.Add(aiCaptionSlot);
   var body=new Grid();Grid.SetRow(body,1);grid.Children.Add(body);
   Res(aiEmpty,TextBlock.ForegroundProperty,"Muted");body.Children.Add(aiEmpty);body.Children.Add(aiHost);
   while(layout.ColumnDefinitions.Count<3)layout.ColumnDefinitions.Add(new ColumnDefinition(0,GridUnitType.Pixel));
   Grid.SetColumn(aiDock,2);layout.Children.Add(aiDock);
   PropertyChanged+=delegate(object s,AvaloniaPropertyChangedEventArgs e) {if(e.Property==WindowStateProperty&&aiOpen)Dispatcher.UIThread.Post(FitAiPanel,DispatcherPriority.Background);};
   Closing+=delegate {DisposeAiView();HideAiBar();};
   ApplyAiSetting();RefreshAiHeader();
  }
  // The setting itself: the button and the selection bar's script on open pages.
  void ApplyAiSetting() {
   aiToggle.IsVisible=AiEnabled;
   if(!AiEnabled){if(aiOpen)SetAiOpen(false);DisposeAiView();HideAiBar();}
   foreach(var tab in tabs.Values)if(tab.View!=null&&tab.View.IsBrowserInitialized&&Navigation.IsWeb(tab.Entry.Url))tab.View.ExecuteJavaScript(AiEnabled?AskAiScript:"window.__micaAiOff&&window.__micaAiOff()","",1);
  }
  internal void SetAiEnabled(bool on) {
   state.AiPanel=on;ApplyAiSetting();Save();
   if(aiSettingsSwitch!=null)aiSettingsSwitch.Content=on?"On":"Off";
   Status(on?"AI side panel is on. Press "+ModShift+"Space or the chat button to open it.":"AI side panel is off.");
  }
  void ToggleAi() {
   if(Program.Private){Status("The AI side panel isn't available in private windows.");return;}
   if(!AiEnabled){ShowAiSettings();return;}
   SetAiOpen(!aiOpen);
  }
  internal void SetAiOpen(bool open) {
   if(open==aiOpen||(open&&!AiEnabled))return;aiOpen=open;HideAiBar();
   // The chat loads once the slide is over: starting a page mid-slide would make the slide stutter.
   if(open){if(aiRest!=null)aiRest.Stop();RevealAi(true,delegate {if(aiOpen)EnsureAiView();});}
   else {RevealAi(false,null);aiClosedAt=DateTime.UtcNow;RestAiLater();}
   aiToggle.Classes.Set("on",open);
   Dispatcher.UIThread.Post(PlaceIsland,DispatcherPriority.Loaded);
  }
  // Where the card sits once it has arrived: beside the page, or edge to edge in full screen.
  void LayoutAi() {
   bool full=WindowState==WindowState.FullScreen;bool show=aiOpen&&!islandFullscreen;
   layout.ColumnDefinitions[2].Width=new GridLength(show?AiWidth:0);aiDock.IsVisible=show;
   aiDock.Margin=new Thickness(full?0:7,0,0,0);aiDock.CornerRadius=new CornerRadius(full?0:12);aiDock.BorderThickness=full?new Thickness(1,0,0,0):new Thickness(1);
   MoveCaptionButtons(show);if(show){RefreshAiHeader();QueueCorners();}
  }
  void MoveCaptionButtons(bool intoPanel) {
   if(captionParts.Count==0)return;bool moved=aiCaptionSlot.Children.Count>0;if(moved==intoPanel)return;
   foreach(var part in captionParts){((Panel)part.Parent).Children.Remove(part);(intoPanel?aiCaptionSlot.Children:captionHome.Children).Add(part);}
  }
  // Opening and closing slide: the window grows outward with the panel (or the page makes room for it when the
  // window can't grow) and shrinks back the same way. The card keeps its full width while it slides, and the chat
  // waits out of sight until it has arrived, so the page inside isn't laid out on every frame.
  void FinishAiSlide() {var end=aiSlideEnd;aiSlideEnd=null;if(end!=null)end();}
  void RevealAi(bool open,Action done) {
   FinishAiSlide();var column=layout.ColumnDefinitions[2];double from=column.Width.Value;
   double width=Width;int left=Position.X;double shift=0,grow=0;double scale=RenderScaling;
   if(open){aiGrown=0;aiShift=0;var plan=PlanAiGrowth();if(plan!=null){grow=AiWidth;shift=(left-plan.Value)/scale;aiShift=shift;aiGrown=AiWidth;}}
   else {if(aiGrown>0&&WindowState==WindowState.Normal){grow=-Math.Min(aiGrown,width-MinWidth);shift=-aiShift;}aiGrown=0;aiShift=0;}
   if(WindowState!=WindowState.Normal){grow=0;shift=0;}
   LayoutAi();bool show=aiOpen&&!islandFullscreen;double to=show?AiWidth:0;
   Action<double> place=delegate(double eased) {
    column.Width=new GridLength(from+(to-from)*eased);
    if(grow!=0||shift!=0){Width=width+grow*eased;Position=new PixelPoint((int)Math.Round(left-shift*scale*eased),Position.Y);}
   };
   aiHost.IsVisible=false;
   if(Math.Abs(to-from)<1){place(1);aiHost.IsVisible=true;if(done!=null)done();return;}
   aiDock.IsVisible=true;aiDock.Width=AiWidth-aiDock.Margin.Left;aiDock.HorizontalAlignment=HorizontalAlignment.Left;
   var clock=Stopwatch.StartNew();
   Action end=null;end=delegate {
    if(aiSlideEnd==end)aiSlideEnd=null;if(aiSlide!=null)aiSlide.Stop();place(1);
    aiDock.ClearValue(WidthProperty);aiDock.ClearValue(HorizontalAlignmentProperty);
    if(!show)aiDock.IsVisible=false;else {aiHost.IsVisible=true;QueueCorners();}
    if(done!=null)done();
   };
   aiSlideEnd=end;place(0);
   if(aiSlide==null){aiSlide=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(15)};aiSlide.Tick+=delegate {var step=aiSlideStep;if(step!=null)step();};}
   aiSlideStep=delegate {double t=Math.Min(1,clock.Elapsed.TotalMilliseconds/AiSlideMilliseconds);if(t<1)place(1-Math.Pow(1-t,3));else end();};
   aiSlide.Start();
  }
  Action aiSlideStep;
  // Growing outward: the window gets wider by the panel's width on its own screen, sliding left if the right edge is
  // too close. Too narrow a screen, maximized or full screen: it docks inside. Returns the new left edge in pixels.
  int? PlanAiGrowth() {
   if(WindowState!=WindowState.Normal)return null;
   double scale=RenderScaling;int width=(int)Math.Round(Bounds.Width*scale),panel=(int)Math.Round(AiWidth*scale);
   var screen=Screens.ScreenFromWindow(this);if(screen==null)return Position.X; // Off-screen (tests): grow in place.
   var area=screen.WorkingArea;if(width+panel>area.Width)return null;
   return Position.X+width+panel<=area.Right?Position.X:Math.Max(area.X,area.Right-width-panel);
  }
  // Going maximized or back while the panel is open: grow outward again when there's room.
  void FitAiPanel() {
   if(!aiOpen)return;
   if(WindowState==WindowState.Normal&&aiGrown==0){var plan=PlanAiGrowth();if(plan!=null){aiShift=(Position.X-plan.Value)/RenderScaling;Width+=AiWidth;Position=new PixelPoint(plan.Value,Position.Y);aiGrown=AiWidth;}}
   else if(WindowState!=WindowState.Normal)aiGrown=0;
   LayoutAi();
  }

  async void EnsureAiView() {
   aiExtension=AiExtensionRunning();RefreshAiHeader();
   string address=aiExtension!=null?aiExtension.PanelUrl:AiAddress();
   if(aiView!=null) {
    if(aiView.IsVisible==false)aiView.IsVisible=true;
    if(aiLoaded!=address){aiLoaded=address;aiEmpty.Text="Opening "+AiName()+"…";aiView.Address=address;}
    else BindClaudePanel(false);
    return;
   }
   aiEmpty.Text="Opening "+AiName()+"…";
   var view=new MicaView {Address=address};aiView=view;aiLoaded=address;
   view.LifeSpanHandler=new PopupsToTabs(this);
   view.PermissionHandler=new SitePermissions(this,()=>aiView==view&&aiOpen);view.CommandHandler=ChromiumUi.CommandHandler;view.DownloadHandler=downloadHandler;
   view.LoadStart+=delegate {Dispatcher.UIThread.Post(delegate {if(aiView==view)aiEmpty.Text="";});};
   view.LoadEnd+=delegate(object s,Xilium.CefGlue.Common.Events.LoadEndEventArgs e) {if(e.Frame.IsMain)Dispatcher.UIThread.Post(delegate {BindClaudePanel(false);});};
   view.LoadError+=delegate(object s,Xilium.CefGlue.Common.Events.LoadErrorEventArgs e) {
    if(!e.Frame.IsMain||e.ErrorCode==CefErrorCode.Aborted)return;
    Dispatcher.UIThread.Post(delegate {if(aiView==view)aiEmpty.Text=AiName()+" couldn't load. Check your connection, or open it in a tab.";});
   };
   aiHost.Children.Add(view);QueueCorners();
   await Task.CompletedTask;
  }
  internal void DisposeAiView() {
   if(aiRest!=null)aiRest.Stop();var view=aiView;if(view==null)return;aiView=null;aiLoaded=null;aiBoundTab=null;aiBoundView=null;
   aiHost.Children.Remove(view);try {view.Dispose();}catch(Exception){}
   aiEmpty.Text="Opening…";
  }
  // Closed: hidden at once (Chromium throttles a hidden page), gone after five minutes.
  void RestAiLater() {
   if(aiView==null)return;
   if(aiRest==null){aiRest=new DispatcherTimer {Interval=AiFreezeAfter};aiRest.Tick+=delegate {AiRestTick();};}
   aiRest.Stop();aiRest.Interval=AiFreezeAfter;aiRest.Start();
  }
  void AiRestTick() {
   if(aiOpen||aiView==null){aiRest.Stop();return;}
   if(DateTime.UtcNow-aiClosedAt>=AiRestAfter){DisposeAiView();return;}
   aiView.IsVisible=false;aiRest.Interval=TimeSpan.FromSeconds(30);
  }

  // The chosen AI's name and site (or its extension), and its pill filled in with the accent.
  void RefreshAiHeader() {
   string id=AiProviderId();aiTitle.Text=AiName();Uri uri;
   var running=aiExtension!=null&&aiExtension.Provider==id?aiExtension:null;
   aiSite.Text=running!=null?running.Name:id!="custom"&&Uri.TryCreate(AiAddress(),UriKind.Absolute,out uri)?(uri.Host.StartsWith("www.")?uri.Host.Substring(4):uri.Host):"";
   if(aiPills==null)return;
   var accent=this.FindResource("Accent") as ISolidColorBrush;Color tint=accent!=null?accent.Color:Colors.SlateBlue;
   foreach(var pill in aiPills.Children.OfType<Button>()) {
    bool on=(string)pill.Tag=="ai-provider:"+id;var surface=(Border)pill.Content;var text=(TextBlock)surface.Child;
    if(on){surface.Background=new SolidColorBrush(Color.FromArgb(0x26,tint.R,tint.G,tint.B));surface.BorderBrush=new SolidColorBrush(Color.FromArgb(0x70,tint.R,tint.G,tint.B));Res(text,TextBlock.ForegroundProperty,"Accent");}
    else {surface.Background=Brushes.Transparent;Res(surface,Border.BorderBrushProperty,"Line");Res(text,TextBlock.ForegroundProperty,"Muted");}
    if((string)pill.Tag=="ai-provider:custom")text.Text=on?AiName():"Your own…";
   }
  }
  WrapPanel aiPills;Button aiSettingsSwitch,aiExtensionGet,aiExtensionRemove,aiRestart;TextBlock aiExtensionNote;
  Button AiPill(string text,string tag,Action act) {
   var label=new TextBlock {Text=text,FontSize=12};
   var surface=new Border {BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(9,3,9,4),Child=label};
   var pill=new Button {Content=surface,Padding=new Thickness(0),Margin=new Thickness(0,0,5,6),Background=Brushes.Transparent,Tag=tag,Cursor=new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)};pill.Classes.Add("pill");
   pill.PointerEntered+=delegate {Res(surface,Border.BorderBrushProperty,"Accent");};pill.PointerExited+=delegate {RefreshAiHeader();};
   pill.Click+=delegate {act();};return pill;
  }
  // The panel's settings: on or off, and which chat it opens, every choice in sight, one click each.
  internal void ShowAiSettings() {
   StackPanel body,actions;var dialog=MicaDialog("AI side panel",460,out body,out actions);
   dialog.Closed+=delegate {aiPills=null;aiSettingsSwitch=null;aiExtensionNote=null;};
   body.Children.Add(DialogText("A chat site beside the page, opened with your profile. Nothing on a page goes to it unless you ask.",12,"Muted"));
   var row=new Grid {Margin=new Thickness(12,9),ColumnDefinitions=new ColumnDefinitions("*,Auto")};
   var name=DialogText("Show the panel",12,"Ink");name.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(name);
   aiSettingsSwitch=DialogButton(state.AiPanel?"On":"Off",false);aiSettingsSwitch.Padding=new Thickness(14,5);Grid.SetColumn(aiSettingsSwitch,1);row.Children.Add(aiSettingsSwitch);
   aiSettingsSwitch.Click+=delegate {SetAiEnabled(!state.AiPanel);};
   body.Children.Add(Card(row));
   SectionLabel(body,"CHAT");
   aiPills=new WrapPanel {Margin=new Thickness(0,2,0,0)};body.Children.Add(aiPills);
   foreach(var p in AiProviders){string id=p[0];aiPills.Children.Add(AiPill(p[1],"ai-provider:"+id,delegate {ChooseAiProvider(id,null);}));}
   aiPills.Children.Add(AiPill("Your own…","ai-provider:custom",ShowAiCustom));
   // An AI with its own extension can use it instead of the site, so it can work on the page too.
   aiExtensionNote=DialogText("",11,"Muted");aiExtensionNote.Margin=new Thickness(2,10,2,0);body.Children.Add(aiExtensionNote);
   var extensionActions=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,6,0,0)};body.Children.Add(extensionActions);
   aiExtensionGet=DialogButton("",false);aiExtensionGet.Padding=new Thickness(12,6);aiExtensionGet.Margin=new Thickness(0,0,8,0);Res(aiExtensionGet,BackgroundProperty,"Hover");extensionActions.Children.Add(aiExtensionGet);
   aiExtensionGet.Click+=async delegate {await GetAiExtension();};
   aiExtensionRemove=DialogButton("Remove the extension",false);aiExtensionRemove.Padding=new Thickness(12,6);aiExtensionRemove.Margin=new Thickness(0,0,8,0);Res(aiExtensionRemove,BackgroundProperty,"Hover");extensionActions.Children.Add(aiExtensionRemove);
   aiExtensionRemove.Click+=delegate {RemoveAiExtension();};
   aiRestart=DialogButton("Restart Mica",false);aiRestart.Padding=new Thickness(12,6);Res(aiRestart,BackgroundProperty,"Hover");extensionActions.Children.Add(aiRestart);
   aiRestart.Click+=delegate {Restart();};
   RefreshAiExtensionNote();
   var keys=DialogText(ModShift+"Space opens and closes it.",11,"Muted");keys.Margin=new Thickness(2,10,2,0);body.Children.Add(keys);
   var done=DialogButton("Done",true);done.Click+=delegate {dialog.IsOpen=false;};actions.Children.Add(done);
   RefreshAiHeader();dialog.IsOpen=true;
  }
  void RefreshAiExtensionNote() {
   if(aiExtensionNote==null)return;
   var ai=BrowserApp.AiExtensionFor(AiProviderId());
   if(ai==null||Program.Private){aiExtensionNote.Text="";aiExtensionNote.IsVisible=aiExtensionGet.IsVisible=aiExtensionRemove.IsVisible=aiRestart.IsVisible=false;return;}
   bool has=CrossExtensions.IsInstalled(profile,ai.Id),running=extensionsLoaded.Contains(ai.Id),claude=ai.Provider=="claude";
   aiExtensionNote.IsVisible=true;
   aiExtensionNote.Text=has&&!running?ai.Name+" is installed. Restart Mica to start it."
    :claude?(has?"Claude opens in your Claude for Chrome extension, so it can read and act on the page.":"With Claude for Chrome installed (paid Claude plans), Claude can read and act on the page from this panel.")
    :(has?"ChatGPT opens in your ChatGPT for Chrome extension. With the ChatGPT desktop app running, it can read and act on the page.":"With ChatGPT for Chrome and the ChatGPT desktop app, ChatGPT can read and act on the page from this panel.");
   aiExtensionGet.Content=claude?"Get Claude for Chrome":"Get ChatGPT for Chrome";
   aiExtensionGet.IsVisible=!has;aiExtensionRemove.IsVisible=has;aiRestart.IsVisible=has!=running;
  }
  internal void ChooseAiProvider(string id,string custom) {
   state.AiProvider=id;if(custom!=null)state.AiCustomUrl=custom;
   RefreshAiHeader();RefreshAiExtensionNote();
   if(aiOpen)EnsureAiView();else DisposeAiView();Save();
  }
  void ShowAiCustom() {
   StackPanel body,actions;var dialog=MicaDialog("Your own AI address",440,out body,out actions);
   body.Children.Add(DialogText("Any chat site you like, opened in the panel with your profile. It must start with https://.",12,"Muted"));
   SectionLabel(body,"ADDRESS");
   var box=new TextBox {Padding=new Thickness(8,6),Text=state.AiCustomUrl??"https://"};box.Classes.Add("field");body.Children.Add(box);
   var problem=DialogText("",11,"Muted");problem.Margin=new Thickness(0,8,0,0);body.Children.Add(problem);
   var use=DialogButton("Use it",true);actions.Children.Add(use);
   use.Click+=delegate {Uri uri;string text=(box.Text??"").Trim();if(!Uri.TryCreate(text,UriKind.Absolute,out uri)||uri.Scheme!="https"||string.IsNullOrEmpty(uri.Host)){problem.Text="Enter a full address starting with https://";box.Focus();return;}dialog.IsOpen=false;ChooseAiProvider("custom",uri.AbsoluteUri);};
   dialog.IsOpen=true;box.Focus();box.SelectAll();
  }
  async Task GetAiExtension() {
   var ai=BrowserApp.AiExtensionFor(AiProviderId());if(ai==null)return;
   aiExtensionGet.IsEnabled=false;aiExtensionNote.Text="Getting "+ai.Name+" from the Chrome Web Store…";
   try {await CrossExtensions.Install(profile,ai.Id,CefRuntime.ChromeVersion);} catch(Exception e) {aiExtensionNote.Text="Couldn't get "+ai.Name+": "+e.Message;aiExtensionGet.IsEnabled=true;return;}
   aiExtensionGet.IsEnabled=true;RefreshAiExtensionNote();
  }
  void RemoveAiExtension() {
   var ai=BrowserApp.AiExtensionFor(AiProviderId());if(ai==null)return;
   try {CrossExtensions.Remove(profile,ai.Id);} catch(Exception e) {Status("Couldn't remove the extension: "+e.Message);return;}
   extensionsLoaded.Remove(ai.Id);
   if(aiView!=null&&ai.IsPanel(aiLoaded)){DisposeAiView();if(aiOpen)EnsureAiView();}
   RefreshAiExtensionNote();
  }
  // Extensions load at start, so a new one needs a restart: the same window comes back with its session.
  void Restart() {
   Save();
   var args=Environment.GetCommandLineArgs().Skip(1).Where(a=>a!="--self-test").ToArray();
   var start=new ProcessStartInfo(Environment.ProcessPath) {UseShellExecute=false};
   foreach(var a in args)start.ArgumentList.Add(a);
   Process.Start(start);Close();
  }

  // Points Claude's panel at the tab in front. Claude's panel works for one tab (sidepanel.html?tabId=N), by the
  // extension's own tab ids; the panel page asks chrome.tabs which tab shows the active page. It follows the page, not
  // just Mica's tab: a tab that rested and woke has a new page under it, with a new id (as on Windows, claudeBoundView).
  internal async void BindClaudePanel(bool force) {
   var tab=Active;
   if(aiExtension==null||!aiExtension.FollowsTab||aiBinding||aiView==null||!aiOpen||tab==null||tab.View==null||!Navigation.IsWeb(tab.Entry.Url))return;
   if(!force&&aiBoundTab==tab.Entry.Id&&aiBoundView==tab.View&&aiExtension.IsPanel(aiView.Address)&&aiView.Address.Contains("tabId="))return;
   aiBinding=true;
   try {
    string url=new JavaScriptSerializer().Serialize(tab.View.Address);
    string id=await EvalAsync(aiView,"(async()=>{const ts=await chrome.tabs.query({});const t=ts.find(x=>x.url==="+url+");return t?String(t.id):'';})()",10);
    if(string.IsNullOrEmpty(id)||aiView==null)return;
    aiBoundTab=tab.Entry.Id;aiBoundView=tab.View;
    string want=aiExtension.PanelUrl+"?tabId="+id;
    if(!string.Equals(aiView.Address,want,StringComparison.OrdinalIgnoreCase))aiView.Address=want;
   } catch(Exception e) {Trace("bind failed: "+e.Message);}
   finally {aiBinding=false;}
  }

  // Ask AI about selected text: a small bar over the selection. Nothing leaves the page unless you pick one; it never
  // shows in private windows or on password fields. The page tells Mica through a console line, as the media bridge does.
  internal const string AskAiScript=@"(()=>{if(window.top!==window||window.__micaAi)return;window.__micaAi=1;let t=0,shown=false,on=true;
const post=m=>{try{console.debug('__mica_ai:'+JSON.stringify(m))}catch(e){}};
const hide=()=>{clearTimeout(t);if(shown){shown=false;post({text:''});}};
const report=()=>{if(!on)return;clearTimeout(t);t=setTimeout(()=>{const s=getSelection();const a=document.activeElement;
 if(!s||s.isCollapsed||a&&a.tagName==='INPUT'&&a.type==='password'){hide();return;}
 const text=String(s).trim();if(text.length<2){hide();return;}let r;try{r=s.getRangeAt(0).getBoundingClientRect();}catch(e){return;}
 shown=true;post({text:text.slice(0,4000),x:r.left*devicePixelRatio/(window.outerWidth/innerWidth||1)/devicePixelRatio,y:r.top,w:r.width,h:r.height,z:outerWidth/innerWidth});},160);};
addEventListener('mouseup',e=>{if(e.button===0)report();},true);
addEventListener('keyup',e=>{if(e.key==='Shift'||e.shiftKey||(e.ctrlKey&&(e.key==='a'||e.key==='A')))report();},true);
addEventListener('mousedown',hide,true);addEventListener('scroll',hide,{capture:true,passive:true});addEventListener('blur',hide);
window.__micaAiOff=()=>{on=false;hide();};})()";
  internal Popup aiBar;internal string aiBarText;
  internal void AiSelection(CrossTab tab,string json) {
   if(!AiEnabled||tab!=Active||tab.View==null)return;
   Dictionary<string,object> message;try {message=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);}catch(Exception){return;}
   object text;message.TryGetValue("text",out text);var selected=text as string;if(string.IsNullOrEmpty(selected)){HideAiBar();return;}
   Func<string,double> number=delegate(string key) {object v;return message.TryGetValue(key,out v)&&v!=null?Convert.ToDouble(v):0;};
   double zoom=tab.Zoom>0?tab.Zoom:1;
   ShowAiBar(tab.View,selected,new Rect(number("x")*zoom,number("y")*zoom,number("w")*zoom,number("h")*zoom));
  }
  // key, label, glyph.
  static string[][] AiActions() {return new[]{new[]{"explain","Explain",Glyphs.Info},new[]{"summarize","Summarize",Glyphs.Reader},new[]{"translate","Translate",Glyphs.Globe},new[]{"ask","Ask…",Glyphs.Chat}};}
  void ShowAiBar(Control view,string text,Rect selection) {
   HideAiBar();aiBarText=text;
   var row=new StackPanel {Orientation=Orientation.Horizontal};
   var card=Res(Res(new Border {CornerRadius=new CornerRadius(10),Padding=new Thickness(4),BorderThickness=new Thickness(1),Child=row},Border.BackgroundProperty,"Panel"),Border.BorderBrushProperty,"Line");
   foreach(var action in AiActions()) {
    string key=action[0];var label=new StackPanel {Orientation=Orientation.Horizontal};
    var icon=Glyph(action[2],11,"accent");icon.Margin=new Thickness(0,0,6,0);label.Children.Add(icon);
    label.Children.Add(new TextBlock {Text=action[1],FontSize=12,VerticalAlignment=VerticalAlignment.Center});
    var button=new Button {Content=label,Padding=new Thickness(9,5),Focusable=false};button.Classes.Add("flat");
    button.Click+=delegate {var chosen=aiBarText;HideAiBar();AskAi(key,chosen);};row.Children.Add(button);
   }
   card.Measure(Size.Infinity);
   double x=Math.Max(4,Math.Min(view.Bounds.Width-card.DesiredSize.Width-4,selection.X+selection.Width/2-card.DesiredSize.Width/2));
   double y=selection.Y-card.DesiredSize.Height-8;if(y<4)y=Math.Min(view.Bounds.Height-card.DesiredSize.Height-4,selection.Bottom+8);
   aiBar=new Popup {Child=card,PlacementTarget=view,Placement=PlacementMode.TopEdgeAlignedLeft,HorizontalOffset=x,VerticalOffset=y+card.DesiredSize.Height,IsLightDismissEnabled=false};
   root.Children.Add(aiBar);aiBar.IsOpen=true;
  }
  internal void HideAiBar() {if(aiBar==null)return;aiBar.IsOpen=false;root.Children.Remove(aiBar);aiBar=null;}
  internal string AiPrompt(string action,string text) {
   bool vi=Localizer.Vietnamese;
   if(action=="explain")return (vi?"Giải thích đoạn này:":"Explain this:")+"\n\n"+text;
   if(action=="summarize")return (vi?"Tóm tắt đoạn này:":"Summarize this:")+"\n\n"+text;
   if(action=="translate")return (vi?"Dịch đoạn này sang tiếng Việt:":"Translate this into English:")+"\n\n"+text;
   return "“"+text+"”\n\n";
  }
  // The text is typed into the provider's own chat box in the panel, the way you would, and sent with Enter unless
  // you chose Ask… (then the cursor waits after it for your question). If the box can't be found, it goes to the clipboard.
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
   for(int i=0;i<50&&aiOpen;i++) {
    if(aiView!=null&&aiView.IsBrowserInitialized&&aiHost.IsVisible){found=await EvalAsync(aiView,"Promise.resolve("+AiFocusScript+")",3)??"none";if(found=="ok")break;}
    await Task.Delay(300);
   }
   var host=aiView==null?null:aiView.Host;
   if(found!="ok"||host==null) {
    try {await Clipboard.SetTextAsync(prompt);}catch(Exception){}
    lastAiResult="clipboard";Status("Copied for "+AiName()+". Click its chat box and press "+Mod+"V.");return;
   }
   var insert=CefDictionaryValue.Create();insert.SetString("text",prompt);host.ExecuteDevToolsMethod(0,"Input.insertText",insert);
   if(send) {
    await Task.Delay(200);
    foreach(var type in new[]{"rawKeyDown","char","keyUp"}) {
     var key=CefDictionaryValue.Create();key.SetString("type",type);key.SetString("key","Enter");key.SetString("code","Enter");key.SetInt("windowsVirtualKeyCode",13);key.SetString("text",type=="char"?"\r":"");
     host.ExecuteDevToolsMethod(0,"Input.dispatchKeyEvent",key);
    }
   }
   lastAiResult=send?"sent":"typed";
  }

  // CefGlue's EvaluateJavaScript returns at once and can't wait for a promise, so async code leaves its result on
  // the page under a one-off name, which is read back until it is there.
  internal static async Task<string> EvalAsync(AvaloniaCefBrowser view,string expression,int seconds) {
   string key="__mica"+Guid.NewGuid().ToString("N");
   view.ExecuteJavaScript("(async()=>{let r;try{r=String(await ("+expression+"));}catch(e){r='error: '+e;}self['"+key+"']=r;})()","",1);
   for(int i=0;i<seconds*10;i++) {
    await Task.Delay(100);
    string value;
    try {value=await view.EvaluateJavaScript<string>("return self['"+key+"']===undefined?null:self['"+key+"']",(string)null,"about:blank",1,TimeSpan.FromSeconds(5));}
    catch(Exception) {continue;}
    if(value!=null){view.ExecuteJavaScript("delete self['"+key+"']","",1);return value;}
   }
   return null;
  }

  // Pages and extensions asking for a new window (target=_blank, window.open, chrome.tabs.create) get a Mica tab.
  // The side-panel shim sends Claude's tab requests to open.mica.invalid, as on Windows.
  sealed class PopupsToTabs : LifeSpanHandler {
   readonly BrowserWindow window;
   public PopupsToTabs(BrowserWindow w) {window=w;}
   protected override bool OnBeforePopup(CefBrowser browser,CefFrame frame,int popupId,string targetUrl,string targetFrameName,CefWindowOpenDisposition targetDisposition,bool userGesture,CefPopupFeatures popupFeatures,CefWindowInfo windowInfo,ref CefClient client,CefBrowserSettings settings,ref CefDictionaryValue extraInfo,ref bool noJavascriptAccess) {
    string url=targetUrl;bool background=targetDisposition==CefWindowOpenDisposition.NewBackgroundTab;
    Dispatcher.UIThread.Post(delegate {window.OpenFromPage(url,background);});
    return true;
   }
   protected override bool DoClose(CefBrowser browser) {int id=browser.Identifier;Dispatcher.UIThread.Post(delegate {window.ClosedByEngine(id);});return false;}
  }
  internal void OpenFromPage(string url,bool background) {
   Trace("popup "+url);
   if((url??"").StartsWith("https://open.mica.invalid/",StringComparison.OrdinalIgnoreCase)) {
    Uri uri;if(!Uri.TryCreate(url,UriKind.Absolute,out uri))return;
    // Claude asks for one of its tabs to come forward (a page out of sight can't be captured or clicked).
    var focus=System.Text.RegularExpressions.Regex.Match(uri.Query,"(?:^|[?&])url=([^&]+)");
    if(uri.Query.Contains("focus=")){if(focus.Success)FrontTabAt(Uri.UnescapeDataString(focus.Groups[1].Value));return;}
    url=Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
    background=uri.Query.Contains("active=0");
   }
   bool home=url=="mica://home";
   if(!home&&!Navigation.IsWeb(url))return;
   string keep=state.ActiveId;var tab=state.AddTab(url);if(!home)tab.Title=new Uri(url).Host;RenderTabs();
   if(background){Show(tab.Id);Show(keep);}else Show(tab.Id);
   Save();
  }
  void FrontTabAt(string address) {
   var found=tabs.Values.FirstOrDefault(t=>t.View!=null&&string.Equals(t.View.Address,address,StringComparison.Ordinal))??tabs.Values.FirstOrDefault(t=>t.Entry.Url==Navigation.FromEngine(address));
   if(found!=null&&found!=Active)Show(found.Entry.Id);
  }
  // The engine closed a page's browser (an extension's chrome.tabs.remove or windows.remove, or the page itself).
  internal void ClosedByEngine(int browser) {
   var gone=tabs.Values.FirstOrDefault(t=>t.View!=null&&t.View.Host!=null&&t.View.Host.GetBrowser().Identifier==browser);
   if(gone!=null)CloseTab(gone.Entry.Id);
  }
 }

 // CEF can't add a script to every page the way WebView2 does, so the panel's own page loads Mica's tab shim from a
 // file: chrome.tabs.getCurrent says "no tab" there, and the panel page is kept out of the tabs Claude sees (the same
 // TabsShim as Windows). Unpacked extensions aren't content-verified, so editing their files is allowed.
 static class PanelPagePatch {
  const string FileName="mica-panel.js";
  internal static bool Apply(string folder,AiExtension ai) {
   bool changed=ClaudeSidePanelPatch.Apply(folder,ai.Pages);
   var utf8=new UTF8Encoding(false);
   string pages=new JavaScriptSerializer().Serialize(ai.Pages);
   string script="// Added by Mica: this page is hosted in Mica's AI panel.\nif(self.chrome&&chrome.tabs){self.__micaPanelPages="+pages+";chrome.tabs.getCurrent=function(callback){if(typeof callback==='function'){callback(undefined);return;}return Promise.resolve(undefined);};\n"+ClaudeSidePanelPatch.TabsShim+"}\n";
   string shim=Path.Combine(folder,FileName);
   if(!File.Exists(shim)||File.ReadAllText(shim)!=script){File.WriteAllText(shim,script,utf8);changed=true;}
   string tag="<script src=\"/"+FileName+"\"></script>";
   foreach(var page in ai.Pages) {
    string path=Path.Combine(folder,page);if(!File.Exists(path))continue;
    string html=File.ReadAllText(path,Encoding.UTF8);if(html.Contains(tag))continue;
    int head=html.IndexOf("<head>",StringComparison.OrdinalIgnoreCase);
    html=head>=0?html.Insert(head+6,tag):tag+html;
    File.WriteAllText(path,html,utf8);changed=true;
   }
   return changed;
  }
 }
}
