using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace Mica {
 // mica://tetris: a game that ships inside Mica, so there is something to do while offline.
 partial class BrowserApp {
  static byte[] tetrisPage;
  static byte[] TetrisPage() {
   if(tetrisPage==null)using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Mica.Tetris.html"))using(var copy=new MemoryStream()){stream.CopyTo(copy);tetrisPage=copy.ToArray();}
   return tetrisPage;
  }
  // The other games, read from inside Mica once each, when first played.
  static readonly System.Collections.Generic.Dictionary<string,byte[]> gamePages=new System.Collections.Generic.Dictionary<string,byte[]>();
  static byte[] GamePage(string resource) {
   lock(gamePages) {
    byte[] page;if(gamePages.TryGetValue(resource,out page))return page;
    using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Mica."+resource))using(var copy=new MemoryStream()){stream.CopyTo(copy);page=copy.ToArray();}
    gamePages[resource]=page;return page;
   }
  }
  // The games take Mica's light or dark theme (not Windows'): the page arrives already dark, and
  // switching the theme while one is open flips it in place (ThemeGames).
  static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[],byte[]> darkPages=new System.Runtime.CompilerServices.ConditionalWeakTable<byte[],byte[]>();
  static byte[] Themed(byte[] page,bool dark) {
   if(!dark)return page;
   return darkPages.GetValue(page,p=>System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(p).Replace("<html lang=\"en\">","<html lang=\"en\" class=\"dark\">")));
  }
  internal void ThemeGames() {
   string script="document.documentElement.classList.toggle('dark',"+(state.Dark?"true":"false")+");dispatchEvent(new Event('micatheme'))";
   foreach(var view in views.Values.ToList()) {
    var core=LiveCore(view);if(core==null)continue;Uri uri;
    try {if(Uri.TryCreate(core.Source,UriKind.Absolute,out uri)&&(uri.Host==Navigation.TetrisHost||uri.Host==Navigation.GamesHost))core.ExecuteScriptAsync(script);}catch(Exception){}
   }
  }
  const string GameHeaders="Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:";
  void ServeBuiltInPages(CoreWebView2 core) {
   core.AddWebResourceRequestedFilter("https://"+Navigation.TetrisHost+"/*",CoreWebView2WebResourceContext.All);
   core.AddWebResourceRequestedFilter("https://"+Navigation.GamesHost+"/*",CoreWebView2WebResourceContext.All);
   core.WebResourceRequested+=delegate(object sender,CoreWebView2WebResourceRequestedEventArgs e) {
    Uri uri;if(!Uri.TryCreate(e.Request.Uri,UriKind.Absolute,out uri)||(uri.Host!=Navigation.TetrisHost&&uri.Host!=Navigation.GamesHost))return;
    string[] game=null;if(uri.Host==Navigation.GamesHost)foreach(var g in Navigation.Games)if(uri.AbsolutePath.Trim('/')==g[2])game=g;
    if(e.Request.Method!="GET"||(uri.Host==Navigation.TetrisHost?uri.AbsolutePath!="/":game==null)){e.Response=environment.CreateWebResourceResponse(null,404,"Not Found","");return;}
    e.Response=environment.CreateWebResourceResponse(new MemoryStream(Themed(game==null?TetrisPage():GamePage(game[3]),state.Dark)),200,"OK",GameHeaders);
   };
  }
  static bool IsConnectionError(CoreWebView2WebErrorStatus status) {
   switch(status) {
    case CoreWebView2WebErrorStatus.Disconnected:case CoreWebView2WebErrorStatus.HostNameNotResolved:case CoreWebView2WebErrorStatus.CannotConnect:
    case CoreWebView2WebErrorStatus.ServerUnreachable:case CoreWebView2WebErrorStatus.Timeout:case CoreWebView2WebErrorStatus.ConnectionReset:return true;
   }
   return false;
  }
  internal static Func<bool> NetworkAvailable=NetworkInterface.GetIsNetworkAvailable;
  void ShowLoadError(CoreWebView2WebErrorStatus status) {
   bool offline=status==CoreWebView2WebErrorStatus.Disconnected||!NetworkAvailable();
   if(offline){
    ShowError("Mica can't reach the internet. Check your Wi-Fi or cable, then try again. Meanwhile, there are games.");
    Find<TextBlock>("ErrorTitle").Text="Oh no, no internet.";
   }else ShowError("This page couldn't load ("+status+"). Check the address and your connection, then try again.");
   if(offline||IsConnectionError(status))Find<Button>("GamesButton").Visibility=Visibility.Visible;
  }
 }
}
