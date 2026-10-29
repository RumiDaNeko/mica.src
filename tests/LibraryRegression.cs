using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Mica {
 partial class BrowserApp {
  async Task RunLibraryTest() {
   try {
    using(var server=new RegressionServer()) {
     Navigate(server.Url+"ok",false);await WaitFor(delegate {return CurrentView()!=null&&CurrentView().CoreWebView2.DocumentTitle=="Regression OK";},10000);await Task.Delay(200);var webTab=Active;var webView=CurrentView();
     ShowMenu();await Task.Delay(240);CapturePopup(menuPopup,"mica-menu-compact-v06.png");
     RegressionCheck("browser menu avoids duplicate sidebar actions and includes Downloads",delegate {var names=((StackPanel)((ScrollViewer)((Border)menuPopup.Child).Child).Content).Children.OfType<Button>().Select(b=>System.Windows.Automation.AutomationProperties.GetName(b)).ToList();Require(!names.Contains("New tab")&&!names.Contains("Search anything")&&!names.Contains("Switch space"),"Menu duplicates sidebar controls");Require(names.Contains("Downloads")&&names.Contains("Passwords & sync")&&names.Contains("New private window"),"Required menu commands missing");});
     OpenLibraryPage("mica://history");await Task.Delay(250);
     RegressionCheck("History opens a native tab while preserving website",delegate {Require(Active.Url=="mica://history"&&libraryPage.IsVisible&&!IsSpotlight,"History is not a standalone native page");Require(CurrentView()==null&&views.ContainsKey(webTab.Id)&&views[webTab.Id]==webView,"Opening History destroyed website tab");Require(Find<Grid>("WebHost").Visibility==Visibility.Collapsed,"Website covers History");});
     string historyId=Active.Id;int tabCount=state.Tabs.Count;OpenLibraryPage("mica://history");Require(state.Tabs.Count==tabCount&&Active.Id==historyId,"History command creates duplicate tabs");
     state.History.Clear();state.History.Add(new PageEntry {Title="Design reference",Url=server.Url+"ok?design",LastUsed=DateTime.Now.Date.AddHours(9).ToUniversalTime()});state.History.Add(new PageEntry {Title="A useful guide",Url=server.Url+"ok?guide",LastUsed=DateTime.Now.Date.AddDays(-1).AddHours(15).ToUniversalTime()});state.History.Add(new PageEntry {Title="Something worth keeping",Url=server.Url+"ok?archive",LastUsed=DateTime.Now.Date.AddDays(-10).ToUniversalTime()});Save();RenderLibrary();await Task.Delay(200);Capture("mica-history.png");
     RegressionCheck("history is grouped by local date",delegate {Require(Library<StackPanel>("LibraryRows").Children.OfType<TextBlock>().Count()==3,"Date headings missing");Require(Library<TextBlock>("LibraryCount").Text=="3 pages","History count wrong");});
     Library<TextBox>("LibrarySearch").Text="DESIGN";RegressionCheck("history search filters titles without case sensitivity",delegate {Require(Library<TextBlock>("LibraryCount").Text=="1 page","Title filter failed");});
     Library<TextBox>("LibrarySearch").Clear();Library<Button>("FilterSecond").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Require(Library<TextBlock>("LibraryCount").Text=="1 page","Today filter failed");Library<Button>("FilterAll").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));libraryPage.UpdateLayout();
     var remove=FindAction(Library<StackPanel>("LibraryRows"),"history-remove:"+server.Url+"ok?guide");Require(remove!=null,"History row action missing");remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
     RegressionCheck("removing one history entry persists",delegate {Require(state.History.Count==2&&StateStore.Load(statePath).History.Count==2,"History removal not saved");});
     state.Dark=true;ApplyTheme();await Task.Delay(180);Capture("mica-history-dark.png");state.Dark=false;ApplyTheme();
     Library<Button>("LibraryClear").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Library<Button>("CancelClearHistory").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Require(state.History.Count==2,"Cancelling clear erased history");Library<Button>("LibraryClear").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Library<Button>("ConfirmClearHistory").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
     RegressionCheck("clear history exposes a useful empty state",delegate {Require(state.History.Count==0&&Library<StackPanel>("LibraryEmpty").IsVisible,"Clear or empty state failed");Require(!Library<Button>("LibraryClear").IsEnabled,"Empty history still offers clear");});
     OpenLibraryPage("mica://downloads");await Task.Delay(200);
     RegressionCheck("Downloads reuses the library tab without a web engine",delegate {Require(Active.Id==historyId&&Active.Title=="Downloads"&&CurrentView()==null,"Downloads creates wrong tab or web engine");Require(Library<StackPanel>("LibraryEmpty").IsVisible,"Fresh downloads has no empty state");});
     string file=Path.Combine(profile,"library-fixture.txt");File.WriteAllText(file,"A harmless local UI fixture.");downloads.Add(new DownloadEntry {Id="library-complete",Url=server.Url+"file",FilePath=file,FileName="Design notes.txt",Status="Completed",Received=new FileInfo(file).Length,Total=new FileInfo(file).Length,StartedUtc=DateTime.UtcNow});downloads.Add(new DownloadEntry {Id="library-interrupted",Url=server.Url+"retry",FilePath=Path.Combine(profile,"interrupted.zip"),FileName="Reference images.zip",Status="Interrupted",Detail="Connection interrupted. Try downloading this file again.",Received=786432,Total=3145728,StartedUtc=DateTime.UtcNow.AddDays(-1)});RenderLibrary();await Task.Delay(200);Capture("mica-downloads.png");
     Library<Button>("FilterThird").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Require(Library<TextBlock>("LibraryCount").Text=="1 file","Completed filter failed");Library<Button>("FilterAll").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Library<TextBox>("LibrarySearch").Text="images";Require(Library<TextBlock>("LibraryCount").Text=="1 file","Filename search failed");Library<TextBox>("LibrarySearch").Clear();
     state.Dark=true;ApplyTheme();await Task.Delay(180);Capture("mica-downloads-dark.png");state.Dark=false;ApplyTheme();window.Width=960;window.Height=680;await Task.Delay(240);Capture("mica-downloads-compact.png");
     Library<Button>("LibraryClear").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
     RegressionCheck("clearing download records preserves downloaded files",delegate {Require(downloads.Count==0&&File.Exists(file),"Clear deleted files or failed to remove records");});
     OpenSpotlight(false);await Task.Delay(100);CloseSpotlight();await Task.Delay(220);RegressionCheck("closing Spotlight keeps the library surface visible",delegate {Require(libraryPage.IsVisible&&Find<Grid>("WebHost").Visibility==Visibility.Collapsed,"Spotlight exposed web host over native library");});
     Save();RegressionCheck("library selection survives restart",delegate {var restored=StateStore.Load(statePath);Require(restored.Tabs.Any(t=>t.Id==historyId&&t.Url=="mica://downloads")&&restored.ActiveId==historyId,"Internal library tab not persisted");});
     await Activate(webTab.Id);RegressionCheck("returning to website hides library and restores view",delegate {Require(!libraryPage.IsVisible&&CurrentView()==webView&&Find<Grid>("WebHost").IsVisible,"Website did not restore after Library");});
    }
   }catch(Exception error){regressionFailures++;regressionLog.Add("FAIL library runner: "+error);}
   regressionLog.Add(regressionFailures==0?"PASS":"FAILURES: "+regressionFailures);File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","library-result.txt"),regressionLog);Application.Current.Shutdown(regressionFailures==0?0:1);
  }
 }
}
