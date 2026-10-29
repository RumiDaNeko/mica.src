using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Mica {
 partial class BrowserApp {
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint MapVirtualKey(uint code,uint type);
  // Real key presses inside a web page (posted to the page's own window) must reach Mica's shortcuts.
  async Task RunKeysTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"keys");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;
   Navigate("https://example.com/",false);await WaitFor(delegate {NavigationProgress p;return CurrentView()!=null&&CurrentView().CoreWebView2!=null&&navigations.TryGetValue(state.ActiveId,out p)&&!p.IsLoading;},25000);
   window.Activate();CurrentView().Focus();await Task.Delay(400);
   var page=KeyboardWindow();log.Add("page window "+page+" window active "+window.IsActive);
   keyLog=new List<string>();
   foreach(var vk in new uint[]{0x7A,0x7B,0x76}) { // F11, F12, F7
    uint scan=MapVirtualKey(vk,0);
    PostMessage(page,0x0100,new IntPtr(vk),new IntPtr(1|(scan<<16)));await Task.Delay(60);
    PostMessage(page,0x0101,new IntPtr(vk),new IntPtr(unchecked((int)(1|(scan<<16)|0xC0000000))));await Task.Delay(500);
   }
   log.Add("keys that reached Mica: "+string.Join(", ",keyLog));
   RegressionCheck("F11 pressed in a page reaches Mica",delegate {Require(keyLog.Contains("F11"),string.Join(",",keyLog));});
   RegressionCheck("F12 pressed in a page reaches Mica",delegate {Require(keyLog.Contains("F12"),string.Join(",",keyLog));});
   RegressionCheck("F7 pressed in a page reaches Mica",delegate {Require(keyLog.Contains("F7"),string.Join(",",keyLog));});
   keyLog=null;
   // A stale "page is fullscreen" flag (tab closed mid-video) must not swallow F11.
   FullscreenBounds.KeepOffscreen=true;pageFullscreen=true;
   HandleKey(System.Windows.Input.Key.F11,System.Windows.Input.ModifierKeys.None);await Task.Delay(500);
   RegressionCheck("F11 goes fullscreen even after a page left fullscreen behind",delegate {Require(fullscreenApplied&&userFullscreen&&!pageFullscreen,"applied "+fullscreenApplied+" user "+userFullscreen+" page "+pageFullscreen);});
   HandleKey(System.Windows.Input.Key.F11,System.Windows.Input.ModifierKeys.None);await Task.Delay(500);
   RegressionCheck("F11 again leaves fullscreen",delegate {Require(!fullscreenApplied,"still fullscreen");});
   // F7: the first press asks; "Never" is kept and F7 then does nothing.
   HandleKey(System.Windows.Input.Key.F7,System.Windows.Input.ModifierKeys.None);await Task.Delay(400);
   var ask=siteBubble;RegressionCheck("the first F7 asks before turning on caret browsing",delegate {Require(ask!=null&&ask.IsOpen,"no question");});
   if(ask!=null){var turnOn=VaultElements<System.Windows.Controls.Button>(ask.Child).First(b=>(b.Tag as string)=="on");turnOn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));}
   await Task.Delay(800);string caret=await CurrentView().CoreWebView2.ExecuteScriptAsync("typeof window.__micaCaret+'/'+!!document.querySelector('div[aria-hidden=true][style*=\"2147483647\"]')");
   RegressionCheck("Turn on shows Mica's caret on the page and is remembered",delegate {Require(caret=="\"function/true\""&&CaretChoice()=="on"&&caretOn,caret+" choice "+CaretChoice());});
   HandleKey(System.Windows.Input.Key.F7,System.Windows.Input.ModifierKeys.None);await Task.Delay(400);
   RegressionCheck("after that F7 toggles quietly",delegate {Require(!caretOn&&(siteBubble==null||!siteBubble.IsOpen),"asked again or still on");});
   SaveCaretChoice("never");HandleKey(System.Windows.Input.Key.F7,System.Windows.Input.ModifierKeys.None);await Task.Delay(400);
   RegressionCheck("after Never, F7 does nothing",delegate {Require(!caretOn&&(siteBubble==null||!siteBubble.IsOpen),"reacted");});
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
