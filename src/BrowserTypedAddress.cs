using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Mica {
 // Addresses you type that aren't pages: javascript: runs on the page you're on, once you've said yes, since
 // scammers ask people to paste one to take over an account; mailto:, ftp:// and app links go to the app
 // Windows has for them. A word with a colon and no app behind it ("todo:milk") is searched instead.
 partial class BrowserApp {
  async void RunTypedScript(string url) {
   var tab=Active;var core=LiveCore(CurrentView());
   if(tab==null||core==null||!(Navigation.IsWeb(core.Source)||Navigation.IsLocalFile(core.Source))){Status("javascript: runs on a web page. Open one first.");return;}
   string site=Navigation.IsWeb(core.Source)?new Uri(core.Source).Host:"this file";
   if(!ConfirmMica(window,"Run JavaScript on "+site+"?","It runs with your sign-in on "+site+" and can read or change anything there. Scammers ask people to paste code like this to take over their accounts. Run it only if you wrote it or trust where it came from.","Run it"))return;
   string code;try {code=Uri.UnescapeDataString(url.Substring("javascript:".Length));}catch(UriFormatException){code=url.Substring("javascript:".Length);}
   try {await core.ExecuteScriptAsync(code);Status("Ran the JavaScript on "+site+".");}catch(Exception){Status("The JavaScript couldn't run on this page.");}
  }
  void OpenAppLink(string url,string typed) {
   string scheme=ExternalApps.Scheme(url);bool registered=false;
   if(scheme!=null)try {using(var key=Registry.ClassesRoot.OpenSubKey(scheme))registered=key!=null&&key.GetValue("URL Protocol")!=null;}catch(Exception){}
   if(!registered){Navigate("https://www.google.com/search?q="+Uri.EscapeDataString(typed.Trim()),false);return;}
   try {Process.Start(new ProcessStartInfo(ExternalApps.Escape(url)) {UseShellExecute=true});Status("Opened "+ExternalApps.AppName(scheme)+".");}catch(Exception){Status("Windows couldn't open "+scheme+": links.");}
  }
 }
}
