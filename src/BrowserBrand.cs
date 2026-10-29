using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // The engine is Edge's WebView2, and by default it tells every site so: "Edg/156" in the user agent and
 // "Microsoft Edge" in the client hints (navigator.userAgentData and the sec-ch-ua headers), which sites
 // turn into "you're on Edge" prompts. Mica calls itself Micarium instead: "Micarium/0.12.5" in the user agent
 // and a Micarium brand beside Chromium's. The network takes its user agent from Settings; page scripts and
 // their requests take the brands from a DevTools override.
 partial class BrowserApp {
  static readonly Regex EdgeToken=new Regex(@"\s+Edg[A]?/[\d.]+",RegexOptions.Compiled);
  internal static string PlainUserAgent(string agent) {return EdgeToken.Replace(agent??"","");}
  internal static string MicaUserAgent(string agent) {string plain=PlainUserAgent(agent);return plain.Contains(" Micarium/")?plain:plain+" Micarium/"+AppVersion;}
  // Client hints: the same brands with Micarium in place of Edge (full: the full-version list).
  internal static string PlainBrands(string brandsJson,bool full=false) {
   var json=new JavaScriptSerializer();
   var brands=json.Deserialize<List<Dictionary<string,object>>>(brandsJson)??new List<Dictionary<string,object>>();
   var kept=brands.Where(b=>!((b["brand"] as string)??"").Contains("Edge")&&(b["brand"] as string)!="Micarium").ToList();
   string[] parts=AppVersion.Split('.');
   kept.Add(new Dictionary<string,object>{{"brand","Micarium"},{"version",full?AppVersion:parts[0]+"."+parts[1]}});
   return json.Serialize(kept);
  }
  // The engine's own error page carries Edge's name and logo ("This page has been blocked by Microsoft Edge"
  // in every frame an ad blocker stops). Off: a failed frame stays empty, and a failed page gets Mica's error screen.
  async Task ApplyMicaAgent(CoreWebView2 core) {
   KeepPagesOffDevTools(core);
   try {
    string agent=MicaUserAgent(core.Settings.UserAgent);if(agent!=core.Settings.UserAgent)core.Settings.UserAgent=agent;
    core.Settings.IsBuiltInErrorPageEnabled=false;
    var metadata=await EngineMetadata(core);if(metadata==null)return;
    await core.CallDevToolsProtocolMethodAsync("Emulation.setUserAgentOverride",new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"userAgent",agent},{"userAgentMetadata",metadata}}));
   }catch(Exception e){if(smoke)Status("Engine brand: "+e.Message);}
  }
  // Read once from the engine itself, so platform, version and architecture stay true. Client hints only exist
  // on secure pages, so a hidden view opens a blank https page Mica serves itself; the answer is kept per engine.
  static Task<Dictionary<string,object>> engineMetadata;
  Task<Dictionary<string,object>> EngineMetadata(CoreWebView2 core) {
   if(engineMetadata==null||engineMetadata.IsFaulted||(engineMetadata.IsCompleted&&engineMetadata.Result==null))engineMetadata=ReadEngineMetadata(core);
   return engineMetadata;
  }
  string EngineMetadataPath {get {return Path.Combine(profile,"engine-agent.json");}}
  async Task<Dictionary<string,object>> ReadEngineMetadata(CoreWebView2 core) {
   var json=new JavaScriptSerializer();string version=core.Environment.BrowserVersionString;
   try {
    if(!privateMode&&File.Exists(EngineMetadataPath)) {
     var saved=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(EngineMetadataPath));
     if(saved!=null&&saved.ContainsKey("version")&&(string)saved["version"]==version&&saved["metadata"] is Dictionary<string,object>)return Branded((Dictionary<string,object>)saved["metadata"]);
    }
   }catch(Exception){}
   string folder=Path.Combine(Path.GetTempPath(),"Mica-agent");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"blank.html"),"<!doctype html><title></title>");
   var view=new WebView2 {Width=1,Height=1,Visibility=System.Windows.Visibility.Hidden,IsHitTestVisible=false,Focusable=false};Find<System.Windows.Controls.Grid>("Root").Children.Add(view);
   try {
    var options=ViewOptions(core.Environment);
    await view.EnsureCoreWebView2Async(core.Environment,options);var probe=view.CoreWebView2;RegisterOwnTarget(probe);
    probe.SetVirtualHostNameToFolderMapping("agent.mica.invalid",folder,CoreWebView2HostResourceAccessKind.Deny);
    var loaded=new TaskCompletionSource<bool>();probe.NavigationCompleted+=delegate(object sender,CoreWebView2NavigationCompletedEventArgs e) {loaded.TrySetResult(e.IsSuccess);};
    probe.Navigate("https://agent.mica.invalid/blank.html");if(!await loaded.Task)return null;
    string raw=await probe.CallDevToolsProtocolMethodAsync("Runtime.evaluate","{\"awaitPromise\":true,\"returnByValue\":true,\"expression\":\"navigator.userAgentData?navigator.userAgentData.getHighEntropyValues(['platformVersion','architecture','bitness','model','fullVersionList','wow64']).then(v=>JSON.stringify(v)):null\"}");
    var reply=json.Deserialize<Dictionary<string,object>>(raw);var result=reply["result"] as Dictionary<string,object>;object value;
    if(result==null||!result.TryGetValue("value",out value)||!(value is string))return null;
    var values=json.Deserialize<Dictionary<string,object>>((string)value);
    if(!privateMode)try {File.WriteAllText(EngineMetadataPath,json.Serialize(new Dictionary<string,object>{{"version",version},{"metadata",values}}));}catch(Exception){}
    return Branded(values);
   }finally {Find<System.Windows.Controls.Grid>("Root").Children.Remove(view);try {view.Dispose();}catch(Exception){}}
  }
  // The engine's own answer, with Micarium in place of Edge.
  static Dictionary<string,object> Branded(Dictionary<string,object> values) {
   var json=new JavaScriptSerializer();
   Func<string,string> plain=delegate(string key) {object list;return values.TryGetValue(key,out list)?PlainBrands(json.Serialize(list),key=="fullVersionList"):"[]";};
   Func<string,object> text=delegate(string key) {return values.ContainsKey(key)?values[key]:"";};
   Func<string,bool> flag=delegate(string key) {return values.ContainsKey(key)&&values[key] is bool&&(bool)values[key];};
   return new Dictionary<string,object>{
    {"brands",json.DeserializeObject(plain("brands"))},{"fullVersionList",json.DeserializeObject(plain("fullVersionList"))},
    {"platform",values.ContainsKey("platform")?values["platform"]:"Windows"},{"platformVersion",text("platformVersion")},
    {"architecture",text("architecture")},{"model",text("model")},{"mobile",flag("mobile")},{"bitness",text("bitness")},{"wow64",flag("wow64")}
   };
  }
 }
}
