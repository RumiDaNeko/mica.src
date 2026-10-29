using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Xilium.CefGlue;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common.Handlers;

namespace Mica {
 // The AI side panel, as on Windows (BrowserAiPanel.cs, BrowserClaudeExtension.cs): with Claude for Chrome installed,
 // the panel shows the extension's own side panel page pointed at the tab in front, so Claude can read and act on
 // the page; without it, claude.ai. Chromium here is Chrome itself (CEF Chrome style), so the extension runs as in
 // Chrome. Like WebView2, CEF has no side panel, so Mica hosts the page and adds the same shims Windows does.
 sealed partial class BrowserWindow {
  static AiExtension Claude {get {return BrowserApp.AiExtensionById(BrowserApp.ClaudeExtensionId);}}
  readonly Border aiPanel=new Border {IsVisible=false,Width=420,BorderThickness=new Thickness(1,0,0,0)};
  readonly Grid aiHost=new Grid();
  readonly StackPanel aiIntro=new StackPanel {Margin=new Thickness(16,14,16,12)};
  readonly TextBlock aiNote=new TextBlock {TextWrapping=TextWrapping.Wrap,FontSize=12};
  readonly Button aiGet=new Button {Content="Get Claude for Chrome",Margin=new Thickness(0,10,8,0),Padding=new Thickness(12,6)},aiRestart=new Button {Content="Restart Mica",Margin=new Thickness(0,10,0,0),Padding=new Thickness(12,6),IsVisible=false};
  Button aiToggle;
  AvaloniaCefBrowser aiView;string aiBoundTab;bool aiBinding;
  // Installed and loaded this run (an extension installed now loads on the next start).
  readonly bool claudeLoaded;

  // As on Windows (BrowserAiPanel.cs): a ✦ button in the toolbar, and a dock beside the page with a slim header.
  void BuildAiPanel(Panel toolbarRight,Grid body) {
   aiToggle=IconButton(Glyphs.Sparkle,"Claude ("+Mod+"Shift+Space)",16);aiToggle.Click+=delegate {ToggleAi();};
   toolbarRight.Children.Insert(0,aiToggle);
   Res(Res(aiPanel,Border.BackgroundProperty,"Canvas"),Border.BorderBrushProperty,"Line");
   var dock=new DockPanel();aiPanel.Child=dock;
   var head=Res(new Border {BorderThickness=new Thickness(0,0,0,1),Height=40},Border.BorderBrushProperty,"Line");
   var headRow=new Grid {Margin=new Thickness(14,0,6,0)};head.Child=headRow;
   var label=new StackPanel {Orientation=Orientation.Horizontal,Spacing=9,VerticalAlignment=VerticalAlignment.Center};
   label.Children.Add(Glyph(Glyphs.Sparkle,14,"accent"));label.Children.Add(new TextBlock {Text="Claude",FontWeight=FontWeight.SemiBold,VerticalAlignment=VerticalAlignment.Center});
   headRow.Children.Add(label);
   var close=CaptionButton(Glyphs.Close,"Close ("+Mod+"Shift+Space)");close.HorizontalAlignment=HorizontalAlignment.Right;close.Click+=delegate {ToggleAi();};headRow.Children.Add(close);
   DockPanel.SetDock(head,Dock.Top);dock.Children.Add(head);
   Muted(aiNote);aiIntro.Children.Add(aiNote);
   var actions=new StackPanel {Orientation=Orientation.Horizontal};
   foreach(var b in new[]{aiGet,aiRestart}){b.Classes.Add("flat");b.Classes.Add("on");actions.Children.Add(b);}
   aiIntro.Children.Add(actions);
   aiGet.Click+=async delegate {await GetClaude();};
   aiRestart.Click+=delegate {Restart();};
   DockPanel.SetDock(aiIntro,Dock.Top);dock.Children.Add(aiIntro);dock.Children.Add(aiHost);
   Grid.SetColumn(aiPanel,1);body.Children.Add(aiPanel);
   RefreshAiNote();
  }
  void RefreshAiNote() {
   bool installed=CrossExtensions.IsInstalled(profile,BrowserApp.ClaudeExtensionId);
   aiNote.Text=installed?"Claude for Chrome is installed. Restart Mica to start it."
    :"With Claude for Chrome installed (paid Claude plans), Claude can read and act on the page from this panel. Until then, this is claude.ai.";
   // With the extension running, its own page says everything.
   aiIntro.IsVisible=!claudeLoaded;
   aiGet.IsVisible=!installed;aiRestart.IsVisible=installed&&!claudeLoaded;
  }
  void ToggleAi() {
   aiPanel.IsVisible=!aiPanel.IsVisible;
   aiToggle.Classes.Set("on",aiPanel.IsVisible);
   if(aiPanel.IsVisible&&aiView==null) {
    aiView=new AvaloniaCefBrowser();
    aiView.Address=claudeLoaded?Claude.PanelUrl:"https://claude.ai/new";
    aiView.LifeSpanHandler=new PopupsToTabs(this);
    aiView.LoadEnd+=delegate(object s,Xilium.CefGlue.Common.Events.LoadEndEventArgs e) {if(e.Frame.IsMain)Dispatcher.UIThread.Post(delegate {BindClaudePanel(false);});};
    aiHost.Children.Add(aiView);
   }
   else if(aiPanel.IsVisible)BindClaudePanel(false);
  }

