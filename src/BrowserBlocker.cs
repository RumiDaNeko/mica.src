using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // Mica's own choice of lists and excluded sites, kept in the profile.
 sealed class BlockerConfig {
  public bool Enabled=true;
  public List<string> Lists=new List<string>();
  public List<string> Excluded=new List<string>();
 }
 sealed class BlockerList {public string id,name,group;public int rules;public bool @default;}
 // Two blockers ship inside Mica (Extensions\) and are added to every normal profile on first start:
 // uBlock Origin Lite (page cosmetics and its own rules) and Mica blocker, which loads uBO Lite's network
 // lists as dynamic rules, since the engine doesn't apply an extension's static rulesets. Ads and their
 // frames never load, which also saves the memory and processes they'd use. They don't show in Extensions:
 // Ad blocking in the menu turns them on or off, picks lists and excludes sites, for both at once.
 partial class BrowserApp {
  static readonly string[] BuiltInExtensions={"mica-blocker","ublock-lite"};
  static readonly HashSet<string> builtInIds=new HashSet<string>();
  const int BlockerRuleLimit=29900; // Chromium's dynamic-rule limit is 30,000; one rule is kept for excluded sites.
  static string BuiltInFolder(string name) {return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Extensions",name);}
  string BuiltInMarker(string name) {return Path.Combine(profile,"builtin-"+name+".txt");}
  string BlockerConfigPath {get {return Path.Combine(profile,"blocker.json");}}
  bool blockerChecked;internal string blockerId,networkBlockerId;
  static string ManifestVersion(string folder) {
   try {var manifest=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(folder,"manifest.json")));object version;return manifest.TryGetValue("version",out version)?version as string:null;}catch(Exception){return null;}
  }
  static List<BlockerList> blockerLists;
  static List<BlockerList> BlockerLists() {
   if(blockerLists==null)try {blockerLists=new JavaScriptSerializer().Deserialize<List<BlockerList>>(File.ReadAllText(Path.Combine(BuiltInFolder("mica-blocker"),"rules","lists.json")));}catch(Exception){blockerLists=new List<BlockerList>();}
   return blockerLists;
  }
  internal BlockerConfig LoadBlockerConfig() {
   BlockerConfig config=null;try {if(File.Exists(BlockerConfigPath))config=new JavaScriptSerializer().Deserialize<BlockerConfig>(File.ReadAllText(BlockerConfigPath));}catch(Exception){}
   if(config==null){config=new BlockerConfig();config.Lists.AddRange(BlockerLists().Where(l=>l.@default).Select(l=>l.id));}
   if(config.Lists==null)config.Lists=new List<string>();if(config.Excluded==null)config.Excluded=new List<string>();
   return config;
  }
  void SaveBlockerConfig(BlockerConfig config) {if(!privateMode)try {File.WriteAllText(BlockerConfigPath,new JavaScriptSerializer().Serialize(config));}catch(Exception){}}
  internal static int RulesFor(IEnumerable<string> lists) {return BlockerLists().Where(l=>lists.Contains(l.id)).Sum(l=>l.rules);}
  // "www.Example.com", "https://example.com/page" and "example.com" all mean example.com (and its subdomains).
  internal static string SiteFor(string input) {
   string text=(input??"").Trim().ToLowerInvariant();if(text.Length==0)return null;
   Uri uri;if(!Uri.TryCreate(text.Contains("://")?text:"https://"+text,UriKind.Absolute,out uri)||string.IsNullOrEmpty(uri.Host)||!uri.Host.Contains('.'))return null;
   string host=uri.IdnHost;return host.StartsWith("www.")?host.Substring(4):host;
  }

  async Task EnsureBuiltInBlocker() {
   if(blockerChecked||privateMode||closing)return;blockerChecked=true;
   if(smoke&&!Environment.GetCommandLineArgs().Any(a=>a=="--bench-test"||a=="--blocker-test"))return;
   try {
    var target=await EnsureExtensionProfile();var all=await target.GetBrowserExtensionsAsync();var config=LoadBlockerConfig();bool added=false;
    foreach(var name in BuiltInExtensions) {
     string folder=BuiltInFolder(name),version=ManifestVersion(folder);if(version==null)continue;
     // The marker holds "id version" of the copy last added.
     string[] marker=File.Exists(BuiltInMarker(name))?File.ReadAllText(BuiltInMarker(name)).Trim().Split(' '):new string[0];
     var present=marker.Length==2?all.FirstOrDefault(e=>e.Id==marker[0]):null;string id=present==null?null:present.Id;
     if(id==null||marker[1]!=version){var fresh=await target.AddBrowserExtensionAsync(folder);id=fresh.Id;File.WriteAllText(BuiltInMarker(name),id+" "+version);added=true;present=fresh;}
     if(present.IsEnabled!=config.Enabled)await present.EnableAsync(config.Enabled);
     lock(builtInIds)builtInIds.Add(id);if(name=="ublock-lite")blockerId=id;else networkBlockerId=id;
    }
    // A freshly added copy starts from its defaults; give it the lists and sites chosen before.
    if(added&&File.Exists(BlockerConfigPath)&&config.Enabled)await ApplyBlocker(config);
   }catch(Exception e){if(smoke)Status("Built-in blocker: "+e.Message);}
  }

  // Changes go to both extensions through their own pages, opened in a hidden view only while applying.
  bool blockerApplying,blockerAgain;
  internal async Task<string> ApplyBlocker(BlockerConfig config) {
   if(blockerApplying){blockerAgain=true;return null;}blockerApplying=true;string result=null;
   try {
    do {
     blockerAgain=false;SaveBlockerConfig(config);
     var target=await EnsureExtensionProfile();
     foreach(var extension in (await target.GetBrowserExtensionsAsync()).Where(e=>builtInIds.Contains(e.Id)))if(extension.IsEnabled!=config.Enabled)await extension.EnableAsync(config.Enabled);
     if(!config.Enabled)continue;
     var json=new JavaScriptSerializer();string choice=json.Serialize(new Dictionary<string,object>{{"lists",config.Lists},{"excluded",config.Excluded}});
     if(networkBlockerId!=null)result=await RunInExtension(networkBlockerId,"control.html","micaApply("+choice+")");
     if(blockerId!=null)await RunInExtension(blockerId,"dashboard.html",
      "(async()=>{const c="+choice+";const send=m=>chrome.runtime.sendMessage(m);await send({what:'applyRulesets',enabledRulesets:c.lists});"+
      "const d=await send({what:'getFilteringModeDetails'});for(const k of ['basic','optimal','complete'])d[k]=(d[k]||[]).filter(h=>!c.excluded.includes(h));d.none=c.excluded;"+
      "await send({what:'setFilteringModeDetails',modes:d});return 'ok';})()");
    } while(blockerAgain&&!closing);
   }finally {blockerApplying=false;CloseBlockerControl();}
   return result;
  }
  WebView2 blockerControl;
  async Task<string> RunInExtension(string id,string page,string expression) {
   var engine=await EnsureEnvironment();await EnsureExtensionProfile();
   if(blockerControl==null){var view=new WebView2 {Width=1,Height=1,Visibility=Visibility.Hidden,IsHitTestVisible=false,Focusable=false};Find<Grid>("Root").Children.Add(view);blockerControl=view;await view.EnsureCoreWebView2Async(engine,ViewOptions(engine));KeepWindowOpen(view);RegisterOwnTarget(view.CoreWebView2);}
   var core=blockerControl.CoreWebView2;string url="chrome-extension://"+id+"/"+page;
   if(core.Source!=url) {
    var loaded=new TaskCompletionSource<string>();EventHandler<CoreWebView2NavigationCompletedEventArgs> done=null;
    done=delegate(object sender,CoreWebView2NavigationCompletedEventArgs e) {core.NavigationCompleted-=done;loaded.TrySetResult(e.IsSuccess?null:e.WebErrorStatus.ToString());};
    core.NavigationCompleted+=done;core.Navigate(url);string failed=await loaded.Task;if(failed!=null)throw new InvalidOperationException("The blocker didn't answer ("+failed+").");
   }
   var json=new JavaScriptSerializer();
   var reply=json.Deserialize<Dictionary<string,object>>(await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",json.Serialize(new Dictionary<string,object>{{"expression",expression},{"awaitPromise",true},{"returnByValue",true}})));
   object error;if(reply.TryGetValue("exceptionDetails",out error))throw new InvalidOperationException("The blocker couldn't apply that.");
   object value;var inner=reply["result"] as Dictionary<string,object>;return inner!=null&&inner.TryGetValue("value",out value)?value as string:null;
  }
  void CloseBlockerControl() {if(blockerControl==null)return;var view=blockerControl;blockerControl=null;Find<Grid>("Root").Children.Remove(view);try {view.Dispose();}catch(Exception){}}

  // The site you're on, for the quick toggle in Sites.
  string CurrentBlockerSite() {var tab=Active;return tab!=null&&Navigation.IsWeb(tab.Url)?SiteFor(tab.Url):null;}
  TextBlock adsHint;
  // How many requests (ads, trackers) the blocker stopped on this page, asked of Mica blocker's page script
  // through a DOM event, so nothing watches the page's traffic; -1 when it can't tell.
  const string BlockedCountScript="new Promise(r=>{const d=document;if(!d.documentElement)return r(-1);const t=setTimeout(()=>r(-1),1500);d.addEventListener('mica-blocked-told',()=>{clearTimeout(t);r(+d.documentElement.getAttribute('data-mica-blocked'))},{once:true});d.dispatchEvent(new Event('mica-blocked-ask'))})";
  async Task<int> BlockedOnPage(WebView2 view) {
   var core=LiveCore(view);if(core==null||!Navigation.IsWeb(core.Source))return -1;
   try {
    string raw=await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"expression",BlockedCountScript},{"awaitPromise",true},{"returnByValue",true}}));
    var match=System.Text.RegularExpressions.Regex.Match(raw,"\"value\":(-?\\d+)");return match.Success?int.Parse(match.Groups[1].Value):-1;
   }catch(Exception){return -1;}
  }
  async void ShowBlockedCount(WebView2 view,TextBlock note) {
   // The blocker's worker may be asleep and miss the first ask; one more try wakes it.
   int blocked=await BlockedOnPage(view);if(blocked<0)blocked=await BlockedOnPage(view);if(blocked<0||note.Text.Length>0)return;
   note.Text=blocked==0?"Nothing blocked on this page":blocked+(blocked==1?" ad or tracker":" ads and trackers")+" blocked on this page";
  }
  string AdsHint() {var config=LoadBlockerConfig();string site=CurrentBlockerSite();return !config.Enabled?"Off":site!=null&&config.Excluded.Contains(site)?"Allowed":"Blocked";}
  async void ToggleAdsHere() {
   string site=CurrentBlockerSite();if(site==null){Status("Open a website to choose whether its ads are blocked.");return;}
   var config=LoadBlockerConfig();if(!config.Enabled){ShowAdBlocking();return;}
   bool allow=!config.Excluded.Contains(site);if(allow)config.Excluded.Add(site);else config.Excluded.Remove(site);
   if(adsHint!=null)RollText(adsHint,allow?"Allowed":"Blocked");
   try {await ApplyBlocker(config);Status(allow?"Ads are allowed on "+site+". Reloading.":"Ads are blocked on "+site+" again. Reloading.");Reload();}
   catch(Exception e){Status("Couldn't change ad blocking: "+e.Message);}
  }

  Window adBlockingWindow;
  void ShowAdBlocking() {
   CloseMenus();if(adBlockingWindow!=null){adBlockingWindow.Activate();return;}
   StackPanel body,actions;var dialog=CreateMicaDialog(window,"Ad blocking",600,760,out body,out actions);adBlockingWindow=dialog;dialog.Closed+=delegate {adBlockingWindow=null;};
   var done=new Button {Content="Done",Padding=new Thickness(22,10,22,10),IsDefault=true};done.SetResourceReference(Button.BackgroundProperty,"Hover");done.Click+=delegate {dialog.Close();};actions.Children.Add(done);
   RenderAdBlocking(body);dialog.Show();
  }
  void RenderAdBlocking(StackPanel body) {
   var config=LoadBlockerConfig();var lists=BlockerLists();
   var status=DialogText("",12,"Muted");status.Margin=new Thickness(0,8,0,0);
   Action count=delegate {int used=RulesFor(config.Lists);status.Text=config.Enabled?used.ToString("N0")+" of "+BlockerRuleLimit.ToString("N0")+" rules in use · powered by uBlock Origin Lite's lists":"Ads and trackers load on every site while this is off.";};
   Action apply=async delegate {count();try {await ApplyBlocker(config);}catch(Exception e){status.Text="Couldn't apply: "+e.Message;}};
   var head=new Grid();head.ColumnDefinitions.Add(new ColumnDefinition());head.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
   var title=DialogText("Block ads and trackers",14,"Ink");title.FontWeight=FontWeights.SemiBold;title.VerticalAlignment=VerticalAlignment.Center;head.Children.Add(title);
   var power=SegmentedChoice(new[]{"On","Off"},config.Enabled?0:1,delegate(int picked,Action undo) {config.Enabled=picked==0;apply();});Grid.SetColumn(power,1);head.Children.Add(power);
   body.Children.Add(head);body.Children.Add(status);count();

   // Excluded sites: everything loads there, for both blockers.
   SectionLabel(body,"SITES WHERE ADS ARE ALLOWED");
   var sites=new StackPanel();body.Children.Add(sites);
   Action<string> addRow=null;
   addRow=delegate(string site) {
    var row=new Grid {Margin=new Thickness(12,6,8,6)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
    var name=DialogText(site,12,"Ink");name.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(name);
    var remove=new Button {Content="Block again",Padding=new Thickness(12,6,12,6),Tag="unexclude:"+site};Grid.SetColumn(remove,1);row.Children.Add(remove);
    var card=Card(row);sites.Children.Add(card);
    remove.Click+=delegate {config.Excluded.Remove(site);remove.IsEnabled=false;FoldAway(card);apply();};
   };
   foreach(var site in config.Excluded.OrderBy(s=>s))addRow(site);
   var adder=new Grid {Margin=new Thickness(0,2,0,4)};adder.ColumnDefinitions.Add(new ColumnDefinition());adder.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
   var field=new TextBox {Padding=new Thickness(8,6,8,6),Tag="exclude-site",VerticalContentAlignment=VerticalAlignment.Center};System.Windows.Automation.AutomationProperties.SetName(field,"Website to allow ads on");
   var add=new Button {Content="Allow ads",Padding=new Thickness(14,6,14,6),Margin=new Thickness(8,0,0,0),Tag="exclude-add"};Grid.SetColumn(add,1);adder.Children.Add(field);adder.Children.Add(add);
   var hint=DialogText("Type a website, like example.com. Its pages, and anything they load, won't be filtered.",11,"Muted");hint.Margin=new Thickness(0,4,0,0);
   Action addSite=delegate {
    string site=SiteFor(field.Text);if(site==null){hint.Text="That doesn't look like a website. Try something like example.com.";return;}
    if(!config.Excluded.Contains(site)){config.Excluded.Add(site);addRow(site);apply();}
    field.Clear();hint.Text="Ads are allowed on "+site+" and its subdomains.";
   };
   add.Click+=delegate {addSite();};field.KeyDown+=delegate(object sender,KeyEventArgs e) {if(e.Key==Key.Enter){e.Handled=true;addSite();}};
   string here=CurrentBlockerSite();if(here!=null&&!config.Excluded.Contains(here))field.Text=here;
   body.Children.Add(adder);body.Children.Add(hint);

   // Lists, by kind. Regional lists sit last and stay folded away in their own section.
   var groups=new[]{new[]{"default","RECOMMENDED"},new[]{"privacy","PRIVACY"},new[]{"malware","MALWARE AND SCAMS"},new[]{"ads","MORE ADS"},new[]{"annoyances","ANNOYANCES"},new[]{"misc","OTHER"},new[]{"regions","REGIONAL"}};
   foreach(var group in groups) {
    var members=lists.Where(l=>l.group==group[0]).ToList();if(members.Count==0)continue;
    SectionLabel(body,group[1]);var panel=new StackPanel();var card=Card(panel);body.Children.Add(card);
    foreach(var list in members) {
     var item=list;var box=new CheckBox {IsChecked=config.Lists.Contains(item.id),Margin=new Thickness(12,7,12,7),Tag="list:"+item.id};
     var label=new StackPanel {Orientation=Orientation.Horizontal};label.Children.Add(DialogText(item.name,12,"Ink"));var size=DialogText(item.rules.ToString("N0")+(item.rules==1?" rule":" rules"),11,"Muted");size.Margin=new Thickness(8,1,0,0);label.Children.Add(size);box.Content=label;
     box.Checked+=delegate {
      if(config.Lists.Contains(item.id))return;
      if(RulesFor(config.Lists)+item.rules>BlockerRuleLimit){box.IsChecked=false;status.Text="That list doesn't fit: Chromium allows "+BlockerRuleLimit.ToString("N0")+" rules. Turn another list off first.";return;}
      config.Lists.Add(item.id);apply();
     };
     box.Unchecked+=delegate {if(config.Lists.Remove(item.id))apply();};
     panel.Children.Add(box);
    }
   }
   var note=DialogText("Changes apply to pages as they load; reload a page to see them there.",11,"Muted");note.Margin=new Thickness(0,10,0,4);body.Children.Add(note);
  }
 }
}
