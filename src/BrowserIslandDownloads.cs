using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Mica {
 // Downloads ride in the island: the two newest, with progress, above the memory line. A finished one stays
 // a few seconds (click it to open the file) and then leaves. While any are shown the island doesn't step aside
 // when the pointer comes near, so they can be clicked.
 partial class BrowserApp {
  const int IslandDownloadLimit=2;
  static readonly TimeSpan IslandDownloadLinger=TimeSpan.FromSeconds(8);
  StackPanel islandDownloads;
  readonly HashSet<string> islandSeenLive=new HashSet<string>();
  readonly Dictionary<string,DateTime> islandDoneAt=new Dictionary<string,DateTime>();
  readonly Dictionary<string,Border> islandDownloadRows=new Dictionary<string,Border>();
  DispatcherTimer islandDownloadsTimer;
  internal bool IslandHasDownloads {get {return islandDownloads!=null&&islandDownloads.Children.Count>0;}}

  // Speed from the bytes seen between refreshes, smoothed so the number doesn't jump every second.
  sealed class IslandSpeedSample {public DateTime At;public long Bytes;public double Rate=-1;}
  readonly Dictionary<string,IslandSpeedSample> islandSpeeds=new Dictionary<string,IslandSpeedSample>();
  internal double IslandDownloadSpeed(string id,long received,DateTime now) {
   IslandSpeedSample sample;
   if(!islandSpeeds.TryGetValue(id,out sample)||received<sample.Bytes){islandSpeeds[id]=new IslandSpeedSample {At=now,Bytes=received};return -1;}
   double seconds=(now-sample.At).TotalSeconds;if(seconds<0.75)return sample.Rate;
   double instant=(received-sample.Bytes)/seconds;
   sample.Rate=sample.Rate<0?instant:sample.Rate*0.6+instant*0.4;sample.At=now;sample.Bytes=received;
   return sample.Rate;
  }

  internal List<DownloadEntry> IslandDownloadEntries() {
   var now=DateTime.UtcNow;
   foreach(var entry in downloads)if(IsDownloadLive(entry))islandSeenLive.Add(entry.Id);
   // One that was live here and has stopped starts its linger.
   foreach(var id in islandSeenLive.ToList()) {
    var entry=downloads.FirstOrDefault(d=>d.Id==id);
    if(entry==null){islandSeenLive.Remove(id);islandDoneAt.Remove(id);continue;}
    if(!IsDownloadLive(entry)&&!islandDoneAt.ContainsKey(id))islandDoneAt[id]=now;
   }
   return downloads.Where(d=>islandSeenLive.Contains(d.Id)&&(IsDownloadLive(d)||now-islandDoneAt[d.Id]<IslandDownloadLinger))
    .OrderByDescending(d=>d.StartedUtc).Take(IslandDownloadLimit).ToList();
  }

  void RefreshIslandDownloads() {
   if(islandDownloads==null||closing)return;
   var shown=IslandDownloadEntries();var keep=new HashSet<string>(shown.Select(d=>d.Id));
   foreach(var id in islandDownloadRows.Keys.ToList())if(!keep.Contains(id)){islandDownloads.Children.Remove(islandDownloadRows[id]);islandDownloadRows.Remove(id);islandSpeeds.Remove(id);}
   for(int i=0;i<shown.Count;i++) {
    Border row;if(!islandDownloadRows.TryGetValue(shown[i].Id,out row)){row=IslandDownloadRow(shown[i]);islandDownloadRows[shown[i].Id]=row;}
    int at=islandDownloads.Children.IndexOf(row);
    if(at!=i){if(at>=0)islandDownloads.Children.RemoveAt(at);islandDownloads.Children.Insert(i,row);if(at<0){row.Opacity=0;Animate(row,UIElement.OpacityProperty,1,220);}}
    FillIslandDownloadRow(row,shown[i]);
   }
   islandDownloads.Margin=new Thickness(0,0,0,shown.Count>0?6:0);
   // The island is a hover-shy card; with downloads in it, it comes back and stays clickable.
   if(shown.Count>0&&islandAway&&islandCard!=null){islandAway=false;Animate(islandCard,UIElement.OpacityProperty,1,220);((TranslateTransform)islandCard.RenderTransform).BeginAnimation(TranslateTransform.YProperty,new System.Windows.Media.Animation.DoubleAnimation(0,TimeSpan.FromMilliseconds(220)));islandCard.IsHitTestVisible=true;}
   if(shown.Count>0||islandDoneAt.Count>0) {
    if(islandDownloadsTimer==null){islandDownloadsTimer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};islandDownloadsTimer.Tick+=delegate {RefreshIslandDownloads();};}
    if(shown.Count>0)islandDownloadsTimer.Start();else islandDownloadsTimer.Stop();
   }else if(islandDownloadsTimer!=null)islandDownloadsTimer.Stop();
   PlaceIsland();
  }

  Border IslandDownloadRow(DownloadEntry entry) {
   var row=new Border {Width=250,Padding=new Thickness(6,5,6,6),Margin=new Thickness(-6,0,-6,1),CornerRadius=new CornerRadius(8),Background=Brushes.Transparent,Cursor=Cursors.Hand,Tag="island-download:"+entry.Id};
   var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(22)});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
   grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
   var icon=new TextBlock {FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=12,VerticalAlignment=VerticalAlignment.Center};icon.SetResourceReference(TextBlock.ForegroundProperty,"Accent");Grid.SetRowSpan(icon,2);grid.Children.Add(icon);
   var name=new TextBlock {FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};name.SetResourceReference(TextBlock.ForegroundProperty,"Ink");Grid.SetColumn(name,1);grid.Children.Add(name);
   var note=new TextBlock {FontSize=10,Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};note.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Grid.SetColumn(note,2);grid.Children.Add(note);
   var track=new Grid {Height=3,Margin=new Thickness(0,4,0,0),ClipToBounds=true};Grid.SetRow(track,1);Grid.SetColumn(track,1);Grid.SetColumnSpan(track,2);
   var rail=new Border {CornerRadius=new CornerRadius(1.5)};rail.SetResourceReference(Border.BackgroundProperty,"Line");track.Children.Add(rail);
   var fill=new Border {CornerRadius=new CornerRadius(1.5),HorizontalAlignment=HorizontalAlignment.Left,Width=0};fill.SetResourceReference(Border.BackgroundProperty,"Accent");track.Children.Add(fill);
   grid.Children.Add(track);row.Child=grid;
   row.MouseEnter+=delegate {row.SetResourceReference(Border.BackgroundProperty,"Hover");};row.MouseLeave+=delegate {row.Background=Brushes.Transparent;};
   string id=entry.Id;
   row.MouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e) {
    e.Handled=true;var current=downloads.FirstOrDefault(d=>d.Id==id);
    if(current!=null&&current.Status=="Completed")OpenDownload(current);else OpenLibraryPage("mica://downloads");
   };
   return row;
  }
  void FillIslandDownloadRow(Border row,DownloadEntry entry) {
   var grid=(Grid)row.Child;var icon=(TextBlock)grid.Children[0];var name=(TextBlock)grid.Children[1];var note=(TextBlock)grid.Children[2];var track=(Grid)grid.Children[3];var fill=(Border)track.Children[1];
   bool done=entry.Status=="Completed",live=IsDownloadLive(entry);
   name.Text=entry.FileName;
   double fraction=done?1:entry.Total>0?Math.Min(1,(double)entry.Received/entry.Total):0;
   icon.Text=done?"\uE73E":entry.Status=="Paused"?"\uE769":live?"\uE896":"\uE783";
   bool moving=live&&entry.Status!="Paused";
   double rate=moving?IslandDownloadSpeed(entry.Id,entry.Received,DateTime.UtcNow):-1;if(!moving)islandSpeeds.Remove(entry.Id);
   string speed=rate>=0?" · "+FormatBytes((long)rate)+"/s":"";
   note.Text=done?"Open":entry.Status=="Paused"?"Paused":live?(entry.Total>0?(int)Math.Floor(fraction*100)+"%":FormatBytes(entry.Received))+speed:entry.Status;
   row.ToolTip=entry.FileName+"\n"+(done?"Downloaded. Click to open it.":live?FormatBytes(entry.Received)+(entry.Total>0?" of "+FormatBytes(entry.Total):"")+(rate>=0?" at "+FormatBytes((long)rate)+"/s":"")+". Click for Downloads.":string.IsNullOrEmpty(entry.Detail)?entry.Status:entry.Detail);
   double width=Math.Max(0,row.Width-row.Padding.Left-row.Padding.Right-22);
   Animate(fill,FrameworkElement.WidthProperty,width*fraction,240);
   track.Opacity=done?0.6:1;
  }
 }
}