  // Points the panel at the tab in front. Claude's panel works for one tab (sidepanel.html?tabId=N), by the
  // extension's own tab ids; the panel page asks chrome.tabs which tab shows the active page.
  internal async void BindClaudePanel(bool force) {
   var tab=Active;
   if(!claudeLoaded||aiBinding||aiView==null||!aiPanel.IsVisible||tab==null||tab.View==null||!Navigation.IsWeb(tab.Entry.Url))return;
   if(!force&&aiBoundTab==tab.Entry.Id&&Claude.IsPanel(aiView.Address)&&aiView.Address.Contains("tabId="))return;
   aiBinding=true;
   try {
    string url=new JavaScriptSerializer().Serialize(tab.View.Address);
    string id=await EvalAsync(aiView,"(async()=>{const ts=await chrome.tabs.query({});const t=ts.find(x=>x.url==="+url+");return t?String(t.id):'';})()",10);
    if(string.IsNullOrEmpty(id)||aiView==null)return;
    aiBoundTab=tab.Entry.Id;
    string want=Claude.PanelUrl+"?tabId="+id;
    if(!string.Equals(aiView.Address,want,StringComparison.OrdinalIgnoreCase))aiView.Address=want;
   } catch(Exception e) {Trace("bind failed: "+e.Message);}
   finally {aiBinding=false;}
  }

  async Task GetClaude() {
   aiGet.IsEnabled=false;aiNote.Text="Getting Claude for Chrome from the Chrome Web Store…";
   try {await CrossExtensions.Install(profile,BrowserApp.ClaudeExtensionId,CefRuntime.ChromeVersion);} catch(Exception e) {aiNote.Text="Couldn't get Claude for Chrome: "+e.Message;aiGet.IsEnabled=true;return;}
   aiGet.IsEnabled=true;RefreshAiNote();
  }
  // Extensions load at start, so a new one needs a restart: the same window comes back with its session.
  void Restart() {
   Save();
   var args=Environment.GetCommandLineArgs().Skip(1).Where(a=>a!="--self-test").ToArray();
   var start=new ProcessStartInfo(Environment.ProcessPath) {UseShellExecute=false};
   foreach(var a in args)start.ArgumentList.Add(a);
   Process.Start(start);Close();
  }

