using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 partial class BrowserApp {
  FaviconCache faviconCache;
  readonly Dictionary<string,object> faviconRequests=new Dictionary<string,object>();
  FaviconCache Icons {get {return faviconCache??(faviconCache=new FaviconCache(Path.Combine(profile,"Favicons"),privateMode));}}

  void SetHomeIcon(string buttonName,string asset) {
   using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Mica.Favicons."+asset+".ico")) {
    var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
    var frame=decoder.Frames.OrderBy(f=>Math.Abs(f.PixelWidth-32)).First();frame.Freeze();
    var icon=new Image {Source=frame,Width=22,Height=22,Stretch=Stretch.Uniform,IsHitTestVisible=false};
    RenderOptions.SetBitmapScalingMode(icon,BitmapScalingMode.HighQuality);
    var card=(StackPanel)Find<Button>(buttonName).Content;((Border)card.Children[0]).Child=icon;
   }
  }

  // In the sidebar, letter and page icons follow how dark the frame is, not the theme, so they stay readable on any frame colour.
  FrameworkElement CreateTabIcon(PageEntry tab,double size,bool sidebar=false) {
   bool lightGlyphs=sidebar?SidebarIsDark():state.Dark;
   var key=FaviconCache.GetKey(tab.Url);
   var bitmap=key==null?null:Icons.Read(key);
   if(bitmap!=null) {
    var image=new Image {Source=bitmap,Width=size,Height=size,Stretch=Stretch.Uniform,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,IsHitTestVisible=false};
    RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);return image;
   }
   if(key==null)return new TextBlock {Text=tab.Url=="mica://history"?"\uE81C":tab.Url=="mica://downloads"?"\uE896":tab.Url==Navigation.Tetris||Navigation.Game(tab.Url)!=null?"\uE7FC":"\uE80F",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=size-2,Foreground=sidebar?SideBrush("Accent"):Brush("Accent"),Width=size,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
   var host=new Uri(tab.Url).IdnHost;if(host.StartsWith("www.",StringComparison.OrdinalIgnoreCase))host=host.Substring(4);
   string[] colors=lightGlyphs?new[]{"#B8C5DF","#C6B6DC","#ACCFC4","#DFC1AB"}:new[]{"#536A95","#80618F","#457D6E","#936344"};
   int index=Convert.ToInt32(key.Substring(0,2),16)%colors.Length;
   return new TextBlock {Text=host.Substring(0,1).ToUpperInvariant(),FontFamily=new FontFamily("Segoe UI"),FontWeight=FontWeights.SemiBold,FontSize=size-2,Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[index])),Width=size,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
  }

  internal bool HasTabFavicon(string id) {
   var tab=state.Tabs.FirstOrDefault(t=>t.Id==id);return tab!=null&&Icons.Read(FaviconCache.GetKey(tab.Url))!=null;
  }

  async void RefreshFavicon(PageEntry tab,WebView2 view) {
   if(tab==null||closing||!IsCurrent(tab.Id,view))return;
   var request=new object();faviconRequests[tab.Id]=request;
   try {
    var core=view.CoreWebView2;if(core==null)return;
    string source=core.Source,key=FaviconCache.GetKey(source),favicon=core.FaviconUri;
    if(key==null||key!=FaviconCache.GetKey(tab.Url)||string.IsNullOrEmpty(favicon))return;
    using(var stream=await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png)) {
     object current;
     if(closing||!IsCurrent(tab.Id,view)||!faviconRequests.TryGetValue(tab.Id,out current)||current!=request||view.CoreWebView2!=core||core.Source!=source||core.FaviconUri!=favicon||FaviconCache.GetKey(tab.Url)!=key)return;
     if(stream==null)return;
     var bitmap=FaviconCache.Decode(stream);
     if(bitmap!=null&&Icons.Store(key,bitmap)){foreach(var same in state.Tabs.Where(t=>FaviconCache.GetKey(t.Url)==key).ToList())RefreshTabRow(same.Id);if(Find<ScrollViewer>("Home").IsVisible)RenderHome();}
    }
   } catch(Exception error) {if(!FaviconCache.IsExpected(error))throw;}
   finally {object current;if(faviconRequests.TryGetValue(tab.Id,out current)&&current==request)faviconRequests.Remove(tab.Id);}
  }
 }

 // Only origin hashes and small PNGs are persisted; page URLs never become file names.
 sealed class FaviconCache {
  const int Capacity=256,MaxBytes=1048576;
  readonly string directory;
  readonly bool memoryOnly;
  sealed class Entry {public BitmapSource Image;public string Digest;public long Used;}
  readonly Dictionary<string,Entry> images=new Dictionary<string,Entry>();
  long clock;
  public FaviconCache(string directory,bool memoryOnly=false) {this.directory=Path.GetFullPath(directory);this.memoryOnly=memoryOnly;}
  // The key of every address shown, kept: the tab list asks for each tab's icon on every redraw.
  static readonly Dictionary<string,string> keys=new Dictionary<string,string>(StringComparer.Ordinal);
  public static string GetKey(string url) {
   if(url==null)return null;string known;lock(keys){if(keys.TryGetValue(url,out known))return known;}
   string key=MakeKey(url);lock(keys){if(keys.Count>4000)keys.Clear();keys[url]=key;}return key;
  }
  static string MakeKey(string url) {
   Uri uri;if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||(uri.Scheme!="http"&&uri.Scheme!="https")||string.IsNullOrEmpty(uri.Host))return null;
   try {return Hash(Encoding.UTF8.GetBytes(uri.Scheme.ToLowerInvariant()+"://"+uri.IdnHost.ToLowerInvariant()+":"+uri.Port));}
   catch(UriFormatException) {return null;}
  }
  static string Hash(byte[] bytes) {using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
  static bool SafeKey(string key) {return key!=null&&key.Length==64&&key.All(c=>(c>='0'&&c<='9')||(c>='a'&&c<='f'));}
  bool SafeDirectory() {return !Directory.Exists(directory)||(File.GetAttributes(directory)&FileAttributes.ReparsePoint)==0;}
  static bool SafeFile(string path) {return !File.Exists(path)||(File.GetAttributes(path)&FileAttributes.ReparsePoint)==0;}
  public BitmapSource Read(string key) {
   if(!SafeKey(key))return null;
   Entry found;if(images.TryGetValue(key,out found)){found.Used=++clock;return found.Image;}
   if(memoryOnly)return null;
   // A site with no saved icon isn't looked for on disk again for a minute.
   DateTime missed;if(misses.TryGetValue(key,out missed)&&DateTime.UtcNow-missed<TimeSpan.FromMinutes(1))return null;
   try {
    string path=Path.Combine(directory,key+".png");
    if(!SafeDirectory()||!File.Exists(path)||!SafeFile(path)||new FileInfo(path).Length>MaxBytes){if(misses.Count>1000)misses.Clear();misses[key]=DateTime.UtcNow;return null;}
    byte[] bytes;using(var input=File.OpenRead(path))bytes=ReadBounded(input);if(bytes==null)return null;BitmapSource bitmap;
    using(var input=new MemoryStream(bytes,false))bitmap=Decode(input);
    if(bitmap!=null)Remember(key,bitmap,Hash(bytes));return bitmap;
   } catch(Exception error) {if(!IsExpected(error))throw;return null;}
  }
  readonly Dictionary<string,DateTime> misses=new Dictionary<string,DateTime>();
  void Remember(string key,BitmapSource bitmap,string digest) {
   misses.Remove(key);
   if(images.Count>=Capacity&&!images.ContainsKey(key))images.Remove(images.OrderBy(pair=>pair.Value.Used).First().Key);
   images[key]=new Entry {Image=bitmap,Digest=digest,Used=++clock};
  }
  public bool Store(string key,BitmapSource bitmap) {
   if(!SafeKey(key)||bitmap==null)return false;
   // Store only a normalized thumbnail, never the potentially large browser response.
   if(bitmap.PixelWidth>32||bitmap.PixelHeight>32) {
    double scale=Math.Min(32.0/bitmap.PixelWidth,32.0/bitmap.PixelHeight);
    bitmap=new TransformedBitmap(bitmap,new ScaleTransform(scale,scale));
   }
   if(!bitmap.IsFrozen)bitmap.Freeze();
   byte[] bytes;using(var output=new MemoryStream()){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(output);bytes=output.ToArray();}
   string digest=Hash(bytes);Entry old;Read(key);
   bool changed=!images.TryGetValue(key,out old)||old.Digest!=digest;Remember(key,bitmap,digest);
   if(memoryOnly)return changed;
   if(!changed)return false;
   string temporary=null;
   try {
    if(!SafeDirectory())return true;Directory.CreateDirectory(directory);
    string path=Path.Combine(directory,key+".png");if(!SafeFile(path))return true;
    temporary=Path.Combine(directory,key+"."+Guid.NewGuid().ToString("N")+".tmp");
    using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))output.Write(bytes,0,bytes.Length);
    if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
    temporary=null;Prune();
   } catch(Exception error) {if(!IsExpected(error))throw;}
   finally {if(temporary!=null)try {File.Delete(temporary);}catch(Exception error){if(!IsExpected(error))throw;}}
   return true;
  }
  void Prune() {
   var files=new DirectoryInfo(directory).GetFiles("*.png").Where(file=>SafeKey(Path.GetFileNameWithoutExtension(file.Name))&&(file.Attributes&FileAttributes.ReparsePoint)==0).OrderByDescending(file=>file.LastWriteTimeUtc).ToArray();
   foreach(var file in files.Skip(Capacity))file.Delete();
  }
  static byte[] ReadBounded(Stream input) {
   if(input.CanSeek&&input.Length>MaxBytes)return null;
   using(var buffer=new MemoryStream()) {
    var block=new byte[8192];int count;
    while((count=input.Read(block,0,block.Length))>0){if(buffer.Length+count>MaxBytes)return null;buffer.Write(block,0,count);}
    return buffer.ToArray();
   }
  }
  public static BitmapSource Decode(Stream input) {
   byte[] bytes=ReadBounded(input);
   // GetFaviconAsync requests PNG, and this cache writes PNG exclusively. Inspect
   // its dimensions before decoding to cap either axis without stretching it.
   if(bytes==null||bytes.Length<24||bytes[0]!=137||bytes[1]!=80||bytes[2]!=78||bytes[3]!=71||bytes[4]!=13||bytes[5]!=10||bytes[6]!=26||bytes[7]!=10)return null;
   uint width=ReadUInt32(bytes,16),height=ReadUInt32(bytes,20);
   if(width==0||height==0||width>16384||height>16384)return null;
   using(var buffer=new MemoryStream(bytes,false)) {
    var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
    if(width>32||height>32){if(width>=height)bitmap.DecodePixelWidth=32;else bitmap.DecodePixelHeight=32;}
    bitmap.StreamSource=buffer;bitmap.EndInit();bitmap.Freeze();return bitmap;
   }
  }
  static uint ReadUInt32(byte[] bytes,int index) {return ((uint)bytes[index]<<24)|((uint)bytes[index+1]<<16)|((uint)bytes[index+2]<<8)|bytes[index+3];}
  internal static bool IsExpected(Exception error) {return error is IOException||error is UnauthorizedAccessException||error is SecurityException||error is ArgumentException||error is FormatException||error is InvalidOperationException||error is NotSupportedException||error is COMException||error is OperationCanceledException||error is System.Net.WebException;}
 }
}
