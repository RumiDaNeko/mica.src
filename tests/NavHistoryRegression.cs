using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace Mica {
 partial class BrowserApp {
  // Right-clicking Back lists the pages behind it, newest first, and picking one goes there.
  async Task RunNavHistoryTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"navhistory");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;
   try {
    Navigate("https://pages.test/one",false);await WaitFor(()=>CurrentView()!=null&&CurrentView().CoreWebView2!=null,20000);
    // The tab's first load may start before these pages are served here; let it finish (or fail) first.
    await WaitForPage().ContinueWith(t=>{});await Task.Delay(300);
    var core=CurrentView().CoreWebView2;var environment=core.Environment;
    core.AddWebResourceRequestedFilter("https://pages.test/*",CoreWebView2WebResourceContext.All);
    core.WebResourceRequested+=delegate(object sender,CoreWebView2WebResourceRequestedEventArgs e) {
     string name=new Uri(e.Request.Uri).AbsolutePath.Trim('/');
     e.Response=environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes("<!doctype html><title>Page "+name+"</title><p>"+name)),200,"OK","Content-Type: text/html");
    };
    // Wait for each page's own navigation to finish: the title alone can still match the previous load.
    foreach(var name in new[]{"one","two","three","four"}){
     string url="https://pages.test/"+name;
     // A late finish of the tab's first load can cancel this one; try again if the page doesn't show up.
     for(int attempt=0;attempt<3&&!(core.Source==url&&core.DocumentTitle=="Page "+name);attempt++) {
      var done=new TaskCompletionSource<bool>();
      EventHandler<CoreWebView2NavigationCompletedEventArgs> landed=delegate {if(core.Source==url)done.TrySetResult(true);};
      core.NavigationCompleted+=landed;core.Navigate(url);
      await Task.WhenAny(done.Task,Task.Delay(5000));core.NavigationCompleted-=landed;
      await WaitFor(()=>core.DocumentTitle=="Page "+name,3000).ContinueWith(t=>{});
     }
     await WaitFor(()=>core.DocumentTitle=="Page "+name,5000);
     // The tab's first load ran before these pages were served and can leave an untitled entry; start the history clean.
     if(name=="one")await core.CallDevToolsProtocolMethodAsync("Page.resetNavigationHistory","{}");
    }
    // Then let the history settle on Page four as its last, current entry.
    string history="";
    for(int i=0;i<50;i++) {
     history=await core.CallDevToolsProtocolMethodAsync("Page.getNavigationHistory","{}");
     var urls=System.Text.RegularExpressions.Regex.Matches(history,"\"url\":\"([^\"]*)\"");
     var index=System.Text.RegularExpressions.Regex.Match(history,"\"currentIndex\":(\\d+)");
     if(urls.Count>0&&urls[urls.Count-1].Groups[1].Value=="https://pages.test/four"&&index.Success&&int.Parse(index.Groups[1].Value)==urls.Count-1)break;
     await Task.Delay(100);
    }
    log.Add("history: "+history);
    var back=Find<Button>("BackButton");ShowNavHistory(back,true);
    await WaitFor(()=>navHistoryPopup!=null&&navHistoryPopup.IsOpen,5000).ContinueWith(t=>{});
    var rows=navHistoryPopup==null?new Button[0]:PopupButtons(navHistoryPopup).Where(b=>(b.Tag as string??"").StartsWith("nav-history:")).ToArray();
    string titles=string.Join(" | ",rows.Select(r=>System.Windows.Automation.AutomationProperties.GetName(r)));log.Add("back list: "+titles);
    RegressionCheck("right-clicking Back lists earlier pages, newest first",delegate {Require(rows.Length==3&&titles=="Page three | Page two | Page one",titles);});
    if(rows.Length==3) {
     rows[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
     await WaitFor(()=>core.DocumentTitle=="Page one",10000).ContinueWith(t=>{});
     RegressionCheck("picking an entry jumps straight to that page",delegate {Require(core.DocumentTitle=="Page one"&&!navHistoryPopup.IsOpen,core.DocumentTitle);});
     await Task.Delay(300);ShowNavHistory(Find<Button>("ForwardButton"),false);
     await WaitFor(()=>navHistoryPopup.IsOpen,5000).ContinueWith(t=>{});
     var ahead=PopupButtons(navHistoryPopup).Where(b=>(b.Tag as string??"").StartsWith("nav-history:")).Select(r=>System.Windows.Automation.AutomationProperties.GetName(r)).ToArray();
     RegressionCheck("right-clicking Forward lists the pages ahead",delegate {Require(string.Join(" | ",ahead)=="Page two | Page three | Page four",string.Join(" | ",ahead));});
     navHistoryPopup.IsOpen=false;
    }
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