  // CefGlue's EvaluateJavaScript returns at once and can't wait for a promise, so async code leaves its result on
  // the page under a one-off name, which is read back until it is there.
  internal static async Task<string> EvalAsync(AvaloniaCefBrowser view,string expression,int seconds) {
   string key="__mica"+Guid.NewGuid().ToString("N");
   view.ExecuteJavaScript("(async()=>{let r;try{r=String(await ("+expression+"));}catch(e){r='error: '+e;}self['"+key+"']=r;})()","",1);
   for(int i=0;i<seconds*10;i++) {
    await Task.Delay(100);
    string value;
    try {value=await view.EvaluateJavaScript<string>("return self['"+key+"']===undefined?null:self['"+key+"']",(string)null,"about:blank",1,TimeSpan.FromSeconds(5));}
    catch(Exception) {continue;}
    if(value!=null){view.ExecuteJavaScript("delete self['"+key+"']","",1);return value;}
   }
   return null;
  }

  // Pages and extensions asking for a new window (target=_blank, window.open, chrome.tabs.create) get a Mica tab.
  // The side-panel shim sends Claude's tab requests to open.mica.invalid, as on Windows.
  sealed class PopupsToTabs : LifeSpanHandler {
   readonly BrowserWindow window;
   public PopupsToTabs(BrowserWindow w) {window=w;}
   protected override bool OnBeforePopup(CefBrowser browser,CefFrame frame,int popupId,string targetUrl,string targetFrameName,CefWindowOpenDisposition targetDisposition,bool userGesture,CefPopupFeatures popupFeatures,CefWindowInfo windowInfo,ref CefClient client,CefBrowserSettings settings,ref CefDictionaryValue extraInfo,ref bool noJavascriptAccess) {
    string url=targetUrl;bool background=targetDisposition==CefWindowOpenDisposition.NewBackgroundTab;
    Dispatcher.UIThread.Post(delegate {window.OpenFromPage(url,background);});
    return true;
   }
  }
  internal void OpenFromPage(string url,bool background) {
   Trace("popup "+url);
   if((url??"").StartsWith("https://open.mica.invalid/",StringComparison.OrdinalIgnoreCase)) {
    Uri uri;if(!Uri.TryCreate(url,UriKind.Absolute,out uri))return;
    url=Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
    background=uri.Query.Contains("active=0");
   }
   if(!Navigation.IsWeb(url))return;
   string keep=state.ActiveId;var tab=state.AddTab(url);tab.Title=new Uri(url).Host;RenderTabs();
   if(background){Show(tab.Id);Show(keep);}else Show(tab.Id);
   Save();
  }
 }

 // CEF can't add a script to every page the way WebView2 does, so the panel's own page loads Mica's tab shim from a
 // file: chrome.tabs.getCurrent says "no tab" there, and the panel page is kept out of the tabs Claude sees (the same
 // TabsShim as Windows). Unpacked extensions aren't content-verified, so editing their files is allowed.
 static class PanelPagePatch {
  const string FileName="mica-panel.js";
  internal static bool Apply(string folder,AiExtension ai) {
   bool changed=ClaudeSidePanelPatch.Apply(folder,ai.Pages);
   var utf8=new UTF8Encoding(false);
   string pages=new JavaScriptSerializer().Serialize(ai.Pages);
   string script="// Added by Mica: this page is hosted in Mica's AI panel.\nif(self.chrome&&chrome.tabs){self.__micaPanelPages="+pages+";chrome.tabs.getCurrent=function(callback){if(typeof callback==='function'){callback(undefined);return;}return Promise.resolve(undefined);};\n"+ClaudeSidePanelPatch.TabsShim+"}\n";
   string shim=Path.Combine(folder,FileName);
   if(!File.Exists(shim)||File.ReadAllText(shim)!=script){File.WriteAllText(shim,script,utf8);changed=true;}
   string tag="<script src=\"/"+FileName+"\"></script>";
   foreach(var page in ai.Pages) {
    string path=Path.Combine(folder,page);if(!File.Exists(path))continue;
    string html=File.ReadAllText(path,Encoding.UTF8);if(html.Contains(tag))continue;
    int head=html.IndexOf("<head>",StringComparison.OrdinalIgnoreCase);
    html=head>=0?html.Insert(head+6,tag):tag+html;
    File.WriteAllText(path,html,utf8);changed=true;
   }
   return changed;
  }
 }
}
