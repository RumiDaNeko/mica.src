using System;
using System.IO;

namespace Mica {
 // scrubnote (Extensions\scrubnote): record a page's animations, scrub to the frame that feels off, comment on an
 // element and copy exact timings. Its extension only injects one file when its button is clicked, and the engine
 // can't deliver toolbar clicks or extension shortcuts, so Mica runs that file in the page itself (main world,
 // past the page's CSP, as the extension does). Nothing is installed: it costs nothing until used and isn't
 // listed in Extensions. Running it again closes it.
 partial class BrowserApp {
  static string scrubnoteScript;
  async void ToggleScrubnote() {
   CloseMenus();
   var view=CurrentView();
   if(view==null||view.CoreWebView2==null||!(Navigation.IsWeb(view.CoreWebView2.Source)||Navigation.IsLocalFile(view.CoreWebView2.Source))){Status("scrubnote works on web pages.");return;}
   if(scrubnoteScript==null)try {scrubnoteScript=File.ReadAllText(Path.Combine(BuiltInFolder("scrubnote"),"scrubnote.js"));}catch(Exception e){Status("scrubnote is missing: "+e.Message);return;}
   try {
    var core=view.CoreWebView2;
    await core.ExecuteScriptAsync(scrubnoteScript+"\n;0");
    string on=await core.ExecuteScriptAsync("!!(document.getElementById('scrubnote-host')||{}).isConnected");
    Status(on=="true"?"scrubnote is on. Alt+Shift+S closes it.":"scrubnote closed.");
   }catch(Exception e){Status("scrubnote couldn't run on this page: "+e.Message);}
  }
 }
}
