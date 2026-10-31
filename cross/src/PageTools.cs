using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Handlers;

namespace Mica {
 // Tools for the page in front, as on Windows (BrowserTools.cs, BrowserSiteCard.cs, BrowserMemory.cs): find, zoom,
 // print, DevTools, bookmarks, mute, the error page, site icons and resting background tabs.
 sealed partial class BrowserWindow {
  // ── Find in page: a slim bar under the toolbar.
  readonly Border findBar=new Border {IsVisible=false,Padding=new Thickness(12,6,8,6),BorderThickness=new Thickness(0,0,0,1)};
  readonly TextBox findInput=new TextBox {Width=260,Watermark="Find in page",Padding=new Thickness(8,5),VerticalContentAlignment=VerticalAlignment.Center};
  readonly TextBlock findCount=new TextBlock {Text="0 / 0",FontSize=11,Margin=new Thickness(12,0),VerticalAlignment=VerticalAlignment.Center,MinWidth=48};
  readonly CheckBox findCase=new CheckBox {Content="Match case",FontSize=11,Margin=new Thickness(4,0,8,0),VerticalAlignment=VerticalAlignment.Center};
  void BuildFindBar() {
   Res(Res(findBar,Border.BackgroundProperty,"Canvas"),Border.BorderBrushProperty,"Line");
   var row=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
   var lens=Glyph(Glyphs.Find,13,"accent");lens.Margin=new Thickness(0,0,8,0);row.Children.Add(lens);
   findInput.Classes.Add("field");row.Children.Add(findInput);Muted(findCount);row.Children.Add(findCount);row.Children.Add(findCase);
   var up=CaptionButton(Glyphs.Chevron,"Previous (Shift+Enter)");up.RenderTransform=new RotateTransform(180);var down=CaptionButton(Glyphs.Chevron,"Next (Enter)");var close=CaptionButton(Glyphs.Close,"Close (Esc)");
   up.Click+=delegate {FindNext(false);};down.Click+=delegate {FindNext(true);};close.Click+=delegate {CloseFind();};
   row.Children.Add(up);row.Children.Add(down);row.Children.Add(close);
   findBar.Child=row;
   findInput.TextChanged+=delegate {Find(true,false);};
   findCase.IsCheckedChanged+=delegate {Find(true,false);};
   findInput.KeyDown+=delegate(object s,KeyEventArgs e) {
    if(e.Key==Key.Enter){FindNext((e.KeyModifiers&KeyModifiers.Shift)==0);e.Handled=true;}
    else if(e.Key==Key.Escape){CloseFind();e.Handled=true;}
   };
  }
  void ShowFind() {
   var tab=Active;if(tab==null||tab.View==null||tab.Entry.Url=="mica://home")return;
   findBar.IsVisible=true;TakeKeyboard();findInput.Focus();findInput.SelectAll();Find(true,false);
  }
  void FindNext(bool forward) {if(!findBar.IsVisible){ShowFind();return;}Find(forward,true);}
  // Chromium's own find reports to Chrome's find bar, which an embedded browser doesn't have, so Mica finds with
  // the page's window.find (which selects and scrolls to each match) and counts the matches itself.
  int findIndex,findTotal;
  async void Find(bool forward,bool next) {
   var tab=Active;if(tab==null||tab.View==null)return;
   string text=findInput.Text??"";bool matchCase=findCase.IsChecked==true;
   if(text.Length==0){tab.View.ExecuteJavaScript("getSelection().removeAllRanges()","",1);findIndex=findTotal=0;findCount.Text="0 / 0";return;}
   string t=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(text),mc=matchCase?"true":"false";
   string script=next?"return String(window.find("+t+","+mc+","+(forward?"false":"true")+",true))"
    :"const s=getSelection();s.removeAllRanges();const hay=document.body?document.body.innerText:'';const needle="+t+";let n=0,i=0;"
     +"const h="+(matchCase?"hay":"hay.toLowerCase()")+",q="+(matchCase?"needle":"needle.toLowerCase()")+";while((i=h.indexOf(q,i))>=0){n++;i+=q.length;}"
     +"if(n>0)window.find(needle,"+mc+",false,true);return String(n)";
   string answer;
   try {answer=await tab.View.EvaluateJavaScript<string>(script,(string)null,"about:blank",1,TimeSpan.FromSeconds(5));} catch(Exception) {return;}
   if(tab!=Active||(findInput.Text??"")!=text)return;
   if(!next){int n;int.TryParse(answer,out n);findTotal=n;findIndex=n>0?1:0;}
   else if(findTotal>0&&answer=="true")findIndex=forward?findIndex%findTotal+1:(findIndex+findTotal-2)%findTotal+1;
   findCount.Text=findTotal==0?"0 / 0":findIndex+" / "+findTotal;
  }
  void CloseFind() {
   if(!findBar.IsVisible)return;findBar.IsVisible=false;
   var view=ActiveView();if(view!=null)view.ExecuteJavaScript("getSelection().removeAllRanges()","",1);
   if(view!=null&&!SpotlightOpen)view.Focus();
  }

