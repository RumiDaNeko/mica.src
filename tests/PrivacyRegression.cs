using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Shell;
namespace Mica {
 partial class BrowserApp {
  async Task RunPrivateExitTest() {
   string artifacts=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts"));
   try {Require(privateMode,"Expected private-only application");using(var server=new RegressionServer()){Navigate(server.Url+"ok",false);await WaitFor(()=>CurrentView()!=null&&CurrentView().CoreWebView2.DocumentTitle=="Regression OK",15000);await CurrentView().ExecuteScriptAsync("document.cookie='privateExit=fixture; path=/';");File.WriteAllText(Path.Combine(artifacts,"private-exit-path.txt"),profile);File.WriteAllText(Path.Combine(artifacts,"private-exit-result.txt"),"PASS private-only window closed; verify profile removal after process exit");window.Close();}}
   catch(Exception e){File.WriteAllText(Path.Combine(artifacts,"private-exit-result.txt"),"FAIL "+e);Application.Current.Shutdown(1);}
  }
  async Task RunPrivacyTest() {
   BrowserApp incognito=null,sibling=null;var log=new System.Collections.Generic.List<string>();int failures=0;
   try {
    using(var server=new RegressionServer()) {
     Navigate(server.Url+"ok",false);await WaitFor(delegate{return CurrentView()!=null&&CurrentView().CoreWebView2.DocumentTitle=="Regression OK";},15000);await Task.Delay(250);
     await CurrentView().ExecuteScriptAsync("document.cookie='micaRegular=normal; path=/';");
     int historyCount=state.History.Count;state.Dark=true;ApplyTheme();Save();
     incognito=Program.OpenWindow(new[]{"--private"},true,profile);await Task.Delay(200);
     Require(incognito.state.Dark,"Private window did not inherit dark theme");
     Require(incognito.state.History.Count==0&&incognito.state.Bookmarks.Count==0,"Theme inheritance copied private library data");
     ToggleTheme();Require(!incognito.state.Dark&&!state.Dark,"Main theme toggle did not update private window");
     incognito.ToggleTheme();Require(incognito.state.Dark&&state.Dark,"Private toggle diverged from main theme");
     Require(!File.Exists(incognito.statePath),"Theme change persisted private session");
     log.Add("PASS private inherits main theme; toggles synchronize without copying library or persisting private session");
     incognito.Navigate(server.Url+"ok",false);
     await WaitFor(delegate{return incognito.CurrentView()!=null&&incognito.CurrentView().CoreWebView2.DocumentTitle=="Regression OK";},15000);await Task.Delay(250);
     Require(incognito.CurrentView().CoreWebView2.Profile.IsInPrivateModeEnabled,"Controller is not InPrivate");
     string cookies=await incognito.CurrentView().ExecuteScriptAsync("document.cookie");Require(!cookies.Contains("micaRegular"),"Private view inherited regular cookie");
     await incognito.CurrentView().ExecuteScriptAsync("document.cookie='micaPrivate=secret; path=/';");
     Require(!(await CurrentView().ExecuteScriptAsync("document.cookie")).Contains("micaPrivate"),"Private cookie leaked to normal view");
     log.Add("PASS actual InPrivate controller isolates normal and private cookies");
     incognito.Save();incognito.SaveDownloads();incognito.OpenLibraryPage("mica://history");
     Require(incognito.state.History.Count==0&&incognito.state.RecentlyClosed.Count==0&&state.History.Count==historyCount,"Private browsing recorded or polluted history");
     Require(!File.Exists(incognito.statePath)&&!File.Exists(incognito.DownloadsPath)&&!Directory.Exists(Path.Combine(incognito.profile,"Favicons")),"Private data persisted to app metadata");
     incognito.ShowVault();incognito.ShowExtensions();Require(incognito.vaultWindow==null&&incognito.extensionsWindow==null,"Private window opened vault or extensions");
     incognito.Navigate("mica://home",false);await Task.Delay(200);incognito.Capture("mica-private.png");
     log.Add("PASS private history, app persistence, favicon disk cache, vault and extensions are disabled");
     string firstPrivate=incognito.profile;incognito.window.Close();incognito=null;
     incognito=Program.OpenWindow(new[]{"--private"},true,profile);await Task.Delay(150);incognito.Navigate(server.Url+"ok",false);
     await WaitFor(delegate{return incognito.CurrentView()!=null&&incognito.CurrentView().CoreWebView2.DocumentTitle=="Regression OK";},15000);
     Require(!(await incognito.CurrentView().ExecuteScriptAsync("document.cookie")).Contains("micaPrivate"),"New private session restored old cookie");
     log.Add("PASS new private session starts without previous private cookies");incognito.window.Close();incognito=null;
     sibling=Program.OpenWindow(new[]{"--new-window"},true,profile);await Task.Delay(200);
     Require(sibling.state!=state&&object.ReferenceEquals(sibling.state.History,state.History)&&object.ReferenceEquals(sibling.downloads,downloads),"Regular windows do not share library data safely");
     sibling.Navigate(server.Url+"ok?sibling",false);await WaitFor(delegate{return sibling.CurrentView()!=null&&sibling.CurrentView().CoreWebView2.DocumentTitle=="Regression OK";},15000);await Task.Delay(250);
     Require((await sibling.CurrentView().ExecuteScriptAsync("document.cookie")).Contains("micaRegular"),"Regular windows do not share browser profile");
     sibling.Save();var stored=StateStore.Load(statePath);Require(stored.Tabs.Any(t=>t.Id==Active.Id)&&stored.Tabs.Any(t=>t.Id==sibling.Active.Id),"Saving sibling discarded original window tabs");
     log.Add("PASS regular windows share profile/library and persist both sets of tabs");sibling.window.Close();sibling=null;
     state.History.Clear();for(int i=0;i<5;i++)state.Visit("https://github.com/","GitHub");for(int i=0;i<3;i++)state.Visit("https://www.youtube.com/","YouTube");state.Visit("https://www.wikipedia.org/","Wikipedia");state.Visit("https://mail.google.com/","Gmail");
     Navigate("mica://home",false);RenderHome();Require(Find<UniformGrid>("EverydaySites").Children.Count==4&&state.TopSites(4)[0].Url=="https://github.com/","Home did not rank real history");window.UpdateLayout();Capture("mica-personal-home.png");
     log.Add("PASS Home renders four real history-ranked sites and their icons");
     state.RecordClosed(new PageEntry {Url="https://github.com/closed",Title="Closed fixture"});
     var tasks=BuildJumpItems().OfType<JumpTask>().ToList();Require(tasks.Any(t=>t.CustomCategory=="Top Sites")&&tasks.Any(t=>t.CustomCategory=="Recently closed")&&tasks.Any(t=>t.Arguments=="--private")&&tasks.Any(t=>t.Arguments=="--new-window"),"Jump List lacks requested categories or tasks");
     Require(tasks.Where(t=>t.CustomCategory!=null).All(t=>t.Arguments.StartsWith("--url \"",StringComparison.Ordinal)),"Jump destination arguments malformed");
     log.Add("PASS Jump List builds Top Sites, Recently closed and new/private window commands");
     state.History.Clear();RenderHome();Require(Find<UniformGrid>("EverydaySites").Children.Count==0&&Find<TextBlock>("EverydayEmpty").IsVisible,"Empty Home displays unrelated fixed sites");
     log.Add("PASS empty history removes all personalized Home cards");
    }
   }catch(Exception e){failures++;log.Add("FAIL "+e);}
   finally {if(incognito!=null)incognito.window.Close();if(sibling!=null)sibling.window.Close();}
   log.Add(failures==0?"PASS":"FAILURES: "+failures);File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","privacy-result.txt"),log);Application.Current.Shutdown(failures==0?0:1);
  }
 }
}
