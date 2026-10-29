using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace Mica {
 // With Claude chosen and Anthropic's Claude for Chrome extension installed, the AI panel shows the extension's
 // own side panel instead of claude.ai, so Claude can read and act on the page. The engine has no side panel, so
 // the panel page is opened in the AI panel and pointed at the current tab, as Chrome does per tab. ChatGPT's
 // extension is hosted the same way; its browser control comes from the ChatGPT desktop app.
 partial class BrowserApp {
  // Mica's panel shows the page the extension chose for the side panel (for the tab in front, else for all tabs).
  const string FollowPanelPath="if(chrome.sidePanel&&chrome.sidePanel.getOptions){const here=()=>location.pathname.replace(/^\\//,'');const check=async()=>{try{let o=null;const t=(await chrome.tabs.query({active:true,currentWindow:true}))[0];if(t)o=await chrome.sidePanel.getOptions({tabId:t.id});if(!o||!o.path)o=await chrome.sidePanel.getOptions({});if(o&&o.enabled!==false&&o.path){const want=o.path.replace(/^\\//,'');if(want.split('?')[0]!==here()&&self.__micaPanelPages.includes(want.split('?')[0]))location.replace(chrome.runtime.getURL(want));}}catch(e){}};setTimeout(check,200);setInterval(check,1500);}";
  static bool IsAiPanelExtension(string id) {return AiExtensionById(id)!=null;}
  static AiExtension Claude {get {return AiExtensionById(ClaudeExtensionId);}}
  internal static string ClaudeExtensionStore {get {return Claude.Store;}}
  static string ClaudePanelPage {get {return Claude.PanelUrl;}}
  // The extension showing in the panel now, if any.
  AiExtension aiExtension;
  bool claudeExtensionActive {get {return aiExtension!=null&&aiExtension.FollowsTab;}}
  // The panel is bound to a page, not just a Mica tab: a rested tab comes back with a new page, which the
  // extension knows by a new id.
  string claudeBoundTab;Microsoft.Web.WebView2.Wpf.WebView2 claudeBoundView;bool claudeBinding;

  // Extensions that drive pages (Claude for Chrome) attach the engine's debugger, which otherwise puts Edge's
  // "started debugging this browser" bar above the page and shrinks it while Claude works.
  const string QuietDebuggerArgument=" --silent-debugger-extension-api";
  static readonly System.Collections.Generic.HashSet<string> aiPatchChecked=new System.Collections.Generic.HashSet<string>();
  internal Task<bool> ClaudeExtensionInstalled() {return AiExtensionInstalled(Claude);}
  internal async Task<bool> AiExtensionInstalled(AiExtension ai) {
   try {
    var target=await EnsureExtensionProfile();var extension=(await target.GetBrowserExtensionsAsync()).FirstOrDefault(e=>e.Id==ai.Id);
    if(extension==null||!extension.IsEnabled)return false;
    // Installed before Mica hosted its panel: add the side panel shim, then reload it so its worker picks it up.
    if(aiPatchChecked.Add(ai.Id)) {
     LoadExtensionRecords();ExtensionRecord record;
     if(extensionRecords.TryGetValue(ai.Id,out record)&&!string.IsNullOrEmpty(record.Folder)&&Directory.Exists(record.Folder)) {
      string folder=record.Folder;
      if(await Task.Run(()=>ClaudeSidePanelPatch.Apply(folder,ai.Pages))) {
       await extension.EnableAsync(false);await extension.EnableAsync(true);
       // Reloading takes the extension's pages down with their view; an open panel starts a fresh one.
       if(aiView!=null&&ai.IsPanel(aiLoaded)){DisposeAiView();claudeBoundTab=null;if(aiOpen)EnsureAiView();}
      }
     }
    }
    return true;
   }
   catch(Exception){return false;}
  }
  // The panel's address: the AI's extension panel when it can take the site's place, the chosen chat site otherwise.
  async Task<string> AiPanelAddress() {
   var ai=privateMode?null:AiExtensionFor(AiProviderId());
   aiExtension=ai!=null&&await AiExtensionInstalled(ai)?ai:null;
   RefreshAiHeader();
   return aiExtension!=null?aiExtension.PanelUrl:AiAddress();
  }
  // In Chrome the panel isn't a tab, and the extensions refuse to run anywhere else. Mica's AI panel is that side
  // panel here, so on an extension's panel pages it answers as one: no tab of its own.
  internal static Task ShimClaudeSidePanel(CoreWebView2 core) {
   var script=new StringBuilder();
   foreach(var ai in AiExtensions) {
    string pages=new JavaScriptSerializer().Serialize(ai.Pages);
    script.Append("if(self.chrome&&chrome.tabs&&"+pages+".some(p=>location.href.startsWith('chrome-extension://"+ai.Id+"/'+p))){self.__micaPanelPages="+pages+";");
    script.Append("chrome.tabs.getCurrent=function(callback){if(typeof callback==='function'){callback(undefined);return;}return Promise.resolve(undefined);};");
    script.Append(ClaudeSidePanelPatch.TabsShim);if(ai.FollowsPanelPath)script.Append(FollowPanelPath);script.Append("}\n");
   }
   return core.AddScriptToExecuteOnDocumentCreatedAsync(script.ToString());
  }
  static bool IsClaudePanel(string url) {return Claude.IsPanel(url);}

  // The AI settings' note on the chosen AI's extension: whether it's in use, how to get it, and removing it.
  TextBlock aiExtensionNote;Button aiExtensionGet,aiExtensionRemove;
  internal void RefreshAiExtensionNote() {
   if(aiExtensionNote==null)return;
   var ai=AiExtensionFor(AiProviderId());var note=aiExtensionNote;
   if(ai==null||privateMode){note.Text="";note.Visibility=aiExtensionGet.Visibility=aiExtensionRemove.Visibility=System.Windows.Visibility.Collapsed;return;}
   AiExtensionInstalled(ai).ContinueWith(t=>{
    if(aiExtensionNote!=note||AiExtensionFor(AiProviderId())!=ai)return;bool has=t.Result;
    bool claude=ai.Provider=="claude";
    note.Visibility=System.Windows.Visibility.Visible;
    note.Text=claude?(has?"Claude opens in your Claude for Chrome extension, so it can read and act on the page.":"With Claude for Chrome installed (paid Claude plans), Claude can read and act on the page from this panel.")
     :(has?"ChatGPT opens in your ChatGPT for Chrome extension. With the ChatGPT desktop app running, it can read and act on the page.":"With ChatGPT for Chrome and the ChatGPT desktop app, ChatGPT can read and act on the page from this panel.");
    aiExtensionGet.Content=claude?"Get Claude for Chrome":"Get ChatGPT for Chrome";
    aiExtensionGet.Visibility=has?System.Windows.Visibility.Collapsed:System.Windows.Visibility.Visible;
    aiExtensionRemove.Visibility=has?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
   },TaskScheduler.FromCurrentSynchronizationContext());
  }
  async void RemoveAiExtension(AiExtension ai) {
   if(ai==null)return;
   try {
    var extension=(await (await EnsureExtensionProfile()).GetBrowserExtensionsAsync()).FirstOrDefault(e=>e.Id==ai.Id);
    if(extension!=null)await RemoveManagedExtension(extension);
   }catch(Exception e){Status("Couldn't remove the extension: "+e.Message);return;}
   aiPatchChecked.Remove(ai.Id);
   foreach(var browser in Program.Browsers.Where(b=>!b.closing&&!b.privateMode&&b.profile==profile).ToList()) {
    if(browser.aiView!=null&&ai.IsPanel(browser.aiLoaded)){browser.DisposeAiView();browser.claudeBoundTab=null;if(browser.aiOpen)browser.EnsureAiView();}
    browser.RefreshAiExtensionNote();
   }
  }
  // The login sends the tab back to the extension's own address, which it watches for; let the engine take it.
  static bool IsClaudeSignInReturn(string url) {return (url??"").StartsWith("chrome-extension://"+ClaudeExtensionId+"/oauth_callback.html",StringComparison.OrdinalIgnoreCase);}

  // Points the panel at the tab in front: the extension knows tabs by its own ids, which it maps to the page's
  // DevTools target. Pages that aren't websites (Mica's own) keep the last binding.
  internal async void BindClaudePanel() {
   if(claudeBinding||!aiOpen||aiView==null)return;
   var panel=LiveCore(aiView);var page=CurrentView();var tab=page==null?null:LiveCore(page);string tabId=state.ActiveId;
   if(panel==null||tab==null||!IsClaudePanel(panel.Source)||!Navigation.IsWeb(tab.Source))return;
   claudeBinding=true;
   try {
    var info=new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(await tab.CallDevToolsProtocolMethodAsync("Target.getTargetInfo","{}"));
    string target=(string)((System.Collections.Generic.Dictionary<string,object>)info["targetInfo"])["targetId"];
    string lookup="(async()=>{const t=(await chrome.debugger.getTargets()).find(x=>x.id==="+new JavaScriptSerializer().Serialize(target)+");return t&&t.tabId?String(t.tabId):'';})()";
    string result=await panel.CallDevToolsProtocolMethodAsync("Runtime.evaluate",new JavaScriptSerializer().Serialize(new {expression=lookup,awaitPromise=true,returnByValue=true}));
    var found=Regex.Match(result,"\"value\":\"(\\d+)\"");
    if(!found.Success||closing||LiveCore(aiView)!=panel)return;
    claudeBoundTab=tabId;claudeBoundView=page;string want=ClaudePanelPage+"?tabId="+found.Groups[1].Value;
    if(!string.Equals(panel.Source,want,StringComparison.OrdinalIgnoreCase))panel.Navigate(want);
   }catch(Exception){}
   finally {claudeBinding=false;}
  }
  // Called as tabs change; binds again only when another tab (or a new page in the bound tab) came to the front. Tabs Claude opened for its own
  // work don't take the panel along, or the chat would reload in the middle of a task.
  void FollowTabForClaude() {if(claudeExtensionActive&&aiOpen&&(state.ActiveId!=claudeBoundTab||CurrentView()!=claudeBoundView)&&!claudeOpenedTabs.Contains(state.ActiveId??""))BindClaudePanel();}
  readonly System.Collections.Generic.HashSet<string> claudeOpenedTabs=new System.Collections.Generic.HashSet<string>();

  // The extension asks for a tab through this address (the engine can't open one in Mica): the page to open
  // after the #, and active=0 to leave it in the background as chrome.tabs.create can.
  internal const string ExtensionTabRequest="https://open.mica.invalid/";
  static bool IsExtensionTabRequest(string url) {return (url??"").StartsWith(ExtensionTabRequest,StringComparison.OrdinalIgnoreCase);}
  internal async void OpenExtensionTab(string request) {
   Uri uri;if(!Uri.TryCreate(request,UriKind.Absolute,out uri))return;
   var focus=Regex.Match(uri.Query,"(?:^|[?&])focus=([^&]+)");
   if(focus.Success){await FrontTabByTarget(Uri.UnescapeDataString(focus.Groups[1].Value));return;}
   string url=Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));if(!Navigation.IsWeb(url))return;
   bool background=Regex.IsMatch(uri.Query,"(^|[?&])active=0(&|$)"),claude=Regex.IsMatch(uri.Query,"(^|[?&])from=claude(&|$)");
   if(!background){Navigate(url,true);if(claude&&state.ActiveId!=null)claudeOpenedTabs.Add(state.ActiveId);return;}
   var tab=state.AddTab(url);tab.Title=new Uri(url).Host;if(claude)claudeOpenedTabs.Add(tab.Id);
   RenderTabs();Save();await CreateTabView(tab,true);
  }
  // Claude asked for one of its tabs to come forward; the panel stays with the conversation's tab.
  static async Task FrontTabByTarget(string target) {
   foreach(var browser in Program.Browsers.Where(b=>!b.closing).ToList())
    foreach(var pair in browser.views.ToList()) {
     var core=LiveCore(pair.Value);if(core==null)continue;
     try {
      var info=new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(await core.CallDevToolsProtocolMethodAsync("Target.getTargetInfo","{}"));
      if((string)((System.Collections.Generic.Dictionary<string,object>)info["targetInfo"])["targetId"]!=target)continue;
     }catch(Exception){continue;}
     if(pair.Key!=browser.claudeBoundTab)browser.claudeOpenedTabs.Add(pair.Key);
     if(browser.state.ActiveId!=pair.Key)browser.SelectTab(pair.Key);
     return;
    }
  }
 }
}