  // ── Zoom, per site, as on Windows (state.SiteZoom). Chromium measures zoom in levels: factor = 1.2^level.
  static readonly double[] ZoomSteps={0.25,0.33,0.5,0.67,0.75,0.8,0.9,1,1.1,1.25,1.5,1.75,2,2.5,3,4,5};
  static string SiteOf(string url) {Uri uri;if(!Navigation.IsWeb(url)||!Uri.TryCreate(url,UriKind.Absolute,out uri))return null;string host=uri.Host.ToLowerInvariant();return host.StartsWith("www.")?host.Substring(4):host;}
  static double NextZoom(double current,int direction) {
   if(direction>0){foreach(var step in ZoomSteps)if(step>current+0.001)return step;return ZoomSteps[ZoomSteps.Length-1];}
   for(int i=ZoomSteps.Length-1;i>=0;i--)if(ZoomSteps[i]<current-0.001)return ZoomSteps[i];return ZoomSteps[0];
  }
  void ZoomStep(int direction) {
   var tab=Active;if(tab==null||tab.View==null||!Navigation.IsWeb(tab.Entry.Url))return;
   double zoom=direction==0?1:NextZoom(tab.Zoom,direction);string site=SiteOf(tab.Entry.Url);
   if(site!=null){if(Math.Abs(zoom-1)<0.001)state.SiteZoom.Remove(site);else state.SiteZoom[site]=zoom;}
   foreach(var t in tabs.Values)if(t.View!=null&&SiteOf(t.Entry.Url)==site)SetZoom(t,zoom);
   UpdateZoomBadge();Status("Zoom "+Math.Round(zoom*100)+"% for "+site+".");Save();
  }
  void ApplySiteZoom(CrossTab tab) {
   double zoom;string site=SiteOf(tab.Entry.Url);
   SetZoom(tab,site!=null&&state.SiteZoom.TryGetValue(site,out zoom)?zoom:1);
   if(tab==Active)UpdateZoomBadge();
  }
  static void SetZoom(CrossTab tab,double zoom) {
   tab.Zoom=zoom;if(tab.View==null||!tab.View.IsBrowserInitialized)return;
   // Text size (Personalize) opens pages larger too.
   tab.View.ZoomLevel=Math.Log(zoom*PageScale)/Math.Log(1.2);
  }
  void UpdateZoomBadge() {
   var tab=Active;bool show=tab!=null&&Navigation.IsWeb(tab.Entry.Url)&&Math.Abs(tab.Zoom-1)>0.001;
   zoomBadge.IsVisible=show;if(show){zoomText.Text=Math.Round(tab.Zoom*100)+"%";ToolTip.SetTip(zoomBadge,"Zoom for this site ("+Mod+"0 resets)");}
   FollowIslandZoom();
  }

