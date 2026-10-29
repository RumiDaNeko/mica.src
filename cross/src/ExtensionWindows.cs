using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Xilium.CefGlue;

namespace Mica {
 // Extensions open tabs and windows without any Mica view asking (chrome.tabs.create, chrome.windows.create). In
 // Chrome style Chromium gives each one a window of its own, with its own tab strip, outside Mica. As on Windows
 // (BrowserExtensionWindows.cs), each such window is closed as soon as its address is known and the page opens as a
 // Mica tab instead; the AI panel shim's requests (open.mica.invalid) arrive this way too. DevTools and extension
 // pages keep their window.
 sealed class ExtensionWindows : CefClient {
  readonly LifeSpan life=new LifeSpan();readonly Display display=new Display();readonly Requests requests=new Requests();
  protected override CefLifeSpanHandler GetLifeSpanHandler() {return life;}
  protected override CefDisplayHandler GetDisplayHandler() {return display;}
  protected override CefRequestHandler GetRequestHandler() {return requests;}
  static readonly HashSet<int> adopted=new HashSet<int>();

  sealed class LifeSpan : CefLifeSpanHandler {
   protected override bool OnBeforePopup(CefBrowser browser,CefFrame frame,int popupId,string targetUrl,string targetFrameName,CefWindowOpenDisposition targetDisposition,bool userGesture,CefPopupFeatures popupFeatures,CefWindowInfo windowInfo,ref CefClient client,CefBrowserSettings settings,ref CefDictionaryValue extraInfo,ref bool noJavascriptAccess) {
    string url=targetUrl;bool background=targetDisposition==CefWindowOpenDisposition.NewBackgroundTab;
    Dispatcher.UIThread.Post(delegate {if(BrowserWindow.Main!=null)BrowserWindow.Main.OpenFromPage(url,background);});
    return true;
   }
   protected override void OnAfterCreated(CefBrowser browser) {var frame=browser.GetMainFrame();if(frame!=null)Adopt(browser,frame.Url);}
   protected override void OnBeforeClose(CefBrowser browser) {lock(adopted)adopted.Remove(browser.Identifier);}
  }
  // The address is known before the page loads: Mica's own requests (open.mica.invalid) never load at all.
  sealed class Requests : CefRequestHandler {
   protected override bool OnBeforeBrowse(CefBrowser browser,CefFrame frame,CefRequest request,bool userGesture,bool isRedirect) {return frame.IsMain&&Adopt(browser,request.Url);}
   protected override CefResourceRequestHandler GetResourceRequestHandler(CefBrowser browser,CefFrame frame,CefRequest request,bool isNavigation,bool isDownload,string requestInitiator,ref bool disableDefaultHandling) {return null;}
  }
  sealed class Display : CefDisplayHandler {
   protected override void OnAddressChange(CefBrowser browser,CefFrame frame,string url) {if(frame.IsMain)Adopt(browser,url);}
  }
  internal static Action<string> Log;
  // True when the window is Mica's to close (its page then shouldn't load).
  static bool Adopt(CefBrowser browser,string url) {
   url=url??"";if(Log!=null)Log("engine window "+browser.Identifier+" "+url);
   bool web=Navigation.IsWeb(url),blank=url.StartsWith("chrome://newtab",StringComparison.OrdinalIgnoreCase);
   if(!web&&!blank)return false;
   lock(adopted)if(!adopted.Add(browser.Identifier))return true;
   Dispatcher.UIThread.Post(delegate {if(BrowserWindow.Main!=null)BrowserWindow.Main.OpenFromPage(blank?"mica://home":url,false);});
   browser.GetHost().CloseBrowser(true);
   return true;
  }
 }
}
