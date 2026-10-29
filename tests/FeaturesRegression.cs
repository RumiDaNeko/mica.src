using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 partial class BrowserApp {
  // Tab search, the site card and zoom, reading mode, split view and profiles.
  async Task RunFeaturesTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"features");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Width=1400;window.Height=900;window.Left=-6000;window.Top=-6000;
   try {
    // Tab search: tabs from both spaces, the current one first, resting ones marked.
    Navigate("https://example.com/",false);await WaitForPage();string exampleTab=state.ActiveId;
    var work=new PageEntry {Id=Guid.NewGuid().ToString("N"),Url="https://en.wikipedia.org/wiki/Web_browser",Space="Work",Title="Web browser - Wikipedia",LastUsed=DateTime.UtcNow};state.Tabs.Add(work);RenderTabs();
    HandleKey(Key.A,ModifierKeys.Control|ModifierKeys.Shift);await Task.Delay(400);
    var hits=(Find<ListBox>("SearchResults").ItemsSource as System.Collections.IEnumerable).OfType<SearchHit>().ToList();
    log.Add("tab search: "+string.Join(" | ",hits.Select(h=>h.Kind+" "+h.Title+" "+h.TabId))+"; active "+state.ActiveId+" example "+exampleTab+" work "+work.Id+" tabs "+state.Tabs.Count);
    RegressionCheck("Ctrl+Shift+A lists tabs from both spaces, this one first",delegate {Require(IsSpotlight&&tabSearch&&hits.Count>=2&&hits[0].Kind=="THIS TAB"&&hits.Any(h=>h.TabId==work.Id&&h.Kind=="RESTING"),string.Join(",",hits.Select(h=>h.Kind)));});
    Find<TextBox>("SpotlightInput").Text="wikipedia browser";await Task.Delay(200);
    hits=(Find<ListBox>("SearchResults").ItemsSource as System.Collections.IEnumerable).OfType<SearchHit>().ToList();
    RegressionCheck("words match in any order",delegate {Require(hits.Count>0&&hits[0].TabId==work.Id,hits.Count+" hits");});
    CloseSpotlight();await Task.Delay(400);

    // Zoom: the card's buttons, kept per site, shown beside the address.
    ShowSiteCard();await WaitFor(()=>siteCard!=null&&siteCard.IsOpen,3000);await Task.Delay(300);
    var zoomIn=LogicalDescendants<Button>(siteCard.Child).First(b=>(b.Tag as string)=="site-zoom-in");
    zoomIn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(300);
    CaptureElement((FrameworkElement)siteCard.Child,"site-card.png");
    var view=CurrentView();double zoom;
    RegressionCheck("the site card zooms the page and remembers it for the site",delegate {Require(Math.Abs(view.ZoomFactor-1.1)<0.01&&state.SiteZoom.TryGetValue("example.com",out zoom)&&Math.Abs(zoom-1.1)<0.01,"zoom "+view.ZoomFactor);});
    RegressionCheck("the address shows the zoom",delegate {Require(Find<Border>("ZoomBadge").Visibility==Visibility.Visible&&Find<TextBlock>("ZoomText").Text=="110%",Find<TextBlock>("ZoomText").Text);});
    siteCard.IsOpen=false;
    Navigate("https://example.com/?another",false);await WaitForPage();await Task.Delay(300);
    RegressionCheck("another page of the site keeps its zoom",delegate {Require(Math.Abs(CurrentView().ZoomFactor-1.1)<0.01,"zoom "+CurrentView().ZoomFactor);});
    Navigate("https://www.iana.org/help/example-domains",false);await WaitForPage();await Task.Delay(300);
    RegressionCheck("another site opens at 100%",delegate {Require(Math.Abs(CurrentView().ZoomFactor-1)<0.01&&Find<Border>("ZoomBadge").Visibility!=Visibility.Visible,"zoom "+CurrentView().ZoomFactor+" at "+CurrentView().Source+", saved "+string.Join(";",state.SiteZoom.Select(p=>p.Key+"="+p.Value)));});
    Navigate("https://example.com/",false);await WaitForPage();ZoomView(CurrentView(),1);await Task.Delay(300);
    RegressionCheck("100% isn't kept",delegate {Require(!state.SiteZoom.ContainsKey("example.com"),"still kept");});

    // Reading mode on a real article.
    SelectTab(work.Id);await WaitFor(()=>state.ActiveId==work.Id&&CurrentView()!=null&&CurrentView().CoreWebView2!=null&&CurrentView().CoreWebView2.Source.Contains("wikipedia.org"),20000);
    for(int i=0;i<40&&await CurrentView().CoreWebView2.ExecuteScriptAsync("document.readyState")!="\"complete\"";i++)await Task.Delay(250);await Task.Delay(800);
    ToggleReader();await WaitFor(()=>ReaderOpen,15000).ContinueWith(t=>{});
    RegressionCheck("reading mode opens on an article",delegate {Require(ReaderOpen&&CurrentView().Visibility!=Visibility.Visible,"not open");});
    if(ReaderOpen) {
     var document=LogicalDescendants<FlowDocumentScrollViewer>(readerHost).First().Document;
     string text=new System.Windows.Documents.TextRange(document.ContentStart,document.ContentEnd).Text;
     log.Add("reader: "+document.Blocks.Count+" blocks, "+text.Length+" chars; starts: "+text.Substring(0,Math.Min(120,text.Length)).Replace("\r\n"," / "));
     RegressionCheck("it holds the article's title and text, not the page around it",delegate {Require(text.Contains("Web browser")&&document.Blocks.Count>20&&text.Length>3000&&!text.Contains("Jump to content"),document.Blocks.Count+" blocks");});
     readerHost.UpdateLayout();await Task.Delay(500);CaptureElement(readerHost,"reader.png");
     readerHost.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(readerHost),0,Key.Escape) {RoutedEvent=Keyboard.PreviewKeyDownEvent});
     RegressionCheck("Esc goes back to the page",delegate {Require(!ReaderOpen&&CurrentView().Visibility==Visibility.Visible,"still reading");});
    }
    SelectTab(exampleTab);await WaitFor(()=>state.ActiveId==exampleTab&&CurrentView()!=null&&CurrentView().IsVisible,5000);
    // example.com now carries its one paragraph in dozens of languages, which reads as an article; a search page doesn't.
    Navigate("https://www.google.com/",false);await WaitForPage();await Task.Delay(800);
    ToggleReader();await Task.Delay(1500);
    RegressionCheck("a page that isn't an article says so",delegate {Require(!ReaderOpen,"opened on "+(readerTab==work.Id?"wikipedia":"example"));});
    if(ReaderOpen) {
     var wrong=LogicalDescendants<FlowDocumentScrollViewer>(readerHost).First().Document;string got=new System.Windows.Documents.TextRange(wrong.ContentStart,wrong.ContentEnd).Text;
     log.Add("reader opened on "+readerView.CoreWebView2.Source+": "+got.Substring(0,Math.Min(300,got.Length)).Replace("\r\n"," / "));CloseReader();
    }

    // Split view: the two pages side by side, both awake.
    OpenSplit(work.Id);await WaitFor(()=>views.ContainsKey(work.Id)&&views[work.Id].IsVisible,15000).ContinueWith(t=>{});await Task.Delay(800);
    var left=views[exampleTab];WebView2 right;views.TryGetValue(work.Id,out right);
    RegressionCheck("split view shows both pages side by side",delegate {Require(SplitOpen&&right!=null&&left.IsVisible&&right.IsVisible&&Grid.GetColumn(left)==0&&Grid.GetColumn(right)==2&&Math.Abs(left.ActualWidth-right.ActualWidth)<12,"open "+SplitOpen+" visible "+left.IsVisible+"/"+(right!=null&&right.IsVisible)+" columns "+Grid.GetColumn(left)+"/"+(right==null?-1:Grid.GetColumn(right))+" widths "+left.ActualWidth+" / "+(right==null?0:right.ActualWidth));});
    hiddenSince[work.Id]=DateTime.UtcNow.AddMinutes(-10);BalanceMemory(true);await Task.Delay(800);
    RegressionCheck("the other side doesn't freeze",delegate {Require(right!=null&&!right.CoreWebView2.IsSuspended,"suspended");});
    SelectTab(work.Id);await Task.Delay(500);
    RegressionCheck("using the other side keeps the layout",delegate {Require(state.ActiveId==work.Id&&left.IsVisible&&right.IsVisible&&Grid.GetColumn(right)==2,"active "+(state.ActiveId==work.Id)+" visible "+left.IsVisible+"/"+right.IsVisible+" column "+Grid.GetColumn(right));});
    HandleKey(Key.S,ModifierKeys.Control|ModifierKeys.Shift);await Task.Delay(400);
    RegressionCheck("Ctrl+Shift+S ends it",delegate {Require(!SplitOpen&&right.IsVisible&&!left.IsVisible&&Find<Grid>("WebHost").ColumnDefinitions.Count==0,"open "+SplitOpen+" visible "+left.IsVisible+"/"+right.IsVisible+" columns "+Find<Grid>("WebHost").ColumnDefinitions.Count);});
    SelectTab(exampleTab);await Task.Delay(500);

    // Profiles: their own window, colour and cookies; removing one deletes it.
    await CurrentView().CoreWebView2.ExecuteScriptAsync("document.cookie='who=main; max-age=600'");
    var created=CreateProfile("Work test","#3E7A6E");
    RegressionCheck("a new profile gets its own folder and colour",delegate {Require(created!=null&&Directory.Exists(ProfileFolder(created.Id))&&StateStore.Load(Path.Combine(ProfileFolder(created.Id),"session.json")).FrameColor=="#3E7A6E",created==null?"none":ProfileFolder(created.Id));});
    var other=OpenProfile(created.Id);other.window.Left=-6000;other.window.Top=-6000;other.window.ShowInTaskbar=false;
    await WaitFor(()=>other.state.ActiveId!=null,5000).ContinueWith(t=>{});
    other.Navigate("https://example.com/",false);
    await WaitFor(()=>other.CurrentView()!=null&&other.CurrentView().CoreWebView2!=null&&other.CurrentView().CoreWebView2.Source.StartsWith("https://example.com"),20000).ContinueWith(t=>{});await Task.Delay(1500);
    string cookie=other.CurrentView()==null?"no view":await other.CurrentView().CoreWebView2.ExecuteScriptAsync("document.cookie");
    log.Add("profile window: "+other.ProfileId+" title '"+other.window.Title+"', cookie "+cookie);
    RegressionCheck("the profile opens in its own window and sees none of the main profile's cookies",delegate {Require(other.ProfileId==created.Id&&other.window.Title.Contains("Work test")&&cookie=="\"\"","cookie "+cookie);});
    RegressionCheck("it shares the main engine (no second browser)",delegate {Require(other.EngineFolder==EngineFolder,"engine "+other.EngineFolder);});
    bool removed=await RemoveProfile(created.Id);await Task.Delay(500);
    RegressionCheck("removing a profile closes it and deletes its data",delegate {Require(removed&&other.closing&&!Directory.Exists(ProfileFolder(created.Id))&&!LoadProfiles().Any(p=>p.Id==created.Id),"folder "+Directory.Exists(ProfileFolder(created.Id)));});

    // Language: Vietnamese translates what's on screen at once, with a serif that has its letters; back to English restores it.
    if(!Localizer.Vietnamese) {
     Func<string,bool> shows=delegate(string text) {return LogicalDescendants<TextBlock>(window.Content as DependencyObject).Any(t=>t.Text==text);};
     bool englishFirst=shows("PINNED");
     SwitchLanguage("vi");await Task.Delay(400);
     bool translated=shows("ĐÃ GHIM")&&!shows("PINNED");var serif=Application.Current.Resources["SerifFont"] as System.Windows.Media.FontFamily;
     RegressionCheck("switching to Vietnamese translates the open window",delegate {Require(englishFirst&&translated,"english "+englishFirst+", translated "+translated);});
     RegressionCheck("Vietnamese uses a serif with every Vietnamese letter",delegate {Require(serif!=null&&serif.Source.StartsWith("Cambria"),serif==null?"none":serif.Source);});
     RegressionCheck("the language is remembered",delegate {Require(Program.ReadLanguage()=="vi",Program.ReadLanguage());});
     RegressionCheck("text built while Mica runs is translated too",delegate {Require(Localizer.Translate("3 ads and trackers blocked on this page")=="Đã chặn 3 quảng cáo và trình theo dõi trên trang này"&&Localizer.Translate("Couldn't save this session: disk full").EndsWith(": disk full")&&Localizer.Translate("Couldn't save this session: disk full")!="Couldn't save this session: disk full",Localizer.Translate("3 ads and trackers blocked on this page"));});
     SwitchLanguage("en");await Task.Delay(400);
     RegressionCheck("switching back restores the English",delegate {Require(shows("PINNED")&&!shows("ĐÃ GHIM"),"english "+shows("PINNED"));});
    }
    // Spaces you make: named in a dialog, opened at once, renamed, and removed with their tabs kept in Personal.
    {
     Func<Window> nameDialog=delegate {return Application.Current.Windows.Cast<Window>().FirstOrDefault(w=>w!=window&&VaultElements<TextBox>(w).Any(b=>(b.Tag as string)=="space-name"));};
     Action<string> submit=delegate(string text) {var d=nameDialog();VaultElements<TextBox>(d).First(b=>(b.Tag as string)=="space-name").Text=text;VaultElements<Button>(d).First(b=>(b.Tag as string)=="space-create").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};
     ShowNewSpace();await Task.Delay(300);submit("School");await Task.Delay(400);
     RegressionCheck("a new space opens with its own tab",delegate {Require(state.Spaces.Contains("School")&&state.Space=="School"&&state.Tabs.Any(t=>t.Space=="School"),"spaces "+string.Join(",",state.Spaces)+" in "+state.Space);});
     ShowNewSpace();await Task.Delay(300);submit("school");await Task.Delay(300);var still=nameDialog();
     RegressionCheck("a second space with the same name is refused",delegate {Require(still!=null&&state.Spaces.Count(s=>s.ToLowerInvariant()=="school")==1,"dialog "+(still!=null));});
     if(still!=null)still.Close();await Task.Delay(200);
     Navigate("https://example.com/",false);await WaitForPage();
     ShowNewSpace("School");await Task.Delay(300);submit("Study");await Task.Delay(300);
     RegressionCheck("renaming a space keeps its tabs",delegate {Require(!state.Spaces.Contains("School")&&state.Spaces.Contains("Study")&&state.Space=="Study"&&state.Tabs.Count(t=>t.Space=="Study")==1,"spaces "+string.Join(",",state.Spaces));});
     string kept=state.ActiveId;Save();var reloaded=StateStore.Load(statePath);
     RegressionCheck("a space you made survives a restart",delegate {Require(reloaded.Spaces.Contains("Study")&&reloaded.Tabs.Any(t=>t.Space=="Study"),"reloaded "+string.Join(",",reloaded.Spaces));});
     RemoveSpace("Study");await Task.Delay(300);
     RegressionCheck("removing a space moves its tabs to Personal",delegate {var tab=state.Tabs.FirstOrDefault(t=>t.Id==kept);Require(!state.Spaces.Contains("Study")&&tab!=null&&tab.Space=="Personal"&&state.Space=="Personal",tab==null?"tab gone":tab.Space);});
    }
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
