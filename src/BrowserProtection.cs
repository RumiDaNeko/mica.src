using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // HTTPS by default, tracker blocking, clearing browsing data, and recovering
 // when the engine's main process goes away.
 partial class BrowserApp {
  // Hosts the user chose to visit over plain HTTP (or that bounced us back to it) this session.
  static readonly HashSet<string> httpAllowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  // Tab id -> the upgrade in flight. Only the https:// navigation Mica started counts:
  // the cancelled http:// one reports its own (not always "cancelled") error.
  sealed class HttpsUpgrade {public string Http,Https;public ulong Id;public bool Started;}
  readonly Dictionary<string,HttpsUpgrade> httpsUpgrades=new Dictionary<string,HttpsUpgrade>();
  internal static bool KeepsHttp(Uri uri) {
   string host=uri.Host.Trim('[',']');
   if(!uri.IsDefaultPort||!host.Contains('.')||host.Equals("localhost",StringComparison.OrdinalIgnoreCase))return true;
   if(Regex.IsMatch(host,@"\.(localhost|local|test|internal|lan|home\.arpa)$",RegexOptions.IgnoreCase))return true;
   IPAddress address;if(!IPAddress.TryParse(host,out address))return false;
   if(IPAddress.IsLoopback(address)||address.IsIPv6LinkLocal||address.IsIPv6SiteLocal)return true;
   var b=address.GetAddressBytes();
   return b.Length==4&&(b[0]==10||(b[0]==172&&b[1]>=16&&b[1]<=31)||(b[0]==192&&b[1]==168)||(b[0]==169&&b[1]==254)||(b[0]==100&&b[1]>=64&&b[1]<=127));
  }
  // Exceptions apply per site (example.co.uk covers www.example.co.uk), like Chrome's HTTPS-First.
  internal static string SiteOf(string host) {
   var labels=host.ToLowerInvariant().Trim('.').Split('.');IPAddress ip;if(labels.Length<=2||IPAddress.TryParse(host,out ip))return host.ToLowerInvariant();
   string second=labels[labels.Length-2];bool shared=labels[labels.Length-1].Length==2&&new[]{"co","com","net","org","gov","edu","ac","or","ne","go"}.Contains(second);
   return string.Join(".",labels.Skip(labels.Length-(shared?3:2)));
  }
  internal static string HttpsAddress(Uri uri) {return new UriBuilder(uri) {Scheme="https",Port=-1}.Uri.AbsoluteUri;}
  // Runs first in a tab's NavigationStarting. Returns true when it replaced the navigation.
  bool UpgradeToHttps(string id,CoreWebView2 core,CoreWebView2NavigationStartingEventArgs e) {
   HttpsUpgrade pending;httpsUpgrades.TryGetValue(id,out pending);
   if(pending!=null&&!pending.Started&&e.Uri==pending.Https){pending.Started=true;pending.Id=e.NavigationId;return false;}
   Uri uri;if(!Uri.TryCreate(e.Uri,UriKind.Absolute,out uri)||uri.Scheme!=Uri.UriSchemeHttp||KeepsHttp(uri)||httpAllowed.Contains(SiteOf(uri.Host)))return false;
   if(pending!=null&&pending.Started&&string.Equals(new Uri(pending.Http).Host,uri.Host,StringComparison.OrdinalIgnoreCase)) {
    // The secure address redirected back to http: the site has no working HTTPS.
    httpsUpgrades.Remove(id);httpAllowed.Add(SiteOf(uri.Host));Status(uri.Host+" doesn't offer a secure connection. This page isn't private.");return false;
   }
   e.Cancel=true;string https=HttpsAddress(uri);httpsUpgrades[id]=new HttpsUpgrade {Http=e.Uri,Https=https};
   window.Dispatcher.BeginInvoke(new Action(delegate {HttpsUpgrade current;if(!closing&&httpsUpgrades.TryGetValue(id,out current)&&current.Https==https)core.Navigate(https);}));
   return true;
  }
  // Runs first in NavigationCompleted: returns the http:// address to offer when the upgrade failed.
  string FinishHttpsUpgrade(string id,CoreWebView2NavigationCompletedEventArgs e) {
   HttpsUpgrade pending;if(!httpsUpgrades.TryGetValue(id,out pending)||!pending.Started||pending.Id!=e.NavigationId)return null;
   httpsUpgrades.Remove(id);
   return e.IsSuccess||e.WebErrorStatus==CoreWebView2WebErrorStatus.OperationCanceled?null:pending.Http;
  }
  void ShowInsecureOffer(string url) {
   var uri=new Uri(url);
   ShowError(uri.Host+" doesn't support a secure connection (HTTPS). Anyone on your network could see or change what you send there.");
   var button=Find<Button>("InsecureButton");button.Tag=url;button.Visibility=Visibility.Visible;
   Status("Mica tried the secure version of "+uri.Host+" first.");
  }
  void ContinueInsecure() {
   var button=Find<Button>("InsecureButton");var url=button.Tag as string;if(url==null)return;
   httpAllowed.Add(SiteOf(new Uri(url).Host));button.Visibility=Visibility.Collapsed;
   var view=views.ContainsKey(state.ActiveId??"")?views[state.ActiveId]:null;
   if(view!=null&&view.CoreWebView2!=null){Find<Border>("ErrorPanel").Visibility=Visibility.Collapsed;Find<Grid>("WebHost").Visibility=Visibility.Visible;view.CoreWebView2.Navigate(url);}
   else Navigate(url,false);
  }

  // A certificate that doesn't check out stops the page. The user can still go on to that host for the rest of
  // the session (a router's or printer's own page, a site whose certificate just expired), never for a revoked one.
  // Private windows keep their own list: the engine remembers the choice per profile, too.
  static readonly HashSet<string> certAllowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  // Tab id -> the address whose certificate failed during the navigation in flight.
  readonly Dictionary<string,string> certFailures=new Dictionary<string,string>();
  string CertKey(string host) {return (privateMode?"private:":"")+host;}
  internal bool CertificateOverridden(string host) {return certAllowed.Contains(CertKey(host));}
  static bool IsCertificateError(CoreWebView2WebErrorStatus status) {
   return status==CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect||status==CoreWebView2WebErrorStatus.CertificateExpired
    ||status==CoreWebView2WebErrorStatus.CertificateIsInvalid||status==CoreWebView2WebErrorStatus.CertificateRevoked;
  }
  void WatchCertificates(string id,CoreWebView2 core) {
   core.ServerCertificateErrorDetected+=delegate(object sender,CoreWebView2ServerCertificateErrorDetectedEventArgs e) {
    Uri uri;if(!Uri.TryCreate(e.RequestUri,UriKind.Absolute,out uri))return;
    if(e.ErrorStatus!=CoreWebView2WebErrorStatus.CertificateRevoked&&CertificateOverridden(uri.Host)){e.Action=CoreWebView2ServerCertificateErrorAction.AlwaysAllow;return;}
    if(!certFailures.ContainsKey(id))certFailures[id]=e.RequestUri;
   };
  }
  void ShowCertificateOffer(string id,string fallback,CoreWebView2WebErrorStatus status) {
   string url;if(!certFailures.TryGetValue(id,out url))url=fallback;certFailures.Remove(id);
   Uri uri;string host=Uri.TryCreate(url??"",UriKind.Absolute,out uri)&&!string.IsNullOrEmpty(uri.Host)?uri.Host:null;
   string name=host??"This site";
   string why=status==CoreWebView2WebErrorStatus.CertificateExpired?name+"'s security certificate has expired, or this PC's clock is wrong."
    :status==CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect?"The certificate "+name+" sent belongs to a different address."
    :status==CoreWebView2WebErrorStatus.CertificateRevoked?name+"'s security certificate was withdrawn by whoever issued it."
    :name+"'s security certificate isn't from anyone Windows trusts.";
   ShowError(why+" Someone could be pretending to be this site to see what you send. Go back unless you know why, like a device on your own network.");
   Find<TextBlock>("ErrorTitle").Text="This connection isn't private.";
   Status(name+" didn't prove it is who it says it is.");
   if(host==null||status==CoreWebView2WebErrorStatus.CertificateRevoked)return;
   var button=Find<Button>("CertButton");button.Tag=url;button.Content="Go to "+host+" anyway";button.Visibility=Visibility.Visible;
  }
  void ContinueDespiteCertificate() {
   var button=Find<Button>("CertButton");var url=button.Tag as string;if(url==null)return;
   certAllowed.Add(CertKey(new Uri(url).Host));button.Visibility=Visibility.Collapsed;
   var view=views.ContainsKey(state.ActiveId??"")?views[state.ActiveId]:null;
   if(view!=null&&view.CoreWebView2!=null){Find<Border>("ErrorPanel").Visibility=Visibility.Collapsed;Find<Grid>("WebHost").Visibility=Visibility.Visible;view.CoreWebView2.Navigate(url);}
   else Navigate(url,false);
  }

  // Strict in private windows; otherwise what the user picked (Balanced by default).
  static readonly string[] TrackingNames={"Off","Basic","Balanced","Strict"};
  int TrackingLevel() {return privateMode?3:Math.Max(0,Math.Min(3,state.TrackingLevel??(state.StrictTracking?3:2)));}
  void ApplyTrackingPrevention(CoreWebView2 core) {
   var levels=new[]{CoreWebView2TrackingPreventionLevel.None,CoreWebView2TrackingPreventionLevel.Basic,CoreWebView2TrackingPreventionLevel.Balanced,CoreWebView2TrackingPreventionLevel.Strict};
   try {core.Profile.PreferredTrackingPreventionLevel=levels[TrackingLevel()];}catch(Exception){}
  }
  void SetTrackingLevel(int level) {
   if(privateMode)return;level=Math.Max(0,Math.Min(3,level));
   if(level==TrackingLevel()&&state.TrackingLevel!=null)return;
   state.TrackingLevel=level;state.StrictTracking=level==3;Save();
   foreach(var view in views.Values.Where(v=>v.CoreWebView2!=null).Take(1))ApplyTrackingPrevention(view.CoreWebView2);
   Status(new[]{"Tracker blocking is off. Sites can follow you from page to page.","Basic tracker blocking: only harmful trackers (fingerprinting, crypto mining) are blocked.","Balanced tracker blocking: known trackers blocked, sites keep working.","Strict tracker blocking is on. A few sites may not work fully; reload them if something looks off."}[level]);
  }
  void ToggleTrackerBlocking() {CloseMenus();SetTrackingLevel(TrackingLevel()==3?2:3);}

  // The menu row for tracker blocking: hovering it slides open a four-step slider (Off, Basic,
  // Balanced, Strict). It stays open while you drag, even if the pointer leaves the row.
  Action resetTrackerRow;
  void TrackerRow(StackPanel body) {
   var row=new Border {CornerRadius=new CornerRadius(9),Padding=new Thickness(12,11,12,11),Margin=new Thickness(0,1,0,1),Background=Brushes.Transparent,Focusable=true,Tag="tracker-row",FocusVisualStyle=null};
   System.Windows.Automation.AutomationProperties.SetName(row,"Tracker blocking");
   var stack=new StackPanel();row.Child=stack;
   var head=new Grid();head.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(28)});head.ColumnDefinitions.Add(new ColumnDefinition());head.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
   var symbol=new TextBlock {Text="\uEA18",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=14,VerticalAlignment=VerticalAlignment.Center};symbol.SetResourceReference(TextBlock.ForegroundProperty,"Accent");head.Children.Add(symbol);
   var name=new TextBlock {Text="Tracker blocking",FontSize=12,VerticalAlignment=VerticalAlignment.Center};name.SetResourceReference(TextBlock.ForegroundProperty,"Ink");Grid.SetColumn(name,1);head.Children.Add(name);
   var value=new TextBlock {Text=TrackingNames[TrackingLevel()],FontSize=10,Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Center,Tag="tracker-value"};value.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Grid.SetColumn(value,2);head.Children.Add(value);
   stack.Children.Add(head);
   var drawer=new Grid {Height=0,Opacity=0,ClipToBounds=true,Margin=new Thickness(28,0,0,0),Tag="tracker-drawer"};stack.Children.Add(drawer);
   var inner=new StackPanel {Margin=new Thickness(0,10,0,0),VerticalAlignment=VerticalAlignment.Top};drawer.Children.Add(inner);
   int level=TrackingLevel();
   FrameworkElement slider=null;
   slider=PillSlider(level/3.0,null,delegate(double v) {
    int step=(int)Math.Round(v*3);((Action<double>)slider.Tag)(step/3.0);RollText(value,TrackingNames[step]);level=step;
   },delegate {SetTrackingLevel(level);});
   System.Windows.Automation.AutomationProperties.SetAutomationId(slider,"tracker-slider");
   slider.IsEnabled=!privateMode;if(privateMode)row.ToolTip="Private windows always use Strict.";
   inner.Children.Add(slider);
   var labels=new Grid {Margin=new Thickness(0,4,0,0),Height=14};inner.Children.Add(labels);
   var marks=new List<TextBlock>();
   for(int i=0;i<4;i++){var mark=new TextBlock {Text=TrackingNames[i],FontSize=9,HorizontalAlignment=HorizontalAlignment.Left};mark.SetResourceReference(TextBlock.ForegroundProperty,"Muted");labels.Children.Add(mark);marks.Add(mark);}
   labels.SizeChanged+=delegate {
    double w=labels.ActualWidth;
    for(int i=0;i<4;i++){var m=marks[i];m.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));double x=9+i/3.0*Math.Max(0,w-18)-m.DesiredSize.Width/2;m.Margin=new Thickness(Math.Max(0,Math.Min(w-m.DesiredSize.Width,x)),0,0,0);}
   };
   bool open=false,dragging=false;
   Action<bool> show=delegate(bool wanted) {
    if(open==wanted)return;open=wanted;
    var time=TimeSpan.FromMilliseconds(wanted?170:140);
    // The slider keeps its size while the drawer folds, so it is drawn once as a bitmap for the move.
    if(SystemParameters.ClientAreaAnimation)inner.CacheMode=motionCache;
    var fold=new System.Windows.Media.Animation.DoubleAnimation(wanted?54:0,time) {EasingFunction=new System.Windows.Media.Animation.CubicEase {EasingMode=System.Windows.Media.Animation.EasingMode.EaseOut}};
    fold.Completed+=delegate {if(open==wanted&&inner.CacheMode==motionCache)inner.CacheMode=null;};
    drawer.BeginAnimation(FrameworkElement.HeightProperty,fold);
    drawer.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(wanted?1:0,time));
    if(wanted)row.SetResourceReference(Border.BackgroundProperty,"Hover");else row.Background=Brushes.Transparent;
   };
   // A reused menu opens with the drawer folded, whatever state it was closed in.
   resetTrackerRow=delegate {open=false;dragging=false;drawer.BeginAnimation(FrameworkElement.HeightProperty,null);drawer.BeginAnimation(UIElement.OpacityProperty,null);drawer.Height=0;drawer.Opacity=0;row.Background=Brushes.Transparent;};
   row.MouseEnter+=delegate {show(true);};
   row.MouseLeave+=delegate {if(!dragging&&!row.IsKeyboardFocusWithin)show(false);};
   slider.PreviewMouseLeftButtonDown+=delegate {dragging=true;};
   slider.LostMouseCapture+=delegate {dragging=false;if(!row.IsMouseOver&&!row.IsKeyboardFocusWithin)show(false);};
   row.GotKeyboardFocus+=delegate {show(true);};
   row.LostKeyboardFocus+=delegate {if(!row.IsMouseOver&&!dragging)show(false);};
   row.KeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e) {
    if(privateMode||e.Key!=System.Windows.Input.Key.Left&&e.Key!=System.Windows.Input.Key.Right)return;
    level=Math.Max(0,Math.Min(3,level+(e.Key==System.Windows.Input.Key.Right?1:-1)));((Action<double>)slider.Tag)(level/3.0);RollText(value,TrackingNames[level]);SetTrackingLevel(level);e.Handled=true;
   };
   body.Children.Add(row);
  }

  Window clearDataWindow;
  void ShowClearBrowsingData() {
   CloseMenus();if(clearDataWindow!=null){clearDataWindow.Activate();return;}
   StackPanel body,actions;var dialog=CreateMicaDialog(window,"Clear browsing data",560,640,out body,out actions);clearDataWindow=dialog;dialog.Closed+=delegate {clearDataWindow=null;};
   body.Children.Add(DialogText("Choose what to remove from this device. Passwords in your vault and your bookmarks are kept.",13,"Muted"));
   var range=new ComboBox {Margin=new Thickness(0,16,0,8),Padding=new Thickness(10,7,10,7)};foreach(var label in new[]{"Last hour","Last 24 hours","Last 7 days","All time"})range.Items.Add(label);range.SelectedIndex=0;body.Children.Add(range);
   var kinds=new List<Tuple<CheckBox,string>>();
   foreach(var option in new[]{new[]{"history","Browsing history","Pages you visited, recently closed tabs"},new[]{"cookies","Cookies and site data","Signs you out of most sites"},new[]{"cache","Cached images and files","Sites may load slower next time"},new[]{"downloads","Download list","Your files stay where they are"},new[]{"autofill","Autofill form data","Saved addresses and form entries"}}) {
    var box=new CheckBox {IsChecked=option[0]!="autofill"&&option[0]!="downloads",Margin=new Thickness(0,12,0,0),Tag=option[0]};
    var text=new StackPanel {Margin=new Thickness(10,0,0,0)};text.Children.Add(DialogText(option[1],13,"Ink"));text.Children.Add(DialogText(option[2],11,"Muted"));box.Content=text;
    body.Children.Add(box);kinds.Add(Tuple.Create(box,option[0]));
   }
   var cancel=new Button {Content="Cancel",Padding=new Thickness(18,10,18,10),Margin=new Thickness(0,0,8,0)};cancel.Click+=delegate {dialog.Close();};actions.Children.Add(cancel);
   var clear=new Button {Content="Clear data",Padding=new Thickness(22,10,22,10),IsDefault=true,Tag="clear"};clear.SetResourceReference(Button.BackgroundProperty,"Hover");actions.Children.Add(clear);
   clear.Click+=async delegate {
    var chosen=kinds.Where(k=>k.Item1.IsChecked==true).Select(k=>k.Item2).ToList();if(chosen.Count==0){dialog.Close();return;}
    clear.IsEnabled=false;var since=new[]{DateTime.Now.AddHours(-1),DateTime.Now.AddDays(-1),DateTime.Now.AddDays(-7),DateTime.MinValue}[range.SelectedIndex];
    string result=await ClearBrowsingData(chosen,since);dialog.Close();Status(result);
   };
   dialog.Show();
  }
  async Task<string> ClearBrowsingData(List<string> chosen,DateTime since) {
   CoreWebView2BrowsingDataKinds kinds=0;
   if(chosen.Contains("history"))kinds|=CoreWebView2BrowsingDataKinds.BrowsingHistory;
   if(chosen.Contains("cookies"))kinds|=CoreWebView2BrowsingDataKinds.AllSite;
   if(chosen.Contains("cache"))kinds|=CoreWebView2BrowsingDataKinds.DiskCache|CoreWebView2BrowsingDataKinds.CacheStorage|CoreWebView2BrowsingDataKinds.ServiceWorkers;
   if(chosen.Contains("downloads"))kinds|=CoreWebView2BrowsingDataKinds.DownloadHistory;
   if(chosen.Contains("autofill"))kinds|=CoreWebView2BrowsingDataKinds.GeneralAutofill;
   var profileObject=AnyProfile();
   if(kinds!=0&&profileObject!=null) {
    try {if(since==DateTime.MinValue)await profileObject.ClearBrowsingDataAsync(kinds);else await profileObject.ClearBrowsingDataAsync(kinds,since,DateTime.Now.AddMinutes(1));}
    catch(Exception e){return "Couldn't clear site data: "+e.Message;}
   }
   if(chosen.Contains("history")) {
    foreach(var browser in Program.Browsers.Where(b=>b.profile==profile)) {
     browser.state.History.RemoveAll(h=>since==DateTime.MinValue||h.LastUsed>=since.ToUniversalTime());
     if(since==DateTime.MinValue)browser.state.RecentlyClosed.Clear();
     browser.Save();browser.RefreshPersonalization();
    }
   }
   if(chosen.Contains("downloads"))ClearFinishedDownloads();
   return profileObject==null&&(chosen.Contains("cookies")||chosen.Contains("cache"))?"History cleared. Open any website once to clear cookies and cache too.":"Browsing data cleared.";
  }

  static bool engineCheckStarted;
  bool engineChecking;
  // Once per launch, in the background, from the first regular window.
  // Called from the constructor, before the UI thread has a synchronization context:
  // a DispatcherTimer keeps the check (and every UI update after it) on the UI thread.
  void ScheduleEngineUpdate() {
   if(engineCheckStarted||privateMode||smoke)return;engineCheckStarted=true;
   var delay=new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background,window.Dispatcher) {Interval=TimeSpan.FromSeconds(45)};
   delay.Tick+=delegate {delay.Stop();if(closing)return;AppUpdater.Prune(CurrentApp,true);CheckAppUpdate(false);CheckEngineUpdate(false);};delay.Start();
  }
  async void CheckEngineUpdate(bool force) {
   if(engineChecking)return;
   // On Windows' own engine, Windows keeps it current; Mica downloads nothing.
   if(BundledRuntime.EvergreenMode&&BundledRuntime.ChosenVersion==null){SetEngineNote(force?"Windows keeps this web engine up to date.":null);return;}
   engineChecking=true;SetEngineNote(null);
   try {
    var current=EngineUpdater.ParseVersion(BundledRuntime.ChosenVersion??(environment!=null?environment.BrowserVersionString.Split(' ')[0]:null));
    string message=await EngineUpdater.Update(EngineUpdater.Root,current,force);
    EngineUpdater.Prune(EngineUpdater.Root,current);
    if(message!=null&&!closing){SetEngineNote(message);if(!force)Status(message);}
   }catch(Exception e){if(force&&!closing)SetEngineNote("Couldn't update the engine: "+e.Message);}
   finally {engineChecking=false;SetEngineNote(engineUpdateNote);RefreshUpdatesHint();}
  }
  // When the engine's main process exits every page goes with it: start a fresh
  // engine and bring the current tab back instead of leaving dead views.
  bool recoveringEngine;
  void HandleProcessFailed(string id,WebView2 view,CoreWebView2ProcessFailedEventArgs e) {
   if(e.ProcessFailedKind==CoreWebView2ProcessFailedKind.BrowserProcessExited) {
    if(recoveringEngine||switchingEngine)return;recoveringEngine=true;
    window.Dispatcher.BeginInvoke(new Action(async delegate {
     try {
      targetWatcher=null;ownTargets.Clear();
      ReleaseEngine();
      if(!closing&&state.ActiveId!=null){await Activate(state.ActiveId);Status("The web engine restarted. Your tabs are safe; other tabs reload when you open them.");}
     }finally {recoveringEngine=false;}
    }));
    return;
   }
   bool page=e.ProcessFailedKind==CoreWebView2ProcessFailedKind.RenderProcessExited||e.ProcessFailedKind==CoreWebView2ProcessFailedKind.RenderProcessUnresponsive;
   if(!page)return; // GPU/utility processes restart on their own.
   window.Dispatcher.BeginInvoke(new Action(delegate {if(!IsCurrent(id,view))return;DisposeView(id);if(state.ActiveId==id)ShowError("This page crashed. Other tabs are fine. Try again to reload it.");}));
  }
 }
}
