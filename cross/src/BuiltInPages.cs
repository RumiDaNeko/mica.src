using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Xilium.CefGlue;

namespace Mica {
 // The built-in games, served the way the Windows build serves them (BrowserGames.cs): from
 // https://tetris.mica.invalid/ and https://games.mica.invalid/<game>, hosts that never resolve, so they work
 // offline. CEF hands those requests to this factory instead of the network. The new tab page lives on
 // https://home.mica.invalid/ the same way.
 static class BuiltInPages {
  internal const string HomeHost="home.mica.invalid",Home="https://"+HomeHost+"/";
  internal static Func<bool> Dark=delegate {return false;};
  // The new tab page's most visited sites, as JSON; kept current by the window, read here on the engine's thread.
  internal static volatile string HomeSites="[]";
  const string Policy="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:";
  static readonly Dictionary<string,byte[]> pages=new Dictionary<string,byte[]>();

  // Handed to CefRuntimeLoader.Initialize, which registers them once the engine is up.
  internal static Xilium.CefGlue.Common.Shared.CustomScheme[] Schemes() {
   var factory=new Factory();
   return new[]{Navigation.TetrisHost,Navigation.GamesHost,HomeHost}.Select(host=>new Xilium.CefGlue.Common.Shared.CustomScheme {SchemeName="https",DomainName=host,SchemeHandlerFactory=factory}).ToArray();
  }
  internal static bool IsBuiltIn(string address) {
   Uri uri;return Uri.TryCreate(address,UriKind.Absolute,out uri)&&(uri.Host==Navigation.TetrisHost||uri.Host==Navigation.GamesHost||uri.Host==HomeHost);
  }
  // The page inside Mica for an address on the games hosts, or null (404).
  internal static string Resource(string method,string address) {
   Uri uri;if(method!="GET"||!Uri.TryCreate(address,UriKind.Absolute,out uri))return null;
   if(uri.Host==Navigation.TetrisHost)return uri.AbsolutePath=="/"?"Tetris.html":null;
   if(uri.Host==HomeHost)return uri.AbsolutePath=="/"?"Home.html":null;
   if(uri.Host!=Navigation.GamesHost)return null;
   foreach(var game in Navigation.Games)if(uri.AbsolutePath.Trim('/')==game[2])return game[3];
   return null;
  }
  internal static byte[] Page(string resource,bool dark) {
   byte[] page;
   lock(pages) {
    string key=resource+(dark?"#dark":"");
    if(pages.TryGetValue(key,out page))return page;
    using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Mica."+resource))using(var copy=new MemoryStream()){stream.CopyTo(copy);page=copy.ToArray();}
    // Like the Windows build, the page arrives already dark when Mica is dark.
    if(dark)page=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(page).Replace("<html lang=\"en\">","<html lang=\"en\" class=\"dark\">"));
    pages[key]=page;return page;
   }
  }
  // The new tab page with this moment's sites filled in (not cached: it changes as you browse).
  static byte[] HomePage(bool dark) {
   string html=Encoding.UTF8.GetString(Page("Home.html",dark));
   return Encoding.UTF8.GetBytes(html.Replace("/*SITES*/[]",HomeSites.Replace("</","<\\/")).Replace("/*MAC*/false",OperatingSystem.IsMacOS()?"true":"false"));
  }
  // The search box on the new tab page sends what you typed to /go?q=; it goes where the address bar would take it.
  internal static string GoTarget(string address) {
   Uri uri;if(!Uri.TryCreate(address,UriKind.Absolute,out uri)||uri.Host!=HomeHost||uri.AbsolutePath!="/go")return null;
   string q="";
   foreach(var part in uri.Query.TrimStart('?').Split('&'))if(part.StartsWith("q="))q=Uri.UnescapeDataString(part.Substring(2).Replace('+',' '));
   string url;
   try {url=Navigation.Resolve(q);} catch(ArgumentException) {return Home;}
   // Only web pages and Mica's own pages: a page never sends you to a local file.
   if(url=="mica://home"||!(Navigation.IsWeb(url)||BuiltInPages.IsBuiltIn(Navigation.EngineAddress(url))))return Home;
   return Navigation.EngineAddress(url);
  }
  // Flips an open game's theme in place, as ThemeGames does on Windows.
  internal static string ThemeScript(bool dark) {return "document.documentElement.classList.toggle('dark',"+(dark?"true":"false")+");dispatchEvent(new Event('micatheme'))";}

  sealed class Factory : CefSchemeHandlerFactory {
   protected override CefResourceHandler Create(CefBrowser browser,CefFrame frame,string schemeName,CefRequest request) {
    string go=request.Method=="GET"?GoTarget(request.Url):null;
    if(go!=null)return new Handler(null,go);
    string resource=Resource(request.Method,request.Url);
    return new Handler(resource==null?null:resource=="Home.html"?HomePage(Dark()):Page(resource,Dark()),null);
   }
  }
  sealed class Handler : CefResourceHandler {
   readonly byte[] body;readonly bool found;readonly string redirect;int offset;
   public Handler(byte[] page,string redirectTo) {body=page??new byte[0];found=page!=null;redirect=redirectTo;}
   protected override bool Open(CefRequest request,out bool handleRequest,CefCallback callback) {handleRequest=true;return true;}
   protected override void GetResponseHeaders(CefResponse response,out long responseLength,out string redirectUrl) {
    redirectUrl=redirect;responseLength=body.Length;
    if(redirect!=null){response.Status=302;response.StatusText="Found";response.SetHeaderByName("Location",redirect,true);return;}
    response.Status=found?200:404;response.StatusText=found?"OK":"Not Found";
    response.MimeType="text/html";response.Charset="utf-8";
    if(found) {response.SetHeaderByName("Cache-Control","no-store",true);response.SetHeaderByName("Content-Security-Policy",Policy,true);}
   }
   protected override bool Skip(long bytesToSkip,out long bytesSkipped,CefResourceSkipCallback callback) {
    int skip=(int)Math.Min(bytesToSkip,body.Length-offset);offset+=skip;bytesSkipped=skip;return skip>0;
   }
   protected override bool Read(Stream response,int bytesToRead,out int bytesRead,CefResourceReadCallback callback) {
    int count=Math.Min(bytesToRead,body.Length-offset);
    if(count<=0){bytesRead=0;return false;}
    response.Write(body,offset,count);offset+=count;bytesRead=count;return true;
   }
   protected override void Cancel() {}
  }
 }
}
