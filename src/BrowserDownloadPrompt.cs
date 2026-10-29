using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace Mica {
 // Download card under the address bar: Open / Save / Save as…, with a clear
 // warning for file types that can run code. Files still get Mark of the Web,
 // so Windows SmartScreen and Defender check them when they are opened.
 partial class BrowserApp {
  static readonly HashSet<string> riskyExtensions=new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
   ".exe",".msi",".msix",".msixbundle",".appx",".appxbundle",".appinstaller",".bat",".cmd",".com",".scr",".pif",".cpl",".ps1",".psm1",".vbs",".vbe",".js",".jse",".wsf",".wsh",".hta",".jar",".reg",".lnk",".url",".scf",".inf",".dll",".sys",".iso",".img",".vhd",".vhdx",".application",".gadget",".msc",".chm",".settingcontent-ms"
  };
  // Tab id -> file name, when a navigation in that tab turned into a download.
  readonly Dictionary<string,string> downloadNavigations=new Dictionary<string,string>();
  // The engine may report the aborted page load before the download starts.
  string abortedTab;
  void NavigationBecameDownload(string id,CoreWebView2 core) {
   if(abortedTab!=id||state.ActiveId!=id||!Find<Border>("ErrorPanel").IsVisible)return;
   abortedTab=null;string name;downloadNavigations.TryGetValue(id,out name);downloadNavigations.Remove(id);
   if(core.CanGoBack){Find<Border>("ErrorPanel").Visibility=Visibility.Collapsed;Find<Grid>("WebHost").Visibility=IsSpotlight?Visibility.Collapsed:Visibility.Visible;Status("Download started: "+name);}
   else ShowDownloadNotice(name);
  }
  // A link that is really a file leaves an empty tab behind: say what happened instead of "couldn't load".
  void ShowDownloadNotice(string fileName) {
   ShowError((string.IsNullOrEmpty(fileName)?"This link":"“"+fileName+"”")+" is a file, not a page. Choose Open, Save or Save as… in the card above; you can close this tab afterwards.");
   Find<TextBlock>("ErrorTitle").Text="A download is waiting.";Find<Button>("RetryButton").Visibility=Visibility.Collapsed;
   Status("Download prompted: "+fileName);
  }
  // Only types known to be inert open directly; anything else (an .rdp, .appref-ms, .library-ms, a type
  // nobody listed yet) is treated as able to run code. riskyExtensions stays for the stronger warning.
  static readonly HashSet<string> inertExtensions=new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
   ".pdf",".txt",".md",".rtf",".png",".jpg",".jpeg",".gif",".webp",".bmp",".heic",".avif",".ico",".mp3",".m4a",".wav",".flac",".ogg",".opus",".aac",".mp4",".m4v",".mkv",".webm",".mov",".avi",".wmv",
   ".docx",".xlsx",".pptx",".odt",".ods",".odp",".epub",".zip",".7z",".rar",".tar",".gz",".srt",".vtt",".ttf",".otf",".woff",".woff2"
  };
  static bool IsRiskyDownload(string fileName) {string type=Path.GetExtension(fileName??"");return riskyExtensions.Contains(type)||!inertExtensions.Contains(type);}
  [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id,uint flags,IntPtr token,out IntPtr path);
  string DownloadsFolder() {
   if(smoke){var test=Path.Combine(profile,"test-downloads");Directory.CreateDirectory(test);return test;}
   IntPtr pointer;
   if(SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"),0,IntPtr.Zero,out pointer)==0) {try {var path=Marshal.PtrToStringUni(pointer);if(Directory.Exists(path))return path;}finally {Marshal.FreeCoTaskMem(pointer);}}
   return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads");
  }
  static string UniqueDownloadPath(string folder,string name) {
   name=string.Join("_",name.Split(Path.GetInvalidFileNameChars()));if(string.IsNullOrWhiteSpace(name))name="download";
   string stem=Path.GetFileNameWithoutExtension(name),extension=Path.GetExtension(name),candidate=Path.Combine(folder,name);
   for(int i=1;File.Exists(candidate)||File.Exists(candidate+".crdownload");i++)candidate=Path.Combine(folder,stem+" ("+i+")"+extension);
   return Path.GetFullPath(candidate);
  }
  static string DownloadSize(long bytes) {if(bytes<0)return null;if(bytes<1024)return bytes+" B";if(bytes<1024*1024)return (bytes/1024.0).ToString("0")+" KB";if(bytes<1024L*1024*1024)return (bytes/1048576.0).ToString("0.0")+" MB";return (bytes/1073741824.0).ToString("0.00")+" GB";}
  static string DownloadSource(string uri) {if(uri!=null&&uri.StartsWith("blob:",StringComparison.OrdinalIgnoreCase))uri=uri.Substring(5);return OriginLabel(uri);}
  Task<string> AskDownload(string fileName,long bytes,string source) {
   var result=new TaskCompletionSource<string>();bool risky=IsRiskyDownload(fileName);
   var details=new List<string>();var size=DownloadSize(bytes);if(size!=null)details.Add(size);if(!string.IsNullOrEmpty(source))details.Add("from "+source);
   ShowChoiceBubble(Find<Button>("AddressButton"),risky?"":"",risky?"This file can change your device":"Download",fileName,string.Join(" · ",details),
    risky?"Only open it if you trust "+(string.IsNullOrEmpty(source)?"this site":source)+". "+(SandboxAvailable()?"Open runs it in Windows Sandbox, away from your files.":"Windows will check it, and Mica runs apps in restricted mode."):null,
    new[]{new[]{"Save as…","saveas",""},new[]{"Save","save",risky?"primary":""},new[]{SandboxAvailable()?"Open in Sandbox":"Open","open",risky?"":"primary"}},()=>!closing,choice=>result.TrySetResult(choice));
   return result.Task;
  }
  [System.Runtime.InteropServices.DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetFocus(IntPtr hwnd);
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetFocus();
  internal IntPtr KeyboardWindow() {return GetFocus();}
  void TakeKeyboardFromPage(Popup popup) {var handle=PopupHandle(popup);if(handle!=IntPtr.Zero&&GetFocus()!=handle)SetFocus(handle);}
  void ShowChoiceBubble(FrameworkElement anchor,string icon,string title,string subject,string detail,string warning,string[][] choices,Func<bool> alive,Action<string> done,FrameworkElement extra=null) {
   window.Dispatcher.BeginInvoke(new Action(delegate {
    siteBubbleQueue.Enqueue(delegate {
     if(closing||!alive()){done(null);NextSiteBubble();return;}
     var card=new Border {Width=360,CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),Padding=new Thickness(16,14,14,14),Margin=new Thickness(14,4,14,22),Resources=window.Resources};card.SetResourceReference(Border.BackgroundProperty,"Panel");card.SetResourceReference(Border.BorderBrushProperty,"Line");
     card.Effect=new System.Windows.Media.Effects.DropShadowEffect {BlurRadius=28,ShadowDepth=6,Opacity=0.18,Color=Color.FromRgb(0x25,0x20,0x32)};
     var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(44)});grid.ColumnDefinitions.Add(new ColumnDefinition());card.Child=grid;grid.Resources.MergedDictionaries.Add(DialogStyles.Create());
     var badge=new Border {Width=32,Height=32,CornerRadius=new CornerRadius(10),VerticalAlignment=VerticalAlignment.Top};badge.SetResourceReference(Border.BackgroundProperty,"Hover");
     var glyph=new TextBlock {Text=icon,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=15,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};glyph.SetResourceReference(TextBlock.ForegroundProperty,"Accent");badge.Child=glyph;grid.Children.Add(badge);
     var stack=new StackPanel();Grid.SetColumn(stack,1);grid.Children.Add(stack);
     var heading=DialogText(title,11,"Muted");heading.Margin=new Thickness(0,0,26,2);stack.Children.Add(heading);
     var what=DialogText(subject,14,"Ink");what.FontWeight=FontWeights.SemiBold;what.TextWrapping=TextWrapping.NoWrap;what.TextTrimming=TextTrimming.CharacterEllipsis;what.ToolTip=subject;stack.Children.Add(what);
     if(!string.IsNullOrEmpty(detail)){var line=DialogText(detail,11,"Muted");line.Margin=new Thickness(0,3,0,0);stack.Children.Add(line);}
     if(!string.IsNullOrEmpty(warning)){var note=new Border {CornerRadius=new CornerRadius(8),Padding=new Thickness(10,8,10,8),Margin=new Thickness(0,10,0,0)};note.SetResourceReference(Border.BackgroundProperty,"Hover");var text=DialogText(warning,11,"Ink");text.LineHeight=16;note.Child=text;stack.Children.Add(note);}
     if(extra!=null)stack.Children.Add(extra);
     // With three or more choices the first (the quiet "never" kind) sits on the left. The rest wrap onto
     // another line when they don't fit (longer labels, more choices), instead of running off the card.
     var row=new Grid {Margin=new Thickness(0,12,0,-6)};row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition());stack.Children.Add(row);
     var aside=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,6)};var buttons=new WrapPanel {HorizontalAlignment=HorizontalAlignment.Right,FlowDirection=FlowDirection.RightToLeft};bool many=choices.Length>=4;if(many)Grid.SetColumnSpan(buttons,2);else Grid.SetColumn(buttons,1); // Four or more: one set of buttons on two lines, the quiet one last.
     row.Children.Add(aside);row.Children.Add(buttons);int place=0;
     var popup=IslandPopup(card);
     // Text fields in a bubble: a web page holds the keyboard, so clicking a field takes it back
     // for Mica's window (the popup can't activate itself), and closing hands it back to the page.
     bool typed=false;IgnoreEarlyClicks(card);
     card.PreviewMouseDown+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs m) {if(m.ChangedButton!=System.Windows.Input.MouseButton.Left)return;
      var source=m.OriginalSource as DependencyObject;while(source!=null&&!(source is TextBox)&&!(source is PasswordBox))source=TreeParent(source);
      if(source==null)return;typed=true;TakeKeyboardFromPage(popup);var field=(UIElement)source;field.Focus();
      // WPF settles focus on the popup root after the window gets it; put it back on the field.
      window.Dispatcher.BeginInvoke(new Action(delegate {field.Focus();System.Windows.Input.Keyboard.Focus(field);}),System.Windows.Threading.DispatcherPriority.ApplicationIdle);
     };
     bool finished=false;
     Action<string> finish=delegate(string value) {if(finished)return;finished=true;siteBubble=null;if(typed){var page=CurrentView();if(page!=null)page.Focus();}done(value);CloseIslandPopup(popup,NextSiteBubble);};
     foreach(var choice in choices) {var value=choice[1];bool quiet=choices.Length>=3&&place++==0,side=quiet&&!many;var button=new Button {Content=choice[0],Padding=new Thickness(quiet?10:14,7,quiet?10:14,7),Margin=side?new Thickness(-10,0,0,0):new Thickness(6,0,0,6),FontSize=12,Tag=value,FlowDirection=FlowDirection.LeftToRight};if(quiet){button.SetResourceReference(Control.ForegroundProperty,"Muted");if(choice.Length>3)button.ToolTip=choice[3];}if(side)aside.Children.Add(button);else if(quiet)buttons.Children.Add(button);if(!quiet){if(choice[2]=="primary")button.SetResourceReference(Button.BackgroundProperty,"Hover");buttons.Children.Insert(0,button);}button.Click+=delegate {finish(value);};}
     var dismiss=new Button {Content="",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=9,Width=24,Height=24,Padding=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,-6,-6,0),ToolTip="Cancel"};dismiss.SetResourceReference(Control.ForegroundProperty,"Muted");dismiss.Click+=delegate {finish(null);};Grid.SetColumn(dismiss,1);grid.Children.Add(dismiss);
     popup.KeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs k) {if(k.Key==System.Windows.Input.Key.Escape){finish(null);k.Handled=true;}};
     siteBubble=popup;popup.Tag=(Action)delegate {finish(null);};
     WatchSiteBubble();KeepWithWindow(popup);
     popup.IsOpen=true;
    });
    if(siteBubble==null&&siteBubbleQueue.Count==1)NextSiteBubble();
   }));
  }
 }
}