  // ── Bookmarks, mute, duplicate, copy, reopen.
  void Bookmark() {
   var t=Active;if(t==null||!Navigation.IsWeb(t.Entry.Url))return;
   var found=state.Bookmarks.FirstOrDefault(b=>b.Url==t.Entry.Url);
   if(found!=null){state.Bookmarks.Remove(found);Status("Bookmark removed.");}
   else{state.Bookmarks.Add(new PageEntry {Title=t.Entry.Title,Url=t.Entry.Url,LastUsed=DateTime.UtcNow});Status("Bookmarked. Find it in Spotlight.");}
   ShowAddress();Save();
  }
  void ToggleMute(CrossTab tab) {
   if(tab==null||tab.View==null)return;var host=tab.View.Host;if(host==null)return;
   tab.Muted=!tab.Muted;host.SetAudioMuted(tab.Muted);RefreshTab(tab);
  }
  void DuplicateTab(CrossTab tab) {if(tab==null||!Navigation.IsWeb(tab.Entry.Url))return;Navigate(tab.Entry.Url,true);}
  async void CopyAddress(CrossTab tab) {
   if(tab==null||!Navigation.IsWeb(tab.Entry.Url))return;
   try {var clipboard=Clipboard;if(clipboard!=null){await clipboard.SetTextAsync(tab.Entry.Url);Status("Address copied.");}}
   catch(Exception) {Status("Couldn't copy the address.");}
  }
  void ReopenClosedTab() {
   if(state.RecentlyClosed.Count==0){Status("No closed tabs to bring back.");return;}
   var last=state.RecentlyClosed[0];state.RecentlyClosed.RemoveAt(0);Navigate(last.Url,true);UpdateHomeSites();
  }
  // Chromium's DevTools, in a window of their own.
  void ShowDevTools() {var view=ActiveView();if(view!=null&&view.IsBrowserInitialized)view.ShowDeveloperTools();}

  // ── Keyboard shortcuts: a card listing them, as on Windows' new tab page.
  void ShowShortcuts() {
   MenuCard(addressButton,340,PlacementMode.Bottom,delegate(StackPanel body,Popup popup) {
    MenuHeading(body,"KEYBOARD SHORTCUTS");
    var keys=new[]{
     new[]{"Spotlight",Mod+"K"},new[]{"Edit address",Mod+"L"},new[]{"Command palette",Mod+"K, then >"},new[]{"New tab",Mod+"T"},new[]{"Close tab",Mod+"W"},
     new[]{"Reopen closed tab",ModShift+"T"},new[]{"Next / previous tab","Ctrl+Tab / Ctrl+Shift+Tab"},new[]{"Tab 1–8, last tab",Mod+"1–8, "+Mod+"9"},
     new[]{"Search tabs",ModShift+"A"},new[]{"Bookmark",Mod+"D"},new[]{"Find in page",Mod+"F"},new[]{"Zoom in / out / reset",Mod+"+ / − / 0"},
     new[]{"Reload",Mac?"⌘R":"F5"},new[]{"Back / forward",Mac?"⌘[ / ⌘]":"Alt+← / →"},new[]{"History",Mac?"⌘Y":"Ctrl+H"},new[]{"Downloads",Mac?"⌥⌘L":"Ctrl+J"},
     new[]{"Print",Mod+"P"},new[]{"Hide sidebar",ModShift+"B"},new[]{"AI side panel",ModShift+"Space"},new[]{"Developer tools","F12"},new[]{"Full screen","F11"},new[]{"scrubnote (page animations)",ScrubnoteKey}};
    foreach(var k in keys) {
     var row=new Grid {Margin=new Thickness(12,5)};row.Children.Add(Muted(new TextBlock {Text=k[0],FontSize=12}));
     row.Children.Add(new TextBlock {Text=k[1],FontSize=12,HorizontalAlignment=HorizontalAlignment.Right});body.Children.Add(row);
    }
   });
  }

  // ── Clear browsing data: history, cookies and site data, cached files.
  void ShowClearData() {
   var history=new CheckBox {Content="Browsing history",IsChecked=true,Margin=new Thickness(12,4)};
   var cookies=new CheckBox {Content="Cookies and site data",IsChecked=true,Margin=new Thickness(12,4)};
   var cache=new CheckBox {Content="Cached images and files",IsChecked=true,Margin=new Thickness(12,4)};
   MenuCard(addressButton,320,PlacementMode.Bottom,delegate(StackPanel body,Popup popup) {
    MenuHeading(body,"CLEAR BROWSING DATA");
    body.Children.Add(Muted(new TextBlock {Text="From all time. Bookmarks and downloaded files stay.",FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12,0,12,8)}));
    body.Children.Add(history);body.Children.Add(cookies);body.Children.Add(cache);
    var go=new Button {Content="Clear data",Margin=new Thickness(12,10,12,6),Padding=new Thickness(18,8)};go.Classes.Add("flat");go.Classes.Add("on");
    go.Click+=delegate {popup.IsOpen=false;ClearData(history.IsChecked==true,cookies.IsChecked==true,cache.IsChecked==true);};
    body.Children.Add(go);
   });
  }
  void ClearData(bool history,bool cookies,bool cache) {
   if(history){state.History.Clear();state.RecentlyClosed.Clear();UpdateHomeSites();UpdateLibrary();}
   if(cookies){var jar=CefCookieManager.GetGlobal(null);if(jar!=null){jar.DeleteCookies("","",null);jar.FlushStore(null);}}
   var view=ActiveView();var host=view==null?null:view.Host;
   if(host!=null) {
    if(cache)host.ExecuteDevToolsMethod(0,"Network.clearBrowserCache",null);
    if(cookies)host.ExecuteDevToolsMethod(0,"Storage.clearDataForOrigin",DevToolsParams("origin","*","storageTypes","local_storage,indexeddb,cache_storage,service_workers,websql,file_systems"));
   }
   Save();Status("Browsing data cleared.");
  }
  static CefDictionaryValue DevToolsParams(params string[] pairs) {
   var d=CefDictionaryValue.Create();for(int i=0;i+1<pairs.Length;i+=2)d.SetString(pairs[i],pairs[i+1]);return d;
  }

