using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // Reading mode (F9 or the book in the toolbar): the article on the page, drawn by Mica in its own type and
 // colours, with nothing else around it. The page stays loaded underneath (hidden), so leaving is instant and
 // the tab, its history and its address don't change. Text can be selected and copied; size and serif are kept.
 partial class BrowserApp {
  Grid readerHost;string readerTab;WebView2 readerView;Dictionary<string,object> readerArticle;
  internal bool ReaderOpen {get {return readerHost!=null;}}
  static string readerScript;
  static string ReaderScript() {
   if(readerScript==null)using(var reader=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Mica.Reader.js")))readerScript=reader.ReadToEnd();
   return readerScript;
  }
  internal async void ToggleReader() {
   if(ReaderOpen){var open=state.Tabs.FirstOrDefault(t=>t.Id==readerTab);if(open!=null&&Navigation.IsReading(open.Url)){OpenOriginal(open);return;}CloseReader();return;}
   var tab=Active;var view=CurrentView();var core=view==null?null:LiveCore(view);
   if(tab==null||!Navigation.IsWeb(tab.Url)||core==null){Status("Open an article to read it here.");return;}
   Dictionary<string,object> article;
   try {
    string raw=await core.ExecuteScriptAsync(ReaderScript());
    article=new JavaScriptSerializer {MaxJsonLength=int.MaxValue}.Deserialize<Dictionary<string,object>>(new JavaScriptSerializer {MaxJsonLength=int.MaxValue}.Deserialize<string>(raw));
   }catch(Exception){Status("This page can't be read in reading mode.");return;}
   if(article==null||!(article["ok"] is bool)||!(bool)article["ok"]){Status("This page doesn't look like an article.");return;}
   if(Active!=tab||CurrentView()!=view)return;
   OpenReader(tab.Id,view,article);
  }
  void OpenReader(string tabId,WebView2 view,Dictionary<string,object> article) {
   CloseReader();readerTab=tabId;readerView=view;readerArticle=article;var savedTab=state.Tabs.FirstOrDefault(t=>t.Id==tabId);bool saved=savedTab!=null&&Navigation.IsReading(savedTab.Url);
   var host=new Grid {Tag="reader"};host.SetResourceReference(Panel.BackgroundProperty,"Canvas");
   var reader=new FlowDocumentScrollViewer {VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=true,IsToolBarVisible=false,Tag="reader-document"};
   reader.Document=ReaderDocument(article);host.Children.Add(reader);reader.SizeChanged+=delegate {CenterReaderPage(reader);};
   // Size, serif and leave, in the top corner.
   var bar=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,12,22,0)};
   Func<string,string,string,Action,Button> tool=delegate(string glyph,string tip,string tag,Action click) {
    var b=new Button {Style=(Style)window.FindResource("Icon"),Content=glyph,ToolTip=tip,Tag=tag,Margin=new Thickness(2,0,2,0)};b.Click+=delegate {click();};bar.Children.Add(b);return b;
   };
   Action redraw=delegate {double offset=reader.VerticalOffsetSafe();reader.Document=ReaderDocument(article);CenterReaderPage(reader);reader.Dispatcher.BeginInvoke(new Action(delegate {reader.ScrollToOffset(offset);}),System.Windows.Threading.DispatcherPriority.Loaded);Save();};
   tool("\uE71F","Smaller text","reader-smaller",delegate {state.ReaderSize=Math.Max(13,ReaderSize-1);redraw();});
   tool("\uE8E8","Larger text","reader-larger",delegate {state.ReaderSize=Math.Min(28,ReaderSize+1);redraw();});
   var serif=tool("Aa","Serif or sans-serif","reader-serif",delegate {state.ReaderSerif=!state.ReaderSerif;redraw();});serif.FontFamily=Localizer.Serif;serif.FontSize=14;
   readAloudButton=tool("\uE767","Read aloud","reader-speak",ReadAloud);
   if(saved)tool("\uE8A7","Open the original page","reader-original",delegate {OpenOriginal(savedTab);});
   else {tool("\uE7BC","Save for later, to read offline","reader-save",SaveForLater);tool("\uE711","Leave reading mode (Esc)","reader-close",CloseReader);}
   host.Children.Add(bar);
   host.PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {if(e.Key==Key.Escape&&!saved){e.Handled=true;CloseReader();}};
   if(view!=null)view.Visibility=Visibility.Hidden;Find<Grid>("WebHost").Children.Add(host);readerHost=host;
   var readerButton=Find<Button>("ReaderButton");if(readerButton!=null)readerButton.SetResourceReference(Button.BackgroundProperty,"Hover");
   host.Opacity=0;Animate(host,UIElement.OpacityProperty,1,180);
   reader.Dispatcher.BeginInvoke(new Action(delegate {reader.Focus();}),System.Windows.Threading.DispatcherPriority.Loaded);
   Status(saved?"Saved on this device: it reads without a connection.":"Reading mode. Press Esc or F9 to go back to the page.");
  }
  internal void CloseReader() {
   if(readerHost==null)return;StopReadAloud();var host=readerHost;var view=readerView;readerHost=null;readerView=null;readerTab=null;readerArticle=null;
   Find<Grid>("WebHost").Children.Remove(host);
   var button=Find<Button>("ReaderButton");if(button!=null)button.ClearValue(Button.BackgroundProperty);
   if(view!=null&&views.Values.Contains(view)&&CurrentView()==view){view.Visibility=Visibility.Visible;view.Focus();}
  }
  // The text keeps a comfortable line length (about 40 characters wide per point of type) in the middle of the window.
  void CenterReaderPage(FlowDocumentScrollViewer reader) {
   var doc=reader.Document;if(doc==null||reader.ActualWidth<=0)return;double column=Math.Max(560,ReaderSize*40);
   double side=Math.Max(36,(reader.ActualWidth-18-column)/2);doc.PagePadding=new Thickness(side,64,side,96);
  }
  int ReaderSize {get {return state.ReaderSize>=13&&state.ReaderSize<=28?state.ReaderSize:18;}}

  FlowDocument ReaderDocument(Dictionary<string,object> article) {
   // A Vietnamese article reads in a serif that has all its letters (Georgia lacks most).
   object lang,named;article.TryGetValue("lang",out lang);article.TryGetValue("title",out named);
   bool vietnamese=(lang as string??"").StartsWith("vi",StringComparison.OrdinalIgnoreCase)||Localizer.HasVietnamese(named as string);
   double size=ReaderSize;var body=state.ReaderSerif?new FontFamily(vietnamese?"Cambria, Segoe UI":"Georgia, Cambria, Segoe UI"):new FontFamily("Segoe UI Variable Text, Segoe UI");
   var heading=new FontFamily("Segoe UI Variable Display, Segoe UI");
   var doc=new FlowDocument {FontFamily=body,FontSize=size,LineHeight=size*1.62,PagePadding=new Thickness(36,64,36,96),ColumnWidth=double.PositiveInfinity,TextAlignment=TextAlignment.Left,IsOptimalParagraphEnabled=true,IsHyphenationEnabled=false};
   doc.SetResourceReference(FlowDocument.ForegroundProperty,"Ink");doc.SetResourceReference(FlowDocument.BackgroundProperty,"Canvas");
   Func<string,double,FontWeight,string,Paragraph> para=delegate(string text,double fontSize,FontWeight weight,string brush) {
    var p=new Paragraph(new Run(text)) {FontSize=fontSize,FontWeight=weight,Margin=new Thickness(0,0,0,fontSize*0.9)};if(brush!=null)p.SetResourceReference(TextElement.ForegroundProperty,brush);return p;
   };
   string site=article.ContainsKey("site")?article["site"] as string:"",author=article.ContainsKey("author")?article["author"] as string:"";
   int words=article.ContainsKey("words")&&article["words"] is int?(int)article["words"]:0;
   var kicker=para((site??"").ToUpperInvariant(),11,FontWeights.SemiBold,"Accent");kicker.FontFamily=heading;kicker.Margin=new Thickness(0,0,0,10);doc.Blocks.Add(kicker);
   var title=para(article["title"] as string??"",size*1.75,FontWeights.SemiBold,"Ink");title.FontFamily=heading;title.LineHeight=size*1.75*1.22;title.Margin=new Thickness(0,0,0,12);doc.Blocks.Add(title);
   string by=string.Join(" · ",new[]{author,words>0?Math.Max(1,(int)Math.Round(words/230.0))+" min read":null}.Where(s=>!string.IsNullOrWhiteSpace(s)));
   if(by.Length>0){var line=para(by,13,FontWeights.Normal,"Muted");line.FontFamily=heading;line.Margin=new Thickness(0,0,0,size*1.8);doc.Blocks.Add(line);}
   List list=null;
   foreach(var item in ((article["blocks"] as System.Collections.IEnumerable)??new object[0]).OfType<Dictionary<string,object>>()) {
    string kind=item["t"] as string,text=item.ContainsKey("text")?item["text"] as string:"";
    if(kind!="li")list=null;
    switch(kind) {
     case "h2":{var h=para(text,size*1.3,FontWeights.SemiBold,"Ink");h.FontFamily=heading;h.Margin=new Thickness(0,size*1.2,0,size*0.5);doc.Blocks.Add(h);break;}
     case "h3":{var h=para(text,size*1.12,FontWeights.SemiBold,"Ink");h.FontFamily=heading;h.Margin=new Thickness(0,size,0,size*0.4);doc.Blocks.Add(h);break;}
     case "li":{if(list==null){list=new List {MarkerStyle=TextMarkerStyle.Disc,Margin=new Thickness(0,0,0,size*0.9),Padding=new Thickness(size*1.4,0,0,0)};doc.Blocks.Add(list);}var p=new Paragraph(new Run(text)) {Margin=new Thickness(0,0,0,size*0.35)};list.ListItems.Add(new ListItem(p));break;}
     case "quote":{var p=para(text,size*1.05,FontWeights.Normal,"Ink");p.FontStyle=FontStyles.Italic;p.BorderThickness=new Thickness(3,0,0,0);p.SetResourceReference(Paragraph.BorderBrushProperty,"Accent");p.Padding=new Thickness(size,2,0,2);doc.Blocks.Add(p);break;}
     case "pre":{var p=para(text,size*0.8,FontWeights.Normal,"Ink");p.FontFamily=new FontFamily("Cascadia Mono, Consolas");p.LineHeight=size*0.8*1.45;p.Padding=new Thickness(14,10,14,10);p.SetResourceReference(Paragraph.BackgroundProperty,"Panel");doc.Blocks.Add(p);break;}
     case "caption":{var p=para(text,13,FontWeights.Normal,"Muted");p.FontFamily=heading;p.Margin=new Thickness(0,0,0,size*1.2);doc.Blocks.Add(p);break;}
     case "img":{var image=ReaderImage(item["src"] as string,text);if(image!=null)doc.Blocks.Add(new BlockUIContainer(image) {Margin=new Thickness(0,size*0.4,0,size*1.1)});break;}
     default:doc.Blocks.Add(para(text,size,FontWeights.Normal,null));break;
    }
   }
   return doc;
  }
  static FrameworkElement ReaderImage(string src,string alt) {
   Uri uri;if(!Uri.TryCreate(src,UriKind.Absolute,out uri)||(uri.Scheme!="https"&&uri.Scheme!="http"))return null;
   var image=new Image {Stretch=Stretch.Uniform,MaxHeight=520,HorizontalAlignment=HorizontalAlignment.Center,ToolTip=string.IsNullOrWhiteSpace(alt)?null:alt};
   RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);
   LoadReaderImage(uri,image);
   return image;
  }
  // A camera photo decoded whole holds 50 MB or more; the column never shows one wider than this.
  const int ReaderImageWidth=1400,ReaderImageBytes=20<<20;
  static async void LoadReaderImage(Uri uri,Image image) {
   try {
    byte[] data;using(var client=new System.Net.WebClient())data=await client.DownloadDataTaskAsync(uri);
    if(data.Length==0||data.Length>ReaderImageBytes){image.Visibility=Visibility.Collapsed;return;}
    // The header gives the size without decoding the picture.
    int width;using(var probe=new System.IO.MemoryStream(data,false))width=BitmapDecoder.Create(probe,BitmapCreateOptions.DelayCreation|BitmapCreateOptions.IgnoreColorProfile,BitmapCacheOption.None).Frames[0].PixelWidth;
    var bitmap=new BitmapImage();
    using(var stream=new System.IO.MemoryStream(data,false)){bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.CreateOptions=BitmapCreateOptions.IgnoreColorProfile;if(width>ReaderImageWidth)bitmap.DecodePixelWidth=ReaderImageWidth;bitmap.StreamSource=stream;bitmap.EndInit();}
    bitmap.Freeze();image.Source=bitmap;
    // Never wider than the picture itself: a small thumbnail stays sharp instead of being stretched.
    image.MaxWidth=bitmap.PixelWidth;
   }catch(Exception){image.Visibility=Visibility.Collapsed;}
  }
 }
 static class ReaderScroll {
  internal static double VerticalOffsetSafe(this FlowDocumentScrollViewer viewer) {var scroll=Find(viewer);return scroll==null?0:scroll.VerticalOffset;}
  internal static void ScrollToOffset(this FlowDocumentScrollViewer viewer,double offset) {var scroll=Find(viewer);if(scroll!=null)scroll.ScrollToVerticalOffset(offset);}
  static ScrollViewer Find(DependencyObject node) {
   for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var child=VisualTreeHelper.GetChild(node,i);var found=child as ScrollViewer??Find(child);if(found!=null)return found;}
   return null;
  }
 }
}
