using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Mica {
 // The sidebar's tab list: pinned tabs, then tabs and groups in the order you arranged them.
 // Drag a tab (or a group's header) to move it: into Pinned to pin it, into a group to join it, out to leave.
 // Ctrl+Shift+Up/Down moves the focused tab. Rows that change place slide to where they now sit.
 partial class BrowserApp {
  sealed class TabDrag {public string TabId,GroupId;public FrameworkElement Source,Body;public Point Start;public bool Active,Captured;public Image Ghost;public double Grab,Size;public DropSpot Spot;public Border Gap;public string GapKey;public List<FrameworkElement> Folded=new List<FrameworkElement>();public int SplitSlot=-1;}
  internal sealed class DropSpot {public string Anchor,Group;public bool After,Pinned,Valid;public double LineY,LineX;}
  TabDrag tabDrag;bool tabsDirty,animateTabMoves;Tuple<string,double> dropFrom;Canvas tabDragLayer;System.Windows.Threading.DispatcherTimer dragScroll;

  void RenderTabs() {
   if(tabDrag!=null&&tabDrag.Active){tabsDirty=true;return;}
   var before=animateTabMoves?TabRowPositions():null;animateTabMoves=false;if(before!=null&&dropFrom!=null)before[dropFrom.Item1]=dropFrom.Item2;
   var focused=Keyboard.FocusedElement as Button;string focusId=focused==null?null:focused.Tag as string;Button restore=null;
   var pinned=Find<StackPanel>("PinnedTabs");var normal=Find<StackPanel>("TabItems");var mini=Find<StackPanel>("MiniTabs");pinned.Children.Clear();normal.Children.Clear();mini.Children.Clear();
   state.NormalizeGroups();
   var visible=state.Tabs.Where(t=>t.Space==state.Space).OrderByDescending(t=>t.Pinned).ToList();Find<TextBlock>("SpaceName").Text=state.Space;Find<Button>("MiniSpace").ToolTip=state.Space+" · Choose a space";RollText(Find<TextBlock>("TabCount"),visible.Count.ToString("00"));
   StackPanel members=null;string currentGroup=null;
   foreach(var tab in visible) {
    var entry=tab;var group=tab.Pinned?null:state.FindGroup(tab.Group);
    if(group==null){currentGroup=null;members=null;}
    else if(group.Id!=currentGroup) {
     currentGroup=group.Id;int count=visible.Count(t=>t.Group==group.Id);var header=GroupHeader(group,count);normal.Children.Add(header);
     if(focusId=="group:"+group.Id)restore=header;
     members=new StackPanel {Margin=new Thickness(8,0,0,0)};var lane=new Grid();lane.Children.Add(new Border {Width=3,CornerRadius=new CornerRadius(1.5),Background=new SolidColorBrush(ParseColor(group.Color)),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,3,0,3),Tag="group-rail"});lane.Children.Add(members);
     var body=new Border {Margin=new Thickness(9,0,0,4),Child=lane,Tag="group-body:"+group.Id};
     normal.Children.Add(body);
    }
    bool folded=group!=null&&group.Collapsed&&tab.Id!=state.ActiveId;
    Button button;var grid=CachedTabRow(entry,group,out button);
    if(!folded)(tab.Pinned?pinned:members??normal).Children.Add(grid);
    var small=CachedMiniTab(entry,group);mini.Children.Add(small);
    if(tab.Id==focusId)restore=sidebarHidden?small:button;
    if(renderedTabs.Add(tab.Id)){if(!folded)AnimateArrival(grid);AnimateArrival(small);}
   }
   renderedTabs.RemoveWhere(id=>!state.Tabs.Any(t=>t.Id==id));
   foreach(var gone in rowCache.Keys.Concat(miniCache.Keys).Where(id=>!state.Tabs.Any(t=>t.Id==id)).Distinct().ToList()){rowCache.Remove(gone);miniCache.Remove(gone);}
   if(restore!=null)restore.Focus();
   if(!visible.Any(t=>t.Pinned))pinned.Children.Add(new TextBlock {Text="Drag a tab here, or right-click it, to keep it",FontSize=10,Foreground=SideBrush("Muted"),Margin=new Thickness(10,2,0,1),Tag="pinned-empty",TextWrapping=TextWrapping.Wrap});
   MarkRestingTabs(false);
   if(before!=null)SlideTabRows(before);
  }
  // Rows are kept between renders and reused while nothing they show has changed, so redrawing the list
  // (a tab opened, closed or moved) builds only the rows that differ.
  readonly Dictionary<string,Tuple<string,Grid,Button>> rowCache=new Dictionary<string,Tuple<string,Grid,Button>>();
  readonly Dictionary<string,Tuple<string,Button>> miniCache=new Dictionary<string,Tuple<string,Button>>();
  string RowLook(PageEntry tab,TabGroup group) {
   Microsoft.Web.WebView2.Wpf.WebView2 view;var core=views.TryGetValue(tab.Id,out view)?LiveCore(view):null;bool sound=false,muted=false;
   try {if(core!=null){sound=core.IsDocumentPlayingAudio;muted=core.IsMuted;}}catch(Exception){}
   var jar=JarOf(tab);var key=FaviconCache.GetKey(tab.Url);var icon=key==null?null:Icons.Read(key);
   return string.Join("|",new object[]{tab.Title,tab.Url,tab.Pinned,tab.Id==state.ActiveId,group==null?"":group.Id+group.Color+group.Name,jar==null?"":jar.Id+jar.Color,sound,muted,TreeDepth(tab),icon==null?0:icon.GetHashCode(),state.Dark,state.FrameColor,state.HomeBackground,SidebarIsDark()});
  }
  static void Detach(FrameworkElement element) {var parent=element.Parent as Panel;if(parent!=null)parent.Children.Remove(element);}
  Grid CachedTabRow(PageEntry tab,TabGroup group,out Button button) {
   string look=RowLook(tab,group);Tuple<string,Grid,Button> kept;
   if(rowCache.TryGetValue(tab.Id,out kept)&&kept.Item1==look){Detach(kept.Item2);button=kept.Item3;return kept.Item2;}
   var row=TabRow(tab,out button);rowCache[tab.Id]=Tuple.Create(look,row,button);return row;
  }
  Button CachedMiniTab(PageEntry tab,TabGroup group) {
   string look=RowLook(tab,group);Tuple<string,Button> kept;
   if(miniCache.TryGetValue(tab.Id,out kept)&&kept.Item1==look){Detach(kept.Item2);return kept.Item2;}
   var small=MiniTab(tab,group);miniCache[tab.Id]=Tuple.Create(look,small);return small;
  }
  Button MiniTab(PageEntry entry,TabGroup group) {
   var tab=entry;FrameworkElement icon=CreateTabIcon(tab,18,true);
   if(group!=null){var marked=new Grid();marked.Children.Add(icon);icon=marked;marked.Children.Add(new Border {Height=2,Width=14,CornerRadius=new CornerRadius(1),Background=new SolidColorBrush(ParseColor(group.Color)),VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,-6)});}
   var small=new Button {HorizontalAlignment=HorizontalAlignment.Center,Tag=tab.Id,Content=icon,Width=36,Height=36,Padding=new Thickness(0),Margin=new Thickness(0,3,0,3),ToolTip=tab.Title+"\n"+tab.Url+(group!=null?"\nGroup: "+GroupName(group):""),Background=tab.Id==state.ActiveId?SideBrush("Panel"):Brushes.Transparent};LazyMenu(small,delegate {return TabMenu(entry);});
   System.Windows.Automation.AutomationProperties.SetName(small,tab.Title);small.Click+=delegate {SelectTab(entry.Id);};return small;
  }
  // A page's title, address, icon or sound changed: only its own row is rebuilt. With many tabs a whole-list
  // rebuild takes tens of milliseconds, and pages retitle themselves often (counters, players, chats).
  void RefreshTabRow(string id) {
   var tab=state.Tabs.FirstOrDefault(t=>t.Id==id);if(tab==null)return;
   if(tabDrag!=null&&tabDrag.Active){tabsDirty=true;return;}
   var old=TabListElements().FirstOrDefault(e=>(string)e.Tag=="tab:"+id) as Grid;var panel=old==null?null:old.Parent as Panel;
   var mini=Find<StackPanel>("MiniTabs");var oldSmall=mini.Children.OfType<Button>().FirstOrDefault(b=>(string)b.Tag==id);
   if(panel!=null) {
    bool focused=old.IsKeyboardFocusWithin;int at=panel.Children.IndexOf(old);rowCache.Remove(id);Button button;var row=CachedTabRow(tab,tab.Pinned?null:state.FindGroup(tab.Group),out button);
    panel.Children.RemoveAt(at);panel.Children.Insert(at,row);if(focused)button.Focus();
    if(button.Content is UIElement)FadeResting((UIElement)button.Content,IsResting(tab),false);
   }
   if(oldSmall!=null) {
    bool focused=oldSmall.IsKeyboardFocused;int at=mini.Children.IndexOf(oldSmall);miniCache.Remove(id);var small=CachedMiniTab(tab,tab.Pinned?null:state.FindGroup(tab.Group));
    mini.Children.RemoveAt(at);mini.Children.Insert(at,small);if(focused)small.Focus();
    if(small.Content is UIElement)FadeResting((UIElement)small.Content,IsResting(tab),false);
   }
  }
  // A plain tab switch only moves the highlight. The list is rebuilt when a folded group has to show or
  // hide the tab, mid-drag, or when a row is missing.
  void RefreshActiveTab(string leaving) {
   Func<string,bool> folds=delegate(string id) {var t=state.Tabs.FirstOrDefault(x=>x.Id==id);var g=t==null||t.Pinned?null:state.FindGroup(t.Group);return g!=null&&g.Collapsed;};
   if(tabsDirty||animateTabMoves||tabDrag!=null&&tabDrag.Active||folds(leaving)||folds(state.ActiveId)){RenderTabs();return;}
   var rows=TabListElements().OfType<Grid>().ToList();var minis=Find<StackPanel>("MiniTabs").Children.OfType<Button>().ToList();
   if(!rows.Any(r=>(string)r.Tag=="tab:"+state.ActiveId)||!minis.Any(b=>(string)b.Tag==state.ActiveId)){RenderTabs();return;}
   foreach(var row in rows) {var select=row.Children.Count>0?row.Children[0] as Button:null;if(select==null)continue;string id=select.Tag as string;if(id==state.ActiveId)select.Background=SideBrush("Panel");else if(id==leaving)select.ClearValue(Control.BackgroundProperty);}
   foreach(var small in minis){string id=small.Tag as string;if(id==state.ActiveId)small.Background=SideBrush("Panel");else if(id==leaving)small.Background=Brushes.Transparent;}
   MarkRestingTabs(false);
  }
  // A resting tab (its page unloaded to save memory) shows a dimmer icon and title instead of a count in the footer.
  const double RestingOpacity=0.42,LightRestingOpacity=0.5;
  bool IsResting(PageEntry tab) {return Navigation.IsWeb(tab.Url)&&tab.Id!=state.ActiveId&&!views.ContainsKey(tab.Id);}
  void MarkRestingTabs(bool animate) {
   if(window==null)return;
   foreach(var element in TabListElements()) {
    var tag=(string)element.Tag;if(!tag.StartsWith("tab:"))continue;var tab=state.Tabs.FirstOrDefault(t=>"tab:"+t.Id==tag);var row=element as Grid;
    if(tab==null||row==null||row.Children.Count==0)continue;var select=row.Children[0] as Button;if(select!=null&&select.Content is UIElement)FadeResting((UIElement)select.Content,IsResting(tab),animate);
   }
   foreach(var small in Find<StackPanel>("MiniTabs").Children.OfType<Button>()) {var tab=state.Tabs.FirstOrDefault(t=>t.Id==(small.Tag as string));if(tab!=null&&small.Content is UIElement)FadeResting((UIElement)small.Content,IsResting(tab),animate);}
  }
  // Dark mode fades the whole row; on a light sidebar fading reads as washed out, so the title turns to
  // the muted ink and only the icon dims: a grey tint.
  void FadeResting(UIElement content,bool resting,bool animate) {
   bool light=!state.Dark;var parts=content as Panel;
   Action<UIElement,double> fade=delegate(UIElement target,double to) {if(Math.Abs(target.Opacity-to)<0.01)return;if(animate&&SystemParameters.ClientAreaAnimation)Tween(target,UIElement.OpacityProperty,to,220,null);else {target.BeginAnimation(UIElement.OpacityProperty,null);target.Opacity=to;}};
   fade(content,resting&&!light?RestingOpacity:1);
   if(parts==null){if(light)fade(content,resting?LightRestingOpacity:1);return;}
   foreach(UIElement part in parts.Children) {
    var title=part as TextBlock;
    if(title!=null&&Grid.GetColumn(title)==1)title.SetResourceReference(TextBlock.ForegroundProperty,resting&&light?"Muted":"Ink");
    else fade(part,resting&&light?LightRestingOpacity:1);
   }
  }

  Grid TabRow(PageEntry entry,out Button button) {
   var tab=entry;var grid=new Grid {Margin=new Thickness(0,2,0,2),Tag="tab:"+tab.Id};grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(27)});
   var select=new Button {Tag=tab.Id,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(12,11,32,11),ToolTip=tab.Title+"\n"+tab.Url};
   if(tab.Id==state.ActiveId)select.Background=SideBrush("Panel");
   var content=new Grid();content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(25)});content.ColumnDefinitions.Add(new ColumnDefinition());
   content.Children.Add(CreateTabIcon(tab,16,true));
   var title=new TextBlock {Text=tab.Title,Foreground=SideBrush("Ink"),FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(title,1);content.Children.Add(title);select.Content=content;select.Click+=delegate {SelectTab(entry.Id);};Grid.SetColumnSpan(select,2);grid.Children.Add(select);
   var close=new Button {Content="",FontFamily=new FontFamily("Segoe MDL2 Assets"),Width=24,Height=24,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,FontSize=9,Padding=new Thickness(0),ToolTip="Close tab",Foreground=SideBrush("Muted")};System.Windows.Automation.AutomationProperties.SetName(close,"Close "+tab.Title);Grid.SetColumn(close,1);close.Click+=delegate {CloseTab(entry.Id);};grid.Children.Add(close);
   LazyMenu(select,delegate {return TabMenu(entry);});DecorateTab(entry,grid,content,select);
   select.MouseEnter+=delegate {WarmSoon(entry.Id,true);};select.MouseLeave+=delegate {WarmSoon(entry.Id,false);};
   select.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) {PrepareTabDrag(e,grid,entry.Id,null);};
   select.PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {
    if((Keyboard.Modifiers&(ModifierKeys.Control|ModifierKeys.Shift))!=(ModifierKeys.Control|ModifierKeys.Shift)||e.Key!=Key.Up&&e.Key!=Key.Down)return;
    MoveTabByKey(entry.Id,e.Key==Key.Up);e.Handled=true;
   };
   button=select;return grid;
  }

  // ---- Groups ----
  static readonly string[] GroupColorNames={"Blue","Red","Yellow","Green","Pink","Purple","Cyan","Orange","Grey"};
  static string GroupName(TabGroup group) {return string.IsNullOrWhiteSpace(group.Name)?"Unnamed group":group.Name;}
  Button GroupHeader(TabGroup group,int count) {
   var id=group.Id;var header=new Button {Tag="group:"+id,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(11,7,10,7),Margin=new Thickness(0,5,0,2)};
   var row=new Grid();foreach(var width in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})row.ColumnDefinitions.Add(new ColumnDefinition {Width=width});
   row.Children.Add(new Ellipse {Width=9,Height=9,Fill=new SolidColorBrush(ParseColor(group.Color)),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)});
   var name=new TextBlock {Text=GroupName(group),FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=SideBrush(string.IsNullOrWhiteSpace(group.Name)?"Muted":"Ink"),TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,Tag="group-name"};Grid.SetColumn(name,1);row.Children.Add(name);
   var tally=new TextBlock {Text=group.Collapsed?count.ToString():"",FontSize=10,Foreground=SideBrush("Muted"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,8,0)};Grid.SetColumn(tally,2);row.Children.Add(tally);
   var chevron=new TextBlock {Text="",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=9,Foreground=SideBrush("Muted"),VerticalAlignment=VerticalAlignment.Center,RenderTransformOrigin=new Point(0.5,0.5),RenderTransform=new RotateTransform(group.Collapsed?-90:0)};Grid.SetColumn(chevron,3);row.Children.Add(chevron);
   header.Content=row;header.ToolTip=GroupName(group)+" · "+count+(count==1?" tab":" tabs")+"\nClick to "+(group.Collapsed?"show":"fold")+" · F2 or right-click to rename";
   System.Windows.Automation.AutomationProperties.SetName(header,"Group "+GroupName(group));
   header.Click+=delegate {ToggleGroup(id);};
   header.PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {if(e.Key==Key.F2){StartGroupRename(id);e.Handled=true;}};
   LazyMenu(header,delegate {return GroupMenu(group);});
   header.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) {if(e.ClickCount==1)PrepareTabDrag(e,header,null,id);};
   return header;
  }
  FrameworkElement GroupElement(string tag) {return Find<StackPanel>("TabItems").Children.OfType<FrameworkElement>().FirstOrDefault(e=>(e.Tag as string)==tag);}
  // Folding animates the group's tabs away (or back) before the list settles, so the rows below glide.
  void ToggleGroup(string id) {
   var group=state.FindGroup(id);if(group==null)return;
   var body=GroupElement("group-body:"+id);var header=GroupElement("group:"+id) as Button;
   if(header!=null){var chevron=((Grid)header.Content).Children.OfType<TextBlock>().Last();Tween((RotateTransform)chevron.RenderTransform,RotateTransform.AngleProperty,group.Collapsed?0:-90,180,null);}
   bool animate=SystemParameters.ClientAreaAnimation&&body!=null;
   if(!group.Collapsed) {
    if(!animate){group.Collapsed=true;RestGroup(id);Save();RenderTabs();return;}
    body.Height=body.ActualHeight;body.ClipToBounds=true;
    var fold=new DoubleAnimation(0,TimeSpan.FromMilliseconds(190)) {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseInOut}};
    fold.Completed+=delegate {group.Collapsed=true;RestGroup(id);Save();RenderTabs();};
    body.BeginAnimation(FrameworkElement.HeightProperty,fold);return;
   }
   group.Collapsed=false;Save();RenderTabs();WakeGroup(id);
   var opened=GroupElement("group-body:"+id);if(opened==null||!SystemParameters.ClientAreaAnimation)return;
   opened.Measure(new Size(opened.Parent is FrameworkElement?((FrameworkElement)opened.Parent).ActualWidth:200,double.PositiveInfinity));double full=opened.DesiredSize.Height;
   opened.ClipToBounds=true;var unfold=new DoubleAnimation(0,full,TimeSpan.FromMilliseconds(210)) {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};
   opened.BeginAnimation(FrameworkElement.HeightProperty,unfold);
  }
  void StartGroupRename(string id) {
   window.Dispatcher.BeginInvoke(new Action(delegate {
    var group=state.FindGroup(id);var header=GroupElement("group:"+id) as Button;if(group==null||header==null)return;
    // The name field stands in the header's place (not inside it), so typing never presses the header.
    var panel=(Panel)header.Parent;int at=panel.Children.IndexOf(header);
    var field=new Grid {Margin=header.Margin,Tag="group-rename:"+id};field.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});field.ColumnDefinitions.Add(new ColumnDefinition());
    field.Children.Add(new Ellipse {Width=9,Height=9,Fill=new SolidColorBrush(ParseColor(group.Color)),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(11,0,10,0)});
    var box=new TextBox {Text=group.Name??"",FontSize=11,FontWeight=FontWeights.SemiBold,Padding=new Thickness(2,5,2,5),Margin=new Thickness(0,0,10,0),BorderThickness=new Thickness(0,0,0,1),Background=Brushes.Transparent,Foreground=SideBrush("Ink"),CaretBrush=SideBrush("Ink"),VerticalAlignment=VerticalAlignment.Center,MaxLength=40};
    box.BorderBrush=SideBrush("Accent");System.Windows.Automation.AutomationProperties.SetName(box,"Group name");Grid.SetColumn(box,1);field.Children.Add(box);
    header.Visibility=Visibility.Collapsed;panel.Children.Insert(at,field);
    bool finished=false;
    Action<bool> finish=delegate(bool keep) {if(finished)return;finished=true;if(keep){group.Name=box.Text.Trim();Save();}RenderTabs();};
    box.KeyDown+=delegate(object sender,KeyEventArgs e) {if(e.Key==Key.Enter){finish(true);e.Handled=true;}else if(e.Key==Key.Escape){finish(false);e.Handled=true;}};
    box.LostKeyboardFocus+=delegate {finish(true);};
    box.Focus();box.SelectAll();
   }),System.Windows.Threading.DispatcherPriority.Loaded);
  }
  ContextMenu GroupMenu(TabGroup group) {
   var id=group.Id;var menu=CreateContextMenu();
   menu.Items.Add(ContextCommand("Rename group","","",delegate {StartGroupRename(id);}));
   var colours=ContextCommand("Colour","","",null);
   for(int i=0;i<BrowserState.GroupColors.Length;i++) {
    string colour=BrowserState.GroupColors[i];var swatch=new Ellipse {Width=11,Height=11,Fill=new SolidColorBrush(ParseColor(colour))};
    var item=new MenuItem {Header=GroupColorNames[i],Icon=swatch,IsChecked=group.Color==colour};item.Click+=delegate(object sender,RoutedEventArgs e) {e.Handled=true;CloseContextMenu();var g=state.FindGroup(id);if(g!=null){g.Color=colour;Save();RenderTabs();}};
    colours.Items.Add(item);
   }
   menu.Items.Add(colours);
   menu.Items.Add(ContextCommand(group.Collapsed?"Show group":"Fold group",group.Collapsed?"":"","",delegate {ToggleGroup(id);}));
   menu.Items.Add(ContextCommand("New tab in group","","",delegate {NewTabInGroup(id);}));
   menu.Items.Add(ContextSeparator());
   menu.Items.Add(ContextCommand("Ungroup","","",delegate {animateTabMoves=true;state.Ungroup(id);Save();RenderTabs();}));
   menu.Items.Add(ContextCommand("Close group","","",delegate {foreach(var t in state.Tabs.Where(t=>t.Group==id).ToList())CloseTab(t.Id);}));
   return menu;
  }
  void NewTabInGroup(string id) {
   var last=state.Tabs.LastOrDefault(t=>t.Group==id);if(last==null)return;
   var group=state.FindGroup(id);if(group!=null)group.Collapsed=false;
   Navigate("mica://home",true);var created=Active;if(created==null||created.Id==last.Id)return;
   state.MoveTab(created.Id,last.Id,true,false,id);Save();RenderTabs();
  }
  // Group entries for a tab's own right-click menu.
  void AddGroupCommands(ContextMenu menu,PageEntry entry) {
   if(entry.Pinned)return;
   menu.Items.Add(ContextCommand("Add to new group","","",delegate {
    var group=state.CreateGroup(entry);animateTabMoves=true;Save();RenderTabs();StartGroupRename(group.Id);
   }));
   var others=state.Groups.Where(g=>g.Id!=entry.Group).ToList();
   if(others.Count>0) {
    var join=ContextCommand("Add to group","","",null);
    foreach(var group in others) {
     var target=group;var item=new MenuItem {Header=GroupName(target),Icon=new Ellipse {Width=11,Height=11,Fill=new SolidColorBrush(ParseColor(target.Color))}};
     item.Click+=delegate(object sender,RoutedEventArgs e) {e.Handled=true;CloseContextMenu();var last=state.Tabs.LastOrDefault(t=>t.Group==target.Id);if(last==null)return;animateTabMoves=true;state.MoveTab(entry.Id,last.Id,true,false,target.Id);Save();RenderTabs();};
     join.Items.Add(item);
    }
    menu.Items.Add(join);
   }
   // Leaving a group puts the tab just below it.
   if(entry.Group!=null)menu.Items.Add(ContextCommand("Remove from group","","",delegate {
    var last=state.Tabs.LastOrDefault(t=>t.Group==entry.Group&&t.Id!=entry.Id);animateTabMoves=true;
    if(last==null)state.Ungroup(entry.Group);else state.MoveTab(entry.Id,last.Id,true,false,null);
    Save();RenderTabs();
   }));
  }

  // ---- Moving ----
  void MoveTabByKey(string id,bool up) {
   var order=state.Tabs.Where(t=>t.Space==state.Space).OrderByDescending(t=>t.Pinned).ToList();int at=order.FindIndex(t=>t.Id==id);if(at<0)return;
   int next=up?at-1:at+1;if(next<0||next>=order.Count)return;var neighbour=order[next];
   animateTabMoves=true;state.MoveTab(id,neighbour.Id,!up,neighbour.Pinned,neighbour.Pinned?null:neighbour.Group);Save();RenderTabs();
  }
  Dictionary<string,double> TabRowPositions() {
   var sidebar=Find<Grid>("Sidebar");var positions=new Dictionary<string,double>();
   foreach(var element in TabListElements())try {positions[(string)element.Tag]=element.TranslatePoint(new Point(0,0),sidebar).Y;}catch(InvalidOperationException){}
   return positions;
  }
  IEnumerable<FrameworkElement> TabListElements() {
   foreach(var panel in new[]{Find<StackPanel>("PinnedTabs"),Find<StackPanel>("TabItems")})
    foreach(var child in panel.Children.OfType<FrameworkElement>()) {
     var tag=child.Tag as string;if(tag==null)continue;
     if(tag.StartsWith("group-body:")){foreach(var member in ((Grid)((Border)child).Child).Children.OfType<StackPanel>().First().Children.OfType<FrameworkElement>())if(member.Tag is string)yield return member;continue;}
     yield return child;
    }
  }
  void SlideTabRows(Dictionary<string,double> before) {
   window.Dispatcher.BeginInvoke(new Action(delegate {
    if(!SystemParameters.ClientAreaAnimation)return;var sidebar=Find<Grid>("Sidebar");
    foreach(var element in TabListElements()) {
     double old;if(!before.TryGetValue((string)element.Tag,out old))continue;double now;try {now=element.TranslatePoint(new Point(0,0),sidebar).Y;}catch(InvalidOperationException){continue;}
     if(Math.Abs(old-now)<0.5)continue;var shift=new TranslateTransform(0,old-now);element.RenderTransform=shift;
     shift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(old-now,0,TimeSpan.FromMilliseconds(230)) {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut}});
    }
   }),System.Windows.Threading.DispatcherPriority.Loaded);
  }

  // ---- Dragging ----
  Canvas DragLayer() {
   if(tabDragLayer!=null)return tabDragLayer;
   var sidebar=Find<Grid>("Sidebar");tabDragLayer=new Canvas {IsHitTestVisible=false};Grid.SetRowSpan(tabDragLayer,5);Panel.SetZIndex(tabDragLayer,100);sidebar.Children.Add(tabDragLayer);
   sidebar.PreviewMouseMove+=delegate(object sender,MouseEventArgs e) {if(simulatingDrag)return;TrackTabDrag(e.LeftButton==MouseButtonState.Pressed,e.GetPosition(tabDragLayer));};
   sidebar.PreviewMouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e) {if(tabDrag==null)return;if(tabDrag.Active){e.Handled=true;EndTabDrag(true);}else tabDrag=null;};
   sidebar.LostMouseCapture+=delegate {if(tabDrag!=null&&tabDrag.Active&&tabDrag.Captured&&(Mouse.Captured==null||!IsInside(Mouse.Captured as DependencyObject,sidebar)))EndTabDrag(false);};
   sidebar.PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {if(e.Key==Key.Escape&&tabDrag!=null&&tabDrag.Active){EndTabDrag(false);e.Handled=true;}};
   return tabDragLayer;
  }
  void PrepareTabDrag(MouseButtonEventArgs e,FrameworkElement source,string tabId,string groupId) {
   if(sidebarHidden)return;tabDrag=new TabDrag {TabId=tabId,GroupId=groupId,Source=source,Start=e.GetPosition(DragLayer())};
  }
  internal void TrackTabDrag(bool pressed,Point at) {
   if(tabDrag==null)return;if(!pressed){if(tabDrag.Active)EndTabDrag(true);else tabDrag=null;return;}
   if(!tabDrag.Active) {
    if(Math.Abs(at.Y-tabDrag.Start.Y)<6&&Math.Abs(at.X-tabDrag.Start.X)<6)return;
    BeginTabDrag();
   }
   MoveTabDrag(at);
  }
  void BeginTabDrag() {
   var layer=DragLayer();var source=tabDrag.Source;tabDrag.Active=true;CloseContextMenu();
   if(tabDrag.GroupId!=null)tabDrag.Body=GroupElement("group-body:"+tabDrag.GroupId);
   Point top;try {top=source.TranslatePoint(new Point(0,0),layer);}catch(InvalidOperationException){tabDrag=null;return;}
   int w=Math.Max(1,(int)Math.Ceiling(source.ActualWidth)),h=Math.Max(1,(int)Math.Ceiling(source.ActualHeight));
   var dpi=VisualTreeHelper.GetDpi(source);var picture=new RenderTargetBitmap((int)(w*dpi.DpiScaleX),(int)(h*dpi.DpiScaleY),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);picture.Render(source);picture.Freeze();
   tabDrag.Ghost=new Image {Source=picture,Width=w,Height=h,Opacity=0.94,RenderTransformOrigin=new Point(0.5,0.5),RenderTransform=new ScaleTransform(1,1)};
   Canvas.SetLeft(tabDrag.Ghost,top.X);Canvas.SetTop(tabDrag.Ghost,top.Y);layer.Children.Add(tabDrag.Ghost);tabDrag.Grab=tabDrag.Start.Y-top.Y;
   Tween((ScaleTransform)tabDrag.Ghost.RenderTransform,ScaleTransform.ScaleXProperty,1.03,140,null);Tween((ScaleTransform)tabDrag.Ghost.RenderTransform,ScaleTransform.ScaleYProperty,1.03,140,null);
   // The tab (or group) leaves the list and a gap opens where it would land, so the other rows move out of the way.
   tabDrag.Folded.Add(source);if(tabDrag.Body!=null)tabDrag.Folded.Add(tabDrag.Body);
   foreach(var part in tabDrag.Folded)tabDrag.Size+=part.ActualHeight+part.Margin.Top+part.Margin.Bottom;
   foreach(var part in tabDrag.Folded)FoldDragged(part);
   tabDrag.Captured=Mouse.Capture(Find<Grid>("Sidebar"),CaptureMode.SubTree);
   if(dragScroll==null){dragScroll=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(30)};dragScroll.Tick+=delegate {DragAutoScroll();};}
   dragScroll.Start();
  }
  void DragAutoScroll() {
   if(tabDrag==null||!tabDrag.Active){dragScroll.Stop();return;}if(simulatingDrag)return;
   var scroller=Find<ScrollViewer>("ExpandedTabScroller");var at=Mouse.GetPosition(scroller);
   double step=at.Y<28?-Math.Min(14,(28-at.Y)/2+4):at.Y>scroller.ActualHeight-28?Math.Min(14,(at.Y-scroller.ActualHeight+28)/2+4):0;
   if(step!=0){scroller.ScrollToVerticalOffset(scroller.VerticalOffset+step);MoveTabDrag(Mouse.GetPosition(DragLayer()));}
  }
  void MoveTabDrag(Point at) {
   if(tabDrag==null||!tabDrag.Active)return;var layer=DragLayer();
   Canvas.SetTop(tabDrag.Ghost,at.Y-tabDrag.Grab);
   // Past the sidebar and over the page, the tab is headed for split view instead of a new place in the list.
   int slot=-1;
   if(tabDrag.TabId!=null&&at.X>Find<Grid>("Sidebar").ActualWidth)try {slot=SplitSlotAt(layer.TranslatePoint(at,Find<Grid>("WebHost")),tabDrag.TabId);}catch(InvalidOperationException){}
   tabDrag.SplitSlot=slot;ShowSplitPreview(tabDrag.TabId,slot);tabDrag.Ghost.Opacity=slot>=0?0:0.94;
   var spot=slot>=0||at.X>Find<Grid>("Sidebar").ActualWidth?new DropSpot {Valid=false}:DropAt(at.Y);tabDrag.Spot=spot;ShowGap(spot);
  }
  static void FoldDragged(FrameworkElement part) {
   double full=part.ActualHeight+part.Margin.Top+part.Margin.Bottom;part.Opacity=0;part.IsHitTestVisible=false;part.ClipToBounds=true;part.Margin=new Thickness(part.Margin.Left,0,part.Margin.Right,0);part.Height=full;
   if(SystemParameters.ClientAreaAnimation)Tween(part,FrameworkElement.HeightProperty,0,170,null);else part.Height=0;
  }
  // Opens an empty slot of the dragged item's height at the drop spot; the previous slot closes as it does.
  void ShowGap(DropSpot spot) {
   string key=spot.Valid?spot.Anchor+"|"+spot.After+"|"+spot.Pinned+"|"+spot.Group:"none";if(key==tabDrag.GapKey)return;tabDrag.GapKey=key;
   if(tabDrag.Gap!=null){var old=tabDrag.Gap;tabDrag.Gap=null;CloseGap(old);}
   if(!spot.Valid)return;
   StackPanel panel=Find<StackPanel>("TabItems");int index=panel.Children.Count;
   var anchor=spot.Anchor==null?null:state.Tabs.FirstOrDefault(t=>t.Id==spot.Anchor);
   if(anchor==null){if(spot.Pinned){panel=Find<StackPanel>("PinnedTabs");index=panel.Children.Count;}}
   else if(!anchor.Pinned&&anchor.Group!=null&&spot.Group!=anchor.Group) {
    // Before or after a whole group: the slot sits outside it, next to the header or the group's tabs.
    var header=GroupElement("group:"+anchor.Group);var body=GroupElement("group-body:"+anchor.Group);var edge=spot.After?(body??header):header;
    if(edge!=null)index=panel.Children.IndexOf(edge)+(spot.After?1:0);
   }
   else {
    var row=TabListElements().FirstOrDefault(e=>(string)e.Tag=="tab:"+anchor.Id);var parent=row==null?null:row.Parent as StackPanel;
    if(parent!=null){panel=parent;index=parent.Children.IndexOf(row)+(spot.After?1:0);}
   }
   var gap=new Border {Height=0,IsHitTestVisible=false};panel.Children.Insert(Math.Max(0,Math.Min(index,panel.Children.Count)),gap);tabDrag.Gap=gap;
   if(SystemParameters.ClientAreaAnimation)Tween(gap,FrameworkElement.HeightProperty,tabDrag.Size,170,null);else gap.Height=tabDrag.Size;
  }
  static void CloseGap(Border gap) {
   Action remove=delegate {var parent=gap.Parent as Panel;if(parent!=null)parent.Children.Remove(gap);};
   if(SystemParameters.ClientAreaAnimation)Tween(gap,FrameworkElement.HeightProperty,0,150,remove);else remove();
  }
  // Where a drop at this height lands: before the row under the pointer's upper half, or after the last row.
  internal DropSpot DropAt(double y) {
   var layer=DragLayer();var spot=new DropSpot {Valid=true};
   var rows=new List<Tuple<FrameworkElement,Rect>>();
   foreach(var element in TabListElements().Concat(Find<StackPanel>("PinnedTabs").Children.OfType<FrameworkElement>().Where(e=>(e.Tag as string)=="pinned-empty"))) {
    if(!element.IsVisible)continue;if(tabDrag!=null&&(tabDrag.Folded.Contains(element)||tabDrag.Body!=null&&IsInside(element,tabDrag.Body)))continue;
    try {var at=element.TranslatePoint(new Point(0,0),layer);rows.Add(Tuple.Create(element,new Rect(at,new Size(element.ActualWidth,element.ActualHeight))));}catch(InvalidOperationException){}
   }
   rows.Sort((a,b)=>a.Item2.Top.CompareTo(b.Item2.Top));
   var target=rows.FirstOrDefault(r=>y<r.Item2.Top+r.Item2.Height/2);
   if(target==null) {
    var last=state.Tabs.LastOrDefault(t=>t.Space==state.Space&&!t.Pinned&&(tabDrag==null||t.Id!=tabDrag.TabId)&&(tabDrag==null||tabDrag.GroupId==null||t.Group!=tabDrag.GroupId));
    var bottom=rows.Count==0?0:rows.Max(r=>r.Item2.Bottom);
    spot.Anchor=last==null?null:last.Id;spot.After=true;spot.Pinned=false;spot.Group=null;spot.LineY=bottom+2;spot.LineX=8;
    return spot;
   }
   string tag=(string)target.Item1.Tag;spot.LineY=target.Item2.Top-1;spot.LineX=target.Item2.Left+4;
   if(tag=="pinned-empty"){spot.Pinned=true;spot.Anchor=null;spot.LineY=target.Item2.Top+target.Item2.Height/2;}
   else if(tag.StartsWith("group:")) {
    string gid=tag.Substring(6);var first=state.Tabs.FirstOrDefault(t=>t.Group==gid);spot.Anchor=first==null?null:first.Id;spot.After=false;spot.Pinned=false;spot.Group=null;spot.LineX=8;
   }
   else {
    var tab=state.Tabs.FirstOrDefault(t=>"tab:"+t.Id==tag);if(tab==null){spot.Valid=false;return spot;}
    spot.Anchor=tab.Id;spot.After=false;spot.Pinned=tab.Pinned;spot.Group=tab.Pinned?null:tab.Group;
   }
   // A whole group can't go into Pinned or into another group: it lands at that group's edge instead.
   if(tabDrag!=null&&tabDrag.GroupId!=null) {
    if(spot.Pinned){spot.Valid=false;return spot;}
    if(spot.Group!=null&&spot.Group!=tabDrag.GroupId){var first=state.Tabs.First(t=>t.Group==spot.Group);spot.Anchor=first.Id;spot.After=false;var head=GroupElement("group:"+spot.Group);if(head!=null)try {spot.LineY=head.TranslatePoint(new Point(0,0),layer).Y-1;}catch(InvalidOperationException){}spot.LineX=8;}
    spot.Group=null;
   }
   return spot;
  }
  static bool IsInside(DependencyObject element,DependencyObject container) {for(var node=element;node!=null;node=TreeParent(node))if(node==container)return true;return false;}
  // One step up from anything a click can land on. Text pieces (a Run) aren't visuals, and asking the visual
  // tree for their parent throws, which crashed Mica when a click landed on a bubble's text.
  internal static DependencyObject TreeParent(DependencyObject node) {
   if(node is Visual||node is System.Windows.Media.Media3D.Visual3D)return VisualTreeHelper.GetParent(node)??LogicalTreeHelper.GetParent(node);
   var content=node as ContentElement;if(content==null)return LogicalTreeHelper.GetParent(node);
   var framework=content as FrameworkContentElement;return (framework!=null?framework.Parent:null)??ContentOperations.GetParent(content);
  }
  void EndTabDrag(bool drop) {
   var drag=tabDrag;if(drag==null)return;tabDrag=null;if(dragScroll!=null)dragScroll.Stop();
   rowCache.Clear(); // Dragging folded and resized rows directly; the list after it is built fresh.
   if(Mouse.Captured==Find<Grid>("Sidebar"))Mouse.Capture(null);
   var layer=DragLayer();
   // The rebuilt list slides into place, with the dropped row gliding from where the ghost was let go.
   if(drag.Ghost!=null){try {dropFrom=Tuple.Create((string)drag.Source.Tag,layer.TranslatePoint(new Point(0,Canvas.GetTop(drag.Ghost)),Find<Grid>("Sidebar")).Y);}catch(InvalidOperationException){}layer.Children.Remove(drag.Ghost);}
   var spot=drag.Spot;ShowSplitPreview(drag.TabId,-1);
   if(drop&&drag.SplitSlot>=0){string dropped=drag.TabId;int slot=drag.SplitSlot;window.Dispatcher.BeginInvoke(new Action(delegate {DropIntoSplit(dropped,slot);}));}
   else if(drop&&spot!=null&&spot.Valid&&spot.Anchor!=drag.TabId) {
    if(drag.GroupId!=null)state.MoveGroup(drag.GroupId,spot.Anchor,spot.After);else state.MoveTab(drag.TabId,spot.Anchor,spot.After,spot.Pinned,spot.Group);
    Save();
   }
   if(drag.Active){animateTabMoves=true;tabsDirty=false;RenderTabs();}else if(tabsDirty){tabsDirty=false;RenderTabs();}
   dropFrom=null;
  }
  // For tests: drag a tab from its row out over the page, to a point given as a share of the page's width.
  internal void SimulateTabDragToPage(string tabId,double across,bool release=true) {
   var source=TabListElements().First(e=>(string)e.Tag=="tab:"+tabId);var layer=DragLayer();var host=Find<Grid>("WebHost");
   var from=source.TranslatePoint(new Point(10,source.ActualHeight/2),layer);Point to;
   tabDrag=new TabDrag {TabId=tabId,Source=source,Start=from};
   simulatingDrag=true;try {TrackTabDrag(true,new Point(from.X+8,from.Y));to=host.TranslatePoint(new Point(host.ActualWidth*across,host.ActualHeight/2),layer);TrackTabDrag(true,to);if(release)TrackTabDrag(false,to);}finally {simulatingDrag=false;}
  }
  // For tests: drag a tab (or group) from its row to a height in the sidebar, as the pointer would.
  bool simulatingDrag;
  internal void SimulateTabDrag(string tabId,string groupId,double toY) {
   var source=tabId!=null?TabListElements().First(e=>(string)e.Tag=="tab:"+tabId):GroupElement("group:"+groupId);
   var layer=DragLayer();var from=source.TranslatePoint(new Point(10,source.ActualHeight/2),layer);
   tabDrag=new TabDrag {TabId=tabId,GroupId=groupId,Source=source,Start=from};
   // Capturing the mouse raises a synthetic move with no button down, which would end the drag early.
   simulatingDrag=true;try {TrackTabDrag(true,new Point(from.X,from.Y+(toY>from.Y?8:-8)));TrackTabDrag(true,new Point(from.X,toY));TrackTabDrag(false,new Point(from.X,toY));}finally {simulatingDrag=false;}
  }
 }
}
