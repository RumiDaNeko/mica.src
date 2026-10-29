using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 partial class BrowserApp {
  // Drag-to-arrange and tab groups in the sidebar, driven through the same drop logic the pointer uses.
  async Task RunTabsTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"tabs");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Width=1280;window.Height=900;window.Left=-6000;window.Top=-6000;
   try {
    foreach(var url in new[]{"https://alpha.test/","https://bravo.test/","https://charlie.test/","https://delta.test/","https://echo.test/"}){var t=state.AddTab(url);t.Title=new Uri(url).Host.Split('.')[0];}
    state.Tabs.RemoveAll(t=>t.Url=="mica://home");state.ActiveId=state.Tabs.Last().Id;RenderTabs();await Task.Delay(400);
    // Named by address: unfolding a group warms its tabs, and a loaded page retitles its tab.
    Func<PageEntry,string> nameOf=delegate(PageEntry t) {return new Uri(t.Url).Host.Split('.')[0];};
    Func<string,PageEntry> tab=delegate(string name) {return state.Tabs.First(t=>nameOf(t)==name);};
    Func<string> order=delegate {return string.Join(",",state.Tabs.Where(t=>t.Space==state.Space).OrderByDescending(t=>t.Pinned).Select(t=>(t.Pinned?"*":"")+nameOf(t)+(t.Group!=null?"["+(state.FindGroup(t.Group).Name??"")+"]":"")));};
    Func<string,FrameworkElement> row=delegate(string tag) {return TabListElements().FirstOrDefault(e=>(string)e.Tag==tag)??Find<StackPanel>("TabItems").Children.OfType<FrameworkElement>().FirstOrDefault(e=>(string)e.Tag==tag);};
    Func<FrameworkElement,double> top=delegate(FrameworkElement e) {return e.TranslatePoint(new Point(0,0),DragLayer()).Y;};

    var group=state.CreateGroup(tab("bravo"));Save();RenderTabs();await Task.Delay(300);
    RegressionCheck("a new group shows a header with its tab inside",delegate {Require(row("group:"+group.Id)!=null&&TabListElements().Any(e=>(string)e.Tag=="tab:"+tab("bravo").Id&&IsInside(e,row("group-body:"+group.Id))),order());});
    StartGroupRename(group.Id);await Task.Delay(200);
    var field=Find<StackPanel>("TabItems").Children.OfType<Grid>().FirstOrDefault(g=>(g.Tag as string)=="group-rename:"+group.Id);
    RegressionCheck("a new group asks for its name in place",delegate {Require(field!=null,"no name field");});
    if(field!=null){var box=field.Children.OfType<TextBox>().First();box.Text="Trip";box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(box),0,Key.Enter) {RoutedEvent=UIElement.KeyDownEvent});await Task.Delay(200);}
    RegressionCheck("Enter names the group",delegate {Require(state.FindGroup(group.Id).Name=="Trip",order());});

    // Drag delta into the group: drop just above bravo's middle, inside the group.
    var bravo=row("tab:"+tab("bravo").Id);SimulateTabDrag(tab("delta").Id,null,top(bravo)+3);await Task.Delay(300);
    RegressionCheck("dragging a tab into a group makes it join",delegate {Require(tab("delta").Group==group.Id&&order()=="alpha,delta[Trip],bravo[Trip],charlie,echo",order());});
    // Mid-drag: the dragged row folds away and a gap opens at the drop spot, pushing the rows below it down.
    {var charlie=row("tab:"+tab("charlie").Id);var echoRow=row("tab:"+tab("echo").Id);double echoBefore=top(echoRow);
     var from=echoRow.TranslatePoint(new Point(10,echoRow.ActualHeight/2),DragLayer());tabDrag=new TabDrag {TabId=tab("echo").Id,Source=echoRow,Start=from};
     // The real pointer is ignored for the whole drag, as it is captured while the test waits.
     simulatingDrag=true;TrackTabDrag(true,new Point(from.X,from.Y-8));TrackTabDrag(true,new Point(from.X,top(charlie)+3));
     await Task.Delay(350);double charlieMoved=top(charlie);var gap=tabDrag.Gap;
     CaptureElement(Find<Grid>("Sidebar"),"tabs-dragging.png");
     RegressionCheck("while dragging, a gap opens where the tab will land",delegate {Require(gap!=null&&gap.ActualHeight>20&&gap.Parent==Find<StackPanel>("TabItems"),gap==null?"no gap":gap.ActualHeight.ToString());});
     RegressionCheck("rows below the gap move down to make room",delegate {Require(charlieMoved>top(row("tab:"+tab("bravo").Id))&&echoRow.ActualHeight<1,"charlie at "+charlieMoved+", echo row "+echoRow.ActualHeight);});
     try {TrackTabDrag(false,new Point(from.X,top(charlie)+3));}finally {simulatingDrag=false;}await Task.Delay(300);
     RegressionCheck("letting go drops the tab into the gap",delegate {Require(order()=="alpha,delta[Trip],bravo[Trip],echo,charlie",order());});
     state.MoveTab(tab("echo").Id,tab("charlie").Id,true,false,null);RenderTabs();await Task.Delay(200);}    // Drag alpha up into Pinned.
    var empty=Find<StackPanel>("PinnedTabs").Children.OfType<FrameworkElement>().First();SimulateTabDrag(tab("alpha").Id,null,top(empty)+2);await Task.Delay(300);
    RegressionCheck("dragging a tab into Pinned pins it",delegate {Require(tab("alpha").Pinned&&order().StartsWith("*alpha,"),order());});
    // Drag the whole group below echo.
    var echo=row("tab:"+tab("echo").Id);SimulateTabDrag(null,group.Id,top(echo)+echo.ActualHeight+20);await Task.Delay(120);
    bool sliding=TabListElements().Any(e=>e.RenderTransform is TranslateTransform&&Math.Abs(((TranslateTransform)e.RenderTransform).Y)>0.5);await Task.Delay(300);
    RegressionCheck("dragging a group's header moves the whole group",delegate {Require(order()=="*alpha,charlie,echo,delta[Trip],bravo[Trip]",order());});
    RegressionCheck("rows slide to their new places",delegate {Require(sliding,"no row was moving");});
    // Drag charlie out of nowhere into the middle of the group, then out again below it.
    SimulateTabDrag(tab("charlie").Id,null,top(row("tab:"+tab("bravo").Id))+3);await Task.Delay(300);
    RegressionCheck("a tab dropped between a group's tabs joins at that spot",delegate {Require(order()=="*alpha,echo,delta[Trip],charlie[Trip],bravo[Trip]",order());});
    var bottom=TabListElements().Max(e=>top(e)+e.ActualHeight);SimulateTabDrag(tab("charlie").Id,null,bottom+30);await Task.Delay(300);
    RegressionCheck("a tab dropped below everything leaves its group",delegate {Require(order()=="*alpha,echo,delta[Trip],bravo[Trip],charlie",order());});
    CaptureElement(Find<Grid>("Sidebar"),"tabs-grouped.png");

    // Folding hides the group's tabs; the header keeps a count.
    ToggleGroup(group.Id);await Task.Delay(450);
    RegressionCheck("folding a group hides its tabs",delegate {Require(state.FindGroup(group.Id).Collapsed&&!TabListElements().Any(e=>(string)e.Tag=="tab:"+tab("delta").Id),order());});
    CaptureElement(Find<Grid>("Sidebar"),"tabs-folded.png");
    ToggleGroup(group.Id);await Task.Delay(450);
    RegressionCheck("unfolding shows them again",delegate {Require(!state.FindGroup(group.Id).Collapsed&&TabListElements().Any(e=>(string)e.Tag=="tab:"+tab("delta").Id&&e.IsVisible),order());});

    // Keyboard: Ctrl+Shift+Up moves the focused tab up one place, taking on its new neighbour's group.
    MoveTabByKey(tab("charlie").Id,true);await Task.Delay(300);
    RegressionCheck("Ctrl+Shift+Up moves a tab up a place",delegate {Require(order()=="*alpha,echo,delta[Trip],charlie[Trip],bravo[Trip]",order());});
    var menu=GroupMenu(state.FindGroup(group.Id));
    RegressionCheck("the group menu offers rename, colour, fold, new tab, ungroup and close",delegate {var names=menu.Items.OfType<MenuItem>().Select(m=>(string)m.Header).ToList();foreach(var n in new[]{"Rename group","Colour","Fold group","New tab in group","Ungroup","Close group"})Require(names.Contains(n),string.Join("/",names));});
    state.FindGroup(group.Id).Color=BrowserState.GroupColors[3];RenderTabs();await Task.Delay(200);
    RegressionCheck("a group's colour paints its header dot and side rail",delegate {var rail=((Grid)((Border)row("group-body:"+group.Id)).Child).Children.OfType<Border>().First();Require(((SolidColorBrush)rail.Background).Color==ParseColor(BrowserState.GroupColors[3])&&rail.CornerRadius.TopLeft>0,rail.Background.ToString());});
    Func<string,double> faded=delegate(string name) {var g=(Grid)row("tab:"+tab(name).Id);var content=(Panel)((Button)g.Children[0]).Content;return content.Opacity*content.Children[0].Opacity;};
    RegressionCheck("resting tabs show a dimmer icon and title",delegate {
     Require(faded("alpha")<0.6&&faded(nameOf(state.Tabs.First(t=>t.Id==state.ActiveId)))>0.99,faded("alpha")+"/"+faded(nameOf(state.Tabs.First(t=>t.Id==state.ActiveId))));
     Require(window.FindName("SleepLabel")==null,"resting count still shown");});    CaptureElement(Find<Grid>("Sidebar"),"tabs-resting-light.png");state.Dark=true;ApplyTheme();RenderTabs();await Task.Delay(300);CaptureElement(Find<Grid>("Sidebar"),"tabs-resting-dark.png");
    RegressionCheck("resting tabs fade in dark mode too",delegate {Require(faded("alpha")<0.6,faded("alpha").ToString());});state.Dark=false;ApplyTheme();RenderTabs();
    state.Ungroup(group.Id);Save();RenderTabs();await Task.Delay(200);
    RegressionCheck("ungrouping keeps the tabs where they were",delegate {Require(order()=="*alpha,echo,delta,charlie,bravo"&&state.Groups.Count==0,order());});
    var saved=StateStore.Load(statePath);
    RegressionCheck("the arrangement is saved",delegate {Require(string.Join(",",saved.Tabs.OrderByDescending(t=>t.Pinned).Select(t=>(t.Pinned?"*":"")+nameOf(t)))=="*alpha,echo,delta,charlie,bravo",string.Join(",",saved.Tabs.Select(nameOf)));});
    // Hovering a resting tab starts its page in the background, unseen, so clicking it shows at once.
    {var resting=tab("alpha");string shown=state.ActiveId;WarmSoon(resting.Id,true);await Task.Delay(700);
     WebView2 warm;bool made=views.TryGetValue(resting.Id,out warm);
     RegressionCheck("hovering a resting tab warms it up without showing it",delegate {Require(made&&!warm.IsVisible&&state.ActiveId==shown,"made "+made+(made?", visible "+warm.IsVisible:""));});
     if(views.ContainsKey(tab("bravo").Id))DisposeView(tab("bravo").Id); // Opening the group earlier woke it.
     WarmSoon(tab("bravo").Id,true);WarmSoon(tab("bravo").Id,false);await Task.Delay(400);
     RegressionCheck("passing over a tab doesn't warm it",delegate {Require(!views.ContainsKey(tab("bravo").Id),"bravo warmed");});}
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
