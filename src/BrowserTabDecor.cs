using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // What a tab row says about its page beyond the title: its cookie jar (a coloured dot), whether it's making
 // sound (a speaker you click to mute), where it came from (indented under its opener with tree tabs), and,
 // on hover, how much memory it's using. Also the memory rules that use those numbers.
 partial class BrowserApp {
  // Private memory of each awake tab's renderer, refreshed with the memory readout (every 15 s).
  readonly Dictionary<string,long> tabMemory=new Dictionary<string,long>();
  readonly Dictionary<string,int> tabSharing=new Dictionary<string,int>();
  bool tabMemoryBusy;
  // WebView2 can fault while listing a process's frames if a page drops a frame at that moment. The
  // fault is caught here and the per-tab readout turns off for this window, instead of closing Mica.
  bool tabMemoryBroken;
  [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions,System.Security.SecurityCritical]
  List<KeyValuePair<int,List<uint>>> RendererTopFrames(IReadOnlyList<CoreWebView2ProcessExtendedInfo> infos) {
   var result=new List<KeyValuePair<int,List<uint>>>();
   try {
    foreach(var info in infos) {
     if(info.ProcessInfo.Kind!=CoreWebView2ProcessKind.Renderer)continue;var tops=new List<uint>();
     foreach(var frame in info.AssociatedFrameInfos){var top=frame;while(top.ParentFrameInfo!=null)top=top.ParentFrameInfo;tops.Add(top.FrameId);}
     result.Add(new KeyValuePair<int,List<uint>>(info.ProcessInfo.ProcessId,tops));
    }
   }catch(Exception){tabMemoryBroken=true;return null;}
   return result;
  }
  async void RefreshTabMemory() {
   if(tabMemoryBusy||tabMemoryBroken||environment==null||closing)return;tabMemoryBusy=true;
   try {
    var frames=new Dictionary<uint,string>();
    foreach(var pair in views){var core=LiveCore(pair.Value);if(core!=null)try {frames[core.FrameId]=pair.Key;}catch(Exception){}}
    var owners=new List<KeyValuePair<int,List<string>>>();
    var renderers=RendererTopFrames(await environment.GetProcessExtendedInfosAsync());
    if(renderers==null){tabMemory.Clear();tabSharing.Clear();return;}
    foreach(var renderer in renderers) {
     var tabs=new List<string>();foreach(var top in renderer.Value){string id;if(frames.TryGetValue(top,out id)&&!tabs.Contains(id))tabs.Add(id);}
     if(tabs.Count>0)owners.Add(new KeyValuePair<int,List<string>>(renderer.Key,tabs));
    }
    var sizes=await Task.Run(delegate {
     var result=new Dictionary<string,long>();var shared=new Dictionary<string,int>();
     foreach(var owner in owners) {
      long bytes=0;try {using(var process=System.Diagnostics.Process.GetProcessById(owner.Key))bytes=process.PrivateMemorySize64;}catch(Exception){continue;}
      foreach(var id in owner.Value){long before;result[id]=(result.TryGetValue(id,out before)?before:0)+bytes/owner.Value.Count;int n;shared[id]=Math.Max(shared.TryGetValue(id,out n)?n:1,owner.Value.Count);}
     }
     return Tuple.Create(result,shared);
    });
    tabMemory.Clear();foreach(var pair in sizes.Item1)tabMemory[pair.Key]=pair.Value;
    tabSharing.Clear();foreach(var pair in sizes.Item2)tabSharing[pair.Key]=pair.Value;
   }catch(Exception){}
   finally {tabMemoryBusy=false;}
  }
  internal string TabMemoryText(string id) {
   long bytes;if(!tabMemory.TryGetValue(id,out bytes))return views.ContainsKey(id)?null:"Resting: uses no memory";
   int shared;string mb=Math.Max(1,bytes>>20)+" MB";return tabSharing.TryGetValue(id,out shared)&&shared>1?mb+" (its process is shared with "+(shared-1)+" more)":mb;
  }

  // The parts of a tab row TabRow leaves to this file.
  void DecorateTab(PageEntry tab,Grid row,Grid content,Button select) {
   int depth=TreeDepth(tab);if(depth>0)row.Margin=new Thickness(Math.Min(depth,4)*14,row.Margin.Top,row.Margin.Right,row.Margin.Bottom);
   var jar=JarOf(tab);
   if(jar!=null) {
    var dot=new Border {Width=7,Height=7,CornerRadius=new CornerRadius(3.5),Background=new SolidColorBrush(ParseColor(jar.Color)),BorderThickness=new Thickness(1),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(11,0,0,-2),IsHitTestVisible=false,Tag="jar-dot"};
    dot.BorderBrush=SideBrush("Canvas");content.Children.Add(dot);
   }
   WebView2 view;var core=views.TryGetValue(tab.Id,out view)?LiveCore(view):null;
   bool sound=false,muted=false;try {if(core!=null){sound=core.IsDocumentPlayingAudio;muted=core.IsMuted;}}catch(Exception){}
   if(sound||muted) {
    if(content.ColumnDefinitions.Count<3)content.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
    var speaker=new Button {Content=muted?"":"",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=10,Width=20,Height=20,Padding=new Thickness(0),Margin=new Thickness(4,0,0,0),Foreground=SideBrush(muted?"Muted":"Ink"),Background=Brushes.Transparent,BorderThickness=new Thickness(0),ToolTip=muted?"Unmute tab":"Mute tab",Tag="tab-sound:"+tab.Id,VerticalAlignment=VerticalAlignment.Center};
    System.Windows.Automation.AutomationProperties.SetName(speaker,muted?"Unmute "+tab.Title:"Mute "+tab.Title);
    speaker.Click+=delegate(object sender,RoutedEventArgs e) {e.Handled=true;ToggleMute(tab.Id);};
    speaker.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) {e.Handled=true;ToggleMute(tab.Id);};
    Grid.SetColumn(speaker,2);content.Children.Add(speaker);
   }
   select.ToolTip=" ";select.ToolTipOpening+=delegate {select.ToolTip=TabTip(tab);};
  }
  string TabTip(PageEntry tab) {
   var lines=new List<string>{tab.Title,Navigation.IsReading(tab.Url)?"Saved for later":tab.Url};
   var group=tab.Pinned?null:state.FindGroup(tab.Group);if(group!=null)lines.Add("Group: "+GroupName(group));
   var jar=JarOf(tab);if(jar!=null)lines.Add("Cookie jar: "+jar.Name);
   if(Navigation.IsWeb(tab.Url)){string memory=TabMemoryText(tab.Id);if(memory!=null)lines.Add("Memory: "+memory);}
   return string.Join("\n",lines);
  }
  internal void ToggleMute(string id) {
   WebView2 view;var core=views.TryGetValue(id,out view)?LiveCore(view):null;if(core==null)return;
   try {core.IsMuted=!core.IsMuted;}catch(Exception){return;}RefreshTabRow(id);UpdateIslandMedia();
  }
  // Tree tabs: how many of the tab's openers are still open in this space.
  int TreeDepth(PageEntry tab) {
   if(!state.TreeTabs||tab.Pinned)return 0;int depth=0;var at=tab;
   for(int guard=0;guard<8&&at.Opener!=null;guard++){var id=at.Opener;var parent=state.Tabs.FirstOrDefault(t=>t.Id==id);if(parent==null||parent.Space!=tab.Space||parent.Pinned||parent.Group!=tab.Group)break;depth++;at=parent;}
   return depth;
  }
  internal void ToggleTreeTabs() {state.TreeTabs=!state.TreeTabs;RenderTabs();Save();Status(state.TreeTabs?"Tree tabs on: tabs you open from a page sit under it.":"Tree tabs off.");}

  // Tab search shows how much each open tab is using.
  List<SearchHit> TabHitsWithMemory(List<SearchHit> hits) {
   foreach(var hit in hits) {
    if(hit.TabId==null)continue;var tab=state.Tabs.FirstOrDefault(t=>t.Id==hit.TabId);if(tab==null||!Navigation.IsWeb(tab.Url))continue;
    string memory=TabMemoryText(hit.TabId);if(memory!=null)hit.Detail+=" · "+memory;
   }
   return hits;
  }

  // Folding a group puts its tabs to rest together (the one you're on stays); unfolding warms the three
  // you used last, so the group is ready again without loading all of it at once.
  internal void RestGroup(string id) {
   int rested=0;
   foreach(var tab in state.Tabs.Where(t=>t.Group==id&&t.Id!=state.ActiveId&&!InSplit(t.Id)).ToList()) {
    WebView2 view;if(!views.TryGetValue(tab.Id,out view)||HasDownloads(view)||RecentlyAudible(tab.Id,view)||typedInput.Contains(tab.Id)||SiteMemoryMode(tab)=="never")continue;
    DisposeView(tab.Id);rested++;
   }
   if(rested>0){UpdateMemory();Status(rested+(rested==1?" tab":" tabs")+" in the group put to rest. They come back when you open the group.");}
  }
  internal async void WakeGroup(string id) {
   foreach(var tab in state.Tabs.Where(t=>t.Group==id&&Navigation.IsWeb(t.Url)&&!views.ContainsKey(t.Id)).OrderByDescending(t=>t.LastUsed).Take(3).ToList()) {
    // Split view (or a click) may have brought it up meanwhile; a second page for it would be orphaned.
    if(closing)return;if(views.ContainsKey(tab.Id)||!state.Tabs.Contains(tab))continue;await CreateTabView(tab,true);await Task.Delay(400);
   }
   MarkRestingTabs(true);
  }

  // ---- Memory rules per site and by weight ----
  internal string SiteMemoryMode(PageEntry tab) {
   string site=tab==null||!Navigation.IsWeb(tab.Url)?null:SiteFor(tab.Url),mode;
   return site!=null&&state.SiteMemory!=null&&state.SiteMemory.TryGetValue(site,out mode)?mode:null;
  }
  internal void SetSiteMemory(string site,string mode) {
   if(site==null)return;if(mode==null)state.SiteMemory.Remove(site);else state.SiteMemory[site]=mode;Save();
   Status(mode=="never"?site+" stays awake in the background.":mode=="quick"?site+" rests two minutes after you leave it.":site+" follows the usual memory saver.");
  }
  // Rest timing: "quick" sites after 2 minutes, heavy pages (over 400 MB) in half the usual time, "never" sites not at all.
  TimeSpan RestAfterFor(PageEntry tab,bool low) {
   string mode=SiteMemoryMode(tab);if(mode=="never")return TimeSpan.MaxValue;if(mode=="quick")return TimeSpan.FromMinutes(2);
   var after=TabLifecycle.RestAfter(low);long bytes;
   if(tabMemory.TryGetValue(tab.Id,out bytes)&&bytes>400L<<20)after=TimeSpan.FromTicks(after.Ticks/2);
   return after;
  }
  // When over budget, the heaviest of the tabs left longest go first.
  double RestScore(PageEntry tab,DateTime now) {
   long bytes;double mb=tabMemory.TryGetValue(tab.Id,out bytes)?bytes/1048576.0:120;
   double minutes=Math.Max(0,(now-tab.LastUsed).TotalMinutes);
   return mb*(1+minutes/10)*(SiteMemoryMode(tab)=="quick"?3:1);
  }
 }
}
