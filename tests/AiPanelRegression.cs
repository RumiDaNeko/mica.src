using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace Mica {
 partial class BrowserApp {
  // The AI side panel against a stand-in chat page: off by default, grows the window outward, types and
  // sends selected text, rests when closed, and the selection bar shows over chosen text.
  async Task RunAiPanelTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"aipanel");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;window.Width=1100;window.Height=760;
   const string chat="<!doctype html><title>Stand-in chat</title><body style='margin:0;font:14px Segoe UI'><p>Ask me anything</p><div id='prompt-textarea' contenteditable='true' style='width:320px;height:80px;border:1px solid #888'></div>"+
    "<script>window.sent=[];document.getElementById('prompt-textarea').addEventListener('keydown',e=>{if(e.key==='Enter'&&!e.shiftKey){e.preventDefault();sent.push(e.target.innerText);e.target.innerText='';}});</script>";
   if(Environment.GetCommandLineArgs().Contains("--real")){await RunAiRealCheck(log,dir);return;}
   try {
    RegressionCheck("the AI panel is off until you turn it on",delegate {Require(!state.AiPanel&&!AiEnabled&&aiButton!=null&&aiButton.Visibility==Visibility.Collapsed,"enabled "+state.AiPanel);});
    ToggleAiPanel();await Task.Delay(200);
    RegressionCheck("while off, the shortcut opens its settings and makes no web view",delegate {Require(!aiOpen&&aiView==null&&aiSettingsWindow!=null,"open "+aiOpen+" settings "+(aiSettingsWindow!=null));});
    if(aiSettingsWindow!=null)aiSettingsWindow.Close();
    RegressionCheck("growing outward keeps the page's width and slides left near the edge",delegate {
     var room=AiGrowPlan(new Rect(100,50,1000,700),new Rect(0,0,1920,1040),400);var edge=AiGrowPlan(new Rect(800,50,1000,700),new Rect(0,0,1920,1040),400);var tight=AiGrowPlan(new Rect(0,0,1300,700),new Rect(0,0,1366,728),400);
     Require(room!=null&&room.Value.Left==100&&room.Value.Width==1400,"room "+room);Require(edge!=null&&edge.Value.Left==520&&edge.Value.Width==1400,"edge "+edge);Require(tight==null,"tight "+tight);
    });
    SetAiEnabled(true);await Task.Delay(200);
    RegressionCheck("turned on, the chat button is in the toolbar",delegate {Require(AiEnabled&&aiButton.Visibility==Visibility.Visible,"visible "+aiButton.Visibility);});
    state.AiProvider="custom";state.AiCustomUrl="https://ai.test/chat";
    aiViewCreatedForTest=delegate(CoreWebView2 core) {
     var engine=core.Environment;core.AddWebResourceRequestedFilter("https://ai.test/*",CoreWebView2WebResourceContext.All);
     core.WebResourceRequested+=delegate(object sender,CoreWebView2WebResourceRequestedEventArgs e) {e.Response=engine.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(chat)),200,"OK","Content-Type: text/html");};
    };
    window.UpdateLayout();double widthBefore=window.ActualWidth,pageBefore=Find<Grid>("PageSurface").ColumnDefinitions[0].ActualWidth;
    // Every frame of the slide: the panel part-way out, the window wider by the same amount, the page unchanged.
    var frames=new System.Collections.Generic.List<double[]>();EventHandler sample=delegate {frames.Add(new[]{Find<Grid>("AppLayout").ColumnDefinitions[2].ActualWidth,window.ActualWidth,Find<Grid>("PageSurface").ColumnDefinitions[0].ActualWidth});};
    System.Windows.Media.CompositionTarget.Rendering+=sample;SetAiOpen(true);await Task.Delay(500);System.Windows.Media.CompositionTarget.Rendering-=sample;
    var partway=frames.Where(f=>f[0]>5&&f[0]<395).ToList();
    log.Add("slide: "+frames.Count+" frames, "+partway.Count+" part-way: "+string.Join(" ",partway.Take(8).Select(f=>Math.Round(f[0]))));
    if(SystemParameters.ClientAreaAnimation)RegressionCheck("the panel slides out with the window instead of jumping",delegate {Require(partway.Count>=3,partway.Count+" part-way frames");foreach(var f in partway)Require(Math.Abs(f[1]-widthBefore-f[0])<3&&Math.Abs(f[2]-pageBefore)<3,"panel "+f[0]+" window "+f[1]+" page "+f[2]);});
    await WaitFor(()=>aiView!=null&&aiView.CoreWebView2!=null&&aiView.IsVisible,20000).ContinueWith(t=>{});await Task.Delay(500);window.UpdateLayout();
    double pageOpen=Find<Grid>("PageSurface").ColumnDefinitions[0].ActualWidth;GrabWindow(window,"mica-ai-panel.png");
    log.Add("window "+widthBefore+" -> "+window.ActualWidth+", page "+pageBefore+" -> "+pageOpen+", grown "+aiGrown+", source "+(aiView!=null&&aiView.CoreWebView2!=null?aiView.CoreWebView2.Source:"none"));
    RegressionCheck("opening grows the window outward and the page keeps its width",delegate {Require(Math.Abs(window.ActualWidth-widthBefore-400)<2&&Math.Abs(pageOpen-pageBefore)<2,"window "+window.ActualWidth+" page "+pageOpen);});
    RegressionCheck("the window's buttons sit at the top right, in the panel's header",delegate {var close=Find<Button>("CloseButton");Require(aiCaptionSlot.Children.Contains(close)&&close.TransformToAncestor(window).Transform(new Point(0,0)).X>window.ActualWidth-60,"at "+close.TransformToAncestor(window).Transform(new Point(0,0)).X);});
    RegressionCheck("the panel has no chat picker of its own",delegate {Require(aiPills==null&&((Grid)aiDock.Child).RowDefinitions.Count==2,"rows "+((Grid)aiDock.Child).RowDefinitions.Count);});
    ShowAiSettings();await Task.Delay(300);
    if(aiSettingsWindow!=null)GrabWindow(aiSettingsWindow,"mica-ai-settings.png");
    RegressionCheck("its settings pick the chat, with the chosen one filled in",delegate {Require(aiSettingsWindow!=null&&aiPills!=null&&(string)aiSettingsSwitch.Content=="On","settings "+(aiSettingsWindow!=null));var on=aiPills.Children.OfType<Button>().First(b=>(string)b.Tag=="ai-provider:custom");Require((string)on.Content=="ai.test"&&on.Background!=Brushes.Transparent,on.Content+"");Require(aiPills.Children.Count==AiProviders.Length+1,"pills "+aiPills.Children.Count);});
    if(aiSettingsWindow!=null)aiSettingsWindow.Close();await Task.Delay(100);
    RegressionCheck("the panel opens the chosen site in your profile",delegate {Require(aiView!=null&&aiView.CoreWebView2.Source=="https://ai.test/chat"&&aiTitle.Text=="ai.test",aiView==null?"none":aiView.CoreWebView2.Source+" "+aiTitle.Text);});
    AskAi("explain","The quick brown fox jumps over the lazy dog");
    await WaitFor(()=>lastAiResult!="waiting",20000).ContinueWith(t=>{});await Task.Delay(400);
    string sent=await aiView.CoreWebView2.ExecuteScriptAsync("JSON.stringify(sent)");log.Add("explain: "+lastAiResult+" "+sent);
    RegressionCheck("Explain types the selection into the chat and sends it",delegate {Require(lastAiResult=="sent"&&sent.Contains("Explain this")&&sent.Contains("quick brown fox"),lastAiResult+" "+sent);});
    AskAi("ask","Second bit of text");
    await WaitFor(()=>lastAiResult!="waiting",20000).ContinueWith(t=>{});await Task.Delay(300);
    string typed=await aiView.CoreWebView2.ExecuteScriptAsync("JSON.stringify({sent:sent.length,box:document.getElementById('prompt-textarea').innerText})");log.Add("ask: "+lastAiResult+" "+typed);
    RegressionCheck("Ask… types it and waits for your question",delegate {Require(lastAiResult=="typed"&&typed.Contains("Second bit")&&typed.Contains("\\\"sent\\\":1"),lastAiResult+" "+typed);});
    SetAiOpen(false);await Task.Delay(400);window.UpdateLayout();
    RegressionCheck("closing gives the window back its width",delegate {Require(Math.Abs(window.ActualWidth-widthBefore)<2&&aiDock.Visibility==Visibility.Collapsed,"window "+window.ActualWidth);});
    RegressionCheck("closing puts the window's buttons back in the toolbar",delegate {var close=Find<Button>("CloseButton");Require(close.Parent==Find<Button>("ReaderButton").Parent&&aiCaptionSlot.Children.Count==0&&Math.Abs(Find<Grid>("Toolbar").ColumnDefinitions[2].Width.Value-250)<0.5,"parent "+close.Parent);});
    AiRestTick();await Task.Delay(1500);bool frozen=aiView!=null&&aiView.CoreWebView2.IsSuspended;
    aiClosedAt=DateTime.UtcNow.AddMinutes(-6);AiRestTick();await Task.Delay(200);
    RegressionCheck("a closed panel freezes, then rests completely",delegate {Require(frozen&&aiView==null,"frozen "+frozen+", view "+(aiView!=null));});
    // The selection bar over a page.
    Navigate("https://pages.test/article",false);await WaitFor(()=>CurrentView()!=null&&CurrentView().CoreWebView2!=null,20000);await WaitForPage().ContinueWith(t=>{});
    var page=CurrentView().CoreWebView2;var env=page.Environment;page.AddWebResourceRequestedFilter("https://pages.test/*",CoreWebView2WebResourceContext.All);
    page.WebResourceRequested+=delegate(object sender,CoreWebView2WebResourceRequestedEventArgs e) {e.Response=env.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes("<!doctype html><title>Article</title><p id=a style='font:20px serif;margin:120px 40px'>WebView2 uses Microsoft Edge as its engine.</p>")),200,"OK","Content-Type: text/html");};
    page.Navigate("https://pages.test/article");await WaitFor(()=>page.DocumentTitle=="Article",15000).ContinueWith(t=>{});await Task.Delay(500);
    await page.ExecuteScriptAsync("getSelection().selectAllChildren(document.getElementById('a'));dispatchEvent(new MouseEvent('mouseup',{button:0,bubbles:true}))");
    await WaitFor(()=>aiBar!=null&&aiBar.IsOpen,5000).ContinueWith(t=>{});
    string watched;log.Add("selection: note "+aiSelectionNote+", script "+(aiScripts.TryGetValue(page,out watched)?"\""+watched+"\"":"none")+", messages "+page.Settings.IsWebMessageEnabled+", selected "+await page.ExecuteScriptAsync("String(getSelection())"));
    RegressionCheck("selecting text shows the Ask AI bar",delegate {Require(aiBar!=null&&aiBar.IsOpen&&aiBarText!=null&&aiBarText.Contains("Microsoft Edge"),"bar "+(aiBar!=null)+" text "+aiBarText);});
    await page.ExecuteScriptAsync("getSelection().removeAllRanges();dispatchEvent(new MouseEvent('mousedown',{button:0,bubbles:true}))");await WaitFor(()=>aiBar==null,3000).ContinueWith(t=>{});
    RegressionCheck("clicking away hides it",delegate {Require(aiBar==null,"still open");});
    var menu=CreateContextMenu();AddAiMenuItems(menu,Active,"WebView2 uses Microsoft Edge");var item=menu.Items.OfType<MenuItem>().FirstOrDefault(m=>(m.Tag as string)=="ai-menu");
    RegressionCheck("the right-click menu offers Ask AI with its four actions",delegate {Require(item!=null&&item.Items.Count==4,item==null?"none":item.Items.Count.ToString());});
    var banking=new PageEntry {Url="https://bank.example/",Jar="banking"};var menu2=CreateContextMenu();AddAiMenuItems(menu2,banking,"account 1234");
    RegressionCheck("never on tabs in the Banking jar",delegate {Require(!AiAllowedOn(banking)&&menu2.Items.Count==0,"items "+menu2.Items.Count);});
    SetAiEnabled(false);await Task.Delay(200);
    RegressionCheck("turning it off removes the button and the panel",delegate {Require(!AiEnabled&&aiButton.Visibility==Visibility.Collapsed&&!aiOpen&&aiView==null,"button "+aiButton.Visibility);});
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
  // Not part of the regular run (it needs the internet): the real chat sites in the panel, logged step by step.
  async Task RunAiRealCheck(System.Collections.Generic.List<string> log,string dir) {
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;window.Width=1100;window.Height=760;
   try {
    aiViewCreatedForTest=delegate(CoreWebView2 core) {
     core.NavigationStarting+=delegate(object s,CoreWebView2NavigationStartingEventArgs e){log.Add("start "+e.Uri);};
     core.NavigationCompleted+=delegate(object s,CoreWebView2NavigationCompletedEventArgs e){log.Add("done ok="+e.IsSuccess+" status="+e.WebErrorStatus+" http="+e.HttpStatusCode+" at "+core.Source);};
     core.ProcessFailed+=delegate(object s,CoreWebView2ProcessFailedEventArgs e){log.Add("process failed "+e.ProcessFailedKind);};
     core.WebResourceResponseReceived+=delegate(object s,CoreWebView2WebResourceResponseReceivedEventArgs e){if(e.Response.StatusCode>=400)log.Add("  http "+e.Response.StatusCode+" "+e.Request.Uri.Substring(0,Math.Min(120,e.Request.Uri.Length)));};
     core.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown").DevToolsProtocolEventReceived+=delegate(object s,CoreWebView2DevToolsProtocolEventReceivedEventArgs e){log.Add("  js error "+e.ParameterObjectAsJson.Substring(0,Math.Min(300,e.ParameterObjectAsJson.Length)));};
     core.CallDevToolsProtocolMethodAsync("Runtime.enable","{}");
     var requests=new System.Collections.Generic.Dictionary<string,string>();
     core.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent").DevToolsProtocolEventReceived+=delegate(object s,CoreWebView2DevToolsProtocolEventReceivedEventArgs e){var j=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(e.ParameterObjectAsJson);var r=j["request"] as System.Collections.Generic.Dictionary<string,object>;requests[(string)j["requestId"]]=r==null?"":(string)r["url"];};
     core.GetDevToolsProtocolEventReceiver("Network.loadingFailed").DevToolsProtocolEventReceived+=delegate(object s,CoreWebView2DevToolsProtocolEventReceivedEventArgs e){var j=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(e.ParameterObjectAsJson);string url;requests.TryGetValue((string)j["requestId"],out url);object reason;j.TryGetValue("blockedReason",out reason);log.Add("  failed "+j["errorText"]+" "+reason+" "+(url??"").Substring(0,Math.Min(110,(url??"").Length)));};
     core.CallDevToolsProtocolMethodAsync("Network.enable","{}");
    };
    SetAiEnabled(true);
    foreach(var id in new[]{"chatgpt","claude","gemini"}) {
     ChooseAiProvider(id,null);SetAiOpen(true);var started=DateTime.UtcNow;
     await WaitFor(()=>aiView!=null&&aiView.CoreWebView2!=null,20000).ContinueWith(x=>{});await Task.Delay(15000);window.UpdateLayout();
     var core=aiView==null?null:aiView.CoreWebView2;
     log.Add(id+": view "+(aiView!=null)+" visible "+(aiView!=null&&aiView.IsVisible)+" size "+(aiView==null?"":aiView.ActualWidth+"x"+aiView.ActualHeight)+" source "+(core==null?"":core.Source)+" title "+(core==null?"":core.DocumentTitle)+" note \""+aiEmpty.Text+"\"");
     const string probe="JSON.stringify({ready:document.readyState,vis:document.visibilityState,ua:navigator.userAgent.slice(-70),text:(document.body&&document.body.innerText||'').slice(0,100)})";
     if(core!=null){log.Add(id+" page: "+await core.ExecuteScriptAsync(probe));await Task.Delay(15000);log.Add(id+" at 30s: "+await core.ExecuteScriptAsync(probe));}
     GrabWindow(window,"mica-ai-real-"+id+".png");SetAiOpen(false);await Task.Delay(300);
    }
    // The same sites in an ordinary tab, to tell the panel apart from the engine.
    foreach(var address in new[]{"https://claude.ai/new","https://gemini.google.com/app"}) {
     Navigate(address,true);await WaitFor(()=>CurrentView()!=null&&CurrentView().CoreWebView2!=null,20000).ContinueWith(x=>{});await Task.Delay(15000);
     var tab=CurrentView().CoreWebView2;log.Add("tab "+address+": "+tab.Source+" | "+await tab.ExecuteScriptAsync("JSON.stringify({ready:document.readyState,vis:document.visibilityState,ua:navigator.userAgent.slice(-70),text:(document.body&&document.body.innerText||'').slice(0,120)})"));
    }
   }catch(Exception e){log.Add("ERROR "+e);}
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
