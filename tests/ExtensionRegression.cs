using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Mica {
 partial class BrowserApp {
  async Task ReloadExtensionFixture(string url) {
   var view=CurrentView();
   var completed=new TaskCompletionSource<bool>();
   EventHandler<CoreWebView2NavigationCompletedEventArgs> handler=null;
   handler=delegate(object sender,CoreWebView2NavigationCompletedEventArgs e) {completed.TrySetResult(e.IsSuccess);};
   view.CoreWebView2.NavigationCompleted+=handler;
   try {
    view.CoreWebView2.Navigate(url);
    Require(await Task.WhenAny(completed.Task,Task.Delay(15000))==completed.Task,"Fixture navigation timed out");
    Require(await completed.Task,"Fixture navigation failed");
    // document_idle content scripts may execute just after NavigationCompleted.
    await Task.Delay(500);
   }finally {view.CoreWebView2.NavigationCompleted-=handler;}
  }
  async Task RunExtensionTest() {
   var log=new List<string>();int failures=0;CoreWebView2BrowserExtension fixture=null;
   try {
    Require(smoke&&Path.GetFileName(profile).StartsWith("smoke-",StringComparison.Ordinal),"Extension test requires an isolated smoke profile");
    Require(Active.Url=="mica://home","Extension initialization must be tested from home");
    // WebView2 may include built-in PDF/clipboard components even in a new profile.
    var initial=await GetExtensions();var baseline=initial.ToDictionary(e=>e.Id,e=>e.IsEnabled);
    log.Add("Runtime baseline: "+string.Join(", ",initial.Select(e=>e.Name+" ("+e.Id+")")));
    log.Add("PASS extension management initializes from home without an existing browser tab");
    ShowExtensions();await WaitFor(delegate {return !extensionsBusy&&extensionsWindow!=null;},8000);await Task.Delay(240);
    Require(extensionCommands.IsEnabled,"Manager controls stayed disabled");
    Require(displayedExtensions.All(e=>!IsHiddenExtension(e.Id))&&ExtensionUi<System.Windows.Controls.TextBlock>("ExtensionCount").Text==initial.Count(e=>!IsHiddenExtension(e.Id))+" installed","Runtime components leaked into user list/count");
    log.Add("PASS built-in PDF and clipboard extensions are hidden without disabling them");
    var visual=extensionsWindow;visual.UpdateLayout();var picture=new System.Windows.Media.Imaging.RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);picture.Render(visual);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(picture));using(var output=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","mica-extensions.png")))png.Save(output);
    extensionsWindow.Close();Require(extensionsWindow==null,"Manager did not close");log.Add("PASS manager opens with enabled controls and closes cleanly");
    string folder=Path.Combine(profile,"extension-fixture");Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder,"manifest.json"),"{\"manifest_version\":3,\"name\":\"Mica local regression fixture\",\"version\":\"1.0\",\"content_scripts\":[{\"matches\":[\"http://127.0.0.1/*\"],\"js\":[\"content.js\"],\"run_at\":\"document_end\"}]}");
    File.WriteAllText(Path.Combine(folder,"content.js"),"document.documentElement.setAttribute('data-mica-extension-test','active');");
    fixture=await InstallExtension(folder);
    var installed=await GetExtensions();Require(!baseline.ContainsKey(fixture.Id)&&installed.Count==baseline.Count+1&&installed.Any(e=>e.Id==fixture.Id&&e.IsEnabled),"Installed extension missing or disabled");
    log.Add("PASS MV3 unpacked extension installs enabled and enumerates");
    ShowExtensions();await WaitFor(()=>!extensionsBusy,8000);await Task.Delay(150);
    Require(displayedExtensions.Count==1&&extensionRows.Children.OfType<System.Windows.Controls.Border>().Any(b=>(string)b.Tag==fixture.Id),"User extension not shown in redesigned manager");
    var row=extensionRows.Children.OfType<System.Windows.Controls.Border>().First(b=>(string)b.Tag==fixture.Id);
    var toggle=((System.Windows.Controls.Grid)row.Child).Children.OfType<System.Windows.Controls.StackPanel>().SelectMany(p=>p.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>()).FirstOrDefault();
    Require(toggle!=null&&toggle.IsChecked==true&&toggle.ActualWidth>=30&&toggle.ActualWidth<=48,"Enabled extension has no switch that's on: "+(toggle==null?"none":toggle.IsChecked+" "+toggle.ActualWidth));
    {var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(row.ActualWidth),(int)Math.Ceiling(row.ActualHeight),96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(row);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(profile,"extension-row.png")))encoder.Save(file);}
    log.Add("PASS an enabled extension shows a switch that's on");
    ExtensionUi<System.Windows.Controls.TextBox>("ExtensionSearch").Text="nothing matches";Require(!extensionRows.Children.OfType<System.Windows.Controls.Border>().Any(b=>b.Tag!=null),"Extension search failed");ExtensionUi<System.Windows.Controls.TextBox>("ExtensionSearch").Clear();
    ExtensionUi<System.Windows.Controls.Button>("FilterDisabledExtensions").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));Require(!extensionRows.Children.OfType<System.Windows.Controls.Border>().Any(b=>b.Tag!=null),"Disabled filter included enabled item");ExtensionUi<System.Windows.Controls.Button>("FilterAllExtensions").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));extensionsWindow.Close();
    log.Add("PASS redesigned manager shows user extension and filters by name and enabled state");
    using(var server=new RegressionServer()) {
     Navigate(server.Url+"ok",false);await WaitFor(delegate {NavigationProgress progress;return CurrentView()!=null&&CurrentView().CoreWebView2.DocumentTitle=="Regression OK"&&navigations.TryGetValue(state.ActiveId,out progress)&&!progress.IsLoading;},15000);
     await ReloadExtensionFixture(server.Url+"ok?installed");
     Require(await CurrentView().CoreWebView2.ExecuteScriptAsync("document.documentElement.getAttribute('data-mica-extension-test')")=="\"active\"","Installed content script did not execute");
     log.Add("PASS extension injects its harmless content script into a real local HTTP page");
     await fixture.EnableAsync(false);
     Require(!(await GetExtensions()).Single(e=>e.Id==fixture.Id).IsEnabled,"Disable state was not saved");
     await ReloadExtensionFixture(server.Url+"ok?disabled");
     Require(await CurrentView().CoreWebView2.ExecuteScriptAsync("document.documentElement.getAttribute('data-mica-extension-test')")=="null","Disabled extension still injected after navigation");
     log.Add("PASS disabled extension stops injecting after page reload");
     await fixture.EnableAsync(true);
     Require((await GetExtensions()).Single(e=>e.Id==fixture.Id).IsEnabled,"Enable state was not saved");
     await ReloadExtensionFixture(server.Url+"ok?enabled");
     Require(await CurrentView().CoreWebView2.ExecuteScriptAsync("document.documentElement.getAttribute('data-mica-extension-test')")=="\"active\"","Re-enabled extension failed to inject");
     log.Add("PASS re-enabled extension resumes content script execution");
     string removedId=fixture.Id;await fixture.RemoveAsync();fixture=null;
     var remaining=await GetExtensions();
     Require(!remaining.Any(e=>e.Id==removedId),"Removed extension still enumerates");
     Require(remaining.Count==baseline.Count&&remaining.All(e=>baseline.ContainsKey(e.Id)&&baseline[e.Id]==e.IsEnabled),"Runtime extensions changed during fixture test");
     await ReloadExtensionFixture(server.Url+"ok?removed");
     Require(await CurrentView().CoreWebView2.ExecuteScriptAsync("document.documentElement.getAttribute('data-mica-extension-test')")=="null","Removed extension still injects");
     log.Add("PASS removal clears enumeration and stops content script execution");
    }
   }catch(Exception e) {failures++;log.Add("FAIL extension regression: "+e);}
   if(fixture!=null)try {await fixture.RemoveAsync();}catch(Exception e){failures++;log.Add("FAIL fixture cleanup: "+e.Message);}
   log.Add(failures==0?"PASS":"FAILURES: "+failures);
   string artifacts=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts"));Directory.CreateDirectory(artifacts);
   File.WriteAllLines(Path.Combine(artifacts,"extensions-result.txt"),log);
   Application.Current.Shutdown(failures==0?0:1);
  }
 }
}
