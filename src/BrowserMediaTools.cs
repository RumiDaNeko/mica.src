using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
namespace Mica {
 partial class BrowserApp {
  [StructLayout(LayoutKind.Explicit,Size=40)] struct CastKeyInput {
   [FieldOffset(0)] public uint Type;
   [FieldOffset(8)] public ushort Key;
   [FieldOffset(12)] public uint Flags;
  }
  [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,CastKeyInput[] input,int size);
  static bool OpenWindowsCast() {
   var keys=new[]{new CastKeyInput {Type=1,Key=0x5B},new CastKeyInput {Type=1,Key=0x4B},new CastKeyInput {Type=1,Key=0x4B,Flags=2},new CastKeyInput {Type=1,Key=0x5B,Flags=2}};
   bool sent=SendInput(4,keys,Marshal.SizeOf(typeof(CastKeyInput)))==4;
   if(!sent)SendInput(2,new[]{keys[2],keys[3]},Marshal.SizeOf(typeof(CastKeyInput)));return sent;
  }
  static string AmbientScript() {using(var reader=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Mica.Ambient.js")))return reader.ReadToEnd();}
  bool ambientMigrationChecked,ambientChanging;
  async Task ApplyAmbient(WebView2 view) {
   if(LiveCore(view)==null)return;
   try {await view.ExecuteScriptAsync("window.__micaAmbient && window.__micaAmbient.stop()");
    if(!ambientMigrationChecked&&!privateMode&&(!smoke||navLog!=null)){ambientMigrationChecked=true;if(state.AmbientYoutube)await SetAmbientExtension(true);}
   }catch(Exception e){Status(ExtensionError(e));}
  }
  async void ToggleAmbient() {
   if(ambientChanging)return;ambientChanging=true;
   try{await SetAmbientExtension(!state.AmbientYoutube);}catch(Exception e){Status(ExtensionError(e));}finally{ambientChanging=false;}
  }
  sealed class CastSink {public string name {get;set;}public string id {get;set;}public override string ToString(){return name;}}
  sealed class CastSinks {public List<CastSink> sinks {get;set;}}
  // Like Edge: casting sends the media that's playing to a device; mirroring the whole tab is the fallback.
  const string PickMedia="(()=>{const all=[...document.querySelectorAll('video,audio')].filter(m=>m.currentSrc||m.srcObject);return all.find(m=>!m.paused)||all.find(m=>m.tagName==='VIDEO'&&m.readyState>0)||all[0]||null;})()";
  WebView2 CastSource() {
   var active=CurrentView();
   foreach(var tab in new[]{Active}.Concat(state.Tabs.Where(t=>t!=Active))) {
    WebView2 view;if(tab==null||!views.TryGetValue(tab.Id,out view))continue;var core=LiveCore(view);
    if(core!=null&&core.IsDocumentPlayingAudio)return view;
   }
   return active!=null&&LiveCore(active)!=null?active:null;
  }
  async void ShowCast() {
   var view=CastSource();if(view==null){Status("Open a page with a video or music to cast.");return;}var core=view.CoreWebView2;
   string media="null";try {media=await core.ExecuteScriptAsync("(()=>{const m="+PickMedia+";return m?{kind:m.tagName.toLowerCase(),playing:!m.paused,remote:!!m.remote&&!m.disableRemotePlayback}:null})()");}catch(Exception){}
   bool hasMedia=media!="null",canRemote=media.Contains("\"remote\":true");string kind=media.Contains("\"kind\":\"audio\"")?"audio":"video";
   StackPanel body,actions;var dialog=ToolWindow("cast",hasMedia?"Cast media":"Cast",380,470,out body,out actions);if(dialog==null)return;
   var what=DialogText(hasMedia?"Casting the "+kind+" on":"No video or music found on",11,"Muted");body.Children.Add(what);
   var title=DialogText(string.IsNullOrWhiteSpace(core.DocumentTitle)?core.Source:core.DocumentTitle,14,"Ink");title.FontWeight=FontWeights.SemiBold;title.TextTrimming=TextTrimming.CharacterEllipsis;title.TextWrapping=TextWrapping.NoWrap;title.Margin=new Thickness(0,2,0,0);body.Children.Add(title);
   var note=DialogText("Checking casting support…",12,"Muted");note.Margin=new Thickness(0,14,0,10);body.Children.Add(note);
   var sinks=new ListBox {MaxHeight=120,Visibility=Visibility.Collapsed,Background=System.Windows.Media.Brushes.Transparent,BorderThickness=new Thickness(0)};sinks.SetResourceReference(Control.ForegroundProperty,"Ink");body.Children.Add(sinks);bool live=true;string casting=null;
   var receiver=core.GetDevToolsProtocolEventReceiver("Cast.sinksUpdated");EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> updated=delegate(object sender,CoreWebView2DevToolsProtocolEventReceivedEventArgs e){if(!live)return;try{var result=VaultCrypto.Json.Deserialize<CastSinks>(e.ParameterObjectAsJson);sinks.ItemsSource=result.sinks;sinks.Visibility=result.sinks.Count>0?Visibility.Visible:Visibility.Collapsed;if(result.sinks.Count>0&&sinks.SelectedIndex<0)sinks.SelectedIndex=0;note.Text=result.sinks.Count==0?"No devices found. Check that your TV or speaker is on the same network.":"Choose a device.";}catch(Exception){note.Text="Device discovery failed.";}};receiver.DevToolsProtocolEventReceived+=updated;
   Func<string,Task> mirror=async delegate(string sink) {await core.CallDevToolsProtocolMethodAsync("Cast.startTabMirroring",VaultCrypto.Json.Serialize(new {sinkName=sink}));casting=sink;if(live)note.Text="Mirroring this tab to "+sink+".";};
   var start=ToolAction(actions,hasMedia?"Cast "+kind:"Mirror tab","\uE88A",async delegate {
    var sink=sinks.SelectedItem as CastSink;if(sink==null){note.Text="Choose a device first.";return;}
    try {
     if(hasMedia&&canRemote) {
      // The page's own media goes to the device (Remote Playback), with the device already chosen.
      await core.CallDevToolsProtocolMethodAsync("Cast.setSinkToUse",VaultCrypto.Json.Serialize(new {sinkName=sink.name}));
      string result=await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",VaultCrypto.Json.Serialize(new {expression="(()=>{const m="+PickMedia+";if(!m||!m.remote)return Promise.resolve('none');return m.remote.prompt().then(()=>'ok',e=>'failed: '+e.name)})()",awaitPromise=true,userGesture=true,returnByValue=true}));
      if(result.Contains("\"ok\"")){casting=sink.name;if(live)note.Text="Casting the "+kind+" to "+sink.name+". Controls stay on this page.";return;}
     }
     await mirror(sink.name);
    }catch(Exception){if(live)note.Text="Couldn't cast to "+sink.name+". Try Mirror this tab or Windows Cast.";}
   });start.IsEnabled=false;
   if(hasMedia)ToolAction(actions,"Mirror this tab","\uE7F4",async delegate {var sink=sinks.SelectedItem as CastSink;if(sink==null){note.Text="Choose a device first.";return;}try {await mirror(sink.name);}catch(Exception){if(live)note.Text="Couldn't mirror to "+sink.name+".";}});
   ToolAction(actions,"Stop","\uE71A",async delegate {if(casting==null)return;try{await core.CallDevToolsProtocolMethodAsync("Cast.stopCasting",VaultCrypto.Json.Serialize(new {sinkName=casting}));casting=null;if(live)note.Text="Casting stopped.";}catch(Exception){if(live)note.Text="Could not stop casting. Use your device's controls.";}});
   ToolAction(body,"Open Windows Cast · Win + K","\uE88A",delegate {if(!OpenWindowsCast())note.Text="Press Win + K to open Windows Cast.";});
   body.Children.Add(DialogText("Windows Cast shares your whole screen with a Miracast display.",11,"Muted"));
   dialog.Closed+=async delegate {live=false;try{receiver.DevToolsProtocolEventReceived-=updated;await core.CallDevToolsProtocolMethodAsync("Cast.disable","{}");}catch(Exception){}};
   DockTool(dialog);dialog.Show();try{await core.CallDevToolsProtocolMethodAsync("Cast.enable","{}");if(live){start.IsEnabled=true;note.Text="Looking for devices on your network…";}}catch(Exception){if(live)note.Text="This engine can't cast. Use Windows Cast below for a wireless display.";}
  }
 }
}
