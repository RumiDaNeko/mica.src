using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
namespace Mica {
 partial class BrowserApp {
  readonly Dictionary<CoreWebView2,string> storeInstallNonces=new Dictionary<CoreWebView2,string>();
  const string AmbientExtensionId="paponcgjfojgemddooebbgniglhkajkj";
  readonly Dictionary<string,Task<CoreWebView2BrowserExtension>> directInstalls=new Dictionary<string,Task<CoreWebView2BrowserExtension>>();
  static string StorePageId(string source) {try {Uri u;if(!Uri.TryCreate(source,UriKind.Absolute,out u)||u.Scheme!="https"||u.Host!="chromewebstore.google.com"||!u.IsDefaultPort||u.UserInfo.Length!=0)return null;return ExtensionPackage.ParseStoreId(source);}catch(FormatException){return null;}}
  async Task<CoreWebView2BrowserExtension> InstallStoreDirect(string input,bool review=false) {
   if(privateMode||closing)throw new InvalidOperationException("Extensions are unavailable in private windows.");
   string id=ExtensionPackage.ParseStoreId(input);Task<CoreWebView2BrowserExtension> task;
   if(directInstalls.TryGetValue(id,out task))return await task;
   task=InstallStorePackage(id,review);directInstalls[id]=task;
   try{return await task;}finally{directInstalls.Remove(id);}
  }
  async Task<CoreWebView2BrowserExtension> InstallStorePackage(string id,bool review) {
   if(IsRuntimeExtension(id))throw new InvalidOperationException("This component is managed by the browser.");
   Status("Downloading extension from Chrome Web Store…");var engine=await EnsureEnvironment();
   using(var prepared=await ExtensionPackage.PrepareAsync(id,Path.Combine(profile,"StoreExtensions"),engine.BrowserVersionString.Split(' ')[0])) {
    if(closing)throw new OperationCanceledException();
    // A click on a store page only asks: you see what the extension can do before it's added.
    if(review) {
     var answer=new TaskCompletionSource<bool>();string can=prepared.Permissions.Length==0?"It asks for no extra permissions.":"It can: "+string.Join("; ",prepared.Permissions.Take(6).Select(p=>PermissionDescription(p)))+(prepared.Permissions.Length>6?"; and "+(prepared.Permissions.Length-6)+" more.":".");
     ShowChoiceBubble(Find<System.Windows.Controls.Button>("AddressButton"),"\uEA86","Add extension?",prepared.Name,"Chrome Web Store · Version "+prepared.Version,can,new[]{new[]{"Cancel","cancel",""},new[]{"Add extension","add","primary"}},()=>!closing,delegate(string choice) {answer.TrySetResult(choice=="add");});
     if(!await answer.Task)throw new OperationCanceledException("Not added.");
    }
    var target=await EnsureExtensionProfile();var installed=await target.AddBrowserExtensionAsync(prepared.Folder);
    if(installed.Id!=id){await installed.RemoveAsync();throw new InvalidOperationException("Extension identity did not match the signed package.");}
    prepared.Commit();RememberExtension(id,prepared.Folder,"Chrome Web Store",prepared.Version);
    await installed.EnableAsync(true);if(id==AmbientExtensionId){foreach(var browser in Program.Browsers.Where(b=>!b.privateMode&&b.profile==profile)){browser.state.AmbientYoutube=true;browser.Save();}}Status(installed.Name+" added. Reload open pages to apply it.");
    if(extensionsWindow!=null){RenderExtensions(await GetExtensions());extensionStatus.Text=installed.Name+" added from Chrome Web Store.";}
    return installed;
   }
  }
  async void InstallFromStorePage(string id) {try{await InstallStoreDirect(id,true);}catch(OperationCanceledException){Status("Extension not added.");}catch(Exception e){Status(ExtensionError(e));}}
  bool HandleStoreInstallNavigation(CoreWebView2 core,CoreWebView2NavigationStartingEventArgs e) {
   if(!e.Uri.StartsWith("mica-install:",StringComparison.OrdinalIgnoreCase))return false;
   e.Cancel=true;Uri requested;string id=StorePageId(core.Source),nonce;
   if(!storeInstallNonces.TryGetValue(core,out nonce))return true;
   storeInstallNonces.Remove(core); // Each token works once; the bridge gets a fresh one when the page is set up again.
   if(!privateMode&&id!=null&&Uri.TryCreate(e.Uri,UriKind.Absolute,out requested)&&requested.Host==id&&(requested.AbsolutePath==""||requested.AbsolutePath=="/")&&requested.Query=="?token="+nonce&&requested.Fragment.Length==0)InstallFromStorePage(id);
   return true;
  }
  bool HandleStoreCrxDownload(WebView2 owner,CoreWebView2DownloadStartingEventArgs e) {
   string id=owner.CoreWebView2==null?null:StorePageId(owner.CoreWebView2.Source);
   if(id==null||!string.Equals(Path.GetExtension(e.ResultFilePath),".crx",StringComparison.OrdinalIgnoreCase))return false;
   e.Cancel=true;e.Handled=true;if(!privateMode)InstallFromStorePage(id);return true;
  }
  async Task AttachStoreButton(WebView2 view) {
   var live=LiveCore(view);if(privateMode||live==null)return;string address;try {address=live.Source;}catch(Exception){return;}Uri source;if(!Uri.TryCreate(address,UriKind.Absolute,out source)||source.Scheme!="https"||source.Host!="chromewebstore.google.com"||!source.IsDefaultPort)return;
   try{if(await view.ExecuteScriptAsync("!!window.__micaStoreBridge")=="true")return;string nonce=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");storeInstallNonces[view.CoreWebView2]=nonce;using(var reader=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Mica.StoreBridge.js")))await view.ExecuteScriptAsync(reader.ReadToEnd().Replace("__MICA_INSTALL_TOKEN__",nonce));}catch(Exception){}
  }
  async Task SetAmbientExtension(bool enabled) {
   if(privateMode)throw new InvalidOperationException("Ambient extensions are disabled in private windows.");
   var extension=(await GetExtensions()).FirstOrDefault(e=>e.Id==AmbientExtensionId);
   bool wasOn=extension!=null&&extension.IsEnabled;
   if(extension==null&&enabled)extension=await InstallStoreDirect(AmbientExtensionId);
   if(extension!=null&&extension.IsEnabled!=enabled)await extension.EnableAsync(enabled);
   // Only a real change needs YouTube reloaded; the launch-time check usually changes nothing.
   bool changed=wasOn!=enabled;
   foreach(var browser in Program.Browsers.Where(b=>!b.closing&&!b.privateMode&&b.profile==profile).ToArray()){
    browser.state.AmbientYoutube=enabled;browser.Save();
    foreach(var view in browser.views.Values.ToArray())try{await view.ExecuteScriptAsync("window.__micaAmbient && window.__micaAmbient.stop()");if(changed&&view.CoreWebView2!=null&&new Uri(view.CoreWebView2.Source).Host=="www.youtube.com")view.Reload();}catch(Exception){}
   }
   Status(enabled?"Ambient Light for YouTube enabled. Its controls appear on YouTube.":"Ambient Light for YouTube disabled.");
  }
 }
}
