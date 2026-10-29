using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 partial class BrowserApp {
  // The toolbar's puzzle button: lists your extensions with their icons and opens one's popup, sized to its page.
  static WebView2 view0(Window popup) {return (WebView2)((Border)popup.Content).Child;}
  async Task RunExtensionToolbarTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"extension-toolbar");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;
   try {
    Navigate("https://example.com/",false);await WaitForPage();
    // A fixture extension with a 300 x 180 popup and an icon.
    string folder=Path.Combine(dir,"fixture");Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder,"manifest.json"),"{\"manifest_version\":3,\"name\":\"Toolbar fixture\",\"version\":\"1.0\",\"action\":{\"default_popup\":\"popup.html\",\"default_icon\":{\"16\":\"icon16.png\",\"32\":\"icon32.png\"}},\"permissions\":[\"tabs\"]}");
    File.WriteAllText(Path.Combine(folder,"popup.html"),"<!doctype html><meta charset=utf-8><style>body{margin:0;width:300px;height:180px;font:13px Segoe UI;background:#eef}</style><body>Fixture popup<script src=popup.js></script>");
    File.WriteAllText(Path.Combine(folder,"popup.js"),"chrome.tabs.query({active:true,currentWindow:true}).then(t=>{document.title='tabs:'+t.length+':'+(t[0]&&t[0].url||'')},e=>{document.title='tabs error '+e});");
    foreach(int size in new[]{16,32}) {
     var visual=new DrawingVisual();using(var context=visual.RenderOpen())context.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x6a,0x4c,0x9c)),null,new Point(size/2.0,size/2.0),size/2.0,size/2.0);
     var bitmap=new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
     using(var file=File.Create(Path.Combine(folder,"icon"+size+".png")))encoder.Save(file);
    }
    var target=await EnsureExtensionProfile();var added=await target.AddBrowserExtensionAsync(folder);RememberExtension(added.Id,folder,"folder","1.0");
    var read=ReadExtensionAction(added.Id,added.Name,folder,true);
    RegressionCheck("the manifest's popup and nearest-32px icon are read",delegate {Require(read.Popup=="popup.html"&&read.Icon!=null&&read.Icon.EndsWith("icon32.png"),read.Popup+" "+read.Icon);});
    RegressionCheck("paths leaving the extension are ignored",delegate {Require(PagePath("../x.html")==null&&PagePath("C:/x.html")==null&&PagePath("/pages/p.html")=="pages/p.html","path check");});

    ShowToolbarExtensions();await WaitFor(()=>extensionsPopup!=null&&extensionsPopup.IsOpen,5000);await Task.Delay(400);
    var rows=LogicalDescendants<Button>(extensionsPopup.Child).Where(b=>(b.Tag as string??"").StartsWith("extension-action:")).ToList();
    log.Add("rows: "+string.Join(", ",rows.Select(b=>System.Windows.Automation.AutomationProperties.GetName(b))));
    RegressionCheck("the puzzle button lists the extension with its icon",delegate {var row=rows.FirstOrDefault(b=>(string)b.Tag=="extension-action:"+added.Id);Require(row!=null,"no row");Require(LogicalDescendants<Image>(row).Any(),"no icon");});
    RegressionCheck("built-in and runtime extensions aren't listed",delegate {Require(rows.All(b=>!IsHiddenExtension(((string)b.Tag).Substring(17))),"hidden one listed");});
    CaptureElement((FrameworkElement)extensionsPopup.Child,"extensions-popup.png");

    var fixtureRow=rows.First(b=>(string)b.Tag=="extension-action:"+added.Id);fixtureRow.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    await WaitFor(()=>extensionActionWindow!=null,5000);
    RegressionCheck("clicking it closes the list and opens the popup",delegate {Require(!extensionsPopup.IsOpen&&extensionActionWindow!=null,"list open "+extensionsPopup.IsOpen);});
    var popup=extensionActionWindow;
    await WaitFor(()=>Math.Abs(popup.Width-302)<2&&Math.Abs(popup.Height-182)<2,8000).ContinueWith(t=>{});
    log.Add("popup size: "+popup.Width+" x "+popup.Height);
    log.Add("measured: "+await view0(popup).CoreWebView2.ExecuteScriptAsync("[document.documentElement.scrollWidth,document.body.scrollWidth,innerWidth,document.documentElement.clientWidth,innerHeight,document.documentElement.scrollHeight,devicePixelRatio].join()")+" view "+view0(popup).ActualWidth+"x"+view0(popup).ActualHeight);RegressionCheck("the popup sizes itself to the page",delegate {Require(Math.Abs(popup.Width-302)<2&&Math.Abs(popup.Height-182)<2,popup.Width+" x "+popup.Height);});
    var view=(WebView2)((Border)popup.Content).Child;await Task.Delay(800);
    log.Add("popup page: "+view.CoreWebView2.Source+" | "+view.CoreWebView2.DocumentTitle);
    RegressionCheck("the popup shows the extension's page",delegate {Require(view.CoreWebView2.Source=="chrome-extension://"+added.Id+"/popup.html","source "+view.CoreWebView2.Source);});
    await view.CoreWebView2.ExecuteScriptAsync("window.close()");await WaitFor(()=>extensionActionWindow==null,5000).ContinueWith(t=>{});
    RegressionCheck("window.close() closes the popup",delegate {Require(extensionActionWindow==null,"still open");});
    await added.RemoveAsync();
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
