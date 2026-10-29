using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Mica {
 partial class BrowserApp {
  // With ChatGPT chosen and its extension installed, the AI panel shows the extension's own panel, the extension
  // stays off the extensions page, and removing it from the AI settings brings the site back. Needs the store
  // package (MICA_CHATGPT_CRX); skipped without it.
  async Task RunChatGptExtProbe() {
   var log=regressionLog;string dir=Path.Combine(profile,"chatgptext");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;
   var flush=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(2)};flush.Tick+=delegate {try {File.WriteAllLines(Path.Combine(dir,"result.txt"),log.Concat(new[]{"(still running)"}));}catch(Exception){}};flush.Start();
   try {
    string crx=Environment.GetEnvironmentVariable("MICA_CHATGPT_CRX");
    if(string.IsNullOrEmpty(crx)||!File.Exists(crx)){log.Add("no ChatGPT extension package; skipped");await Task.Delay(1500);}
    else {
     var ai=AiExtensionById(ChatGptExtensionId);
     var prepared=ExtensionPackage.PrepareBytes(File.ReadAllBytes(crx),ChatGptExtensionId,Path.Combine(profile,"StoreExtensions"));
     var installed=await (await EnsureExtensionProfile()).AddBrowserExtensionAsync(prepared.Folder);
     LoadExtensionRecords();RememberExtension(ChatGptExtensionId,prepared.Folder,"Chrome Web Store",prepared.Version);
     RegressionCheck("the store package keeps its store id",delegate {Require(installed.Id==ChatGptExtensionId,installed.Id);});
     string worker=File.ReadAllText(Path.Combine(prepared.Folder,"background.js"));
     RegressionCheck("its classic worker loads Mica's side panel shim first",delegate {Require(worker.StartsWith("importScripts('/mica-sidepanel.js');")&&File.Exists(Path.Combine(prepared.Folder,"mica-sidepanel.js")),worker.Substring(0,Math.Min(80,worker.Length)));});
     Navigate("https://example.com/",false);await WaitFor(()=>CurrentView()!=null&&CurrentView().CoreWebView2!=null,20000);await WaitForPage().ContinueWith(t=>{});
     SetAiEnabled(true);ChooseAiProvider("chatgpt",null);SetAiOpen(true);
     Func<string> panel=()=>{var core=aiView==null?null:LiveCore(aiView);return core==null?"":core.Source;};
     await WaitFor(()=>ai.IsPanel(panel()),25000).ContinueWith(t=>{});await Task.Delay(8000);
     string shown=panel();log.Add("panel: "+shown);
     RegressionCheck("the AI panel shows the ChatGPT extension",delegate {Require(ai.IsPanel(shown),shown);});
     var core2=LiveCore(aiView);
     string current=core2==null?"":await core2.CallDevToolsProtocolMethodAsync("Runtime.evaluate","{\"expression\":\"chrome.tabs.getCurrent().then(t=>t===undefined?'side panel':'tab '+t.id)\",\"awaitPromise\":true,\"returnByValue\":true}");
     RegressionCheck("the panel counts as a side panel, not a tab",delegate {Require(current.Contains("side panel"),current);});
     string text=core2==null?"":await core2.ExecuteScriptAsync("(document.body&&document.body.innerText||'').slice(0,300)");log.Add("panel says: "+text);
     RegressionCheck("the panel moves on from the loading page to what the extension chose",delegate {Require(!shown.Contains("side-panel-loading")&&text.Trim('"').Length>0,shown+" "+text);});
     RegressionCheck("the panel names the extension",delegate {Require(aiSite.Text=="ChatGPT for Chrome",aiSite.Text);});
     var shot=await CapturePage(LiveCore(aiView),false);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shot));
     using(var output=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","chatgpt-extension-panel.png")))encoder.Save(output);
     // Signing in goes through chatgpt.com, which must open as a Mica tab.
     int before=state.Tabs.Count;if(core2!=null)await core2.ExecuteScriptAsync("(()=>{const b=[...document.querySelectorAll('button,a')].find(x=>/Continue to ChatGPT/i.test(x.innerText));if(b)b.click();})()");
     await WaitFor(()=>state.Tabs.Any(x=>(x.Url??"").StartsWith("https://chatgpt.com",StringComparison.OrdinalIgnoreCase)),15000).ContinueWith(t=>{});
     log.Add("tabs after signing in: "+string.Join(" | ",state.Tabs.Select(x=>x.Url)));
     RegressionCheck("signing in opens ChatGPT in a Mica tab",delegate {Require(state.Tabs.Any(x=>(x.Url??"").StartsWith("https://chatgpt.com",StringComparison.OrdinalIgnoreCase)),string.Join(" | ",state.Tabs.Select(x=>x.Url)));});
     ShowExtensions();await Task.Delay(2500);bool listed=displayedExtensions.Any(e=>e.Id==ChatGptExtensionId);if(activeExtensionsWindow!=null)activeExtensionsWindow.Close();
     RegressionCheck("the extensions page leaves the AI extension out",delegate {Require(!listed,"listed");});
     ShowAiSettings();await Task.Delay(1500);
     var note=aiSettingsWindow==null?null:LogicalDescendants<TextBlock>(aiSettingsWindow.Content as DependencyObject).FirstOrDefault(b=>(b.Tag as string)=="ai-claude-extension");
     var remove=aiSettingsWindow==null?null:LogicalDescendants<Button>(aiSettingsWindow.Content as DependencyObject).FirstOrDefault(b=>(b.Tag as string)=="ai-extension-remove");
     RegressionCheck("settings say ChatGPT uses the extension and offer to remove it",delegate {Require(note!=null&&note.Text.Contains("ChatGPT for Chrome extension")&&remove!=null&&remove.Visibility==Visibility.Visible,note==null?"no note":note.Text);});
     if(remove!=null)remove.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
     await WaitFor(()=>panel().StartsWith("https://chatgpt.com",StringComparison.OrdinalIgnoreCase),20000).ContinueWith(t=>{});
     bool gone=!(await (await EnsureExtensionProfile()).GetBrowserExtensionsAsync()).Any(e=>e.Id==ChatGptExtensionId);
     log.Add("after removing: "+panel());
     RegressionCheck("removing it brings the ChatGPT site back",delegate {Require(gone&&panel().StartsWith("https://chatgpt.com",StringComparison.OrdinalIgnoreCase),(gone?"":"still installed ")+panel());});
     if(aiSettingsWindow!=null)aiSettingsWindow.Close();
    }
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
