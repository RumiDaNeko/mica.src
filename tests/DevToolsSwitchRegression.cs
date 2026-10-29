using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Mica {
 partial class BrowserApp {
  // Turning "Dock DevTools" on or off takes effect at once: the engine restarts under the new port, the
  // tab comes back, and F12 docks (or, turned off, the port is gone).
  async Task RunDevToolsSwitchTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"devtools-switch");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;
   try {
    Navigate("https://example.com/",false);await WaitForPage();
    RegressionCheck("docking starts off, with no debugging port",delegate {Require(!state.DockedDevTools&&devToolsPort==0,"port "+devToolsPort);});
    string tab=state.ActiveId;
    ToggleDockedDevTools();
    await WaitFor(()=>!switchingEngine&&devToolsPort!=0&&CurrentView()!=null&&CurrentView().CoreWebView2!=null,30000).ContinueWith(t=>{});await WaitForPage();
    log.Add("after switching on: port "+devToolsPort+", page "+(CurrentView()!=null&&CurrentView().CoreWebView2!=null?CurrentView().CoreWebView2.Source:"none"));
    RegressionCheck("switching on opens a port without restarting Mica",delegate {Require(state.DockedDevTools&&devToolsPort!=0,"port "+devToolsPort);});
    RegressionCheck("the tab comes back in the new engine",delegate {Require(state.ActiveId==tab&&CurrentView()!=null&&CurrentView().CoreWebView2.Source.StartsWith("https://example.com/"),"active "+state.ActiveId);});
    ToggleDevTools();
    await WaitFor(()=>devToolsView!=null&&devToolsView.CoreWebView2!=null&&devToolsView.CoreWebView2.Source.Contains("/devtools/"),15000).ContinueWith(t=>{});
    RegressionCheck("F12 then docks DevTools",delegate {Require(devToolsOpen&&devToolsView!=null&&devToolsView.CoreWebView2.Source.Contains("127.0.0.1:"+devToolsPort),devToolsView==null?"no view":devToolsView.CoreWebView2.Source);});
    ToggleDockedDevTools();
    await WaitFor(()=>!switchingEngine&&devToolsPort==0&&CurrentView()!=null&&CurrentView().CoreWebView2!=null,30000).ContinueWith(t=>{});await WaitForPage();
    RegressionCheck("switching off closes the port and the dock",delegate {Require(!state.DockedDevTools&&devToolsPort==0&&!devToolsOpen,"port "+devToolsPort+" open "+devToolsOpen);});
    RegressionCheck("the tab is back again",delegate {Require(CurrentView()!=null&&CurrentView().CoreWebView2.Source.StartsWith("https://example.com/"),"no page");});
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
