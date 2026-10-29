using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace Mica {
 static class DesktopShell {
  internal const string AppId=WindowsIntegration.AppId;
  [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
  internal static void SetIdentity() {try {SetCurrentProcessExplicitAppUserModelID(AppId);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}}
 }
 partial class BrowserApp {
  JumpList desktopJumpList;
  static readonly HashSet<string> removedJumpArguments=new HashSet<string>(StringComparer.Ordinal);
  static string jumpRemovalPath;
  System.Windows.Threading.DispatcherTimer homeClock;
  void UpdateHomeClock() {
   var now=DateTime.Now;RollText(Find<TextBlock>("HomeHeadline"),now.ToString("H:mm"));RollText(Find<TextBlock>("DateLabel"),Localizer.LongDate(now));
   string part=now.Hour<5?"Good night":now.Hour<12?"Good morning":now.Hour<18?"Good afternoon":"Good evening";
   var account=privateMode?null:ReadVaultAccount();
   RollText(Find<TextBlock>("Greeting"),privateMode?"Private browsing · history is not saved":part+(account!=null&&!string.IsNullOrWhiteSpace(account.username)?", "+account.username+".":"."));
  }
  void RenderHome() {
   var grid=Find<UniformGrid>("EverydaySites");if(grid==null)return;
   if(homeClock==null){homeClock=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(10)};homeClock.Tick+=delegate {if(Find<ScrollViewer>("Home").IsVisible)UpdateHomeClock();};homeClock.Start();window.Closed+=delegate {homeClock.Stop();};}
   UpdateHomeClock();
   var sites=state.TopSites(8);grid.Children.Clear();grid.Columns=8;
   Find<TextBlock>("EverydayEmpty").Text=privateMode?"Sites from this private window won't be added to your history or Top Sites.":"Your frequently visited sites will appear here.";
   Find<TextBlock>("EverydayEmpty").Visibility=sites.Count==0?Visibility.Visible:Visibility.Collapsed;
   grid.Visibility=sites.Count==0?Visibility.Collapsed:Visibility.Visible;
   foreach(var entry in sites) {
    var site=entry;string host=new Uri(site.Url).IdnHost;if(host.StartsWith("www.",StringComparison.OrdinalIgnoreCase))host=host.Substring(4);
    var content=new StackPanel {HorizontalAlignment=HorizontalAlignment.Center};
    FrameworkElement icon=CreateTabIcon(site,24);var known=KnownSiteIcon(site.Url);
    if(Icons.Read(FaviconCache.GetKey(site.Url))==null&&known!=null)icon=new Image {Source=known,Width=24,Height=24,Stretch=Stretch.Uniform};
    var tile=new Border {Width=52,Height=52,CornerRadius=new CornerRadius(17),BorderThickness=new Thickness(1),HorizontalAlignment=HorizontalAlignment.Center,Child=icon};tile.SetResourceReference(Border.BackgroundProperty,"Panel");tile.SetResourceReference(Border.BorderBrushProperty,"Line");content.Children.Add(tile);
    string name=host.Split('.')[0];name=name.Length>0?char.ToUpperInvariant(name[0])+name.Substring(1):host;
    var title=new TextBlock {Text=name,FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis,TextAlignment=TextAlignment.Center,MaxWidth=64,Margin=new Thickness(0,8,0,0)};title.SetResourceReference(TextBlock.ForegroundProperty,"Ink");content.Children.Add(title);
    var button=new Button {Content=content,Padding=new Thickness(4,10,4,8),Margin=new Thickness(2,0,2,0),Background=Brushes.Transparent,ToolTip=site.Title+"\n"+site.Url};AutomationProperties.SetName(button,"Open "+host);button.Click+=delegate {Navigate(site.Url,false);};grid.Children.Add(button);
   }
   var recent=Find<WrapPanel>("RecentPlaces");recent.Children.Clear();
   var closed=privateMode?new List<PageEntry>():state.RecentlyClosed.Where(e=>Navigation.IsWeb(e.Url)).Take(3).ToList();
   Find<StackPanel>("RecentSection").Visibility=closed.Count==0?Visibility.Collapsed:Visibility.Visible;
   foreach(var entry in closed) {
    var page=entry;var row=new StackPanel {Orientation=Orientation.Horizontal};var icon=CreateTabIcon(page,14);icon.Margin=new Thickness(0,0,9,0);row.Children.Add(icon);
    var label=new TextBlock {Text=string.IsNullOrWhiteSpace(page.Title)?new Uri(page.Url).Host:page.Title,FontSize=12,MaxWidth=170,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};label.SetResourceReference(TextBlock.ForegroundProperty,"Ink");row.Children.Add(label);
    var chip=new Button {Content=row,Padding=new Thickness(12,8,14,8),Margin=new Thickness(0,0,8,8),ToolTip=page.Url};chip.SetResourceReference(Button.BackgroundProperty,"Panel");chip.Click+=delegate {Navigate(page.Url,false);};recent.Children.Add(chip);
   }
  }
  BitmapSource KnownSiteIcon(string url) {
   if(!Navigation.IsWeb(url))return null;string host=new Uri(url).IdnHost.ToLowerInvariant(),asset=null;
   if(host=="github.com"||host=="www.github.com")asset="github";
   else if(host=="youtube.com"||host=="www.youtube.com"||host=="m.youtube.com")asset="youtube";
   else if(host=="mail.google.com")asset="gmail";
   else if(host=="wikipedia.org"||host.EndsWith(".wikipedia.org",StringComparison.Ordinal))asset="wikipedia";
   if(asset==null)return null;
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Mica.Favicons."+asset+".ico")) {
    if(stream==null)return null;var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);var frame=decoder.Frames.OrderBy(f=>Math.Abs(f.PixelWidth-32)).First();frame.Freeze();return frame;
   }
  }
  void InitializeDesktopIntegration() {
   if(smoke||privateMode)return;
   try {
    desktopJumpList=JumpList.GetJumpList(Application.Current);
    if(desktopJumpList==null) {
     jumpRemovalPath=Path.Combine(profile,"jump-list-removed.txt");if(File.Exists(jumpRemovalPath))foreach(var line in File.ReadAllLines(jumpRemovalPath))removedJumpArguments.Add(line);
     desktopJumpList=new JumpList {ShowFrequentCategory=false,ShowRecentCategory=false};
     desktopJumpList.JumpItemsRemovedByUser+=OnJumpItemsRemoved;
     desktopJumpList.JumpItemsRejected+=OnJumpItemsRejected;
     JumpList.SetJumpList(Application.Current,desktopJumpList);
    }
    UpdateJumpList();
   }catch(Exception error){if(!FaviconCache.IsExpected(error))throw;desktopJumpList=null;}
  }
  static void OnJumpItemsRemoved(object sender,JumpItemsRemovedEventArgs e) {RememberRemovedJumpItems(e.RemovedItems);}
  static void OnJumpItemsRejected(object sender,JumpItemsRejectedEventArgs e) {
   // Windows may reject custom categories when its history setting is disabled.
   try {if(jumpRemovalPath!=null)File.WriteAllLines(Path.Combine(Path.GetDirectoryName(jumpRemovalPath),"jump-list-status.txt"),e.RejectionReasons.Select(reason=>DateTime.UtcNow.ToString("o")+" Windows rejected a Jump List item: "+reason).ToArray());}catch(IOException){}catch(UnauthorizedAccessException){}
   for(int i=0;i<e.RejectedItems.Count;i++)if(e.RejectionReasons[i]==JumpItemRejectionReason.RemovedByUser)RememberRemovedJumpItems(new[]{e.RejectedItems[i]});
  }
  static void RememberRemovedJumpItems(IEnumerable<JumpItem> items) {
   foreach(var task in items.OfType<JumpTask>())if(!string.IsNullOrEmpty(task.CustomCategory)&&!string.IsNullOrEmpty(task.Arguments))removedJumpArguments.Add(task.Arguments);
   try {if(jumpRemovalPath!=null)File.WriteAllLines(jumpRemovalPath,removedJumpArguments);}catch(Exception error){if(!FaviconCache.IsExpected(error))throw;}
  }
  void UpdateJumpList() {
   if(smoke||privateMode||desktopJumpList==null)return;
   try {
    desktopJumpList.JumpItems.Clear();desktopJumpList.JumpItems.AddRange(BuildJumpItems());
    desktopJumpList.Apply();
   }catch(Exception error){if(!FaviconCache.IsExpected(error))throw;}
  }
  List<JumpItem> BuildJumpItems() {
   var items=new List<JumpItem>();if(privateMode)return items;
   string exe;using(var process=Process.GetCurrentProcess())exe=process.MainModule.FileName;
   foreach(var entry in state.TopSites(4))AddJumpSite(items,entry,"Top Sites",exe);
   foreach(var entry in state.RecentlyClosed.Take(8))AddJumpSite(items,entry,"Recently closed",exe);
   items.Add(new JumpTask {Title="New window",Description="Open a new Mica window",Arguments="--new-window",ApplicationPath=exe,IconResourcePath=exe,IconResourceIndex=0});
   items.Add(new JumpTask {Title="New private window",Description="Browse without keeping history on this device",Arguments="--private",ApplicationPath=exe,IconResourcePath=exe,IconResourceIndex=0});
   return items;
  }
  void AddJumpSite(List<JumpItem> items,PageEntry entry,string category,string exe) {
   // Sign-in redirects and tracking URLs make long, useless shell links.
   if(!Navigation.IsWeb(entry.Url)||entry.Url.Length>300)return;string arguments="--url \""+new Uri(entry.Url).AbsoluteUri.Replace("\"","%22")+"\"";if(removedJumpArguments.Contains(arguments))return;
   string icon=null;try {icon=JumpSiteIcon(entry.Url);}catch(Exception error){if(!FaviconCache.IsExpected(error))throw;}
   items.Add(new JumpTask {Title=string.IsNullOrWhiteSpace(entry.Title)?new Uri(entry.Url).IdnHost:entry.Title,Description=entry.Url,CustomCategory=category,Arguments=arguments,ApplicationPath=exe,IconResourcePath=icon??exe,IconResourceIndex=0});
  }
  string JumpSiteIcon(string url) {
   string key=FaviconCache.GetKey(url);if(key==null)return null;var bitmap=Icons.Read(key)??KnownSiteIcon(url);if(bitmap==null)return null;
   string directory=Path.Combine(profile,"JumpIcons"),path=Path.Combine(directory,key+".ico");
   if(Directory.Exists(directory)&&(File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)return null;
   Directory.CreateDirectory(directory);if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)return null;
   if(bitmap.PixelWidth>32||bitmap.PixelHeight>32)bitmap=new TransformedBitmap(bitmap,new ScaleTransform(Math.Min(32.0/bitmap.PixelWidth,32.0/bitmap.PixelHeight),Math.Min(32.0/bitmap.PixelWidth,32.0/bitmap.PixelHeight)));
   byte[] png;using(var buffer=new MemoryStream()){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(buffer);png=buffer.ToArray();}
   // ICO directory followed by a PNG frame, supported by the Windows shell.
   byte[] ico;using(var buffer=new MemoryStream()){using(var writer=new BinaryWriter(buffer)){writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)1);writer.Write((byte)bitmap.PixelWidth);writer.Write((byte)bitmap.PixelHeight);writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(png.Length);writer.Write(22);writer.Write(png);ico=buffer.ToArray();}}
   if(!File.Exists(path)||!File.ReadAllBytes(path).SequenceEqual(ico))File.WriteAllBytes(path,ico);return path;
  }
 }
}
