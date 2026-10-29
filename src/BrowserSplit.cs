using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // Split view: two to five tabs side by side in one window, with dividers you can drag. Whichever pane you
 // click into is the active tab (address bar, shortcuts); opening another tab replaces that pane. All panes
 // stay awake: none freezes or rests while shown. Ends when only one pane is left or you open a non-web page.
 partial class BrowserApp {
  const int MaxPanes=5;
  readonly List<string> splitPanes=new List<string>();readonly List<GridSplitter> splitDividers=new List<GridSplitter>();readonly List<double> splitShares=new List<double>();
  internal bool SplitOpen {get {return splitPanes.Count>=2;}}
  internal bool InSplit(string id) {return SplitOpen&&id!=null&&splitPanes.Contains(id);}
  string SplitPartner(string id) {return InSplit(id)?splitPanes.FirstOrDefault(p=>p!=id):null;}

  // Starts a split of the active tab and another (the one you used most recently, if none is named).
  internal void OpenSplit(string otherId=null) {
   var active=Active;if(active==null||!Navigation.IsWeb(active.Url)){Status("Open a website first, then split it with another tab.");return;}
   if(SplitOpen){AddToSplit(otherId);return;}
   var other=PickSplitTab(otherId,new[]{active.Id});if(other==null)return;
   splitPanes.Clear();splitPanes.Add(active.Id);splitPanes.Add(other.Id);EvenShares();LayoutSplit();
   EnsureSplitView(other);RenderTabs();RefreshSplitHint();
   Status("Split view. Click a side to use it; drag a divider to resize. Ctrl+Shift+S ends it.");
  }
  // A third, fourth or fifth pane, to the right.
  internal void AddToSplit(string otherId) {
   if(!SplitOpen){OpenSplit(otherId);return;}
   if(splitPanes.Count>=MaxPanes){Status("Split view holds up to "+MaxPanes+" tabs.");return;}
   var other=PickSplitTab(otherId,splitPanes);if(other==null)return;
   splitPanes.Add(other.Id);EvenShares();LayoutSplit();EnsureSplitView(other);RenderTabs();RefreshSplitHint();
  }
  PageEntry PickSplitTab(string otherId,IEnumerable<string> taken) {
   var used=new HashSet<string>(taken);
   var other=otherId!=null?state.Tabs.FirstOrDefault(t=>t.Id==otherId):state.Tabs.Where(t=>!used.Contains(t.Id)&&t.Space==state.Space&&Navigation.IsWeb(t.Url)).OrderByDescending(t=>t.LastUsed).FirstOrDefault();
   if(other==null||used.Contains(other.Id)){Status("Open another website tab to split with.");return null;}
   if(!Navigation.IsWeb(other.Url)){Status("Only website tabs can go side by side.");return null;}
   if(other.Space!=state.Space){other.Space=state.Space;state.NormalizeGroups();}
   return other;
  }
  // Takes a tab out of the split; with one left, the split ends.
  internal void RemoveFromSplit(string id) {
   if(!InSplit(id))return;int at=splitPanes.IndexOf(id);splitPanes.RemoveAt(at);if(at<splitShares.Count)splitShares.RemoveAt(at);
   if(splitPanes.Count<2){CloseSplit();return;}
   if(state.ActiveId==id)SelectTab(splitPanes[Math.Min(at,splitPanes.Count-1)]);else PlaceViews(state.ActiveId);
   NormalizeShares();LayoutSplit();RenderTabs();RefreshSplitHint();
  }
  internal void CloseSplit() {
   if(splitPanes.Count==0)return;string keep=state.ActiveId;splitPanes.Clear();splitShares.Clear();LayoutSplit();
   foreach(var pair in views)pair.Value.Visibility=pair.Key==keep?Visibility.Visible:Visibility.Collapsed;
   RenderTabs();RefreshSplitHint();BalanceMemory(false);
  }
  internal void ToggleSplit() {if(SplitOpen)CloseSplit();else OpenSplit();}
  void EvenShares() {splitShares.Clear();for(int i=0;i<splitPanes.Count;i++)splitShares.Add(1.0/splitPanes.Count);}
  void NormalizeShares() {while(splitShares.Count<splitPanes.Count)splitShares.Add(1.0/splitPanes.Count);double sum=splitShares.Sum();for(int i=0;i<splitShares.Count;i++)splitShares[i]=sum>0?splitShares[i]/sum:1.0/splitShares.Count;}

  // From Activate: shows the active tab, and the rest of the split beside it.
  void PlaceViews(string id,string leaving=null) {
   if(SplitOpen&&!InSplit(id)) {
    var tab=state.Tabs.FirstOrDefault(t=>t.Id==id);
    // Another website replaces the pane you were on; anything else ends the split.
    string was=InSplit(leaving)?leaving:InSplit(state.ActiveId)?state.ActiveId:null;
    if(tab!=null&&Navigation.IsWeb(tab.Url)&&was!=null)splitPanes[splitPanes.IndexOf(was)]=id;
    else {splitPanes.Clear();splitShares.Clear();RefreshSplitHint();}
   }
   var shown=SplitOpen?new HashSet<string>(splitPanes):new HashSet<string>{id};
   foreach(var pair in views)if(shown.Contains(pair.Key))ShowView(pair.Value);else SetViewAside(pair.Value);
   LayoutSplit();
   if(SplitOpen)foreach(var pane in splitPanes.Where(p=>p!=id).ToList()){var other=state.Tabs.FirstOrDefault(t=>t.Id==pane);if(other!=null)EnsureSplitView(other);}
  }
  // Hiding a page tells the engine at once and waits for it (25-45 ms on the UI thread), right when the new
  // tab should appear. So the page you leave first slides far off to the side (one cheap window move) and is
  // really hidden a moment later, when nothing is moving; hidden, it can be frozen to save memory.
  readonly HashSet<WebView2> asideViews=new HashSet<WebView2>();System.Windows.Threading.DispatcherTimer asideTimer;
  internal const int AsideHideMs=400;
  static readonly TranslateTransform OffToTheSide=CreateOffToTheSide();
  static TranslateTransform CreateOffToTheSide() {var shift=new TranslateTransform(-40000,0);shift.Freeze();return shift;}
  void ShowView(WebView2 view) {
   asideViews.Remove(view);if(view.RenderTransform==OffToTheSide)view.RenderTransform=Transform.Identity;
   view.Visibility=Visibility.Visible;
  }
  void SetViewAside(WebView2 view) {
   if(view.Visibility!=Visibility.Visible||asideViews.Contains(view))return;
   if(!SystemParameters.ClientAreaAnimation||closing){view.Visibility=Visibility.Collapsed;return;}
   view.RenderTransform=OffToTheSide;asideViews.Add(view);
   if(asideTimer==null) {
    asideTimer=new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) {Interval=TimeSpan.FromMilliseconds(AsideHideMs)};
    asideTimer.Tick+=delegate {asideTimer.Stop();HideAsideViews();};
   }
   asideTimer.Stop();asideTimer.Start();
  }
  // Moving keyboard focus into a page also waits on the engine (10-40 ms), so it happens after the new tab
  // is on screen, if that page is still the one shown.
  void FocusPageSoon(WebView2 view) {
   window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(delegate {
    if(!closing&&CurrentView()==view&&!IsSpotlight&&view.CoreWebView2!=null&&!view.IsKeyboardFocusWithin)view.Focus();
   }));
  }
  void HideAsideViews() {
   foreach(var view in asideViews.ToList()){if(!views.ContainsValue(view))continue;view.Visibility=Visibility.Collapsed;view.RenderTransform=Transform.Identity;}
   asideViews.Clear();BalanceMemory(false); // Hidden now, so the page you left can freeze.
  }
  // A pane may be resting: bring its page up, shown in its place.
  async void EnsureSplitView(PageEntry tab) {
   WebView2 view;
   if(!views.TryGetValue(tab.Id,out view)){await CreateTabView(tab,true);if(!views.TryGetValue(tab.Id,out view))return;}
   if(!InSplit(tab.Id))return;view.Visibility=Visibility.Visible;LayoutSplit();
   var core=LiveCore(view);if(core!=null&&core.IsSuspended)try {core.Resume();}catch(Exception){}
  }
  void LayoutSplit() {
   var host=Find<Grid>("WebHost");var columns=host.ColumnDefinitions;
   bool split=SplitOpen&&!fullscreenApplied;
   foreach(var divider in splitDividers)host.Children.Remove(divider);
   if(split) {
    NormalizeShares();int wanted=splitPanes.Count*2-1;
    if(columns.Count!=wanted){columns.Clear();for(int i=0;i<wanted;i++)columns.Add(i%2==0?new ColumnDefinition {MinWidth=splitPanes.Count>2?200:240}:new ColumnDefinition {Width=new GridLength(6)});}
    for(int i=0;i<splitPanes.Count;i++)columns[i*2].Width=new GridLength(splitShares[i],GridUnitType.Star);
    while(splitDividers.Count<splitPanes.Count-1)splitDividers.Add(NewDivider(splitDividers.Count));
    for(int i=0;i<splitPanes.Count-1;i++){Grid.SetColumn(splitDividers[i],i*2+1);host.Children.Add(splitDividers[i]);}
   }else columns.Clear();
   foreach(var pair in views) {
    int pane=split?splitPanes.IndexOf(pair.Key):-1;Grid.SetColumn(pair.Value,pane<0?0:pane*2);Grid.SetColumnSpan(pair.Value,1);
    if(fullscreenApplied&&SplitOpen&&pair.Key!=state.ActiveId)pair.Value.Visibility=Visibility.Collapsed;
   }
   if(readerHost!=null){Grid.SetColumn(readerHost,readerView!=null?Grid.GetColumn(readerView):0);}
   QueuePageCorners();
  }
  GridSplitter NewDivider(int index) {
   var divider=new GridSplitter {Width=6,HorizontalAlignment=HorizontalAlignment.Stretch,ResizeBehavior=GridResizeBehavior.PreviousAndNext,Background=System.Windows.Media.Brushes.Transparent,Cursor=System.Windows.Input.Cursors.SizeWE,Tag="split-divider",ToolTip="Drag to resize; double-click to make them even"};
   divider.Template=SplitDividerTemplate();
   divider.DragCompleted+=delegate {
    var columns=Find<Grid>("WebHost").ColumnDefinitions;double total=0;for(int i=0;i<splitPanes.Count&&i*2<columns.Count;i++)total+=columns[i*2].ActualWidth;
    if(total>0)for(int i=0;i<splitPanes.Count&&i*2<columns.Count;i++)splitShares[i]=columns[i*2].ActualWidth/total;QueuePageCorners();
   };
   divider.MouseDoubleClick+=delegate {EvenShares();LayoutSplit();};
   return divider;
  }
  static ControlTemplate SplitDividerTemplate() {
   var template=new ControlTemplate(typeof(GridSplitter));
   var root=new FrameworkElementFactory(typeof(Border));root.SetValue(Border.BackgroundProperty,System.Windows.Media.Brushes.Transparent);
   var line=new FrameworkElementFactory(typeof(Border));line.SetValue(FrameworkElement.WidthProperty,2.0);line.SetValue(Border.CornerRadiusProperty,new CornerRadius(1));line.SetValue(FrameworkElement.MarginProperty,new Thickness(0,10,0,10));
   line.SetResourceReference(Border.BackgroundProperty,"Line");root.AppendChild(line);template.VisualTree=root;return template;
  }
  // Clicking into another pane makes it the active tab, without moving anything.
  void WatchSplitFocus(string id,WebView2 view) {
   view.GotFocus+=delegate {if(InSplit(id)&&state.ActiveId!=id&&!closing)SelectTab(id);};
  }
  TextBlock splitHint;
  void RefreshSplitHint() {if(splitHint!=null)splitHint.Text=SplitOpen?(splitPanes.Count>2?splitPanes.Count+" tabs":"On"):"Off";}

  // ---- Dragging a tab onto the page ----
  // A tab dragged out of the sidebar and over the page joins the split at the slot under the pointer: the left
  // or right half of a pane puts it before or after that pane. Not split yet, it pairs with the open tab.
  internal int SplitSlotAt(Point onPage,string tabId) {
   var host=Find<Grid>("WebHost");if(host.ActualWidth<=0||onPage.X<0||onPage.Y<0||onPage.X>host.ActualWidth||onPage.Y>host.ActualHeight)return -1;
   var tab=state.Tabs.FirstOrDefault(t=>t.Id==tabId);var active=Active;
   if(tab==null||!Navigation.IsWeb(tab.Url)||active==null||!Navigation.IsWeb(active.Url)||fullscreenApplied)return -1;
   if(!SplitOpen) {
    if(active.Id==tabId)return -1;
    return onPage.X<host.ActualWidth/2?0:1;
   }
   if(!InSplit(tabId)&&splitPanes.Count>=MaxPanes)return -1;
   var columns=host.ColumnDefinitions;double left=0;
   for(int i=0;i<splitPanes.Count&&i*2<columns.Count;i++) {
    double width=columns[i*2].ActualWidth;
    if(onPage.X<left+width)return onPage.X<left+width/2?i:i+1;
    left+=width+(i*2+1<columns.Count?columns[i*2+1].ActualWidth:0);
   }
   return splitPanes.Count;
  }
  // The panes as they would be after a drop at this slot.
  List<string> PanesWith(string tabId,int slot) {
   var panes=SplitOpen?new List<string>(splitPanes):new List<string>{state.ActiveId};
   int old=panes.IndexOf(tabId);if(old>=0){panes.RemoveAt(old);if(old<slot)slot--;}
   panes.Insert(Math.Max(0,Math.Min(slot,panes.Count)),tabId);return panes;
  }
  internal void DropIntoSplit(string tabId,int slot) {
   var tab=state.Tabs.FirstOrDefault(t=>t.Id==tabId);if(tab==null||slot<0)return;
   bool joining=!InSplit(tabId);var panes=PanesWith(tabId,slot);
   if(panes.Count>MaxPanes){Status("Split view holds up to "+MaxPanes+" tabs.");return;}
   if(tab.Space!=state.Space){tab.Space=state.Space;state.NormalizeGroups();}
   bool wasOpen=SplitOpen;splitPanes.Clear();splitPanes.AddRange(panes);EvenShares();LayoutSplit();
   EnsureSplitView(tab);if(state.ActiveId!=tabId)SelectTab(tabId);else PlaceViews(tabId);
   RenderTabs();RefreshSplitHint();
   if(!wasOpen)Status("Split view. Click a side to use it; drag a divider to resize. Ctrl+Shift+S ends it.");
   else if(joining)Status(splitPanes.Count+" tabs side by side.");
  }
  // Shown over the page while a tab is dragged there: the layout after the drop, with the new place lit.
  // The page is its own window that nothing in Mica's window can cover, so the preview is a see-through popup.
  System.Windows.Controls.Primitives.Popup splitPreview;Grid splitPreviewGrid;string splitPreviewKey;
  void ShowSplitPreview(string tabId,int slot) {
   string key=slot<0?null:tabId+"|"+slot+"|"+string.Join(",",splitPanes);if(key==splitPreviewKey)return;splitPreviewKey=key;
   if(slot<0){if(splitPreview!=null)splitPreview.IsOpen=false;return;}
   var host=Find<Grid>("WebHost");
   if(splitPreview==null) {
    splitPreviewGrid=new Grid {IsHitTestVisible=false,Tag="split-preview"};
    splitPreview=new System.Windows.Controls.Primitives.Popup {AllowsTransparency=true,PopupAnimation=System.Windows.Controls.Primitives.PopupAnimation.None,Placement=System.Windows.Controls.Primitives.PlacementMode.Relative,IsHitTestVisible=false,Child=splitPreviewGrid};
   }
   splitPreview.PlacementTarget=host;splitPreviewGrid.Width=host.ActualWidth;splitPreviewGrid.Height=host.ActualHeight;
   var panes=PanesWith(tabId,slot);splitPreviewGrid.Children.Clear();splitPreviewGrid.ColumnDefinitions.Clear();
   for(int i=0;i<panes.Count;i++) {
    splitPreviewGrid.ColumnDefinitions.Add(new ColumnDefinition());
    bool lit=panes[i]==tabId;var tab=state.Tabs.FirstOrDefault(t=>t.Id==panes[i]);
    var card=new Border {CornerRadius=new CornerRadius(12),Margin=new Thickness(6),BorderThickness=new Thickness(lit?2:1),Tag=lit?"split-preview-new":"split-preview-pane"};
    card.SetResourceReference(Border.BorderBrushProperty,lit?"Accent":"Line");
    var fill=new SolidColorBrush(lit?((SolidColorBrush)window.FindResource("Accent")).Color:Colors.Black) {Opacity=lit?0.22:0.12};fill.Freeze();card.Background=fill;
    var label=new TextBlock {Text=tab==null?"":tab.Title,FontSize=13,FontWeight=lit?FontWeights.SemiBold:FontWeights.Normal,TextTrimming=TextTrimming.CharacterEllipsis,TextAlignment=TextAlignment.Center,Margin=new Thickness(16),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    label.SetResourceReference(TextBlock.ForegroundProperty,"Ink");
    var chip=new Border {CornerRadius=new CornerRadius(10),Padding=new Thickness(12,6,12,6),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Child=label};chip.SetResourceReference(Border.BackgroundProperty,"PopupPanel");
    card.Child=chip;Grid.SetColumn(card,i);splitPreviewGrid.Children.Add(card);
   }
   if(!splitPreview.IsOpen)splitPreview.IsOpen=true;else {splitPreview.HorizontalOffset+=0.01;splitPreview.HorizontalOffset=0;}
  }
  // A pane's tab closed: the others stay split (with two or more left).
  void SplitEnded(string closedId) {
   if(!InSplit(closedId))return;int at=splitPanes.IndexOf(closedId);splitPanes.RemoveAt(at);if(at<splitShares.Count)splitShares.RemoveAt(at);
   if(splitPanes.Count<2){splitPanes.Clear();splitShares.Clear();}NormalizeShares();LayoutSplit();RefreshSplitHint();
  }
 }
}
