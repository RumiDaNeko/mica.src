using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 partial class BrowserApp {
  ContextMenu openContextMenu;
  Action finishContextRequest;
  void InitializeContextMenus() {
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Mica.ContextMenus.xaml"))window.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
   window.Resources["ContextPopupAnimation"]=SystemParameters.ClientAreaAnimation?PopupAnimation.Fade:PopupAnimation.None;
   window.Resources["ContextMenuHeight"]=Math.Max(240,SystemParameters.WorkArea.Height-40);
  }
  void CloseContextMenu() {var menu=openContextMenu;var finish=finishContextRequest;openContextMenu=null;finishContextRequest=null;if(menu!=null)menu.IsOpen=false;if(finish!=null)finish();}
  ContextMenu CreateContextMenu() {
   var menu=new ContextMenu {Resources=window.Resources,Style=(Style)window.FindResource(typeof(ContextMenu)),MaxHeight=Math.Max(240,SystemParameters.WorkArea.Height-32)};
   menu.Opened+=delegate {
    if(openContextMenu!=null&&openContextMenu!=menu)CloseContextMenu();if(spacesPopup!=null)spacesPopup.IsOpen=false;if(menuPopup!=null)menuPopup.IsOpen=false;openContextMenu=menu;
    menu.ApplyTemplate();var surface=menu.Template.FindName("MenuSurface",menu) as Border;if(surface!=null)AnimateArrival(surface);
   };
   menu.Closed+=delegate {if(openContextMenu==menu)openContextMenu=null;};return menu;
  }
  MenuItem ContextCommand(string label,string icon,string shortcut,Action action) {
   var item=new MenuItem {Header=label,Icon=ContextIcon(icon),InputGestureText=shortcut};
   if(action!=null)item.Click+=delegate(object sender,RoutedEventArgs e) {e.Handled=true;CloseContextMenu();action();};return item;
  }
  TextBlock ContextIcon(string glyph) {var icon=new TextBlock {Text=glyph,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=14,VerticalAlignment=VerticalAlignment.Center};icon.SetResourceReference(TextBlock.ForegroundProperty,"Accent");return icon;}
  Separator ContextSeparator() {return new Separator {Style=(Style)window.FindResource(typeof(Separator))};}
  // Menus are built on right-click, not with every row: re-rendering the tab list stays cheap on a tab switch.
  void LazyMenu(FrameworkElement owner,Func<ContextMenu> build) {owner.ContextMenuOpening+=delegate {owner.ContextMenu=build();};}
  ContextMenu TabMenu(PageEntry entry) {
   var menu=CreateContextMenu();
   menu.Items.Add(ContextCommand(entry.Pinned?"Unpin tab":"Pin tab","\uE718","",delegate {entry.Pinned=!entry.Pinned;animateTabMoves=true;state.NormalizeGroups();RenderTabs();Save();}));
   if(InSplit(entry.Id)){if(splitPanes.Count>2)menu.Items.Add(ContextCommand("Take out of split view","\uE7F4","",delegate {RemoveFromSplit(entry.Id);}));menu.Items.Add(ContextCommand("End split view","\uE7F4","Ctrl+Shift+S",CloseSplit));}
   else if(entry.Id!=state.ActiveId&&Navigation.IsWeb(entry.Url)&&Active!=null&&Navigation.IsWeb(Active.Url)){var add=ContextCommand(SplitOpen?"Add to split view":"Split view with this tab","\uE7F4","",delegate {if(SplitOpen)AddToSplit(entry.Id);else OpenSplit(entry.Id);});add.IsEnabled=!SplitOpen||splitPanes.Count<MaxPanes;menu.Items.Add(add);}
   AddGroupCommands(menu,entry);
   if(!privateMode)menu.Items.Add(JarMenu(entry));
   {WebView2 live;var sound=views.TryGetValue(entry.Id,out live)?LiveCore(live):null;if(sound!=null){bool muted=false;try {muted=sound.IsMuted;}catch(Exception){}menu.Items.Add(ContextCommand(muted?"Unmute tab":"Mute tab",muted?"":"","",delegate {ToggleMute(entry.Id);}));}}
   var move=ContextCommand("Move to space","\uE8F1","",null);
   foreach(string name in state.Spaces) {string space=name;var choice=ContextCommand(space,SpaceIcon(space),"",delegate {entry.Space=space;entry.Group=null;state.NormalizeGroups();if(state.ActiveId==entry.Id)SwitchSpace(state.Space);else RenderTabs();Save();});choice.IsChecked=entry.Space==space;choice.IsEnabled=entry.Space!=space;move.Items.Add(choice);}menu.Items.Add(move);
   menu.Items.Add(ContextSeparator());
   var reload=ContextCommand("Reload tab","\uE72C",entry.Id==state.ActiveId?"Ctrl+R":"",delegate {SelectTab(entry.Id);Reload();});reload.IsEnabled=Navigation.IsWeb(entry.Url);menu.Items.Add(reload);
   menu.Items.Add(ContextSeparator());menu.Items.Add(ContextCommand("Close tab","\uE8BB",entry.Id==state.ActiveId&&!entry.Pinned?"Ctrl+W":"",delegate {CloseTab(entry.Id);}));return menu;
  }
  void ShowWebContextMenu(PageEntry tab,WebView2 view,CoreWebView2ContextMenuRequestedEventArgs args) {
   if(!IsCurrent(tab.Id,view)||state.ActiveId!=tab.Id||IsSpotlight){args.Handled=true;return;}
   CloseMenus();var deferral=args.GetDeferral();bool finished=false;ContextMenu menu=null;
   Action finish=null;finish=delegate {if(finished)return;finished=true;if(finishContextRequest==finish)finishContextRequest=null;try {deferral.Dispose();}catch(COMException){}catch(ObjectDisposedException){}};
   try {
    menu=CreateContextMenu();args.Handled=true;
    AddWebMenuItems(menu,args.MenuItems,delegate(int command) {args.SelectedCommandId=command;CloseContextMenu();});
    if(args.ContextMenuTarget.HasSelection&&Navigation.IsWeb(tab.Url)){if(menu.Items.Count>0)menu.Items.Insert(0,ContextSeparator());menu.Items.Insert(0,ContextCommand("Highlight","","",delegate {HighlightSelection(tab,view);}));}
    if(args.ContextMenuTarget.HasSelection)AddAiMenuItems(menu,tab,args.ContextMenuTarget.SelectionText);
    // Location comes from the engine, so keyboard-invoked menus use their target,
    // rather than the unrelated mouse cursor. Convert physical pixels to WPF DIPs.
    var source=PresentationSource.FromVisual(view);var location=new Point(args.Location.X,args.Location.Y);
    if(source!=null&&source.CompositionTarget!=null)location=source.CompositionTarget.TransformFromDevice.Transform(location);
    menu.PlacementTarget=view;menu.Placement=PlacementMode.RelativePoint;menu.HorizontalOffset=location.X;menu.VerticalOffset=location.Y;
    menu.Closed+=delegate {finish();};if(menu.Items.Count==0){finish();return;}finishContextRequest=finish;menu.IsOpen=true;
   } catch {
    // Preserve the engine's own menu if native commands cannot be projected.
    args.Handled=false;finish();if(menu!=null)menu.IsOpen=false;
   }
  }
  void AddWebMenuItems(ItemsControl parent,IList<CoreWebView2ContextMenuItem> source,Action<int> select) {
   foreach(var command in source) {
    if(command.Kind==CoreWebView2ContextMenuItemKind.Separator){parent.Items.Add(ContextSeparator());continue;}
    var native=command;
    var item=ContextCommand(MenuAccessLabel(command.Label),WebCommandGlyph(command.Name),command.ShortcutKeyDescription,null);
    item.Tag=command.Name;item.IsEnabled=command.IsEnabled;
    if(command.Kind==CoreWebView2ContextMenuItemKind.CheckBox||command.Kind==CoreWebView2ContextMenuItemKind.Radio)item.IsChecked=command.IsChecked;
    if(command.Kind==CoreWebView2ContextMenuItemKind.Submenu)AddWebMenuItems(item,command.Children,select);
    else item.Click+=delegate(object sender,RoutedEventArgs e) {e.Handled=true;select(native.CommandId);};
    parent.Items.Add(item);
   }
  }
  static string MenuAccessLabel(string label) {
   label=label??"";var result=new StringBuilder();
   for(int i=0;i<label.Length;i++){char c=label[i];if(c=='_')result.Append("__");else if(c=='&'&&i+1<label.Length){if(label[i+1]=='&'){result.Append('&');i++;}else result.Append('_');}else result.Append(c);}return result.ToString();
  }
  static string WebCommandGlyph(string name) {
   string key=(name??"").ToLowerInvariant();
   if(key.Contains("back"))return "\uE72B";if(key.Contains("forward"))return "\uE72A";if(key.Contains("reload"))return "\uE72C";
   if(key.Contains("copy"))return "\uE8C8";if(key.Contains("paste"))return "\uE77F";if(key.Contains("cut"))return "\uE8C6";
   if(key.Contains("save"))return "\uE74E";if(key.Contains("print"))return "\uE749";if(key.Contains("link"))return "\uE71B";
   if(key.Contains("undo"))return "\uE7A7";if(key.Contains("redo"))return "\uE7A6";if(key.Contains("select"))return "\uE8B3";
   if(key.Contains("inspect")||key.Contains("source"))return "\uE943";if(key.Contains("search"))return "\uE721";
   if(key.Contains("image"))return "\uE91B";if(key.Contains("extension"))return "\uEA86";return "\uE712";
  }
 }
}
