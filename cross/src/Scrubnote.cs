using System;
using System.IO;
using System.Threading.Tasks;

namespace Mica {
 // scrubnote, as on Windows (BrowserScrubnote.cs): record a page's animations, scrub to the frame that feels off,
 // comment on an element and copy exact timings. Mica runs Extensions/scrubnote/scrubnote.js in the page's main
 // frame (its own world, past the page's CSP) instead of installing the extension, so it costs nothing until used
 // and isn't listed in Extensions. Running it again closes it.
 sealed partial class BrowserWindow {
  static string scrubnoteScript;
  static string ScrubnoteKey {get {return Mac?"⌥⇧S":"Alt+Shift+S";}}
  internal async Task ToggleScrubnote() {
   var tab=Active;
   if(tab==null||tab.View==null||!tab.View.IsBrowserInitialized||!Navigation.IsWeb(tab.Entry.Url)){Status("scrubnote works on web pages.");return;}
   if(scrubnoteScript==null)try {scrubnoteScript=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Extensions","scrubnote","scrubnote.js"));}catch(Exception e) when (e is IOException||e is UnauthorizedAccessException){Status("scrubnote is missing: "+e.Message);return;}
   tab.View.ExecuteJavaScript(scrubnoteScript,"",1);
   string on=await EvalAsync(tab.View,"!!(document.getElementById('scrubnote-host')||{}).isConnected",3);
   Status(on=="true"?"scrubnote is on. "+ScrubnoteKey+" closes it.":on=="false"?"scrubnote closed.":"scrubnote couldn't run on this page.");
  }
 }
}
