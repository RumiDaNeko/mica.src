using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
namespace Mica {
 partial class BrowserApp {
  async Task RunStoreBridgeTest() {
   var log=new System.Collections.Generic.List<string>();int failures=0;CoreWebView2BrowserExtension installed=null;
   try {
    Require(smoke&&Path.GetFileName(profile).StartsWith("smoke-"),"Isolated profile required");
    Require(StorePageId("https://evil.test/detail/name/"+AmbientExtensionId)==null&&StorePageId("https://chromewebstore.google.com.evil.test/detail/name/"+AmbientExtensionId)==null,"Store origin validation failed");
    using(var server=new RegressionServer()) {
     Navigate(server.Url+"ok",false);await WaitFor(()=>CurrentView()!=null&&CurrentView().CoreWebView2.DocumentTitle=="Regression OK",15000);var view=CurrentView();var core=view.CoreWebView2;
     core.AddWebResourceRequestedFilter("https://chromewebstore.google.com/*",CoreWebView2WebResourceContext.All);
     core.WebResourceRequested+=delegate(object sender,CoreWebView2WebResourceRequestedEventArgs e){e.Response=environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes("<!doctype html><title>Store bridge fixture</title><button style='width:180px;height:44px'>Add to Chrome</button>")),200,"OK","Content-Type: text/html");};
     Navigate("https://chromewebstore.google.com/detail/ambient-light-for-youtube/"+AmbientExtensionId,false);await WaitFor(()=>core.DocumentTitle=="Store bridge fixture",10000);await Task.Delay(400);
     Require(await view.ExecuteScriptAsync("!!document.getElementById('mica-install-extension')")=="true","Store install bridge missing");
     await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",VaultCrypto.Json.Serialize(new {expression="setTimeout(()=>location.href='mica-install://"+AmbientExtensionId+"',20)",userGesture=false}));await Task.Delay(200);Require(!directInstalls.ContainsKey(AmbientExtensionId),"Unsolicited navigation installed extension");
     await view.ExecuteScriptAsync("document.querySelector('button').click()");await Task.Delay(200);Require(!directInstalls.ContainsKey(AmbientExtensionId),"Synthetic page click installed an extension");
     await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent","{\"type\":\"mousePressed\",\"x\":55,\"y\":25,\"button\":\"left\",\"clickCount\":1}");
     await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent","{\"type\":\"mouseReleased\",\"x\":55,\"y\":25,\"button\":\"left\",\"clickCount\":1}");
     await WaitFor(()=>extensionRecords.ContainsKey(AmbientExtensionId),45000);installed=(await GetExtensions()).Single(e=>e.Id==AmbientExtensionId);Require(installed.IsEnabled,"Official ambient is disabled");
     Require(extensionsWindow==null&&pendingExtension==null,"One-click install opened a second confirmation");
     log.Add("PASS trusted Store Add click downloads/verifies/installs official Ambient Light with no file picker or second prompt; synthetic click ignored");
     string folder=extensionRecords[AmbientExtensionId].Folder;File.Copy(Path.Combine(folder,"manifest.json"),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","ambient-official-manifest.json"),true);Require(File.Exists(Path.Combine(folder,"manifest.json")),"Official package missing");log.Add("INFO official ambient installed: "+installed.Name+" version "+extensionRecords[AmbientExtensionId].Version);
     await SetAmbientExtension(false);Require(!(await GetExtensions()).Single(e=>e.Id==AmbientExtensionId).IsEnabled,"Ambient disable failed");
     await SetAmbientExtension(true);Require((await GetExtensions()).Single(e=>e.Id==AmbientExtensionId).IsEnabled&&StateStore.Load(statePath).AmbientYoutube,"Ambient enable/persistence failed");
     Require(await view.ExecuteScriptAsync("!window.__micaAmbient")=="true","Custom ambient effect still running");log.Add("PASS integrated ambient toggles the actual Store extension and persists preference");
     ShowExtensions();await WaitFor(()=>!extensionsBusy,8000);await Task.Delay(250);Require(extensionsWindow.WindowStyle==WindowStyle.None&&extensionsWindow.AllowsTransparency,"Extensions popup still has native square frame");CaptureExtensionWindow("mica-extensions-v0111-light.png");extensionsWindow.Close();state.Dark=true;ApplyTheme();ShowExtensions();await WaitFor(()=>!extensionsBusy,8000);await Task.Delay(250);CaptureExtensionWindow("mica-extensions-v0111-dark.png");extensionsWindow.Close();log.Add("PASS extension manager uses rounded frameless themed popup");
     ShowFind();await Task.Delay(250);var find=toolWindows["find"];Require(find.Width<=440&&find.Height<=120,"Find is not compact");var spot=IslandBounds();Require(spot.HasValue&&Math.Abs(spot.Value.Right-(find.Left+find.Width))<2&&Math.Abs(spot.Value.Bottom-(find.Top+find.Height))<2,"Find not anchored to the island corner");await Task.Delay(400);CaptureDialog(find,"mica-find-bottom-right.png");find.Close();log.Add("PASS compact Find grows from the island corner");
    }
   }catch(Exception e){failures++;log.Add("FAIL "+e);}
   finally {CloseTools();if(extensionsWindow!=null)extensionsWindow.Close();}
   if(installed!=null)try{await RemoveManagedExtension(installed);}catch(Exception){}
   log.Add(failures==0?"PASS":"FAILURES: "+failures);File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","store-bridge-result.txt"),log);Application.Current.Shutdown(failures==0?0:1);
  }
 }
}

