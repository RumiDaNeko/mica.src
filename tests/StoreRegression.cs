using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
namespace Mica {
 partial class BrowserApp {
  void CaptureExtensionWindow(string name) {extensionsWindow.UpdateLayout();var shot=new RenderTargetBitmap((int)extensionsWindow.ActualWidth,(int)extensionsWindow.ActualHeight,96,96,PixelFormats.Pbgra32);shot.Render(extensionsWindow);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(shot));using(var stream=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts",name)))png.Save(stream);}
  async Task RunStoreTest() {
   var log=new System.Collections.Generic.List<string>();int failures=0;string id="eimadpbcbfnmbkopoojfekhnkhdbieeh";CoreWebView2BrowserExtension added=null;PreparedExtension prepared=null;
   try {
    Require(smoke&&Path.GetFileName(profile).StartsWith("smoke-"),"Store test requires an isolated profile");
    var initial=await GetExtensions();string version=(await EnsureEnvironment()).BrowserVersionString.Split(' ')[0];
    ShowExtensions();await WaitFor(()=>!extensionsBusy,8000);await Task.Delay(200);CaptureExtensionWindow("mica-extensions-empty-light.png");
    prepared=await ExtensionPackage.PrepareAsync(id,Path.Combine(profile,"StoreExtensions"),version);string cancelledFolder=prepared.Folder;
    PresentExtensionReview(prepared);prepared=null;await Task.Delay(400);Require(!(await GetExtensions()).Any(e=>e.Id==id),"Package installed before confirmation");CaptureExtensionWindow("mica-extension-permissions.png");
    ExtensionUi<Button>("CancelExtension").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Require(!Directory.Exists(cancelledFolder)&&!(await GetExtensions()).Any(e=>e.Id==id),"Cancel left package or installed extension");
    log.Add("PASS official Store package verified; review grants no access and Cancel removes staging files");
    ExtensionUi<TextBox>("StoreLink").Text="https://chromewebstore.google.com/detail/dark-reader/"+id;
    await ReviewStoreExtension();Require(pendingExtension!=null,"Store review action failed: "+extensionStatus.Text);await ConfirmStoreExtension();
    added=(await GetExtensions()).SingleOrDefault(e=>e.Id==id);Require(added!=null&&added.IsEnabled,"Store extension not installed: "+extensionStatus.Text);
    Require(extensionRecords[id].Source=="Chrome Web Store"&&Directory.Exists(extensionRecords[id].Folder),"Store files or source metadata missing");
    CaptureExtensionWindow("mica-extensions-installed-light.png");extensionsWindow.Close();state.Dark=true;ApplyTheme();ShowExtensions();await WaitFor(()=>!extensionsBusy,8000);await Task.Delay(200);CaptureExtensionWindow("mica-extensions-installed-dark.png");
    log.Add("PASS Chrome Web Store link installs actual Dark Reader package with original ID and persists its files");
    using(var server=new RegressionServer()) {
     Navigate(server.Url+"ok",false);await WaitFor(()=>CurrentView()!=null&&CurrentView().CoreWebView2.DocumentTitle=="Regression OK",15000);await Task.Delay(1800);
     string active=await CurrentView().ExecuteScriptAsync("!!document.querySelector('style.darkreader')");Require(active=="true","Installed Dark Reader did not apply its content stylesheet on local fixture");
     log.Add("PASS Store-installed Dark Reader actually injects its stylesheet on a web page");
    }
    string installedFolder=extensionRecords[id].Folder;await RemoveManagedExtension(added);Require(!Directory.Exists(installedFolder)&&!extensionRecords.ContainsKey(id),"Removal left managed package files");added=null;Require((await GetExtensions()).Count==initial.Count,"Store removal altered runtime components");
    log.Add("PASS Store extension removal preserves runtime components");
   }catch(Exception e){failures++;log.Add("FAIL "+e);}
   if(prepared!=null)DisposePrepared(prepared);if(added!=null)try{await RemoveManagedExtension(added);}catch{}if(extensionsWindow!=null)extensionsWindow.Close();
   log.Add(failures==0?"PASS":"FAILURES: "+failures);File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","store-result.txt"),log);Application.Current.Shutdown(failures==0?0:1);
  }
 }
}
