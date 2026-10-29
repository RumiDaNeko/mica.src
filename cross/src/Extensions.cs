using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Mica {
 // Extensions in the cross-platform shell. Chromium (CEF 134, Chrome style) runs them itself; Mica only decides which
 // folders it loads at start (--load-extension): the built-in blockers shipped next to Mica, and extensions installed
 // from the Chrome Web Store through ExtensionPackage (verified, unpacked, the AI side-panel shim added), kept in the
 // profile. CEF has no API to add an extension while running, so a new one loads on the next start.
 static class CrossExtensions {
  static readonly string[] BuiltIn={"mica-blocker","ublock-lite"};
  static string StoreRoot(string profile) {return Path.Combine(profile,"StoreExtensions");}
  static string RecordPath(string profile) {return Path.Combine(profile,"extensions.json");}

  // Installed store extensions: id -> folder.
  internal static Dictionary<string,string> Installed(string profile) {
   try {
    var map=File.Exists(RecordPath(profile))?new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(RecordPath(profile))):null;
    if(map!=null)return map.Where(p=>Directory.Exists(p.Value)).ToDictionary(p=>p.Key,p=>p.Value);
   } catch(ArgumentException) {} catch(IOException) {}
   return new Dictionary<string,string>();
  }
  internal static bool IsInstalled(string profile,string id) {return Installed(profile).ContainsKey(id);}

  // Every folder Chromium should load this run. Chromium splits the list on commas, so a folder with one is skipped.
  internal static List<string> Folders(string profile) {
   var folders=new List<string>();
   foreach(var name in BuiltIn) {string f=Path.Combine(AppContext.BaseDirectory,"Extensions",name);if(File.Exists(Path.Combine(f,"manifest.json")))folders.Add(f);}
   // An AI extension installed by an older Mica gets the current panel and tab shims before Chromium loads it.
   foreach(var pair in Installed(profile)) {
    var ai=BrowserApp.AiExtensionById(pair.Key);
    if(ai!=null)try {PanelPagePatch.Apply(pair.Value,ai);} catch(IOException) {} catch(UnauthorizedAccessException) {}
    folders.Add(pair.Value);
   }
   return folders.Where(f=>!f.Contains(",")).ToList();
  }

  // Downloads, verifies and unpacks an extension from the Chrome Web Store (an id or its store address).
  internal static async Task<string> Install(string profile,string input,string chromeVersion) {
   string id=ExtensionPackage.ParseStoreId(input);
   using(var prepared=await ExtensionPackage.PrepareAsync(id,StoreRoot(profile),chromeVersion)) {
    var map=Installed(profile);string old;
    if(map.TryGetValue(id,out old)&&old!=prepared.Folder)try {ExtensionPackage.DeleteStage(old);} catch(IOException) {}
    // The AI panel hosts these extensions' side panel page (AiPanel.cs).
    var ai=BrowserApp.AiExtensionById(id);if(ai!=null)PanelPagePatch.Apply(prepared.Folder,ai);
    map[id]=prepared.Folder;
    File.WriteAllText(RecordPath(profile),new JavaScriptSerializer().Serialize(map));
    prepared.Commit();
    return prepared.Folder;
   }
  }
  internal static void Remove(string profile,string id) {
   var map=Installed(profile);string folder;if(!map.TryGetValue(id,out folder))return;
   map.Remove(id);File.WriteAllText(RecordPath(profile),new JavaScriptSerializer().Serialize(map));
   try {ExtensionPackage.DeleteStage(folder);} catch(IOException) {}
  }
 }
}
