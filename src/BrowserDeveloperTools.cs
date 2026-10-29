using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
namespace Mica {
 partial class BrowserApp {
  async void ShowDeveloperTools() {
   var view=ToolPage();if(view==null)return;var core=view.CoreWebView2;StackPanel body,actions;var dialog=ToolWindow("devtools","Developer tools",880,740,out body,out actions);if(dialog==null)return;
   body.Children.Add(DialogText(core.Source,11,"Muted"));var navigation=new WrapPanel();body.Children.Add(navigation);
   var output=ToolField(body,"Console","");output.IsReadOnly=true;output.AcceptsReturn=true;output.Height=290;output.FontFamily=new FontFamily("Consolas");output.FontSize=12;output.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;output.HorizontalScrollBarVisibility=ScrollBarVisibility.Auto;
   var expression=ToolField(body,"JavaScript expression · runs in this page","");var note=DialogText("Console and network capture are active only while this window is open.",11,"Muted");body.Children.Add(note);
   var console=new Queue<string>();var network=new Queue<string>();string mode="Console";bool live=true;
   Action<Queue<string>,string> append=(queue,text)=>{queue.Enqueue(text.Length>4000?text.Substring(0,4000)+"…":text);while(queue.Count>120)queue.Dequeue();if(live&&((mode=="Console"&&queue==console)||(mode=="Network"&&queue==network))){output.Text=string.Join("\n",queue);output.ScrollToEnd();}};
   var consoleEvents=core.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled");var exceptionEvents=core.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown");var networkEvents=core.GetDevToolsProtocolEventReceiver("Network.responseReceived");
   EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> onConsole=delegate(object sender,CoreWebView2DevToolsProtocolEventReceivedEventArgs e){if(live)append(console,e.ParameterObjectAsJson);};
   EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> onNetwork=delegate(object sender,CoreWebView2DevToolsProtocolEventReceivedEventArgs e){if(!live)return;try{var data=VaultCrypto.Json.Deserialize<Dictionary<string,object>>(e.ParameterObjectAsJson);var response=(Dictionary<string,object>)data["response"];append(network,response["status"]+"   "+response["mimeType"]+"   "+response["url"]);}catch(Exception){append(network,"Response received (details unavailable)");}};
   consoleEvents.DevToolsProtocolEventReceived+=onConsole;exceptionEvents.DevToolsProtocolEventReceived+=onConsole;networkEvents.DevToolsProtocolEventReceived+=onNetwork;
   ToolAction(navigation,"Console","\uE756",delegate {mode="Console";output.Text=string.Join("\n",console);});
   ToolAction(navigation,"DOM","\uE943",async delegate {mode="DOM";try{string document=await core.CallDevToolsProtocolMethodAsync("DOM.getDocument","{}");var parsed=VaultCrypto.Json.Deserialize<Dictionary<string,object>>(document);var root=(Dictionary<string,object>)parsed["root"];string result=await core.CallDevToolsProtocolMethodAsync("DOM.getOuterHTML",VaultCrypto.Json.Serialize(new {nodeId=root["nodeId"]}));if(live&&mode=="DOM"){var html=VaultCrypto.Json.Deserialize<Dictionary<string,object>>(result);output.Text=Convert.ToString(html["outerHTML"]);}}catch(Exception){if(live)note.Text="DOM could not be read.";}});
   ToolAction(navigation,"Network","\uE774",delegate {mode="Network";output.Text=string.Join("\n",network);});
   ToolAction(actions,"Run","\uE768",async delegate {if(string.IsNullOrWhiteSpace(expression.Text))return;string code=expression.Text;try {string result=await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",VaultCrypto.Json.Serialize(new {expression=code,returnByValue=true,awaitPromise=false,timeout=3000}));if(live){mode="Console";append(console,"> "+code);append(console,result);}}catch(Exception){if(live)note.Text="Evaluation failed or timed out.";}});
   ToolAction(actions,"Clear","\uE74D",delegate {console.Clear();network.Clear();output.Clear();});
   ToolAction(actions,"Full engine tools","\uE8A7",delegate {core.OpenDevToolsWindow();});
   dialog.Closed+=async delegate {live=false;console.Clear();network.Clear();output.Clear();expression.Clear();try{consoleEvents.DevToolsProtocolEventReceived-=onConsole;exceptionEvents.DevToolsProtocolEventReceived-=onConsole;networkEvents.DevToolsProtocolEventReceived-=onNetwork;await core.CallDevToolsProtocolMethodAsync("Network.disable","{}");await core.CallDevToolsProtocolMethodAsync("Runtime.disable","{}");}catch(Exception){}};
   dialog.Show();try{await core.CallDevToolsProtocolMethodAsync("Runtime.enable","{}");if(live)await core.CallDevToolsProtocolMethodAsync("Network.enable","{}");}catch(Exception){if(live)note.Text="Capture unavailable; try the full engine tools.";}
  }
 }
}
