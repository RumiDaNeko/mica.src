using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Mica {
 // Right-click Back (or Forward) to see the pages that button would go through, as in other browsers,
 // and jump straight to one. The list is the tab's own history, from the engine.
 partial class BrowserApp {
  Popup navHistoryPopup;
  const int NavHistoryShown=15;
  void WireNavHistory() {
   foreach(var pair in new[]{Tuple.Create("BackButton",true),Tuple.Create("ForwardButton",false)}) {
    var button=Find<Button>(pair.Item1);bool back=pair.Item2;
    button.MouseRightButtonUp+=delegate(object sender,MouseButtonEventArgs e) {e.Handled=true;ShowNavHistory(button,back);};
   }
  }
  sealed class NavEntry {internal int Id;internal string Url,Title;}
  async void ShowNavHistory(Button anchor,bool back) {
   var core=LiveCore(CurrentView());if(core==null)return;
   List<NavEntry> entries=new List<NavEntry>();int current;
   try {
    var data=new JavaScriptSerializer().DeserializeObject(await core.CallDevToolsProtocolMethodAsync("Page.getNavigationHistory","{}")) as Dictionary<string,object>;
    current=Convert.ToInt32(data["currentIndex"]);
    foreach(var item in ((object[])data["entries"]).OfType<Dictionary<string,object>>())entries.Add(new NavEntry {Id=Convert.ToInt32(item["id"]),Url=item["url"] as string??"",Title=item.ContainsKey("title")?item["title"] as string:null});
   }catch(Exception){return;}
   if(closing||LiveCore(CurrentView())!=core)return;
   var steps=(back?entries.Take(current).Reverse():entries.Skip(current+1)).Where(e=>e.Url!="about:blank").Take(NavHistoryShown).ToList();
   if(steps.Count==0)return;
   if(navHistoryPopup==null){navHistoryPopup=CreatePopup("NavHistoryPopup",320);navHistoryPopup.Placement=PlacementMode.Bottom;navHistoryPopup.HorizontalOffset=0;navHistoryPopup.VerticalOffset=6;}
   CloseMenus();var body=PopupBody(navHistoryPopup);MenuHeading(body,back?"BACK":"FORWARD");
   foreach(var step in steps) {
    var entry=step;string address=entry.Url;
    string title=string.IsNullOrWhiteSpace(entry.Title)||entry.Title==entry.Url?Navigation.AddressLabel(address):entry.Title;
    var row=MenuCommand(body,navHistoryPopup,back?"\uE72B":"\uE72A",title,"",async delegate {
     var live=LiveCore(CurrentView());if(live!=core)return;
     try {await core.CallDevToolsProtocolMethodAsync("Page.navigateToHistoryEntry","{\"entryId\":"+entry.Id+"}");}catch(Exception){}
    });
    row.Tag="nav-history:"+entry.Id;row.ToolTip=Navigation.AddressLabel(address);
   }
   MenuDivider(body);MenuCommand(body,navHistoryPopup,"\uE81C","Show full history","Ctrl+H",delegate {OpenLibraryPage("mica://history");});
   navHistoryPopup.PlacementTarget=anchor;navHistoryPopup.IsOpen=true;
  }
 }
}
