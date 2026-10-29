using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace Mica {
 // Apps registered for a URI scheme (roblox-player:, discord:, zoommtg: …).
 // Schemes with a history of abuse never launch, whatever the user chooses.
 static class ExternalApps {
  static readonly HashSet<string> blocked=new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
   "file","javascript","vbscript","data","blob","about","shell","mk","its","ms-its","hcp","res","search","search-ms","ms-search",
   "ms-msdt","ms-officecmd","ms-cxh","ms-cxh-full","ms-appinstaller","ms-excel","ms-word","ms-powerpoint","ms-access","ms-visio","ms-project","ms-publisher","ms-infopath","ms-spd","ms-officeapp",
   "ms-settings","ms-windows-store","windowsdefender","ms-availablenetworks","ms-gamingoverlay","ms-screenclip","msnweather","ldap","telnet","ftp","gopher","jar","view-source","chrome","edge","microsoft-edge","mica","mica.url"
  };
  internal static string Scheme(string uri) {int colon=uri.IndexOf(':');return colon>1&&colon<40?uri.Substring(0,colon).ToLowerInvariant():null;}
  internal static bool IsLaunchable(string uri) {
   string scheme=Scheme(uri);if(scheme==null||!System.Text.RegularExpressions.Regex.IsMatch(scheme,"^[a-z][a-z0-9+.-]*$")||blocked.Contains(scheme)||scheme=="http"||scheme=="https")return false;
   using(var key=Registry.ClassesRoot.OpenSubKey(scheme))return key!=null&&key.GetValue("URL Protocol")!=null;
  }
  // The app gets the link as one quoted argument ("%1"); a quote or space left raw would let the page add
  // switches of its own, so they are percent-encoded first, as Chrome does.
  internal static string Escape(string uri) {
   var text=new System.Text.StringBuilder();
   foreach(char c in uri){if(c<=0x20||c==0x7F||"\"<>`^{}|\\".IndexOf(c)>=0)text.Append(Uri.HexEscape(c));else text.Append(c);}
   return text.ToString();
  }
  internal static string AppName(string scheme) {
   try {using(var key=Registry.ClassesRoot.OpenSubKey(scheme)){var name=key==null?null:key.GetValue("") as string;if(!string.IsNullOrWhiteSpace(name)){name=name.Trim();if(name.StartsWith("URL:",StringComparison.OrdinalIgnoreCase))name=name.Substring(4).Trim();if(name.EndsWith(" Protocol",StringComparison.OrdinalIgnoreCase))name=name.Substring(0,name.Length-9).Trim();if(name.Length>0)return name;}}}catch(Exception){}
   return scheme;
  }
 }
 partial class BrowserApp {
  static readonly CoreWebView2PermissionKind[] SitePermissionKinds={CoreWebView2PermissionKind.Geolocation,CoreWebView2PermissionKind.Camera,CoreWebView2PermissionKind.Microphone,CoreWebView2PermissionKind.Notifications,CoreWebView2PermissionKind.OtherSensors,CoreWebView2PermissionKind.ClipboardRead,CoreWebView2PermissionKind.MultipleAutomaticDownloads,CoreWebView2PermissionKind.FileReadWrite,CoreWebView2PermissionKind.Autoplay,CoreWebView2PermissionKind.LocalFonts,CoreWebView2PermissionKind.MidiSystemExclusiveMessages,CoreWebView2PermissionKind.WindowManagement};
  static string PermissionLabel(CoreWebView2PermissionKind kind) {
   switch(kind) {
    case CoreWebView2PermissionKind.Geolocation:return "Location";case CoreWebView2PermissionKind.Camera:return "Camera";case CoreWebView2PermissionKind.Microphone:return "Microphone";
    case CoreWebView2PermissionKind.Notifications:return "Notifications";case CoreWebView2PermissionKind.OtherSensors:return "Motion and light sensors";case CoreWebView2PermissionKind.ClipboardRead:return "Clipboard";
    case CoreWebView2PermissionKind.MultipleAutomaticDownloads:return "Automatic downloads";case CoreWebView2PermissionKind.FileReadWrite:return "File editing";case CoreWebView2PermissionKind.Autoplay:return "Sound and autoplay";
    case CoreWebView2PermissionKind.LocalFonts:return "Fonts";case CoreWebView2PermissionKind.MidiSystemExclusiveMessages:return "MIDI device control";case CoreWebView2PermissionKind.WindowManagement:return "Window management";
   }
   return kind.ToString();
  }
  static string PermissionQuestion(CoreWebView2PermissionKind kind) {
   switch(kind) {
    case CoreWebView2PermissionKind.Geolocation:return "know your location";case CoreWebView2PermissionKind.Camera:return "use your camera";case CoreWebView2PermissionKind.Microphone:return "use your microphone";
    case CoreWebView2PermissionKind.Notifications:return "show notifications";case CoreWebView2PermissionKind.OtherSensors:return "use motion and light sensors";case CoreWebView2PermissionKind.ClipboardRead:return "see text and images copied to the clipboard";
    case CoreWebView2PermissionKind.MultipleAutomaticDownloads:return "download multiple files";case CoreWebView2PermissionKind.FileReadWrite:return "edit files on your device";case CoreWebView2PermissionKind.LocalFonts:return "use fonts installed on your device";
    case CoreWebView2PermissionKind.MidiSystemExclusiveMessages:return "control and reprogram MIDI devices";case CoreWebView2PermissionKind.WindowManagement:return "manage windows on all your displays";
   }
   return "use "+PermissionLabel(kind).ToLowerInvariant();
  }
  static string OriginLabel(string origin) {Uri uri;return Uri.TryCreate(origin,UriKind.Absolute,out uri)&&!string.IsNullOrEmpty(uri.Host)?uri.Host:origin;}
  string ExternalAllowPath {get {return Path.Combine(profile,"external-apps.txt");}}
  HashSet<string> ReadExternalAllows() {
   var set=new HashSet<string>(StringComparer.OrdinalIgnoreCase);if(privateMode)return set;
   try {if(File.Exists(ExternalAllowPath))foreach(var line in File.ReadAllLines(ExternalAllowPath))if(line.Contains(" "))set.Add(line.Trim());}catch(Exception){}
   return set;
  }
  void WriteExternalAllows(HashSet<string> set) {if(privateMode)return;try {File.WriteAllLines(ExternalAllowPath,set.OrderBy(s=>s).ToArray());}catch(Exception){}}
  void WatchSitePermissions(Func<bool> alive,WebView2 view,FrameworkElement anchor) {
   var core=view.CoreWebView2;
   core.PermissionRequested+=delegate(object sender,CoreWebView2PermissionRequestedEventArgs e) {
    if(Array.IndexOf(SitePermissionKinds,e.PermissionKind)<0||e.PermissionKind==CoreWebView2PermissionKind.Autoplay)return;
    if(closing||!alive()){e.State=CoreWebView2PermissionState.Deny;return;}
    var deferral=e.GetDeferral();var kind=e.PermissionKind;string origin=OriginLabel(e.Uri);
    AskSiteBubble(anchor,PermissionIcon(kind),origin+" wants to",PermissionQuestion(kind),"Allow","Block",!privateMode,alive,delegate(bool allow,bool remember) {
     try {e.State=allow?CoreWebView2PermissionState.Allow:CoreWebView2PermissionState.Deny;e.SavesInProfile=remember&&!privateMode;}finally {deferral.Complete();}
    });
   };
   core.LaunchingExternalUriScheme+=delegate(object sender,CoreWebView2LaunchingExternalUriSchemeEventArgs e) {
    e.Cancel=true;string uri=e.Uri;
    if(closing||!alive()||!ExternalApps.IsLaunchable(uri)){Status("Blocked an app link.");return;}
    string scheme=ExternalApps.Scheme(uri),origin=string.IsNullOrEmpty(e.InitiatingOrigin)?"This page":OriginLabel(e.InitiatingOrigin),app=ExternalApps.AppName(scheme);
    // Remembered per full origin (scheme, host, port), and only for HTTPS pages, so a plain-http copy of a
    // trusted site can't reuse the choice.
    Uri initiator;bool secure=Uri.TryCreate(e.InitiatingOrigin??"",UriKind.Absolute,out initiator)&&initiator.Scheme==Uri.UriSchemeHttps;
    string key=(secure?initiator.GetLeftPart(UriPartial.Authority):"insecure")+" "+scheme;
    Action launch=delegate {try {Process.Start(new ProcessStartInfo(ExternalApps.Escape(uri)) {UseShellExecute=true});Status("Opened "+app+".");}catch(Exception){Status("Windows could not open that app.");}};
    // A remembered "always open" still needs the user's click, or a page could launch the app on a timer.
    if(secure&&e.IsUserInitiated&&ReadExternalAllows().Contains(key)){window.Dispatcher.BeginInvoke(launch);return;}
    AskSiteBubble(anchor,"",origin+" wants to open",app,"Open "+app,"Cancel",!privateMode&&e.IsUserInitiated&&secure,alive,delegate(bool open,bool remember) {
     if(!open){Status("App link cancelled.");return;}
     if(remember){var allowed=ReadExternalAllows();allowed.Add(key);WriteExternalAllows(allowed);}
     launch();
    });
   };
  }
  static string PermissionIcon(CoreWebView2PermissionKind kind) {
   switch(kind) {
    case CoreWebView2PermissionKind.Geolocation:return "";case CoreWebView2PermissionKind.Camera:return "";case CoreWebView2PermissionKind.Microphone:return "";
    case CoreWebView2PermissionKind.Notifications:return "";case CoreWebView2PermissionKind.ClipboardRead:return "";case CoreWebView2PermissionKind.MultipleAutomaticDownloads:return "";
    case CoreWebView2PermissionKind.FileReadWrite:return "";case CoreWebView2PermissionKind.LocalFonts:return "";case CoreWebView2PermissionKind.WindowManagement:return "";
   }
   return "";
  }
  // A small card under the address bar, like other browsers. Requests queue;
  // the card hides while Mica is in the background so it never floats over other apps.
  readonly Queue<Action> siteBubbleQueue=new Queue<Action>();
  Popup siteBubble;bool siteBubbleWatching;
  void WatchSiteBubble() {
   if(siteBubbleWatching)return;siteBubbleWatching=true;
   window.Closed+=delegate {if(siteBubble!=null)((Action)siteBubble.Tag)();};
   // Popups don't follow their window on their own; nudge the offset so WPF re-places it.
   window.LocationChanged+=delegate {if(siteBubble==null||!siteBubble.IsOpen)return;double offset=siteBubble.HorizontalOffset;siteBubble.HorizontalOffset=offset+0.01;siteBubble.HorizontalOffset=offset;};
  }
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
  [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
  [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
  static IntPtr PopupHandle(Popup popup) {var source=popup.Child==null?null:PresentationSource.FromVisual(popup.Child) as System.Windows.Interop.HwndSource;return source==null?IntPtr.Zero:source.Handle;}
  // WPF popups are always-on-top windows, so a bubble would float over every other
  // app. Owned by Mica and not topmost, it stacks with Mica and hides when Mica does.
  void KeepWithWindow(Popup popup) {
   popup.Opened+=delegate {
    IntPtr handle=PopupHandle(popup),owner=new System.Windows.Interop.WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;
    SetWindowLongPtr(handle,-8,owner);
    SetWindowPos(handle,new IntPtr(-2),0,0,0,0,0x0001|0x0002|0x0010);
   };
  }
  internal bool BubbleIsTopmost() {IntPtr handle=siteBubble==null?IntPtr.Zero:PopupHandle(siteBubble);return handle!=IntPtr.Zero&&((long)GetWindowLongPtr(handle,-20)&0x8)!=0;}
  // A prompt grows out of the corner a page can point at; clicks in its first moments (a fast click
  // meant for the page) are ignored, so no one can be tricked into allowing something.
  const int PromptArmDelay=700;
  static void IgnoreEarlyClicks(FrameworkElement card) {
   var armed=DateTime.UtcNow.AddMilliseconds(PromptArmDelay);
   // Only buttons and boxes are held back; clicking into a text field decides nothing.
   Func<object,bool> decides=delegate(object source) {for(var node=source as DependencyObject;node!=null&&node!=card;node=TreeParent(node))if(node is System.Windows.Controls.Primitives.ButtonBase)return true;return false;};
   card.PreviewMouseDown+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs m) {if(DateTime.UtcNow<armed&&decides(m.OriginalSource))m.Handled=true;};
   card.PreviewMouseUp+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs m) {if(DateTime.UtcNow<armed&&decides(m.OriginalSource))m.Handled=true;};
  }
  void AskSiteBubble(FrameworkElement anchor,string icon,string title,string subject,string acceptLabel,string declineLabel,bool offerRemember,Func<bool> alive,Action<bool,bool> done) {
   window.Dispatcher.BeginInvoke(new Action(delegate {
    siteBubbleQueue.Enqueue(delegate {
     if(closing||!alive()){done(false,false);NextSiteBubble();return;}
     var card=new Border {Width=340,CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),Padding=new Thickness(16,14,14,14),Margin=new Thickness(14,4,14,22),Resources=window.Resources};card.SetResourceReference(Border.BackgroundProperty,"Panel");card.SetResourceReference(Border.BorderBrushProperty,"Line");
     card.Effect=new System.Windows.Media.Effects.DropShadowEffect {BlurRadius=28,ShadowDepth=6,Opacity=0.18,Color=Color.FromRgb(0x25,0x20,0x32)};
     var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(44)});grid.ColumnDefinitions.Add(new ColumnDefinition());card.Child=grid;
     var badge=new Border {Width=32,Height=32,CornerRadius=new CornerRadius(10),VerticalAlignment=VerticalAlignment.Top};badge.SetResourceReference(Border.BackgroundProperty,"Hover");
     var glyph=new TextBlock {Text=icon,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=15,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};glyph.SetResourceReference(TextBlock.ForegroundProperty,"Accent");badge.Child=glyph;grid.Children.Add(badge);
     var stack=new StackPanel();Grid.SetColumn(stack,1);grid.Children.Add(stack);
     var heading=DialogText(title,11,"Muted");heading.Margin=new Thickness(0,0,26,2);stack.Children.Add(heading);
     var what=DialogText(subject,14,"Ink");what.FontWeight=FontWeights.SemiBold;stack.Children.Add(what);
     var remember=new CheckBox {Content="Remember for this site",IsChecked=false,FontSize=11,Margin=new Thickness(0,10,0,0),Visibility=offerRemember?Visibility.Visible:Visibility.Collapsed};stack.Children.Add(remember);
     var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};stack.Children.Add(buttons);
     var popup=IslandPopup(card);
     bool finished=false;
     Action<bool,bool> finish=delegate(bool accepted,bool keep) {if(finished)return;finished=true;siteBubble=null;done(accepted,keep);CloseIslandPopup(popup,NextSiteBubble);};
     // Block sits in the corner, where a page can lure a click; Allow is the one you have to aim for.
     var accept=new Button {Content=acceptLabel,Padding=new Thickness(14,7,14,7),Margin=new Thickness(0,0,6,0),FontSize=12};accept.SetResourceReference(Button.BackgroundProperty,"Hover");accept.Click+=delegate {finish(true,offerRemember&&remember.IsChecked==true);};buttons.Children.Add(accept);
     var decline=new Button {Content=declineLabel,Padding=new Thickness(14,7,14,7),FontSize=12};decline.Click+=delegate {finish(false,offerRemember&&remember.IsChecked==true);};buttons.Children.Add(decline);
     IgnoreEarlyClicks(card);
     var dismiss=new Button {Content="",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=9,Width=24,Height=24,Padding=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,-6,-6,0),ToolTip="Dismiss"};dismiss.SetResourceReference(Control.ForegroundProperty,"Muted");dismiss.Click+=delegate {finish(false,false);};Grid.SetColumn(dismiss,1);grid.Children.Add(dismiss);
     popup.KeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs k) {if(k.Key==System.Windows.Input.Key.Escape){finish(false,false);k.Handled=true;}};
     siteBubble=popup;popup.Tag=(Action)delegate {finish(false,false);};
     WatchSiteBubble();KeepWithWindow(popup);
     popup.IsOpen=true;
    });
    if(siteBubble==null&&siteBubbleQueue.Count==1)NextSiteBubble();
   }));
  }
  void NextSiteBubble() {if(siteBubble!=null||siteBubbleQueue.Count==0)return;siteBubbleQueue.Dequeue()();}
  Window sitePermissionsWindow;
  CoreWebView2Profile AnyProfile() {var current=CurrentView();if(current!=null&&current.CoreWebView2!=null)return current.CoreWebView2.Profile;var any=views.Values.FirstOrDefault(v=>v.CoreWebView2!=null);return any==null?null:any.CoreWebView2.Profile;}
  async void ShowSitePermissions() {
   CloseMenus();if(sitePermissionsWindow!=null){sitePermissionsWindow.Activate();return;}
   StackPanel body,actions;var dialog=CreateMicaDialog(window,"Site permissions",600,720,out body,out actions);sitePermissionsWindow=dialog;dialog.Closed+=delegate {sitePermissionsWindow=null;};
   var done=new Button {Content="Done",Padding=new Thickness(22,10,22,10),IsDefault=true};done.SetResourceReference(Button.BackgroundProperty,"Hover");done.Click+=delegate {dialog.Close();};actions.Children.Add(done);
   dialog.Show();await RenderSitePermissions(body);
  }
  async Task RenderSitePermissions(StackPanel body) {
   body.Children.Clear();var profileObject=AnyProfile();
   if(profileObject==null){body.Children.Add(DialogText("Open any website first, then come back here to manage permissions.",13,"Muted"));return;}
   IReadOnlyList<CoreWebView2PermissionSetting> settings;try {settings=await profileObject.GetNonDefaultPermissionSettingsAsync();}catch(Exception){body.Children.Add(DialogText("Permissions are not available right now.",13,"Muted"));return;}
   var active=Active;string currentOrigin=active!=null&&Navigation.IsWeb(active.Url)?new Uri(active.Url).GetLeftPart(UriPartial.Authority):null;
   var origins=settings.Select(s=>s.PermissionOrigin.TrimEnd('/')).Distinct(StringComparer.OrdinalIgnoreCase).Where(o=>!string.Equals(o,currentOrigin,StringComparison.OrdinalIgnoreCase)).OrderBy(o=>OriginLabel(o)).ToList();
   if(currentOrigin!=null){SectionLabel(body,"THIS SITE · "+OriginLabel(currentOrigin).ToUpperInvariant());body.Children.Add(OriginPermissions(body,profileObject,currentOrigin,settings,true));}
   SectionLabel(body,"OTHER SITES");
   if(origins.Count==0)body.Children.Add(DialogText("No other site has changed permissions yet.",12,"Muted"));
   foreach(var origin in origins)body.Children.Add(OriginPermissions(body,profileObject,origin,settings,false));
   var apps=ReadExternalAllows();SectionLabel(body,"APPS SITES MAY OPEN");
   if(apps.Count==0)body.Children.Add(DialogText("When a site opens an app (Roblox, Discord, Zoom…) Mica asks first.",12,"Muted"));
   foreach(var entry in apps.ToList()) {
    string item=entry;var parts=item.Split(new[]{' '},2);var row=new Grid {Margin=new Thickness(12,7,8,7)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
    var text=DialogText(parts[0]+" → "+ExternalApps.AppName(parts[1]),12,"Ink");text.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(text);
    var forget=new Button {Content="Forget",Padding=new Thickness(12,6,12,6)};Grid.SetColumn(forget,1);row.Children.Add(forget);
    var card=Card(row);body.Children.Add(card);
    forget.Click+=delegate {var set=ReadExternalAllows();set.Remove(item);WriteExternalAllows(set);forget.IsEnabled=false;FoldAway(card);};
   }
   var note=DialogText("Mica supports the permissions its Chromium engine exposes. USB, serial, payment, VR/AR and similar device APIs stay blocked.",11,"Muted");note.Margin=new Thickness(0,14,0,4);note.LineHeight=17;body.Children.Add(note);
  }
  // A row that goes away fades and folds its height, so what's below slides up instead of jumping.
  internal static void FoldAway(FrameworkElement element) {
   var parent=element.Parent as Panel;
   if(!SystemParameters.ClientAreaAnimation||parent==null){if(parent!=null)parent.Children.Remove(element);return;}
   element.Height=element.ActualHeight;element.ClipToBounds=true;var ease=new System.Windows.Media.Animation.CubicEase {EasingMode=System.Windows.Media.Animation.EasingMode.EaseInOut};
   element.BeginAnimation(UIElement.OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(0,TimeSpan.FromMilliseconds(140)));
   var fold=new System.Windows.Media.Animation.DoubleAnimation(0,TimeSpan.FromMilliseconds(220)) {EasingFunction=ease,BeginTime=TimeSpan.FromMilliseconds(60)};
   var margin=element.Margin;
   element.BeginAnimation(FrameworkElement.MarginProperty,new System.Windows.Media.Animation.ThicknessAnimation(new Thickness(margin.Left,0,margin.Right,0),TimeSpan.FromMilliseconds(220)) {EasingFunction=ease,BeginTime=TimeSpan.FromMilliseconds(60)});
   fold.Completed+=delegate {parent.Children.Remove(element);};
   element.BeginAnimation(FrameworkElement.HeightProperty,fold);
  }
  // A row of options with one highlight behind the chosen one; picking another slides it across.
  // chosen gets the new index and a way to slide back if applying it fails.
  internal FrameworkElement SegmentedChoice(string[] labels,int selected,Action<int,Action> chosen) {
   var host=new Grid {Tag="segmented"};var pill=new Border {CornerRadius=new CornerRadius(7),HorizontalAlignment=HorizontalAlignment.Left,IsHitTestVisible=false,RenderTransform=new TranslateTransform()};
   pill.SetResourceReference(Border.BackgroundProperty,"Hover");host.Children.Add(pill);
   var row=new StackPanel {Orientation=Orientation.Horizontal};host.Children.Add(row);
   var buttons=new List<Button>();int current=selected;
   Action<bool> place=delegate(bool animate) {
    if(current<0||current>=buttons.Count||!host.IsLoaded){pill.Opacity=current<0?0:1;return;}
    var target=buttons[current];var at=target.TranslatePoint(new Point(0,0),host);
    pill.Height=target.ActualHeight;pill.VerticalAlignment=VerticalAlignment.Center;pill.Opacity=1;
    var shift=(TranslateTransform)pill.RenderTransform;
    if(!animate||!SystemParameters.ClientAreaAnimation||double.IsNaN(pill.Width)){shift.BeginAnimation(TranslateTransform.XProperty,null);pill.BeginAnimation(FrameworkElement.WidthProperty,null);shift.X=at.X;pill.Width=target.ActualWidth;return;}
    var ease=new System.Windows.Media.Animation.CubicEase {EasingMode=System.Windows.Media.Animation.EasingMode.EaseOut};var time=TimeSpan.FromMilliseconds(230);
    shift.BeginAnimation(TranslateTransform.XProperty,new System.Windows.Media.Animation.DoubleAnimation(at.X,time) {EasingFunction=ease});
    pill.BeginAnimation(FrameworkElement.WidthProperty,new System.Windows.Media.Animation.DoubleAnimation(target.ActualWidth,time) {EasingFunction=ease});
   };
   for(int i=0;i<labels.Length;i++) {
    int index=i;var button=new Button {Content=labels[i],Padding=new Thickness(11,5,11,5),Margin=new Thickness(2,0,0,0),FontSize=11,Background=Brushes.Transparent,Tag="segment"};
    button.Click+=delegate {
     if(index==current)return;int before=current;current=index;place(true);
     chosen(index,delegate {current=before;place(true);});
    };
    buttons.Add(button);row.Children.Add(button);
   }
   host.Loaded+=delegate {place(false);};host.SizeChanged+=delegate {place(false);};
   host.Tag=new Func<int>(delegate {return current;});
   return host;
  }
  void SectionLabel(StackPanel body,string text) {var label=DialogText(text,10,"Muted");label.Margin=new Thickness(0,12,0,8);body.Children.Add(label);}
  Border Card(UIElement child) {var card=new Border {CornerRadius=new CornerRadius(10),Padding=new Thickness(0,4,0,4),Margin=new Thickness(0,0,0,10),Child=child};card.SetResourceReference(Border.BackgroundProperty,"Panel");return card;}
  FrameworkElement OriginPermissions(StackPanel body,CoreWebView2Profile profileObject,string origin,IReadOnlyList<CoreWebView2PermissionSetting> settings,bool showAll) {
   var rows=new StackPanel();
   if(!showAll){var title=DialogText(OriginLabel(origin),13,"Ink");title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(12,6,12,2);rows.Children.Add(title);}
   foreach(var kind in SitePermissionKinds) {
    var setting=settings.FirstOrDefault(s=>s.PermissionKind==kind&&string.Equals(s.PermissionOrigin.TrimEnd('/'),origin,StringComparison.OrdinalIgnoreCase));
    if(!showAll&&setting==null)continue;
    var state=setting==null?CoreWebView2PermissionState.Default:setting.PermissionState;var permission=kind;
    var row=new Grid {Margin=new Thickness(12,5,8,5)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
    var label=DialogText(PermissionLabel(kind),12,"Ink");label.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(label);
    var options=new[]{CoreWebView2PermissionState.Default,CoreWebView2PermissionState.Allow,CoreWebView2PermissionState.Deny};
    // The row changes where it stands: the highlight slides to the new choice and the dialog isn't rebuilt.
    var choices=SegmentedChoice(new[]{"Ask","Allow","Block"},Array.IndexOf(options,state),async delegate(int picked,Action undo) {
     try {await profileObject.SetPermissionStateAsync(permission,origin,options[picked]);}catch(Exception){Status("Could not change that permission.");undo();}
    });
    System.Windows.Automation.AutomationProperties.SetName(choices,PermissionLabel(kind));Grid.SetColumn(choices,1);row.Children.Add(choices);
    rows.Children.Add(row);
   }
   return Card(rows);
  }
 }
}
namespace Mica {
 // Page dialogs (alert/confirm/prompt/beforeunload) and HTTP sign-in use Mica
 // chrome instead of the engine's default Edge-styled windows.
 partial class BrowserApp {
  void WatchPageDialogs(Func<bool> alive,WebView2 view) {
   var core=view.CoreWebView2;core.Settings.AreDefaultScriptDialogsEnabled=false;
   // A dialog blocks the whole window, so only the page you're looking at may show one, and a page that
   // keeps opening them (alert in a loop) is silenced until it navigates.
   var recent=new List<DateTime>();bool silenced=false;
   core.NavigationStarting+=delegate {silenced=false;recent.Clear();};
   core.ScriptDialogOpening+=delegate(object sender,CoreWebView2ScriptDialogOpeningEventArgs e) {
    var deferral=e.GetDeferral();string origin=OriginLabel(e.Uri);
    window.Dispatcher.BeginInvoke(new Action(delegate {
     try {
      if(closing||!alive())return;
      if(!view.IsVisible){Status("A background tab ("+origin+") tried to show a dialog. Mica dismissed it.");return;}
      var now=DateTime.UtcNow;recent.RemoveAll(t=>now-t>TimeSpan.FromSeconds(10));recent.Add(now);
      if(silenced)return;if(recent.Count>3){silenced=true;Status("Mica stopped "+origin+" from opening more dialogs.");return;}
      string text;bool ok=PageDialog(e.Kind,origin,e.Message,e.DefaultText,out text);
      if(ok){if(e.Kind==CoreWebView2ScriptDialogKind.Prompt)e.ResultText=text;e.Accept();}
     }finally {deferral.Complete();}
    }));
   };
   core.BasicAuthenticationRequested+=delegate(object sender,CoreWebView2BasicAuthenticationRequestedEventArgs e) {
    var deferral=e.GetDeferral();string origin=OriginLabel(e.Uri),challenge=e.Challenge;
    window.Dispatcher.BeginInvoke(new Action(delegate {
     try {
      string user,password;if(closing||!alive()||!SignInDialog(origin,out user,out password)){e.Cancel=true;return;}
      e.Response.UserName=user;e.Response.Password=password;
     }finally {deferral.Complete();}
    }));
   };
  }
  bool PageDialog(CoreWebView2ScriptDialogKind kind,string origin,string message,string defaultText,out string text) {
   text=null;bool leave=kind==CoreWebView2ScriptDialogKind.Beforeunload;bool? chosen=null;
   StackPanel body,actions;var dialog=CreateMicaDialog(window,leave?"Leave this page?":"This page says",380,kind==CoreWebView2ScriptDialogKind.Prompt?320:260,out body,out actions);
   // Hostnames can be very long (Apps Script, CDNs): one muted line, never the title.
   string host=OriginLabel(origin);if(string.IsNullOrEmpty(host))host=origin;
   if(!string.IsNullOrEmpty(host)){var from=DialogText(host,11,"Muted");from.TextWrapping=TextWrapping.NoWrap;from.TextTrimming=TextTrimming.CharacterEllipsis;from.ToolTip=origin;from.Margin=new Thickness(0,0,0,12);body.Children.Add(from);}
   // Size to the message instead of a fixed height, up to the screen.
   dialog.SizeToContent=SizeToContent.Height;dialog.MaxHeight=SystemParameters.WorkArea.Height-32;
   var detail=DialogText(leave?"Changes you made may not be saved.":message,13,leave?"Muted":"Ink");detail.LineHeight=22;body.Children.Add(detail);
   TextBox input=null;
   if(kind==CoreWebView2ScriptDialogKind.Prompt){input=new TextBox {Text=defaultText??"",Margin=new Thickness(0,14,0,0),Padding=new Thickness(10,8,10,8),BorderThickness=new Thickness(1)};input.SetResourceReference(Control.BackgroundProperty,"Panel");input.SetResourceReference(Control.BorderBrushProperty,"Line");body.Children.Add(input);}
   if(kind!=CoreWebView2ScriptDialogKind.Alert){var cancel=new Button {Content=leave?"Stay":"Cancel",IsCancel=true,Padding=new Thickness(18,10,18,10),Margin=new Thickness(0,0,8,0)};cancel.Click+=delegate {chosen=false;dialog.Close();};actions.Children.Add(cancel);}
   var accept=new Button {Content=leave?"Leave":"OK",IsDefault=true,Padding=new Thickness(22,10,22,10)};accept.SetResourceReference(Button.BackgroundProperty,"Hover");accept.Click+=delegate {chosen=true;dialog.Close();};actions.Children.Add(accept);
   dialog.Loaded+=delegate {if(input!=null){input.Focus();input.SelectAll();}else accept.Focus();};
   dialog.Closing+=delegate {if(chosen==null&&dialog.DialogResult.HasValue)chosen=dialog.DialogResult;};DockTool(dialog);dialog.ShowDialog();bool result=chosen==true||(kind==CoreWebView2ScriptDialogKind.Alert);if(input!=null)text=input.Text;return result;
  }
  bool SignInDialog(string origin,out string user,out string password) {
   bool? chosen=null;
   StackPanel body,actions;var dialog=CreateMicaDialog(window,"Sign in",380,400,out body,out actions);
   var detail=DialogText(origin+" is asking for a username and password. Your connection to this site may not be private if it isn't HTTPS.",13,"Muted");detail.LineHeight=22;body.Children.Add(detail);
   var name=new TextBox {Margin=new Thickness(0,14,0,0),Padding=new Thickness(10,8,10,8),BorderThickness=new Thickness(1)};var secret=new PasswordBox {Margin=new Thickness(0,10,0,0),Padding=new Thickness(10,8,10,8),BorderThickness=new Thickness(1)};
   foreach(Control box in new Control[]{name,secret}){box.SetResourceReference(Control.BackgroundProperty,"Panel");box.SetResourceReference(Control.BorderBrushProperty,"Line");box.SetResourceReference(Control.ForegroundProperty,"Ink");}
   body.Children.Add(DialogText("Username",11,"Muted"));((FrameworkElement)body.Children[1]).Margin=new Thickness(0,14,0,0);name.Margin=new Thickness(0,6,0,0);body.Children.Add(name);
   var passLabel=DialogText("Password",11,"Muted");passLabel.Margin=new Thickness(0,12,0,0);body.Children.Add(passLabel);secret.Margin=new Thickness(0,6,0,0);body.Children.Add(secret);
   var cancel=new Button {Content="Cancel",IsCancel=true,Padding=new Thickness(18,10,18,10),Margin=new Thickness(0,0,8,0)};cancel.Click+=delegate {chosen=false;dialog.Close();};actions.Children.Add(cancel);
   var accept=new Button {Content="Sign in",IsDefault=true,Padding=new Thickness(18,10,18,10)};accept.SetResourceReference(Button.BackgroundProperty,"Hover");accept.Click+=delegate {chosen=true;dialog.Close();};actions.Children.Add(accept);
   dialog.Loaded+=delegate {name.Focus();};dialog.Closing+=delegate {if(chosen==null&&dialog.DialogResult.HasValue)chosen=dialog.DialogResult;};DockTool(dialog);dialog.ShowDialog();bool ok=chosen==true;user=name.Text;password=secret.Password;secret.Clear();return ok;
  }
 }
}
