using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // window.open() with size/position (sign-in, payment, share dialogs) gets a
 // real Mica popup that stays attached to its opener, so OAuth flows complete.
 // Plain target=_blank links keep opening as tabs.
 partial class BrowserApp {
  readonly List<Window> popupWindows=new List<Window>();
  // The engine leaves popup blocking to the host: a page may only open windows or tabs from a click or
  // key press. Others are held back, with a bubble that opens them if you want.
  DateTime lastPopupBubble;
  bool BlockUnaskedPopup(CoreWebView2NewWindowRequestedEventArgs e,CoreWebView2 opener) {
   if(e.IsUserInitiated)return false;e.Handled=true;string uri=e.Uri;
   if(!Navigation.IsWeb(uri)||(siteBubble!=null&&siteBubble.IsOpen)||DateTime.UtcNow-lastPopupBubble<TimeSpan.FromSeconds(3))return true;lastPopupBubble=DateTime.UtcNow;
   string from=OriginLabel(opener.Source);
   window.Dispatcher.BeginInvoke(new Action(delegate {
    if(closing)return;
    ShowChoiceBubble(Find<Button>("AddressButton"),"\uE8BD","Pop-up blocked",from,"This site tried to open "+OriginLabel(uri)+" without a click.",null,new[]{new[]{"Keep blocking","skip",""},new[]{"Open it","open","primary"}},()=>!closing,delegate(string choice) {if(choice=="open")Navigate(uri,true);});
   }));
   return true;
  }
  // Pop-ups get the same upgrade to HTTPS as tabs, tracked under their own key.
  bool PopupUpgrade(CoreWebView2 core,CoreWebView2NavigationStartingEventArgs e) {return UpgradeToHttps("popup:"+System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(core),core,e);}
  static bool IsExtensionPage(string url) {return (url??"").StartsWith("chrome-extension://",StringComparison.OrdinalIgnoreCase);}
  bool TryOpenPopup(CoreWebView2NewWindowRequestedEventArgs e,string jar=null) {
   // A blank window or a page's own blob (a report or preview it fills in) needs to stay tied to the page
   // that opened it, so it always gets a real window even without a size.
   bool filled=e.Uri=="about:blank"||Navigation.IsSiteBlob(e.Uri);
   var features=e.WindowFeatures;if(!features.HasSize&&!features.HasPosition&&!filled)return false;
   if(!Navigation.IsWeb(e.Uri)&&!filled)return false;
   e.Handled=true;var deferral=e.GetDeferral();OpenPopup(e,deferral,jar);return true;
  }
  async void OpenPopup(CoreWebView2NewWindowRequestedEventArgs e,CoreWebView2Deferral deferral,string jar) {
   var features=e.WindowFeatures;
   try {
    double width=features.HasSize?Math.Max(320,Math.Min(features.Width,SystemParameters.WorkArea.Width-40)):520,height=features.HasSize?Math.Max(240,Math.Min(features.Height,SystemParameters.WorkArea.Height-80)):640;
    var core=await CreatePopupWindow(width,height,features.HasPosition,features.Left,features.Top,false,jar);
    if(core!=null){await PageScriptsReady(core);e.NewWindow=core;}
   }catch(Exception){e.Handled=false;}
   finally {deferral.Complete();}
  }
  // Pages an extension opens on its own (options, onboarding) get the same Mica popup.
  async void OpenExtensionPage(string url) {
   try {var core=await CreatePopupWindow(560,680,false,0,0,true);if(core!=null)core.Navigate(url);}catch(Exception error){Status("Couldn't open the extension page: "+error.Message);}
  }
  async Task<CoreWebView2> CreatePopupWindow(double width,double height,bool positioned,double left,double top,bool allowExtensionPages,string jar=null) {
   bool alive=true;
    var popup=new Window {Owner=window,Title="Mica",Width=width+2,Height=height+42,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.CanResize,ShowInTaskbar=true,FontFamily=window.FontFamily,FontSize=12,Resources=window.Resources,WindowStartupLocation=WindowStartupLocation.CenterOwner};KeepOwnerInFront(popup);
    popup.SetResourceReference(Window.BackgroundProperty,"Canvas");popup.SetResourceReference(Window.ForegroundProperty,"Ink");
    WindowChrome.SetWindowChrome(popup,new WindowChrome {CaptionHeight=40,ResizeBorderThickness=new Thickness(5),CornerRadius=new CornerRadius(10),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});
    if(positioned){popup.WindowStartupLocation=WindowStartupLocation.Manual;popup.Left=Math.Max(SystemParameters.WorkArea.Left,Math.Min(left,SystemParameters.WorkArea.Right-popup.Width));popup.Top=Math.Max(SystemParameters.WorkArea.Top,Math.Min(top,SystemParameters.WorkArea.Bottom-popup.Height));}
    var frame=new Border {BorderThickness=new Thickness(1)};frame.SetResourceReference(Border.BorderBrushProperty,"Line");popup.Content=frame;
    var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition {Height=new GridLength(40)});layout.RowDefinitions.Add(new RowDefinition());frame.Child=layout;
    var bar=new Grid {Margin=new Thickness(0)};bar.SetResourceReference(Panel.BackgroundProperty,"Canvas");layout.Children.Add(bar);
    var lockIcon=new TextBlock {Text="",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=11,Margin=new Thickness(14,0,0,0),VerticalAlignment=VerticalAlignment.Center};lockIcon.SetResourceReference(TextBlock.ForegroundProperty,"Muted");bar.Children.Add(lockIcon);
    var address=new TextBlock {Margin=new Thickness(36,0,44,0),VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,FontSize=12};address.SetResourceReference(TextBlock.ForegroundProperty,"Muted");bar.Children.Add(address);
    var close=new Button {Style=(Style)window.FindResource("CaptionButton"),Content="",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,0,8,0),ToolTip="Close"};close.Click+=delegate {popup.Close();};bar.Children.Add(close);
    var view=new WebView2 {DefaultBackgroundColor=System.Drawing.Color.White};Grid.SetRow(view,1);layout.Children.Add(view);
    popupWindows.Add(popup);popup.Closed+=delegate {alive=false;popupWindows.Remove(popup);ReplaceTargetWatcher(view);try{view.Dispose();}catch(Exception){}};
    popup.Show();
    var options=ViewOptions(environment,jar);
    await view.EnsureCoreWebView2Async(environment,options);await ApplyMicaAgent(view.CoreWebView2);
    if(!alive||closing)return null;
    var core=view.CoreWebView2;core.Settings.IsWebMessageEnabled=false;core.Settings.AreHostObjectsAllowed=false;core.Settings.IsGeneralAutofillEnabled=false;core.Settings.IsPasswordAutosaveEnabled=false;
    Func<bool> live=()=>alive&&!closing;WatchSitePermissions(live,view,address);WatchPageDialogs(live,view);WatchLogins(live,view,core,popup);
    Action showAddress=delegate {Uri uri;bool web=Uri.TryCreate(core.Source,UriKind.Absolute,out uri)&&Navigation.IsWeb(core.Source);address.Text=web?uri.Host+uri.PathAndQuery:core.Source;lockIcon.Text=web&&uri.Scheme=="https"&&!CertificateOverridden(uri.Host)?"":"";popup.Title=(string.IsNullOrWhiteSpace(core.DocumentTitle)?address.Text:core.DocumentTitle)+" — Mica";};
    core.SourceChanged+=delegate {showAddress();};core.DocumentTitleChanged+=delegate {showAddress();};
    core.WindowCloseRequested+=delegate {popup.Close();};
    // A player inside a pop-up goes fullscreen across the monitor, not just inside the pop-up.
    bool popupFull=false;var popupPlacement=new FullscreenBounds.Placement();WindowChrome popupChrome=null;
    AddPageScript(core,FullscreenShim);AddPageScript(core,ScrollbarScript);
    core.ContainsFullScreenElementChanged+=delegate {
     bool on=core.ContainsFullScreenElement;if(on==popupFull||!alive)return;popupFull=on;var hwnd=new System.Windows.Interop.WindowInteropHelper(popup).Handle;
     if(on){popupChrome=WindowChrome.GetWindowChrome(popup);WindowChrome.SetWindowChrome(popup,new WindowChrome {CaptionHeight=0,ResizeBorderThickness=new Thickness(0),CornerRadius=new CornerRadius(0),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});layout.RowDefinitions[0].Height=new GridLength(0);frame.BorderThickness=new Thickness(0);popupPlacement=FullscreenBounds.Enter(hwnd);}
     else {FullscreenBounds.Leave(hwnd,popupPlacement);WindowChrome.SetWindowChrome(popup,popupChrome);layout.RowDefinitions[0].Height=new GridLength(40);frame.BorderThickness=new Thickness(1);}
    };
    core.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs n) {if(PopupUpgrade(core,n))return;if(!Navigation.IsWeb(n.Uri)&&n.Uri!="about:blank"&&!Navigation.IsSiteBlob(n.Uri)&&!ExternalApps.IsLaunchable(n.Uri)&&!(allowExtensionPages&&n.Uri.StartsWith("chrome-extension://",StringComparison.OrdinalIgnoreCase)))n.Cancel=true;};
    core.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs n) {if(BlockUnaskedPopup(n,core))return;if(TryOpenPopup(n,jar))return;n.Handled=true;string uri=n.Uri;if(Navigation.IsWeb(uri))window.Dispatcher.BeginInvoke(new Action(delegate {if(!closing){if(jar==null)Navigate(uri,true);else {var holder=new PageEntry {Jar=jar};OpenFromTab(holder,uri);}}}));else if(uri.StartsWith("chrome-extension://",StringComparison.OrdinalIgnoreCase)&&IsExtensionPage(core.Source))window.Dispatcher.BeginInvoke(new Action(delegate {if(!closing)OpenExtensionPage(uri);}));};
    core.DownloadStarting+=delegate(object sender,CoreWebView2DownloadStartingEventArgs d) {TrackDownload(view,d);};
    RegisterOwnTarget(core);
    return core;
  }
  void ClosePopups() {foreach(var popup in popupWindows.ToArray())popup.Close();}
 }
}
