using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // Zoom rides in the island too: Ctrl +/-, Ctrl+wheel or the command palette bring up − 110% + and a reset
 // above the memory line, for a few seconds (longer while the pointer is on it). The menu and the site card
 // have their own zoom row, so changes made there don't bring it up.
 partial class BrowserApp {
  static readonly TimeSpan IslandZoomLinger=TimeSpan.FromSeconds(3.5);
  StackPanel islandZoom;TextBlock islandZoomLevel;DispatcherTimer islandZoomHide;WebView2 islandZoomView;
  internal bool IslandHasZoom {get {return islandZoom!=null&&islandZoom.Children.Count>0;}}

  internal void ShowIslandZoom(WebView2 view) {
   if(islandZoom==null||closing||view==null||view!=CurrentView())return;
   if(menuPopup!=null&&menuPopup.IsOpen||siteCard!=null&&siteCard.IsOpen)return;
   islandZoomView=view;
   if(!IslandHasZoom)islandZoom.Children.Add(IslandZoomRow());
   islandZoomLevel.Text=Math.Round(view.ZoomFactor*100)+"%";
   if(islandAway&&islandCard!=null){islandAway=false;Animate(islandCard,UIElement.OpacityProperty,1,220);((System.Windows.Media.TranslateTransform)islandCard.RenderTransform).BeginAnimation(TranslateTransform.YProperty,null);islandCard.IsHitTestVisible=true;}
   if(islandZoomHide==null){islandZoomHide=new DispatcherTimer {Interval=IslandZoomLinger};islandZoomHide.Tick+=delegate {if(islandCard!=null&&islandCard.IsMouseOver)return;HideIslandZoom();};}
   islandZoomHide.Stop();islandZoomHide.Start();
   PlaceIsland();
  }
  internal void HideIslandZoom() {
   if(islandZoomHide!=null)islandZoomHide.Stop();islandZoomView=null;
   if(!IslandHasZoom)return;islandZoom.Children.Clear();PlaceIsland();
  }
  // Another tab (or a page without zoom) in front: the row belonged to the old one.
  void FollowIslandZoom() {
   if(!IslandHasZoom)return;var tab=Active;
   if(islandZoomView!=CurrentView()||tab==null||!Navigation.IsWeb(tab.Url)||IsSpotlight)HideIslandZoom();
   else islandZoomLevel.Text=Math.Round(islandZoomView.ZoomFactor*100)+"%";
  }
  Grid IslandZoomRow() {
   var row=new Grid {Margin=new Thickness(-4,0,-6,6),Tag="island-zoom"};
   row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(22)});for(int i=0;i<4;i++)row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
   var icon=new TextBlock {Text="",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=12,VerticalAlignment=VerticalAlignment.Center};icon.SetResourceReference(TextBlock.ForegroundProperty,"Accent");row.Children.Add(icon);
   islandZoomLevel=new TextBlock {FontSize=11,Width=40,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Tag="island-zoom-level"};islandZoomLevel.SetResourceReference(TextBlock.ForegroundProperty,"Ink");
   Func<string,string,int,int,Button> step=delegate(string glyph,string tip,int column,int direction) {
    var b=new Button {Content=glyph,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=10,Width=24,Height=22,Padding=new Thickness(0),ToolTip=tip,Background=Brushes.Transparent,BorderThickness=new Thickness(0),
     Tag=direction>0?"island-zoom-in":direction<0?"island-zoom-out":"island-zoom-reset"};
    b.SetResourceReference(Button.ForegroundProperty,"Ink");Grid.SetColumn(b,column);row.Children.Add(b);
    b.Click+=delegate {var view=islandZoomView;if(view==null||LiveCore(view)==null){HideIslandZoom();return;}ZoomView(view,direction==0?PageBase:NextZoom(view.ZoomFactor,direction));ShowIslandZoom(view);};
    return b;
   };
   step("","Zoom out (Ctrl+Minus)",1,-1);Grid.SetColumn(islandZoomLevel,2);row.Children.Add(islandZoomLevel);step("","Zoom in (Ctrl+Plus)",3,1);step("","Reset to 100% (Ctrl+0)",4,0);
   return row;
  }
 }
}