  // ── The error page, as on Windows: the page steps aside for "A small detour." and a way back.
  readonly Border errorPanel=new Border {IsVisible=false};
  readonly TextBlock errorTitle=new TextBlock {Text="A small detour.",FontSize=32,TextWrapping=TextWrapping.Wrap},errorText=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16)};
  readonly Button errorGames=new Button {Content="Play a game while you wait",Margin=new Thickness(8,0,0,0)},errorCert=new Button {Content="Go anyway",Margin=new Thickness(8,0,0,0)};
  void BuildErrorPanel() {
   Res(errorPanel,Border.BackgroundProperty,"Canvas");
   var stack=new StackPanel {VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,MaxWidth=480};
   Res(errorTitle,TextBlock.FontFamilyProperty,"SerifFont");stack.Children.Add(errorTitle);Muted(errorText);stack.Children.Add(errorText);
   var actions=new StackPanel {Orientation=Orientation.Horizontal};
   var retry=new Button {Content="Try again"};retry.Classes.Add("flat");retry.Classes.Add("on");retry.Click+=delegate {Reload();};
   errorGames.Classes.Add("flat");errorGames.Classes.Add("muted");errorGames.Click+=delegate {Navigate("mica://games");};
   errorCert.Classes.Add("flat");errorCert.Classes.Add("muted");errorCert.Click+=delegate {ContinueDespiteCertificate();};
   actions.Children.Add(retry);actions.Children.Add(errorCert);actions.Children.Add(errorGames);stack.Children.Add(actions);
   errorPanel.Child=stack;
  }
  static bool ConnectionError(CefErrorCode code) {
   return code==CefErrorCode.InternetDisconnected||code==CefErrorCode.NameNotResolved||code==CefErrorCode.ConnectionRefused||code==CefErrorCode.AddressUnreachable
    ||code==CefErrorCode.ConnectionReset||code==CefErrorCode.ConnectionTimedOut||code==CefErrorCode.TimedOut||code==CefErrorCode.NetworkChanged;
  }
  void LoadFailed(CrossTab tab,CefErrorCode code,string text,string failed) {
   tab.Error=code;tab.CertUrl=null;
   if(CertificateError(code)){CertificateFailed(tab,code,failed);return;}
   tab.ErrorText=code==CefErrorCode.InternetDisconnected?"Mica can't reach the internet. Check your Wi-Fi or cable, then try again."
    :code==CefErrorCode.NameNotResolved?"We couldn't find "+HostOf(failed)+". Check the address, or your connection."
    :ConnectionError(code)?HostOf(failed)+" didn't answer. Check your connection, then try again."
    :code.ToString().Contains("Ssl")?HostOf(failed)+" didn't prove it is who it says it is, so Mica stopped here to keep you safe."
    :"This page couldn't load ("+text+"). Check the address and your connection, then try again.";
   if(tab==Active)Show(tab.Entry.Id);
  }
  static string HostOf(string url) {Uri uri;return Uri.TryCreate(url??"",UriKind.Absolute,out uri)&&!string.IsNullOrEmpty(uri.Host)?uri.Host:"This site";}
  void ShowError(CrossTab tab) {
   bool show=tab!=null&&tab.Error!=null&&!SpotlightOpen;errorPanel.IsVisible=show;
   if(!show)return;
   bool cert=CertificateError(tab.Error.Value);errorTitle.Text=cert?"This connection isn't private.":"A small detour.";
   errorText.Text=tab.ErrorText;errorGames.IsVisible=ConnectionError(tab.Error.Value);
   errorCert.IsVisible=cert&&tab.CertUrl!=null;if(errorCert.IsVisible)errorCert.Content="Go to "+HostOf(tab.CertUrl)+" anyway";
   if(tab.View!=null)tab.View.IsVisible=false;
  }
  void HideError(CrossTab tab) {
   if(tab==null||tab.Error==null)return;tab.Error=null;tab.CertUrl=null;
   if(tab==Active){errorPanel.IsVisible=false;if(tab.View!=null&&!SpotlightOpen)tab.View.IsVisible=true;}
  }
  // A certificate that doesn't check out stops the page, as on Windows (BrowserProtection.cs). The user can still go
  // on to that host for the rest of the session (a router's or printer's own page, a site whose certificate just
  // expired), never for a revoked one. A private window is a process of its own, so it keeps its own list.
  static readonly HashSet<string> certAllowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  static bool CertificateOverridden(string host) {lock(certAllowed)return host!=null&&certAllowed.Contains(host);}
  static bool CertificateError(CefErrorCode code) {return code<=CefErrorCode.CertCommonNameInvalid&&code>=CefErrorCode.CertEnd;}
  // Chrome style shows Chromium's own warning page for a certificate the handler leaves alone, so every one is
  // answered here: on for a host the user allowed, otherwise cancelled and Mica's page says why.
  sealed class Certificates : RequestHandler {
   readonly BrowserWindow window;readonly CrossTab tab;
   public Certificates(BrowserWindow w,CrossTab t) {window=w;tab=t;}
   protected override bool OnCertificateError(CefBrowser browser,CefErrorCode certError,string requestUrl,CefSslInfo sslInfo,CefCallback callback) {
    Uri uri;bool allowed=certError!=CefErrorCode.CertRevoked&&Uri.TryCreate(requestUrl??"",UriKind.Absolute,out uri)&&CertificateOverridden(uri.Host);
    window.Trace("certificate "+certError+" "+requestUrl+(allowed?" (allowed)":""));
    if(allowed){callback.Continue();return true;}
    callback.Cancel();
    Dispatcher.UIThread.Post(delegate {if(window.tabs.ContainsKey(tab.Entry.Id))window.LoadFailed(tab,certError,"",requestUrl);});
    return true;
   }
  }
  void CertificateFailed(CrossTab tab,CefErrorCode code,string url) {
   string host=HostOf(url);
   string why=code==CefErrorCode.CertDateInvalid?host+"'s security certificate has expired, or this computer's clock is wrong."
    :code==CefErrorCode.CertCommonNameInvalid?"The certificate "+host+" sent belongs to a different address."
    :code==CefErrorCode.CertRevoked?host+"'s security certificate was withdrawn by whoever issued it."
    :host+"'s security certificate isn't from anyone this computer trusts.";
   tab.ErrorText=why+" Someone could be pretending to be this site to see what you send. Go back unless you know why, like a device on your own network.";
   Uri uri;if(code!=CefErrorCode.CertRevoked&&Uri.TryCreate(url??"",UriKind.Absolute,out uri)&&!string.IsNullOrEmpty(uri.Host))tab.CertUrl=url;
   if(tab==Active){Show(tab.Entry.Id);Status(host+" didn't prove it is who it says it is.");}
  }
  void ContinueDespiteCertificate() {
   var tab=Active;if(tab==null||tab.CertUrl==null)return;string url=tab.CertUrl;
   lock(certAllowed)certAllowed.Add(new Uri(url).Host);
   Navigate(url);
  }
  // What the site card says about the connection.
  static string ConnectionLine(string url) {
   Uri uri;bool https=Uri.TryCreate(url??"",UriKind.Absolute,out uri)&&uri.Scheme=="https";
   return https&&CertificateOverridden(uri.Host)?"Not secure: you went on despite this site's certificate"
    :https?"Connection is secure. What you send here stays between you and this site.":"Not secure. Don't type passwords or card numbers here.";
  }
  // Kept for the self-test's offline checks: a load that fails shows the error page.
  void ShowLoadError(CefErrorCode code,string text) {var tab=Active;if(tab!=null)LoadFailed(tab,code,text,tab.Entry.Url);}

  // ── Site icons: the page names them; Mica fetches the first one it can draw.
  static readonly HttpClient iconClient=new HttpClient {Timeout=TimeSpan.FromSeconds(8)};
  static readonly Dictionary<string,IImage> iconCache=new Dictionary<string,IImage>();
  sealed class PageDisplay : DisplayHandler {
   readonly BrowserWindow window;readonly CrossTab tab;
   public PageDisplay(BrowserWindow w,CrossTab t) {window=w;tab=t;}
   protected override void OnFaviconUrlChange(CefBrowser browser,string[] iconUrls) {
    var urls=(iconUrls??new string[0]).Where(u=>u.StartsWith("https://")||u.StartsWith("http://")).ToArray();
    Dispatcher.UIThread.Post(delegate {window.LoadFavicon(tab,urls);});
   }
   protected override bool OnConsoleMessage(CefBrowser browser,CefLogSeverity level,string message,string source,int line) {
    if(message.StartsWith("__mica_ai:",StringComparison.Ordinal)){string json=message.Substring(10);Dispatcher.UIThread.Post(delegate {window.AiSelection(tab,json);});return true;}
    if(message=="__mica_print"){Dispatcher.UIThread.Post(delegate {if(tab==window.Active)window.Print();});return true;}
    if(message=="__mica_zoom:+"||message=="__mica_zoom:-"){int direction=message.EndsWith("+")?1:-1;Dispatcher.UIThread.Post(delegate {window.WheelZoom(tab,direction);});return true;}
    if(message=="__mica_media:1"||message=="__mica_media:0"){bool on=message.EndsWith("1");Dispatcher.UIThread.Post(delegate {window.MediaChanged(tab,on);});return true;}
    return false;
   }
   protected override void OnFullscreenModeChange(CefBrowser browser,bool fullscreen) {
    Dispatcher.UIThread.Post(delegate {window.PageFullscreen(fullscreen);});
   }
  }
  async void LoadFavicon(CrossTab tab,string[] urls) {
   foreach(var url in urls.OrderBy(u=>u.EndsWith(".svg",StringComparison.OrdinalIgnoreCase)?1:0)) {
    if(url.EndsWith(".svg",StringComparison.OrdinalIgnoreCase))continue;
    IImage image;
    if(!iconCache.TryGetValue(url,out image)) {
     try {
      var bytes=await iconClient.GetByteArrayAsync(url);
      if(bytes.Length>512*1024)continue;
      image=await Task.Run(()=>(IImage)new Bitmap(new MemoryStream(bytes)));
     } catch(Exception) {continue;}
     iconCache[url]=image;
    }
    tab.Favicon=image;tab.FaviconUrl=url;RefreshTab(tab);return;
   }
  }
  // A page's own full screen (a video) fills the window, as in Chrome: the sidebar and toolbar step away.
  WindowState beforeFullscreen;
  void PageFullscreen(bool on) {
   islandFullscreen=on;RefreshIsland();HideAiBar();
   if(on){beforeFullscreen=WindowState;WindowState=WindowState.FullScreen;layout.Margin=new Thickness(0);sidebar.IsVisible=false;layout.ColumnDefinitions[0].Width=new GridLength(0);((Grid)((Border)layout.Children[1]).Child).RowDefinitions[0].Height=new GridLength(0);((Border)layout.Children[1]).CornerRadius=new CornerRadius(0);}
   else{WindowState=beforeFullscreen;layout.Margin=new Thickness(7);sidebar.IsVisible=true;((Grid)((Border)layout.Children[1]).Child).RowDefinitions[0].Height=new GridLength(44);((Border)layout.Children[1]).CornerRadius=new CornerRadius(12);SetSidebar(!sidebarHidden);}
   if(aiOpen)LayoutAi();
  }

  // ── Resting: background tabs left alone for a while give their page back (Core's TabLifecycle), unless they
  // are playing something, loading, or downloading. They reload when picked.
  DispatcherTimer restTimer;
  void StartResting() {restTimer=new DispatcherTimer {Interval=TimeSpan.FromMinutes(1)};restTimer.Tick+=delegate {RestIdleTabs(false);};restTimer.Start();}
  async void RestIdleTabs(bool all) {
   if(downloads.Any(IsDownloadLive))return;
   int count=0;
   foreach(var tab in tabs.Values.ToList()) {
    if(tab.View==null||tab.Loading||tab.Entry.Id==state.ActiveId)continue;
    string memory;state.SiteMemory.TryGetValue(SiteOf(tab.Entry.Url)??"",out memory);
    if(memory=="never")continue;
    if(!TabLifecycle.ShouldSleep(tab.Entry,false,false,DateTime.UtcNow,all,memory=="quick"?TimeSpan.FromMinutes(2):TabLifecycle.RestAfter(false)))continue;
    if(tab.Playing||await Playing(tab))continue;
    if(tab.View==null||tab.Entry.Id==state.ActiveId)continue;
    pages.Children.Remove(tab.View);tab.View.Dispose();tab.View=null;tab.Loading=false;tab.Playing=false;count++;
    RefreshTab(tab);
   }
   if(all)Status(count==0?"No background tabs to rest.":count+(count==1?" background tab":" background tabs")+" put to rest. They reload when picked.");
  }
  // Media playing on the page (or in its frames we can see), or audio Mica can't see into: kept awake.
  static async Task<bool> Playing(CrossTab tab) {
   try {
    string answer=await tab.View.EvaluateJavaScript<string>("return String([...document.querySelectorAll('video,audio')].some(m=>!m.paused&&!m.ended)||!!(navigator.mediaSession&&navigator.mediaSession.playbackState==='playing'))",(string)null,"about:blank",1,TimeSpan.FromSeconds(3));
    return answer!="false";
   } catch(Exception) {return true;}
  }

  // Opens a file or folder with the system (Finder, Explorer, the desktop's file manager); reveal selects it.
  static void OpenPath(string path,bool reveal) {
   try {
    if(OperatingSystem.IsWindows()) {
     if(reveal)Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+path+"\"") {UseShellExecute=false});
     else Process.Start(new ProcessStartInfo(path) {UseShellExecute=true});
    } else if(OperatingSystem.IsMacOS()) {
     var start=new ProcessStartInfo("open") {UseShellExecute=false};if(reveal)start.ArgumentList.Add("-R");start.ArgumentList.Add(path);Process.Start(start);
    } else {
     var start=new ProcessStartInfo("xdg-open") {UseShellExecute=false};start.ArgumentList.Add(reveal?Path.GetDirectoryName(path):path);Process.Start(start);
    }
   } catch(Exception) {}
  }

  // Mica's scrollbar on every page (src/BrowserPersonalize.cs ScrollbarScript): a slim rounded thumb on a clear track
  // that firms up on hover, no arrow buttons. Standard scrollbar-color/width would override these rules, so they are reset.
  internal const string ScrollbarScript=@"(function(){
 if(window.__micaScrollbar)return;window.__micaScrollbar=1;
 var css='*{scrollbar-color:auto!important;scrollbar-width:auto!important}'+
  '::-webkit-scrollbar{width:11px!important;height:11px!important;background:transparent!important}'+
  '::-webkit-scrollbar-track,::-webkit-scrollbar-corner{background:transparent!important;border:0!important}'+
  '::-webkit-scrollbar-thumb{background:rgba(124,114,142,.42)!important;border-radius:8px!important;border:3px solid transparent!important;background-clip:padding-box!important;min-height:36px}'+
  '::-webkit-scrollbar-thumb:hover{background:rgba(124,114,142,.72)!important;border-width:2px!important;background-clip:padding-box!important}'+
  '::-webkit-scrollbar-thumb:active{background:rgba(113,96,140,.88)!important;background-clip:padding-box!important}'+
  '::-webkit-scrollbar-button{display:none!important;width:0!important;height:0!important}';
 var style=document.createElement('style');style.setAttribute('data-mica','scrollbar');style.textContent=css;
 function put(){var host=document.head||document.documentElement;if(host&&style.parentNode!==host)host.appendChild(style);}
 if(document.documentElement)put();
 new MutationObserver(function(m,o){if(document.head){put();o.disconnect();}else put();}).observe(document,{childList:true,subtree:true});
 document.addEventListener('DOMContentLoaded',put);
})();";
 }
}