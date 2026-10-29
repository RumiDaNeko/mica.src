using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Mica {
 // Zoom rides in the island too, as on Windows (BrowserIslandZoom.cs): the keyboard, Ctrl+wheel or the command
 // palette bring up − 110% + and a reset above the downloads, for a few seconds (longer while the pointer is on it).
 // The menu and the site card have their own zoom row, so changes made there don't bring it up.
 sealed partial class BrowserWindow {
  static readonly TimeSpan IslandZoomLinger=TimeSpan.FromSeconds(3.5);
  readonly StackPanel islandZoom=new StackPanel();
  TextBlock islandZoomLevel;DispatcherTimer islandZoomHide;string islandZoomTab;
  internal bool IslandHasZoom {get {return islandZoom.Children.Count>0;}}

  // A zoom change the user made themselves, from the keyboard, the wheel or the palette.
  void UserZoom(int direction) {ZoomStep(direction);ShowIslandZoom();}
  void ShowIslandZoom() {
   var tab=Active;if(island==null||tab==null||tab.View==null||!Navigation.IsWeb(tab.Entry.Url)||SpotlightOpen)return;
   islandZoomTab=tab.Entry.Id;
   if(!IslandHasZoom)islandZoom.Children.Add(IslandZoomRow());
   islandZoomLevel.Text=Math.Round(tab.Zoom*100)+"%";
   if(islandAway)StepAside(false);
   if(islandZoomHide==null){islandZoomHide=new DispatcherTimer {Interval=IslandZoomLinger};islandZoomHide.Tick+=delegate {if(islandCard.IsPointerOver)return;HideIslandZoom();};}
   islandZoomHide.Stop();islandZoomHide.Start();
  }
  internal void HideIslandZoom() {
   if(islandZoomHide!=null)islandZoomHide.Stop();islandZoomTab=null;
   islandZoom.Children.Clear();
  }
  // Another tab (or a page without zoom, or Spotlight) in front: the row belonged to the old one.
  void FollowIslandZoom() {
   if(!IslandHasZoom)return;var tab=Active;
   if(tab==null||tab.Entry.Id!=islandZoomTab||!Navigation.IsWeb(tab.Entry.Url)||SpotlightOpen)HideIslandZoom();
   else islandZoomLevel.Text=Math.Round(tab.Zoom*100)+"%";
  }
  Grid IslandZoomRow() {
   var row=new Grid {ColumnDefinitions=new ColumnDefinitions("22,Auto,Auto,Auto,Auto"),Margin=new Thickness(-4,0,-6,6),Tag="island-zoom"};
   var icon=Glyph(Glyphs.Search,12,"accent");icon.HorizontalAlignment=HorizontalAlignment.Left;row.Children.Add(icon);
   islandZoomLevel=new TextBlock {FontSize=11,Width=40,TextAlignment=Avalonia.Media.TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Tag="island-zoom-level"};Res(islandZoomLevel,TextBlock.ForegroundProperty,"Ink");
   Func<string,string,int,int,Button> step=delegate(string glyph,string tip,int column,int direction) {
    var b=CaptionButton(glyph,tip);b.Width=24;b.Height=22;b.Tag=direction>0?"island-zoom-in":direction<0?"island-zoom-out":"island-zoom-reset";
    Grid.SetColumn(b,column);row.Children.Add(b);
    b.Click+=delegate {var tab=Active;if(tab==null||tab.Entry.Id!=islandZoomTab){HideIslandZoom();return;}UserZoom(direction);};
    return b;
   };
   step(Glyphs.Minimize,"Zoom out ("+Mod+"Minus)",1,-1);Grid.SetColumn(islandZoomLevel,2);row.Children.Add(islandZoomLevel);step(Glyphs.Plus,"Zoom in ("+Mod+"Plus)",3,1);step(Glyphs.Reload,"Reset to 100% ("+Mod+"0)",4,0);
   return row;
  }
  // Ctrl+wheel (and a trackpad pinch, which pages see the same way) zooms through Mica, so the level is kept for
  // the site and the island shows it, rather than Chromium zooming on its own.
  internal const string WheelZoomScript="(()=>{if(window.__micaWheel)return;window.__micaWheel=1;let at=0;addEventListener('wheel',e=>{if(!e.ctrlKey||e.defaultPrevented)return;e.preventDefault();const now=Date.now();if(now-at<120||!e.deltaY)return;at=now;console.debug('__mica_zoom:'+(e.deltaY<0?'+':'-'));},{passive:false,capture:true});})()";
  internal void WheelZoom(CrossTab tab,int direction) {if(tab==Active)UserZoom(direction);}
 }
}
