using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // The site card: everything about the site you're on in one place, from the badge at the start of the
 // address (or Sites › Site settings). Connection, zoom (kept per site), ads, this site's permissions, and
 // clearing what it stored.
 partial class BrowserApp {
  static readonly double[] ZoomSteps={0.25,0.33,0.5,0.67,0.75,0.8,0.9,1,1.1,1.25,1.5,1.75,2,2.5,3,4,5};
  Popup siteCard;

  // Zoom follows the site: each page load puts back what you chose there, and changing it (Ctrl +/-,
  // Ctrl+wheel or the card) remembers it for that site.
  // The zoom Mica last gave each view. The engine reports zoom changes later, so a report matching it is
  // Mica's own change coming back, and only a different one (Ctrl +/-, Ctrl+wheel) is the user's.
  readonly Dictionary<WebView2,double> zoomSet=new Dictionary<WebView2,double>();readonly Dictionary<WebView2,DateTime> zoomSetAt=new Dictionary<WebView2,DateTime>();
  // Only an actual change starts the quiet moment: the user's own zoom passing back through here (to remember it)
  // must not make the next Ctrl+wheel notch look like Mica's.
  void SetViewZoom(WebView2 view,double zoom) {zoomSet[view]=zoom;if(Math.Abs(view.ZoomFactor-zoom)<=0.001)return;zoomSetAt[view]=DateTime.UtcNow;view.ZoomFactor=zoom;if(CurrentView()==view)UpdateZoomBadge();}
  void AttachSiteZoom(WebView2 view) {
   Action apply=delegate {
    var core=LiveCore(view);if(core==null)return;string site=SiteFor(core.Source);if(site==null)return;
    double zoom;if(state.SiteZoom==null||!state.SiteZoom.TryGetValue(site,out zoom))zoom=PageBase;
    SetViewZoom(view,zoom);
   };
   view.CoreWebView2.SourceChanged+=delegate {apply();};
   view.ZoomFactorChanged+=delegate {
    if(CurrentView()==view)UpdateZoomBadge();
    double set;DateTime at;if(zoomSet.TryGetValue(view,out set)&&Math.Abs(view.ZoomFactor-set)<0.001)return;
    // Reports can come late and out of order; for a moment after Mica sets the zoom, none are the user's.
    if(zoomSetAt.TryGetValue(view,out at)&&(DateTime.UtcNow-at).TotalMilliseconds<1000){if(Math.Abs(view.ZoomFactor-set)>0.001)SetViewZoom(view,set);return;}
    zoomSet[view]=view.ZoomFactor;
    var core=LiveCore(view);if(core==null)return;string site=SiteFor(core.Source);if(site==null)return;
    RememberZoom(site,view.ZoomFactor);ShowIslandZoom(view);
   };
   view.Unloaded+=delegate {if(LiveCore(view)==null){zoomSet.Remove(view);zoomSetAt.Remove(view);}};
  }
  void RememberZoom(string site,double zoom) {
   if(state.SiteZoom==null)state.SiteZoom=new Dictionary<string,double>();
   if(Math.Abs(zoom-PageBase)<0.001)state.SiteZoom.Remove(site);else state.SiteZoom[site]=Math.Round(zoom,2);
   // Other tabs on the same site follow.
   foreach(var pair in views) {
    if(pair.Value==null)continue;var core=LiveCore(pair.Value);if(core==null||SiteFor(core.Source)!=site)continue;
    SetViewZoom(pair.Value,zoom);
   }
   Save();
  }
  // Zoom set by Mica itself: the engine doesn't always report it back, so it's remembered here.
  void ZoomView(WebView2 view,double zoom) {
   SetViewZoom(view,zoom);var core=LiveCore(view);string site=core==null?null:SiteFor(core.Source);
   if(site!=null)RememberZoom(site,zoom);if(CurrentView()==view)UpdateZoomBadge();
  }
  static double NextZoom(double current,int direction) {
   if(direction>0){foreach(var step in ZoomSteps)if(step>current+0.001)return step;return ZoomSteps[ZoomSteps.Length-1];}
   for(int i=ZoomSteps.Length-1;i>=0;i--)if(ZoomSteps[i]<current-0.001)return ZoomSteps[i];return ZoomSteps[0];
  }

  void InitializeSiteCard() {
   var badge=Find<Border>("SiteBadge");
   // The badge sits inside the address button; a click on it opens the card instead of Spotlight.
   badge.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) {e.Handled=true;ShowSiteCard();};
   badge.MouseEnter+=delegate {badge.SetResourceReference(Border.BackgroundProperty,"Hover");};
   badge.MouseLeave+=delegate {badge.Background=System.Windows.Media.Brushes.Transparent;};
   Find<Border>("ZoomBadge").PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) {e.Handled=true;ShowSiteCard();};
  }
  // The badge shows a lock on secure sites and a warning on plain http; beside the address, the zoom when it isn't 100%.
  void UpdateSiteBadge() {
   var glyph=Find<TextBlock>("SiteGlyph");if(glyph==null)return;var tab=Active;
   bool web=tab!=null&&Navigation.IsWeb(tab.Url);
   glyph.Text=!web?"\uE774":tab.Url.StartsWith("https:",StringComparison.OrdinalIgnoreCase)?"\uE72E":"\uE7BA";
   UpdateZoomBadge();
  }
  void UpdateZoomBadge() {
   var badge=Find<Border>("ZoomBadge");if(badge==null)return;var view=CurrentView();var tab=Active;
   double zoom=view!=null&&tab!=null&&Navigation.IsWeb(tab.Url)&&!IsSpotlight?view.ZoomFactor:PageBase;
   bool show=Math.Abs(zoom-PageBase)>0.001;badge.Visibility=show?Visibility.Visible:Visibility.Collapsed;
   if(show)Find<TextBlock>("ZoomText").Text=Math.Round(zoom*100)+"%";
   if(zoomMenuLevel!=null)zoomMenuLevel.Text=Math.Round(zoom*100)+"%";
   FollowIslandZoom();
  }
  // Zoom in the browser menu, as − 100% + on one row; the menu stays open while you click.
  TextBlock zoomMenuLevel;
  void ZoomMenuRow(StackPanel body) {
   var row=new Grid {Margin=new Thickness(12,4,6,4),Tag="menu-zoom"};
   row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(28)});row.ColumnDefinitions.Add(new ColumnDefinition());for(int i=0;i<4;i++)row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
   var symbol=DialogText("\uE8A3",14,"Accent");symbol.FontFamily=new System.Windows.Media.FontFamily("Segoe MDL2 Assets");symbol.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(symbol);
   var name=DialogText("Zoom",12,"Ink");name.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(name,1);row.Children.Add(name);
   zoomMenuLevel=DialogText("100%",11,"Ink");zoomMenuLevel.Width=40;zoomMenuLevel.TextAlignment=TextAlignment.Center;zoomMenuLevel.VerticalAlignment=VerticalAlignment.Center;
   Func<string,string,int,int,Button> step=delegate(string glyph,string tip,int column,int direction) {
    var b=new Button {Style=(Style)window.FindResource("Icon"),Content=glyph,FontSize=11,Width=28,Height=26,ToolTip=tip,Tag=direction>0?"menu-zoom-in":direction<0?"menu-zoom-out":"menu-zoom-reset"};Grid.SetColumn(b,column);row.Children.Add(b);
    b.Click+=delegate {var view=CurrentView();var tab=Active;if(view==null||tab==null||!Navigation.IsWeb(tab.Url)){Status("Open a website to zoom it.");return;}ZoomView(view,direction==0?PageBase:NextZoom(view.ZoomFactor,direction));};
    return b;
   };
   step("\uE738","Zoom out (Ctrl+Minus)",2,-1);Grid.SetColumn(zoomMenuLevel,3);row.Children.Add(zoomMenuLevel);step("\uE710","Zoom in (Ctrl+Plus)",4,1);step("\uE72C","Reset to 100% (Ctrl+0)",5,0);
   body.Children.Add(row);UpdateZoomBadge();
  }

  internal async void ShowSiteCard() {
   if(siteCard!=null&&siteCard.IsOpen){siteCard.IsOpen=false;return;}
   CloseMenus();var tab=Active;var view=CurrentView();var core=view==null?null:LiveCore(view);
   if(tab==null||!Navigation.IsWeb(tab.Url)||core==null){Status("Open a website to see its settings.");return;}
   if(siteCard==null){siteCard=CreatePopup("SiteCardPopup",330);siteCard.Placement=PlacementMode.Bottom;siteCard.VerticalOffset=6;}
   var body=PopupBody(siteCard);var uri=new Uri(tab.Url);string site=SiteFor(tab.Url),origin=uri.GetLeftPart(UriPartial.Authority);bool secure=uri.Scheme=="https";

   // Who you're talking to, and whether anyone can listen in.
   var head=new StackPanel {Margin=new Thickness(12,8,12,6)};
   var host=DialogText(Navigation.AddressLabel(origin),14,"Ink");host.FontWeight=FontWeights.SemiBold;host.TextTrimming=TextTrimming.CharacterEllipsis;head.Children.Add(host);
   var line=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,5,0,0)};
   var lockIcon=DialogText(secure?"\uE72E":"\uE7BA",11,secure?"Accent":"Muted");lockIcon.FontFamily=new System.Windows.Media.FontFamily("Segoe MDL2 Assets");lockIcon.Margin=new Thickness(0,1,6,0);line.Children.Add(lockIcon);
   line.Children.Add(DialogText(secure?"Connection is secure":"Not secure: what you send here can be read on the way",11,"Muted"));head.Children.Add(line);
   body.Children.Add(head);MenuDivider(body);

   // Zoom: − 100% + and a reset, for this site.
   var zoomRow=new Grid {Margin=new Thickness(12,4,8,4)};zoomRow.ColumnDefinitions.Add(new ColumnDefinition());for(int i=0;i<4;i++)zoomRow.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
   var zoomLabel=DialogText("Zoom",12,"Ink");zoomLabel.VerticalAlignment=VerticalAlignment.Center;zoomRow.Children.Add(zoomLabel);
   var level=DialogText("",12,"Ink");level.Width=46;level.TextAlignment=TextAlignment.Center;level.VerticalAlignment=VerticalAlignment.Center;level.Tag="site-zoom";
   Action showLevel=delegate {level.Text=Math.Round(view.ZoomFactor*100)+"%";};
   Func<string,string,int,Button> zoomButton=delegate(string glyph,string tip,int column) {
    var b=new Button {Style=(Style)window.FindResource("Icon"),Content=glyph,FontSize=12,Width=30,Height=28,ToolTip=tip};Grid.SetColumn(b,column);zoomRow.Children.Add(b);return b;
   };
   var smaller=zoomButton("\uE738","Zoom out (Ctrl+Minus)",1);Grid.SetColumn(level,2);zoomRow.Children.Add(level);var bigger=zoomButton("\uE710","Zoom in (Ctrl+Plus)",3);
   var reset=new Button {Content="Reset",Padding=new Thickness(10,4,10,4),Margin=new Thickness(6,0,0,0),Tag="site-zoom-reset"};Grid.SetColumn(reset,4);zoomRow.Children.Add(reset);
   smaller.Tag="site-zoom-out";bigger.Tag="site-zoom-in";
   smaller.Click+=delegate {ZoomView(view,NextZoom(view.ZoomFactor,-1));showLevel();};
   bigger.Click+=delegate {ZoomView(view,NextZoom(view.ZoomFactor,1));showLevel();};
   reset.Click+=delegate {ZoomView(view,PageBase);showLevel();};
   showLevel();body.Children.Add(zoomRow);

   // Ads, for this site only; the lists and other sites are in Ad blocking.
   var config=LoadBlockerConfig();
   var adsRow=new Grid {Margin=new Thickness(12,6,8,6)};adsRow.ColumnDefinitions.Add(new ColumnDefinition());adsRow.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
   var adsText=new StackPanel();adsText.Children.Add(DialogText("Ads and trackers",12,"Ink"));
   var adsNote=DialogText(!config.Enabled?"Ad blocking is off everywhere":"",10,"Muted");adsNote.Tag="site-blocked";adsText.Children.Add(adsNote);adsRow.Children.Add(adsText);
   if(config.Enabled&&!config.Excluded.Contains(site))ShowBlockedCount(view,adsNote);
   var ads=SegmentedChoice(new[]{"Block","Allow"},config.Enabled&&!config.Excluded.Contains(site)?0:1,async delegate(int picked,Action undo) {
    var now=LoadBlockerConfig();if(!now.Enabled){siteCard.IsOpen=false;ShowAdBlocking();return;}
    bool allow=picked==1;if(allow==now.Excluded.Contains(site))return;
    if(allow)now.Excluded.Add(site);else now.Excluded.Remove(site);
    try {await ApplyBlocker(now);adsNote.Text="Reload to see the change";}catch(Exception e){undo();adsNote.Text="Couldn't change it: "+e.Message;}
   });
   Grid.SetColumn(ads,1);ads.VerticalAlignment=VerticalAlignment.Center;adsRow.Children.Add(ads);body.Children.Add(adsRow);
   SiteCardExtras(body,tab,view,site);
   MenuDivider(body);

   // This site's permissions, as in Site permissions.
   MenuHeading(body,"PERMISSIONS");
   var permissions=new StackPanel {Margin=new Thickness(4,0,4,0)};body.Children.Add(permissions);
   permissions.Children.Add(DialogText("Loading…",11,"Muted"));
   MenuDivider(body);
   MenuCommand(body,siteCard,"\uE74D","Clear this site's data","",async delegate {
    try {
     await core.CallDevToolsProtocolMethodAsync("Storage.clearDataForOrigin","{\"origin\":\""+origin+"\",\"storageTypes\":\"all\"}");
     Status("Cleared cookies and data from "+site+". Reloading.");Reload();
    }catch(Exception e){Status("Couldn't clear the site's data: "+e.Message);}
   }).Tag="site-clear";
   MenuCommand(body,siteCard,"\uE713","All site permissions","",ShowSitePermissions);

   var anchor=Find<Button>("AddressButton");siteCard.PlacementTarget=anchor;siteCard.HorizontalOffset=(anchor.ActualWidth-330)/2;siteCard.IsOpen=true;
   try {
    var profileObject=core.Profile;var settings=await profileObject.GetNonDefaultPermissionSettingsAsync();
    permissions.Children.Clear();
    bool any=settings.Any(s=>string.Equals(s.PermissionOrigin.TrimEnd('/'),origin,StringComparison.OrdinalIgnoreCase));
    if(any)permissions.Children.Add(OriginPermissions(permissions,profileObject,origin,settings,false));
    else {var none=DialogText("This site hasn't asked for anything yet.",11,"Muted");none.Margin=new Thickness(8,0,8,6);permissions.Children.Add(none);}
   }catch(Exception){permissions.Children.Clear();permissions.Children.Add(DialogText("Permissions aren't available right now.",11,"Muted"));}
  }
 }
}
