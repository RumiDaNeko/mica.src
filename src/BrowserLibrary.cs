using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace Mica {
 partial class BrowserApp {
  Grid libraryPage;
  string libraryKind,libraryFilter="All";
  bool libraryUpdating;
  T Library<T>(string name) where T:FrameworkElement {return (T)libraryPage.FindName(name);}
  void InitializeLibrary() {
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Mica.Library.xaml"))libraryPage=(Grid)XamlReader.Load(stream);
   libraryPage.Visibility=Visibility.Collapsed;Find<Grid>("PageSurface").Children.Add(libraryPage);
   Library<Button>("HistorySection").Click+=delegate {OpenLibraryPage("mica://history");};
   Library<Button>("DownloadsSection").Click+=delegate {OpenLibraryPage("mica://downloads");};
   Library<TextBox>("LibrarySearch").TextChanged+=delegate {if(!libraryUpdating)RenderLibrary();};
   Library<Button>("ClearLibrarySearch").Click+=delegate {Library<TextBox>("LibrarySearch").Clear();Library<TextBox>("LibrarySearch").Focus();};
   foreach(string name in new[]{"FilterAll","FilterSecond","FilterThird"}){string filter=name;Library<Button>(name).Click+=delegate {libraryFilter=filter=="FilterAll"?"All":filter=="FilterSecond"?"Second":"Third";RenderLibrary();};}
   Library<Button>("LibraryClear").Click+=delegate {if(libraryKind=="mica://history")Library<Border>("ClearConfirmation").Visibility=Visibility.Visible;else ClearFinishedDownloads();};
   Library<Button>("CancelClearHistory").Click+=delegate {Library<Border>("ClearConfirmation").Visibility=Visibility.Collapsed;};
   Library<Button>("ConfirmClearHistory").Click+=delegate {state.History.Clear();state.RecentlyClosed.Clear();Save();RefreshPersonalization();Library<Border>("ClearConfirmation").Visibility=Visibility.Collapsed;RenderLibrary();};
  }
  void OpenLibraryPage(string url) {
   CloseMenus();CloseSpotlight();
   var tab=state.Tabs.FirstOrDefault(t=>t.Space==state.Space&&Navigation.IsLibrary(t.Url))??state.AddTab(url);
   tab.Url=url;tab.Title=Navigation.InternalTitle(url);SelectTab(tab.Id);
  }
  void RefreshDownloadsUi() {RefreshIslandDownloads();if(!closing&&libraryPage!=null&&libraryPage.IsVisible&&libraryKind=="mica://downloads")RenderLibrary();}
  TextBlock LibraryText(string text,double size,string color) {var result=new TextBlock {Text=text,FontSize=size,TextTrimming=TextTrimming.CharacterEllipsis};result.SetResourceReference(TextBlock.ForegroundProperty,color);return result;}
  Button LibraryAction(string title,Action action,string key) {
   var button=new Button {Content=title,FontSize=11,Padding=new Thickness(10,7,10,7),Margin=new Thickness(4,0,0,0),Tag=key};button.Click+=delegate {action();};System.Windows.Automation.AutomationProperties.SetName(button,title);return button;
  }
  Border LibraryCard(UIElement content) {var card=new Border {CornerRadius=new CornerRadius(11),Padding=new Thickness(14),Margin=new Thickness(0,0,0,8),Child=content,BorderThickness=new Thickness(1)};card.SetResourceReference(Border.BackgroundProperty,"Panel");card.SetResourceReference(Border.BorderBrushProperty,"Line");return card;}
  void RenderLibrary() {
   if(libraryPage==null||Active==null||!Navigation.IsLibrary(Active.Url))return;
   bool changed=libraryKind!=Active.Url;libraryKind=Active.Url;bool history=libraryKind=="mica://history";
   if(changed){libraryUpdating=true;Library<TextBox>("LibrarySearch").Clear();libraryUpdating=false;libraryFilter="All";Library<Border>("ClearConfirmation").Visibility=Visibility.Collapsed;Library<ScrollViewer>("LibraryScroll").ScrollToTop();}
   string query=Library<TextBox>("LibrarySearch").Text.Trim();
   Library<TextBlock>("LibraryTitle").Text=history?"History":"Downloads";
   Library<TextBlock>("LibrarySubtitle").Text=history?"Pick up a thought where you left it.":"Everything you brought back from the web.";
   Library<Button>("LibraryClear").Content=history?"Clear history":"Clear finished";
   Library<Button>("LibraryClear").IsEnabled=history?state.History.Count>0:downloads.Any(d=>!IsDownloadLive(d));
   Library<Button>("HistorySection").Background=history?Brush("Hover"):Brushes.Transparent;Library<Button>("DownloadsSection").Background=history?Brushes.Transparent:Brush("Hover");
   Library<TextBlock>("SearchPlaceholder").Text=history?"Search title or website":"Search file name or source";Library<TextBlock>("SearchPlaceholder").Visibility=query.Length==0?Visibility.Visible:Visibility.Collapsed;
   Library<Button>("ClearLibrarySearch").Visibility=query.Length>0?Visibility.Visible:Visibility.Hidden;
   Library<Button>("FilterSecond").Content=history?"Today":"Active";Library<Button>("FilterThird").Content=history?"Past 7 days":"Completed";
   Library<Button>("FilterAll").Background=libraryFilter=="All"?Brush("Hover"):Brushes.Transparent;Library<Button>("FilterSecond").Background=libraryFilter=="Second"?Brush("Hover"):Brushes.Transparent;Library<Button>("FilterThird").Background=libraryFilter=="Third"?Brush("Hover"):Brushes.Transparent;
   Library<TextBlock>("LibraryFootnote").Text=history?"Your latest 500 unique pages, stored on this device.":"Up to 200 recent finished records. Removing a record keeps its file on disk.";
   var focus=Keyboard.FocusedElement as Button;string focusKey=focus==null?null:focus.Tag as string;
   var rows=Library<StackPanel>("LibraryRows");rows.Children.Clear();int count=0;DateTime? lastDay=null;
   if(history) {
    foreach(var entry in state.History.OrderByDescending(h=>h.LastUsed).Where(h=>Matches((h.Title??"")+" "+h.Url,query)&&HistoryDateMatches(h.LastUsed))) {
     AddDayHeading(rows,entry.LastUsed,ref lastDay);rows.Children.Add(HistoryCard(entry));count++;
    }
   }else {
    foreach(var entry in downloads.OrderByDescending(d=>d.StartedUtc).Where(d=>Matches(d.FileName+" "+d.Url,query)&&(libraryFilter=="All"||(libraryFilter=="Second"?IsDownloadLive(d):d.Status=="Completed")))) {
     AddDayHeading(rows,entry.StartedUtc,ref lastDay);rows.Children.Add(DownloadCard(entry));count++;
    }
   }
   if(focusKey!=null){var match=FindAction(rows,focusKey);if(match!=null)match.Focus();}
   Library<TextBlock>("LibraryCount").Text=count+ (history?(count==1?" page":" pages"):(count==1?" file":" files"));Library<StackPanel>("LibraryEmpty").Visibility=count==0?Visibility.Visible:Visibility.Collapsed;
   Library<TextBlock>("EmptyGlyph").Text=history?"\uE81C":"\uE896";bool filtered=query.Length>0||libraryFilter!="All";
   Library<TextBlock>("EmptyTitle").Text=filtered?"Nothing matches yet.":history?"A fresh page.":"Nothing downloaded yet.";
   Library<TextBlock>("EmptyDetail").Text=filtered?"Try another search or choose All.":history?"Your visited pages will appear here.":"Save a file from the web to see its progress here.";
  }
  static bool Matches(string value,string query) {return (value??"").IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0;}
  bool HistoryDateMatches(DateTime utc) {DateTime day=utc.ToLocalTime().Date;return libraryFilter=="All"||(libraryFilter=="Second"?day==DateTime.Today:day>=DateTime.Today.AddDays(-6));}
  void AddDayHeading(StackPanel rows,DateTime utc,ref DateTime? lastDay) {
   DateTime day=utc.ToLocalTime().Date;if(lastDay==day)return;lastDay=day;string label=day==DateTime.Today?"TODAY":day==DateTime.Today.AddDays(-1)?"YESTERDAY":day.ToString("dddd, d MMMM yyyy").ToUpperInvariant();var heading=LibraryText(label,10,"Muted");heading.FontWeight=FontWeights.SemiBold;heading.Margin=new Thickness(2,15,0,12);rows.Children.Add(heading);
  }
  Border HistoryCard(PageEntry entry) {
   var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(34)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
   row.Children.Add(CreateTabIcon(entry,20));
   var details=new StackPanel();var title=LibraryText(string.IsNullOrWhiteSpace(entry.Title)?entry.Url:entry.Title,13,"Ink");title.FontWeight=FontWeights.SemiBold;details.Children.Add(title);var address=LibraryText(entry.Url,11,"Muted");address.Margin=new Thickness(0,5,0,0);details.Children.Add(address);
   var open=new Button {Content=details,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(4,5,12,5),ToolTip="Open in a new tab",Tag="history-open:"+entry.Url};open.Click+=delegate {Navigate(entry.Url,true);};Grid.SetColumn(open,1);row.Children.Add(open);
   var actions=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};var time=LibraryText(entry.LastUsed.ToLocalTime().ToString("HH:mm"),10,"Muted");time.VerticalAlignment=VerticalAlignment.Center;time.Margin=new Thickness(9,0,10,0);actions.Children.Add(time);var remove=LibraryAction("×",delegate {state.History.Remove(entry);state.RecentlyClosed.RemoveAll(t=>t.Url==entry.Url);Save();RefreshPersonalization();RenderLibrary();},"history-remove:"+entry.Url);remove.ToolTip="Remove from history";System.Windows.Automation.AutomationProperties.SetName(remove,"Remove from history");actions.Children.Add(remove);Grid.SetColumn(actions,2);row.Children.Add(actions);return LibraryCard(row);
  }
  Border DownloadCard(DownloadEntry entry) {
   var content=new StackPanel();var header=new Grid();header.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(38)});header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
   var glyph=new TextBlock {Text=entry.Status=="Completed"?"\uE73E":"\uE896",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=20,VerticalAlignment=VerticalAlignment.Center};glyph.SetResourceReference(TextBlock.ForegroundProperty,"Accent");header.Children.Add(glyph);
   var info=new StackPanel();var title=LibraryText(entry.FileName,13,"Ink");title.FontWeight=FontWeights.SemiBold;title.ToolTip=entry.FilePath;info.Children.Add(title);Uri uri;var source=LibraryText(Uri.TryCreate(entry.Url,UriKind.Absolute,out uri)?uri.Host:entry.Url,11,"Muted");source.Margin=new Thickness(0,5,0,0);info.Children.Add(source);Grid.SetColumn(info,1);header.Children.Add(info);
   var status=LibraryText(entry.Status,10,"Accent");status.VerticalAlignment=VerticalAlignment.Center;status.Margin=new Thickness(12,0,0,0);Grid.SetColumn(status,2);header.Children.Add(status);content.Children.Add(header);
   if(entry.Status=="Downloading"||entry.Status=="Paused"){var progress=new ProgressBar {Height=3,Minimum=0,Maximum=100,Value=entry.Total>0?100.0*entry.Received/entry.Total:0,IsIndeterminate=entry.Total<=0&&entry.Status=="Downloading",Margin=new Thickness(38,14,0,0),BorderThickness=new Thickness(0)};progress.SetResourceReference(ProgressBar.ForegroundProperty,"Accent");progress.SetResourceReference(ProgressBar.BackgroundProperty,"Line");content.Children.Add(progress);}
   var bottom=new WrapPanel {Margin=new Thickness(34,12,0,0)};string size=FormatBytes(entry.Received)+(entry.Total>0?" / "+FormatBytes(entry.Total):"");var sizeText=LibraryText(size,10,"Muted");sizeText.Margin=new Thickness(4,7,12,7);bottom.Children.Add(sizeText);
   if(entry.Status=="Downloading")bottom.Children.Add(LibraryAction("Pause",delegate {PauseDownload(entry);},"pause:"+entry.Id));
   if(CanResumeDownload(entry))bottom.Children.Add(LibraryAction("Resume",delegate {ResumeDownload(entry);},"resume:"+entry.Id));
   if(IsDownloadLive(entry))bottom.Children.Add(LibraryAction("Cancel",delegate {CancelDownload(entry);},"cancel:"+entry.Id));
   if(entry.Status=="Completed") {bool exists=File.Exists(entry.FilePath);if(exists){bottom.Children.Add(LibraryAction("Open file",delegate {OpenDownload(entry);},"open:"+entry.Id));bottom.Children.Add(LibraryAction("Show in folder",delegate {RevealDownload(entry);},"folder:"+entry.Id));}else{var missing=LibraryText("File moved or deleted",11,"Muted");missing.Margin=new Thickness(4,7,4,7);bottom.Children.Add(missing);}}
   if(!IsDownloadLive(entry)){if(entry.Status!="Completed"&&Navigation.IsWeb(entry.Url))bottom.Children.Add(LibraryAction("Try again",delegate {Navigate(entry.Url,true);},"retry:"+entry.Id));bottom.Children.Add(LibraryAction("Remove",delegate {RemoveDownload(entry);},"remove:"+entry.Id));}
   content.Children.Add(bottom);
   if(!string.IsNullOrEmpty(entry.Detail)&&entry.Status=="Interrupted"){var detail=LibraryText(entry.Detail,11,"Muted");detail.TextWrapping=TextWrapping.Wrap;detail.Margin=new Thickness(38,7,0,0);content.Children.Add(detail);}
   return LibraryCard(content);
  }
  static string FormatBytes(long bytes) {double value=Math.Max(0,bytes);string[] units={"B","KB","MB","GB"};int unit=0;while(value>=1024&&unit<3){value/=1024;unit++;}return value.ToString(unit==0?"0":"0.#")+" "+units[unit];}
  static Button FindAction(DependencyObject parent,string tag) {for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);var button=child as Button;if(button!=null&&string.Equals(button.Tag as string,tag,StringComparison.Ordinal))return button;var found=FindAction(child,tag);if(found!=null)return found;}return null;}
 }
}

