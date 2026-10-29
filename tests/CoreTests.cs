using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mica;
class CoreTests {
 static int failures;
 static void Check(string name, Action action) { try { action(); Console.WriteLine("PASS " + name); } catch(Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
 static void Equal(object expected,object actual) { if(!object.Equals(expected,actual)) throw new Exception("Expected " + expected + ", got " + actual); }
 static void Main() {
  Check("tab search finds tabs in both spaces, words in any order",delegate {
   var s=new BrowserState {Tabs=new List<PageEntry>(),History=new List<PageEntry>(),Bookmarks=new List<PageEntry>(),RecentlyClosed=new List<PageEntry>(),Space="Personal"};
   s.Tabs.Add(new PageEntry {Id="a",Title="Rust book: ownership",Url="https://doc.rust-lang.org/book/",Space="Personal",LastUsed=DateTime.UtcNow});
   s.Tabs.Add(new PageEntry {Id="b",Title="Team board",Url="https://linear.app/team",Space="Work",LastUsed=DateTime.UtcNow.AddMinutes(-5)});
   s.RecentlyClosed.Add(new PageEntry {Title="Ownership in Rust (video)",Url="https://youtube.com/watch?v=1"});
   s.History.Add(new PageEntry {Title="Rust ownership forum",Url="https://users.rust-lang.org/t/1",VisitCount=1});
   s.ActiveId="b";
   var hits=Spotlight.SearchTabs(s,"ownership rust",id=>id=="a");
   Equal("OPEN,CLOSED,HISTORY",string.Join(",",hits.Select(h=>h.Kind)));
   Equal("a",hits[0].TabId);
   Equal(2,Spotlight.SearchTabs(s,"",id=>false).Count(h=>h.TabId!=null));
   Equal("THIS TAB",Spotlight.SearchTabs(s,"board",id=>true)[0].Kind);
   Equal(0,Spotlight.SearchTabs(s,"rust nothing",id=>true).Count);
  });
  Check("aborted successful HTTP page stays visible",delegate {var n=new NavigationProgress();n.Begin(1);Equal(NavigationResult.Stopped,n.Complete(1,false,200,"ConnectionAborted"));Equal(false,n.IsLoading);});
  Check("actual aborted connection remains an error",delegate {var n=new NavigationProgress();n.Begin(1);Equal(NavigationResult.Failed,n.Complete(1,false,0,"ConnectionAborted"));});
  Check("old aborted navigation cannot overwrite new one",delegate {var n=new NavigationProgress();n.Begin(1);n.Begin(2);Equal(NavigationResult.Ignore,n.Complete(1,false,0,"ConnectionAborted"));Equal(true,n.IsLoading);Equal(NavigationResult.Ready,n.Complete(2,true,200,"Unknown"));});
  Check("cancellation retains page without fake error",delegate {var n=new NavigationProgress();n.Begin(1);Equal(NavigationResult.Stopped,n.Complete(1,false,0,"OperationCanceled"));});
  Check("certificate failure is never hidden by HTTP status",delegate {var n=new NavigationProgress();n.Begin(1);Equal(NavigationResult.Failed,n.Complete(1,false,200,"CertificateIsInvalid"));});
  Check("domain becomes HTTPS",delegate { Equal("https://example.com/docs",Navigation.Resolve(" example.com/docs ")); });
  Check("localhost preserves port",delegate { Equal("http://localhost:3000/",Navigation.Resolve("localhost:3000")); });
  Check("domain with port becomes HTTPS",delegate { Equal("https://example.com:8443/docs",Navigation.Resolve("example.com:8443/docs")); });
  Check("search encodes Vietnamese and punctuation",delegate { Equal("https://www.google.com/search?q="+Uri.EscapeDataString("cà phê & sách"),Navigation.Resolve("cà phê & sách")); });
  Check("empty input returns home",delegate { Equal("mica://home",Navigation.Resolve("  ")); });
  foreach(var scheme in new[]{"javascript:alert(1)","file:///C:/secret", "data:text/html,test", "mailto:a@b.com", "ftp://example.com"}) { var input=scheme; Check("reject " + input,delegate { bool rejected=false; try { Navigation.Resolve(input); } catch(ArgumentException) { rejected=true; } Equal(true,rejected); }); }
  Check("tab creation selects new tab",delegate { var s=new BrowserState(); var t=s.AddTab("https://example.com/"); Equal(t.Id,s.ActiveId); Equal("Personal",t.Space); });
  Check("closing last tab restores home",delegate { var s=new BrowserState(); var t=s.AddTab("https://example.com/"); s.CloseTab(t.Id); Equal(1,s.Tabs.Count); Equal("mica://home",s.Tabs[0].Url); Equal(s.Tabs[0].Id,s.ActiveId); });
  Check("history deduplicates and caps",delegate { var s=new BrowserState(); for(int i=0;i<510;i++) s.Visit("https://example.com/"+i,"Page "+i); s.Visit("https://example.com/509","Updated"); Equal(500,s.History.Count); Equal("Updated",s.History[0].Title); });
  Check("Spotlight ranks tabs before bookmarks and removes same URL",delegate { var s=new BrowserState(); var t=s.AddTab("https://github.com/"); t.Title="GitHub"; s.Bookmarks.Add(new PageEntry{Title="GitHub",Url=t.Url}); s.History.Add(new PageEntry{Title="GitHub Docs",Url="https://docs.github.com/"}); var r=Spotlight.Search(s,"git"); Equal("TAB",r[0].Kind); Equal(t.Id,r[0].TabId); Equal(1,r.Count(x=>x.Url==t.Url)); Equal(true,r.Any(x=>x.Kind=="HISTORY")); });
  Check("Spotlight searches other spaces",delegate { var s=new BrowserState(); var t=s.AddTab("https://example.com/"); t.Title="Design brief"; t.Space="Work"; Equal(t.Id,Spotlight.Search(s,"design").First(h=>h.TabId!=null).TabId); });
  Check("Spotlight: Enter searches a query even when a page title contains it",delegate { var s=new BrowserState(); s.History.Add(new PageEntry{Title="Best pho recipes",Url="https://food.example/pho"}); var r=Spotlight.Search(s,"pho"); Equal("SEARCH",r[0].Kind); Equal(true,r.Any(x=>x.Url=="https://food.example/pho")); Equal("SEARCH",Spotlight.Search(s,"pho recipes")[0].Kind); });
  Check("Spotlight: the start of a visited site's address stays on top",delegate { var s=new BrowserState(); s.History.Add(new PageEntry{Title="GitHub",Url="https://www.github.com/"}); Equal("https://www.github.com/",Spotlight.Search(s,"git")[0].Url); Equal("SEARCH",Spotlight.Search(s,"git")[1].Kind); });
  Check("Spotlight: a typed address goes first",delegate { var s=new BrowserState(); for(int i=0;i<3;i++)s.AddTab("https://example.com/page"+i); s.History.Add(new PageEntry{Title="Home",Url="https://example.com/"}); Equal("https://example.com/",Spotlight.Search(s,"example.com")[0].Url); Equal("https://news.example.org/",Spotlight.Search(s,"news.example.org")[0].Url); });
  Check("Spotlight preserves case-sensitive paths",delegate { var s=new BrowserState(); s.AddTab("https://example.com/A"); s.AddTab("https://example.com/a"); Equal(2,Spotlight.Search(s,"").Count); });
  Check("Spotlight keeps explicit destination beyond result limit",delegate {var s=new BrowserState();for(int i=0;i<7;i++)s.AddTab("https://example.com/page"+i);s.History.Add(new PageEntry {Url="https://example.com/",Title="Home"});Equal(true,Spotlight.Search(s,"example.com").Any(h=>h.Url=="https://example.com/"));});
  Check("switching tabs starts idle clock at departure",delegate { var s=new BrowserState(); var first=s.AddTab("https://example.com/"); var second=s.AddTab("mica://home"); s.ActiveId=first.Id; first.LastUsed=DateTime.UtcNow.AddHours(-1); var now=DateTime.UtcNow; s.ActivateTab(second.Id,now); Equal(now,first.LastUsed); Equal(second.Id,s.ActiveId); });
  Check("new tab starts old tab idle clock",delegate { var s=new BrowserState(); var first=s.AddTab("https://example.com/"); first.LastUsed=DateTime.UtcNow.AddHours(-1); s.AddTab("mica://home"); Equal(true,first.LastUsed>DateTime.UtcNow.AddSeconds(-5)); });
  Check("sleep policy preserves active and audio tabs",delegate {var t=new PageEntry {LastUsed=DateTime.UtcNow.AddMinutes(-5)};Equal(false,TabLifecycle.ShouldSleep(t,true,false,DateTime.UtcNow,true));Equal(false,TabLifecycle.ShouldSleep(t,false,true,DateTime.UtcNow,true));});
  Check("sleep policy honors inactivity grace period",delegate {var now=DateTime.UtcNow;var t=new PageEntry {LastUsed=now.AddMinutes(-14)};Equal(false,TabLifecycle.ShouldSleep(t,false,false,now,false));Equal(true,TabLifecycle.ShouldSleep(t,false,false,now.AddMinutes(2),false));Equal(true,TabLifecycle.ShouldSleep(t,false,false,now,false,TabLifecycle.RestAfter(true)));});
  Check("tabs move before or after another and take its pinned state",delegate {var s=new BrowserState();var a=s.AddTab("https://a.test/");var b=s.AddTab("https://b.test/");var c=s.AddTab("https://c.test/");b.Pinned=true;
   s.MoveTab(c.Id,a.Id,false,false,null);Equal(c.Id+","+a.Id+","+b.Id,string.Join(",",s.Tabs.Select(t=>t.Id)));s.MoveTab(a.Id,b.Id,true,true,null);Equal(true,a.Pinned);Equal(a.Id,s.Tabs.Last().Id);});
  Check("a group's tabs stay together and an empty group goes away",delegate {var s=new BrowserState();var a=s.AddTab("https://a.test/");var b=s.AddTab("https://b.test/");var c=s.AddTab("https://c.test/");
   var g=s.CreateGroup(a);s.MoveTab(c.Id,a.Id,true,false,g.Id);Equal(a.Id+","+c.Id+","+b.Id,string.Join(",",s.Tabs.Select(t=>t.Id)));
   s.Tabs[2].Group=g.Id;s.Tabs.Reverse();s.NormalizeGroups();Equal(3,s.Tabs.Count(t=>t.Group==g.Id));Equal(true,s.Tabs.Select(t=>t.Group).SequenceEqual(new[]{g.Id,g.Id,g.Id}));
   s.Ungroup(g.Id);Equal(0,s.Groups.Count);});
  Check("groups get distinct colours, pinned tabs leave groups, whole groups move",delegate {var s=new BrowserState();var a=s.AddTab("https://a.test/");var b=s.AddTab("https://b.test/");var c=s.AddTab("https://c.test/");var d=s.AddTab("https://d.test/");
   var g1=s.CreateGroup(a);var g2=s.CreateGroup(b);if(g1.Color==g2.Color)throw new Exception("same colour");s.MoveTab(c.Id,b.Id,true,false,g2.Id);
   s.MoveGroup(g2.Id,a.Id,false);Equal(b.Id+","+c.Id+","+a.Id+","+d.Id,string.Join(",",s.Tabs.Select(t=>t.Id)));
   a.Pinned=true;s.NormalizeGroups();Equal(null,a.Group);Equal(false,s.Groups.Any(g=>g.Id==g1.Id));});
  Check("the address never hides the real site behind a user@ part or a long host",delegate {
   Equal("https://evil.example/signin",Navigation.AddressLabel("https://www.paypal.com@evil.example/signin"));
   Equal("https://…com.verify-session-0000000000000.evil.example/…",Navigation.AddressLabel("https://accounts.google.com.verify-session-0000000000000.evil.example/login"));
   Equal("https://example.com/a?b=1",Navigation.AddressLabel("https://example.com/a?b=1"));
  });
  Check("search pages show their query, lookalikes and address-like queries show the real URL",delegate {
   Equal("cats",Navigation.AddressLabel("https://www.google.com/search?q=cats"));Equal("cats",Navigation.AddressLabel("https://www.google.co.uk/search?q=cats"));
   foreach(var spoof in new[]{"https://google9.app/search?q=cats","https://googlez.com/search?q=cats","https://evilgoogle.com/search?q=cats","https://www.google.com/search?q=https://www.paypal.com/signin","https://www.google.com/search?q=paypal.com"})Equal(spoof,Navigation.AddressLabel(spoof));
  });
  Check("groups survive a session reload",delegate {var p=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");try {var s=new BrowserState();var a=s.AddTab("https://a.test/");var g=s.CreateGroup(a);g.Name="Trip";g.Collapsed=true;StateStore.Save(p,s);var r=StateStore.Load(p);Equal("Trip",r.Groups.Single().Name);Equal(true,r.Groups.Single().Collapsed);Equal(r.Groups.Single().Id,r.Tabs.First(t=>t.Url=="https://a.test/").Group);}finally {if(File.Exists(p))File.Delete(p);}});
  Check("Spotlight blocks unsafe navigation",delegate { Equal(0,Spotlight.Search(new BrowserState(),"javascript:alert(1)").Count); });
  Check("state survives atomic replacement",delegate { var p=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"); try { var s=new BrowserState(); s.AddTab("https://example.com/"); StateStore.Save(p,s); s.Dark=true; StateStore.Save(p,s); var loaded=StateStore.Load(p); Equal(true,loaded.Dark); Equal("https://example.com/",loaded.Tabs[0].Url); } finally { if(File.Exists(p))File.Delete(p); } });
  Check("corrupt state recovers home",delegate { var p=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"); try { File.WriteAllText(p,"not json"); Equal("mica://home",StateStore.Load(p).Tabs[0].Url); } finally { File.Delete(p); } });
  Check("library URLs resolve without becoming web searches",delegate {Equal("mica://history",Navigation.Resolve("mica://history"));Equal("mica://downloads",Navigation.Resolve("mica://downloads"));});
  Check("library tabs survive session reload",delegate {var p=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");try {var s=new BrowserState();s.AddTab("mica://history");s.AddTab("mica://downloads");StateStore.Save(p,s);var restored=StateStore.Load(p);Equal(2,restored.Tabs.Count);Equal("mica://downloads",restored.Tabs.Single(t=>t.Id==restored.ActiveId).Url);}finally {if(File.Exists(p))File.Delete(p);}});
  Check("history tracks repeated visits",delegate {var s=new BrowserState();s.Visit("https://example.com/a","A");s.Visit("https://example.com/a","A");Equal(2,s.History[0].VisitCount);});
  Check("top sites aggregate origins and navigate most recent page",delegate {var s=new BrowserState();s.Visit("https://example.com/one","One");s.Visit("https://example.com/two","Two");s.Visit("https://other.test/","Other");Equal("https://example.com/two",s.TopSites(4)[0].Url);Equal(2,s.TopSites(4).Count);});
  Check("top sites break frequency ties by recency and cap at four",delegate {var s=new BrowserState();for(int i=0;i<6;i++){s.Visit("https://site"+i+".test/","Site");s.History[0].LastUsed=new DateTime(2026,1,1).AddDays(i);}Equal(4,s.TopSites(4).Count);Equal("https://site5.test/",s.TopSites(4)[0].Url);Equal(0,new BrowserState().TopSites(4).Count);});
  Check("closed tabs retain snapshots only for web pages",delegate {var s=new BrowserState();var tab=s.AddTab("https://example.com/");tab.Title="Original";s.RecordClosed(tab);tab.Title="Changed";Equal("Original",s.RecentlyClosed[0].Title);s.RecordClosed(s.AddTab("mica://home"));s.RecordClosed(s.AddTab("mica://history"));Equal(1,s.RecentlyClosed.Count);for(int i=0;i<10;i++)s.RecordClosed(new PageEntry{Url="https://example.com/"+i});Equal(8,s.RecentlyClosed.Count);Equal("https://example.com/9",s.RecentlyClosed[0].Url);});
  Check("legacy history becomes one visit and new state persists counts",delegate {var p=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");try {File.WriteAllText(p,"{\"History\":[{\"Url\":\"https://example.com/\",\"Title\":\"Old\"}]}");var s=StateStore.Load(p);Equal(1,s.History[0].VisitCount);s.Visit("https://example.com/","Updated");s.RecordClosed(s.History[0]);StateStore.Save(p,s);var loaded=StateStore.Load(p);Equal(2,loaded.History[0].VisitCount);Equal(1,loaded.RecentlyClosed.Count);}finally{if(File.Exists(p))File.Delete(p);}});
  Console.WriteLine(failures+" failing tests"); Environment.ExitCode=failures==0?0:1;
 }
}
