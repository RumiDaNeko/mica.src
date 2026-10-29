using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // Peek: Shift+click a link and it opens in a floating preview over the page instead of a new window.
 // Esc or a click outside closes it; "Open as tab" keeps it (in the same cookie jar).
 partial class BrowserApp {
  const string PeekScript=@"(()=>{if(!window.chrome||!chrome.webview)return;
addEventListener('click',e=>{if(!e.shiftKey||e.ctrlKey||e.altKey||e.metaKey||e.button!==0||e.defaultPrevented)return;
 const a=e.target&&e.target.closest&&e.target.closest('a[href]');if(!a||!/^https?:/i.test(a.href))return;
 e.preventDefault();e.stopImmediatePropagation();try{chrome.webview.postMessage({mica:'peek',url:a.href})}catch(x){}},true);})();";
  void WatchPeek(PageEntry tab,WebView2 view,CoreWebView2 core) {
   AddPageScript(core,PeekScript);core.Settings.IsWebMessageEnabled=true;
   core.WebMessageReceived+=delegate(object sender,CoreWebView2WebMessageReceivedEventArgs e) {
    if(closing||!IsCurrent(tab.Id,view))return;
    Dictionary<string,object> message;try {message=VaultCrypto.Json.Deserialize<Dictionary<string,object>>(e.WebMessageAsJson);}catch(Exception){return;}
    object kind,url;if(message==null||!message.TryGetValue("mica",out kind)||(kind as string)!="peek"||!message.TryGetValue("url",out url))return;
    // Only for a real Shift+click: a page can post the message, but it can't hold your Shift key.
    if((GetAsyncKeyState(0x10)&0x8000)==0&&!peekTestShift)return;
    string target=url as string;if(target==null||!Navigation.IsWeb(target)||target.Length>8192)return;
    OpenPeek(tab,target);
   };
  }
  internal bool peekTestShift;
  internal Window peekWindow;
  internal async void OpenPeek(PageEntry from,string url) {
   ClosePeek();
   var owner=window;double width=Math.Max(480,owner.ActualWidth*0.72),height=Math.Max(360,owner.ActualHeight*0.8);
   var peek=new Window {Owner=owner,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Width=width,Height=height,Title="Peek",AllowsTransparency=false};
   peek.Left=owner.Left+(owner.ActualWidth-width)/2;peek.Top=owner.Top+(owner.ActualHeight-height)/2+16;
   WindowChrome.SetWindowChrome(peek,new WindowChrome {CaptionHeight=40,ResizeBorderThickness=new Thickness(0),CornerRadius=new CornerRadius(10),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});
   peek.SetResourceReference(Window.BackgroundProperty,"Canvas");peek.SetResourceReference(Window.ForegroundProperty,"Ink");
   var frame=new Border {BorderThickness=new Thickness(1)};frame.SetResourceReference(Border.BorderBrushProperty,"Line");peek.Content=frame;
   var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition {Height=new GridLength(40)});layout.RowDefinitions.Add(new RowDefinition());frame.Child=layout;
   var bar=new Grid();bar.ColumnDefinitions.Add(new ColumnDefinition());bar.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});layout.Children.Add(bar);
   var address=new TextBlock {Text=Navigation.AddressLabel(url),Margin=new Thickness(16,0,8,0),VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,FontSize=12};address.SetResourceReference(TextBlock.ForegroundProperty,"Muted");bar.Children.Add(address);
   var actions=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,8,0)};Grid.SetColumn(actions,1);bar.Children.Add(actions);
   var keep=new Button {Content="Open as tab",Padding=new Thickness(12,4,12,4),Margin=new Thickness(0,0,6,0),VerticalAlignment=VerticalAlignment.Center,Tag="peek-open"};WindowChrome.SetIsHitTestVisibleInChrome(keep,true);actions.Children.Add(keep);
   var close=new Button {Style=(Style)owner.FindResource("CaptionButton"),Content="",ToolTip="Close (Esc)",Tag="peek-close"};WindowChrome.SetIsHitTestVisibleInChrome(close,true);actions.Children.Add(close);
   var view=new WebView2 {DefaultBackgroundColor=System.Drawing.Color.White};Grid.SetRow(view,1);layout.Children.Add(view);
   bool alive=true;peekWindow=peek;
   peek.Closed+=delegate {alive=false;if(peekWindow==peek)peekWindow=null;ReplaceTargetWatcher(view);try {view.Dispose();}catch(Exception){}if(!closing)owner.Activate();};
   close.Click+=delegate {peek.Close();};
   keep.Click+=delegate {string at=url;try {var c=LiveCore(view);if(c!=null&&Navigation.IsWeb(c.Source))at=c.Source;}catch(Exception){}peek.Close();OpenFromTab(from,at);};
   peek.PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {if(e.Key==Key.Escape){e.Handled=true;peek.Close();}};
   peek.Deactivated+=delegate {if(alive&&!closing&&!peekStaysOpen)window.Dispatcher.BeginInvoke(new Action(delegate {if(alive&&!peek.IsActive)peek.Close();}),System.Windows.Threading.DispatcherPriority.Background);};
   peek.Opacity=0;peek.Show();Animate(peek,UIElement.OpacityProperty,1,140);
   try {
    var engine=await EnsureEnvironment();if(!alive)return;
    await view.EnsureCoreWebView2Async(engine,ViewOptions(engine,from==null?null:from.Jar));if(!alive)return;
    await ApplyMicaAgent(view.CoreWebView2);var core=view.CoreWebView2;RegisterOwnTarget(core);ApplyTrackingPrevention(core);
    core.Settings.IsWebMessageEnabled=false;core.Settings.AreHostObjectsAllowed=false;core.Settings.IsGeneralAutofillEnabled=false;
    core.SourceChanged+=delegate {if(alive&&Navigation.IsWeb(core.Source))address.Text=Navigation.AddressLabel(core.Source);};
    // A preview is only for looking: no permissions, downloads or app links, and only a click opens another page.
    core.PermissionRequested+=delegate(object sender,CoreWebView2PermissionRequestedEventArgs e) {e.State=CoreWebView2PermissionState.Deny;e.Handled=true;};
    core.DownloadStarting+=delegate(object sender,CoreWebView2DownloadStartingEventArgs e) {e.Cancel=true;window.Dispatcher.BeginInvoke(new Action(delegate {Status("Open the preview as a tab to download from it.");}));};
    core.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e) {if(!Navigation.IsWeb(e.Uri)&&e.Uri!="about:blank")e.Cancel=true;};
    core.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e) {e.Handled=true;string next=e.Uri;if(e.IsUserInitiated&&Navigation.IsWeb(next))window.Dispatcher.BeginInvoke(new Action(delegate {if(alive)peek.Close();OpenFromTab(from,next);}));};
    core.Navigate(url);
   }catch(Exception e){if(alive){peek.Close();Status("Couldn't preview the link: "+e.Message);}}
  }
  internal bool peekStaysOpen;
  void ClosePeek() {if(peekWindow!=null){var p=peekWindow;peekWindow=null;try {p.Close();}catch(Exception){}}}
 }
}
