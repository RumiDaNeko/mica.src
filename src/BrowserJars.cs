using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // Cookie jars: separate cookies, storage and sign-ins inside one profile, chosen per tab, so two
 // accounts on one site can sit in two tabs. Each jar is its own engine profile in the shared engine, so
 // it costs no second browser. Links and pop-ups opened from a jar tab stay in its jar.
 partial class BrowserApp {
  CookieJar JarOf(PageEntry tab) {return tab==null||tab.Jar==null||privateMode||state.Jars==null?null:state.Jars.FirstOrDefault(j=>j.Id==tab.Jar);}
  string JarProfileName(string jar) {return (EngineProfileName??"mica-main")+"-jar-"+jar;}

  // Moves a tab into a jar (or back to the profile's own cookies): its page reloads there.
  internal void SetTabJar(PageEntry tab,string jar) {
   if(tab==null)return;if(privateMode){Status("A private window already keeps nothing after it closes.");return;}
   if(jar!=null&&!state.Jars.Any(j=>j.Id==jar))return;if(tab.Jar==jar)return;
   tab.Jar=jar;DisposeView(tab.Id);
   if(state.ActiveId==tab.Id||InSplit(tab.Id))SelectTab(state.ActiveId);
   RenderTabs();Save();var now=JarOf(tab);
   Status(now==null?"Back to this profile's own cookies.":"In the "+now.Name+" jar: its own cookies and sign-ins.");
  }
  internal void NewTabInJar(string jar) {var tab=state.AddTab("mica://home");tab.Jar=privateMode?null:jar;SelectTab(tab.Id);OpenSpotlight(false);Save();}
  // A tab opened from another (a link, a pop-up turned tab) keeps its jar and sits under it in tree tabs.
  internal void OpenFromTab(PageEntry from,string url) {
   if(IsExtensionTabRequest(url)){OpenExtensionTab(url);return;}
   var tab=state.AddTab(url);tab.Jar=from==null?null:from.Jar;tab.Opener=from==null?null:from.Id;
   if(from!=null){tab.Group=from.Pinned?null:from.Group;state.Tabs.Remove(tab);int at=state.Tabs.IndexOf(from);int after=at;while(after+1<state.Tabs.Count&&IsDescendant(state.Tabs[after+1],from.Id))after++;state.Tabs.Insert(at<0?state.Tabs.Count:after+1,tab);state.NormalizeGroups();}
   CloseSpotlight();SelectTab(tab.Id);Save();
  }
  bool IsDescendant(PageEntry tab,string ancestor) {
   for(int guard=0;tab!=null&&tab.Opener!=null&&guard<32;guard++){if(tab.Opener==ancestor)return true;var id=tab.Opener;tab=state.Tabs.FirstOrDefault(t=>t.Id==id);}
   return false;
  }

  MenuItem JarMenu(PageEntry entry) {
   var menu=ContextCommand("Cookie jar","","",null);
   var none=ContextCommand("This profile's own","","",delegate {SetTabJar(entry,null);});none.IsChecked=entry.Jar==null;menu.Items.Add(none);
   foreach(var jar in state.Jars) {
    var j=jar;var item=ContextCommand(j.Name,"","",delegate {SetTabJar(entry,j.Id);});item.IsChecked=entry.Jar==j.Id;
    ((TextBlock)item.Icon).Foreground=new SolidColorBrush(ParseColor(j.Color));menu.Items.Add(item);
   }
   menu.Items.Add(ContextSeparator());
   menu.Items.Add(ContextCommand("New tab in this jar","","",delegate {NewTabInJar(entry.Jar);}));
   menu.Items.Add(ContextCommand("Manage jars…","","",ShowJars));
   menu.IsEnabled=!privateMode;return menu;
  }

  // The jar list: rename, add, and remove (which signs out of everything in it and deletes its data).
  Popup jarsPopup;
  internal void ShowJars() {
   if(privateMode){Status("Private windows don't use cookie jars.");return;}
   CloseMenus();if(jarsPopup==null){jarsPopup=CreatePopup("JarsPopup",320);jarsPopup.Placement=PlacementMode.Center;}
   RenderJars();jarsPopup.PlacementTarget=Find<Grid>("Root");jarsPopup.IsOpen=true;
  }
  string confirmJar;
  void RenderJars() {
   var body=PopupBody(jarsPopup);
   var head=DialogText("Cookie jars",15,"Ink");head.FontWeight=FontWeights.SemiBold;head.Margin=new Thickness(14,12,14,2);body.Children.Add(head);
   var about=DialogText("Each jar keeps its own cookies and sign-ins. Right-click a tab to move it into one.",11,"Muted");about.TextWrapping=TextWrapping.Wrap;about.Margin=new Thickness(14,0,14,8);body.Children.Add(about);
   foreach(var jar in state.Jars.ToList()) {
    var j=jar;int count=Program.Browsers.Where(b=>!b.closing&&b.profile==profile).Sum(b=>b.state.Tabs.Count(t=>t.Jar==j.Id));
    var row=new Grid {Margin=new Thickness(14,3,8,3),Tag="jar:"+j.Id};
    row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(22)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
    row.Children.Add(new Border {Width=10,Height=10,CornerRadius=new CornerRadius(5),Background=new SolidColorBrush(ParseColor(j.Color)),HorizontalAlignment=HorizontalAlignment.Left});
    var name=new TextBox {Text=j.Name,Padding=new Thickness(6,3,6,3),MaxLength=24,Tag="jar-name:"+j.Id};Grid.SetColumn(name,1);row.Children.Add(name);
    name.LostKeyboardFocus+=delegate {string v=name.Text.Trim();if(v.Length>0&&v!=j.Name){j.Name=v;Save();RenderTabs();}};
    var tally=DialogText(count==0?"":count+(count==1?" tab":" tabs"),11,"Muted");tally.VerticalAlignment=VerticalAlignment.Center;tally.Margin=new Thickness(8,0,4,0);Grid.SetColumn(tally,2);row.Children.Add(tally);
    bool asking=confirmJar==j.Id;
    var remove=new Button {Content=asking?"Delete":"",FontFamily=asking?null:new FontFamily("Segoe MDL2 Assets"),Padding=new Thickness(8,3,8,3),ToolTip="Remove the jar and delete its cookies",Tag="jar-remove:"+j.Id};
    if(asking)remove.SetResourceReference(Button.ForegroundProperty,"Accent");else remove.FontFamily=new FontFamily("Segoe MDL2 Assets");
    remove.Click+=async delegate {if(confirmJar!=j.Id){confirmJar=j.Id;RenderJars();return;}confirmJar=null;await RemoveJar(j.Id);RenderJars();};
    Grid.SetColumn(remove,3);row.Children.Add(remove);body.Children.Add(row);
   }
   if(confirmJar!=null){var warn=DialogText("Deleting signs you out of every site in that jar. Click Delete again to go ahead.",11,"Muted");warn.TextWrapping=TextWrapping.Wrap;warn.Margin=new Thickness(14,4,14,4);body.Children.Add(warn);}
   var add=new Button {Content="New jar",Margin=new Thickness(14,8,14,12),HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,5,12,5),Tag="jar-add"};
   add.Click+=delegate {AddJar(null);RenderJars();};body.Children.Add(add);
  }
  internal CookieJar AddJar(string name) {
   var used=state.Jars.Select(j=>j.Color).ToList();
   var jar=new CookieJar {Id=Guid.NewGuid().ToString("N").Substring(0,12),Name=name??"Jar "+(state.Jars.Count+1),Color=BrowserState.GroupColors.FirstOrDefault(c=>!used.Contains(c))??BrowserState.GroupColors[state.Jars.Count%BrowserState.GroupColors.Length]};
   state.Jars.Add(jar);Save();return jar;
  }
  internal async Task RemoveJar(string id) {
   var jar=state.Jars.FirstOrDefault(j=>j.Id==id);if(jar==null)return;
   foreach(var b in Program.Browsers.Where(b=>!b.closing&&b.profile==profile).ToList()) {
    foreach(var tab in b.state.Tabs.Where(t=>t.Jar==id).ToList()){tab.Jar=null;b.DisposeView(tab.Id);}
    b.state.Jars.RemoveAll(j=>j.Id==id);if(b.state.ActiveId!=null)b.SelectTab(b.state.ActiveId);b.RenderTabs();b.Save();
   }
   await DeleteEngineProfile(JarProfileName(id));Status("The "+jar.Name+" jar and its cookies are gone.");
  }
  // The engine deletes a profile's data once nothing uses it; a hidden view asks it to.
  async Task DeleteEngineProfile(string name) {
   var view=new WebView2 {Width=1,Height=1,Visibility=Visibility.Hidden,IsHitTestVisible=false,Focusable=false};Find<Grid>("Root").Children.Add(view);
   try {
    var engine=await EnsureEnvironment();var options=engine.CreateCoreWebView2ControllerOptions();options.ProfileName=name;
    await view.EnsureCoreWebView2Async(engine,options);RegisterOwnTarget(view.CoreWebView2);view.CoreWebView2.Profile.Delete();
   }catch(Exception e){if(smoke)Status("Engine profile: "+e.Message);}
   finally {Find<Grid>("Root").Children.Remove(view);try {view.Dispose();}catch(Exception){}}
  }
 }
}
