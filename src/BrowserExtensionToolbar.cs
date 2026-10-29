using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // The puzzle button in the toolbar: your extensions, each opening its own popup (the page its manifest names
 // as action.default_popup) in a small window under the button, as Chrome does. The engine has no toolbar of
 // its own, so the popup is read from the extension's folder.
 sealed class ExtensionAction {public string Id,Name,Folder,Popup,Options,Icon;public bool Enabled;}
 partial class BrowserApp {
  Popup extensionsPopup;
  internal Window extensionActionWindow;
  internal static ExtensionAction ReadExtensionAction(string id,string name,string folder,bool enabled) {
   var action=new ExtensionAction {Id=id,Name=name,Folder=folder,Enabled=enabled};
   try {
    if(folder==null||!File.Exists(Path.Combine(folder,"manifest.json")))return action;
    var manifest=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(folder,"manifest.json")));
    object value;Dictionary<string,object> button=null;
    foreach(var key in new[]{"action","browser_action","page_action"})if(manifest.TryGetValue(key,out value)&&value is Dictionary<string,object>){button=(Dictionary<string,object>)value;break;}
    if(button!=null&&button.TryGetValue("default_popup",out value))action.Popup=PagePath(value as string);
    if(manifest.TryGetValue("options_ui",out value)&&value is Dictionary<string,object>){object page;if(((Dictionary<string,object>)value).TryGetValue("page",out page))action.Options=PagePath(page as string);}
    if(action.Options==null&&manifest.TryGetValue("options_page",out value))action.Options=PagePath(value as string);
    // The toolbar icon, else the extension's own; the size nearest 32 px.
    object icons=null;if(button!=null)button.TryGetValue("default_icon",out icons);
    if(icons==null)manifest.TryGetValue("icons",out icons);
    string icon=icons as string;
    if(icon==null&&icons is Dictionary<string,object>)icon=((Dictionary<string,object>)icons).OrderBy(p=>{int size;return int.TryParse(p.Key,out size)?Math.Abs(size-32)+(size<32?100:0):999;}).Select(p=>p.Value as string).FirstOrDefault(p=>p!=null);
    icon=PagePath(icon);
    if(icon!=null){string file=Path.GetFullPath(Path.Combine(folder,icon.Replace('/','\\')));if(file.StartsWith(Path.GetFullPath(folder),StringComparison.OrdinalIgnoreCase)&&File.Exists(file))action.Icon=file;}
   }catch(Exception){}
   return action;
  }
  // Manifest paths are relative to the extension's root; anything leaving it is ignored.
  static string PagePath(string path) {
   if(string.IsNullOrWhiteSpace(path))return null;path=path.Trim().TrimStart('/').Replace('\\','/');
   return path.Contains("..")||path.Contains(":")?null:path;
  }
  async Task<List<ExtensionAction>> ToolbarExtensions() {
   var target=await EnsureExtensionProfile();LoadExtensionRecords();
   var actions=new List<ExtensionAction>();
   foreach(var extension in (await target.GetBrowserExtensionsAsync()).Where(e=>!IsHiddenExtension(e.Id))) {
    ExtensionRecord record;extensionRecords.TryGetValue(extension.Id,out record);
    actions.Add(ReadExtensionAction(extension.Id,extension.Name,record==null?null:record.Folder,extension.IsEnabled));
   }
   return actions.OrderBy(a=>!a.Enabled).ThenBy(a=>a.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
  }

  async void ShowToolbarExtensions() {
   if(extensionsPopup!=null&&extensionsPopup.IsOpen){extensionsPopup.IsOpen=false;return;}
   CloseMenus();CloseExtensionAction();
   if(privateMode){Status("Extensions don't run in private windows.");return;}
   if(extensionsPopup==null){extensionsPopup=CreatePopup("ExtensionsPopup",270);extensionsPopup.Placement=PlacementMode.Bottom;extensionsPopup.HorizontalOffset=0;extensionsPopup.VerticalOffset=6;}
   List<ExtensionAction> actions;
   try {actions=await ToolbarExtensions();}catch(Exception e){Status("Couldn't list extensions: "+e.Message);return;}
   var body=PopupBody(extensionsPopup);MenuHeading(body,"EXTENSIONS");
   if(actions.Count==0){var empty=new TextBlock {Text="No extensions yet. Add one from the Chrome Web Store or a folder.",FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12,2,12,10)};empty.SetResourceReference(TextBlock.ForegroundProperty,"Muted");body.Children.Add(empty);}
   foreach(var action in actions)ExtensionActionRow(body,action);
   MenuDivider(body);MenuCommand(body,extensionsPopup,"\uE713","Manage extensions","",ShowExtensions);
   var anchor=Find<Button>("ExtensionsButton");extensionsPopup.PlacementTarget=anchor;
   extensionsPopup.HorizontalOffset=anchor.ActualWidth-270;extensionsPopup.IsOpen=true;
  }
  void ExtensionActionRow(StackPanel body,ExtensionAction action) {
   var button=new Button {Style=(Style)window.FindResource(typeof(Button)),HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(12,9,12,9),Margin=new Thickness(0,1,0,1),Tag="extension-action:"+action.Id};
   var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(28)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
   row.Children.Add(ExtensionIcon(action));
   var name=new TextBlock {Text=action.Name,FontSize=12,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};Grid.SetColumn(name,1);row.Children.Add(name);
   string hint=!action.Enabled?"Off":action.Popup!=null?"":action.Options!=null?"Options":"";
   var note=new TextBlock {Text=hint,FontSize=10,Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Center};note.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Grid.SetColumn(note,2);row.Children.Add(note);
   if(!action.Enabled)row.Opacity=0.55;
   button.Content=row;System.Windows.Automation.AutomationProperties.SetName(button,action.Name);
   button.ToolTip=!action.Enabled?"Turned off. Turn it on in Manage extensions.":action.Popup==null&&action.Options==null?"This extension works on pages by itself; it has no popup.":null;
   button.Click+=delegate {
    if(!action.Enabled){extensionsPopup.IsOpen=false;ShowExtensions();return;}
    if(action.Popup!=null){extensionsPopup.IsOpen=false;OpenExtensionAction(action);}
    else if(action.Options!=null){extensionsPopup.IsOpen=false;OpenExtensionPage("chrome-extension://"+action.Id+"/"+action.Options);}
   };
   body.Children.Add(button);
  }
  FrameworkElement ExtensionIcon(ExtensionAction action) {
   if(action.Icon!=null)try {
    var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=36;bitmap.UriSource=new Uri(action.Icon);bitmap.EndInit();bitmap.Freeze();
    var image=new Image {Source=bitmap,Width=18,Height=18,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);return image;
   }catch(Exception){}
   var symbol=new TextBlock {Text="\uEA86",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=14,VerticalAlignment=VerticalAlignment.Center};symbol.SetResourceReference(TextBlock.ForegroundProperty,"Accent");return symbol;
  }

  // The popup: a borderless window under the puzzle button that sizes itself to the page, like Chrome's
  // (25-800 px wide, 25-600 px tall), and closes when you click elsewhere or the page calls window.close().
  internal async Task OpenExtensionAction(ExtensionAction action) {
   CloseExtensionAction();
   var anchor=Find<Button>("ExtensionsButton");
   var popup=new Window {Owner=window,Title=action.Name,Width=320,Height=120,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=true,FontFamily=window.FontFamily,Resources=window.Resources,WindowStartupLocation=WindowStartupLocation.Manual};
   System.Windows.Shell.WindowChrome.SetWindowChrome(popup,new System.Windows.Shell.WindowChrome {CaptionHeight=0,ResizeBorderThickness=new Thickness(0),CornerRadius=new CornerRadius(8),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});
   popup.SetResourceReference(Window.BackgroundProperty,"Canvas");
   var frame=new Border {BorderThickness=new Thickness(1)};frame.SetResourceReference(Border.BorderBrushProperty,"Line");popup.Content=frame;
   var view=new WebView2 {DefaultBackgroundColor=System.Drawing.Color.White};frame.Child=view;
   Action place=delegate {
    if(!anchor.IsVisible||PresentationSource.FromVisual(anchor)==null)return;
    var transform=PresentationSource.FromVisual(anchor).CompositionTarget.TransformFromDevice;
    var corner=transform.Transform(anchor.PointToScreen(new Point(anchor.ActualWidth,anchor.ActualHeight)));
    popup.Left=Math.Max(SystemParameters.WorkArea.Left+4,corner.X-popup.Width);popup.Top=Math.Min(corner.Y+6,SystemParameters.WorkArea.Bottom-popup.Height-4);
   };
   place();if(smoke){popup.Left=-5000;popup.Top=-5000;}
   bool alive=true;extensionActionWindow=popup;
   popup.Closed+=delegate {alive=false;if(extensionActionWindow==popup)extensionActionWindow=null;ReplaceTargetWatcher(view);try {view.Dispose();}catch(Exception){}};
   // Clicking back into Mica (or anywhere) closes it; so does Escape.
   popup.Deactivated+=delegate {if(alive&&!smoke)popup.Close();};
   popup.PreviewKeyDown+=delegate(object sender,KeyEventArgs e) {if(e.Key==Key.Escape){e.Handled=true;popup.Close();}};
   popup.Show();
   try {
    var engine=await EnsureEnvironment();await EnsureExtensionProfile();
    await view.EnsureCoreWebView2Async(engine,ViewOptions(engine));await ApplyMicaAgent(view.CoreWebView2);if(!alive||closing)return;
    var core=view.CoreWebView2;RegisterOwnTarget(core);
    core.Settings.IsWebMessageEnabled=false;core.Settings.AreHostObjectsAllowed=false;core.Settings.AreDefaultContextMenusEnabled=true;
    core.WindowCloseRequested+=delegate {if(alive)popup.Close();};
    // Links in a popup open as tabs; the extension's own pages open in a Mica window.
    core.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e) {
     e.Handled=true;string uri=e.Uri;
     window.Dispatcher.BeginInvoke(new Action(delegate {if(closing)return;if(Navigation.IsWeb(uri))Navigate(uri,true);else if(IsExtensionPage(uri))OpenExtensionPage(uri);if(alive)popup.Close();}));
    };
    string start="chrome-extension://"+action.Id+"/";
    core.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e) {
     if(e.Uri.StartsWith(start,StringComparison.OrdinalIgnoreCase))return;
     e.Cancel=true;string uri=e.Uri;if(Navigation.IsWeb(uri))window.Dispatcher.BeginInvoke(new Action(delegate {if(!closing)Navigate(uri,true);if(alive)popup.Close();}));
    };
    var loaded=new TaskCompletionSource<bool>();
    core.NavigationCompleted+=delegate(object sender,CoreWebView2NavigationCompletedEventArgs e) {loaded.TrySetResult(e.IsSuccess);};
    core.Navigate(start+action.Popup);
    if(!await loaded.Task){if(alive){popup.Close();Status("Couldn't open "+action.Name+"'s popup.");}return;}
    // Size to the page's own width (max-content, as Chrome does), now and again as it lays itself out.
    for(int i=0;i<6&&alive;i++) {
     string size=await core.ExecuteScriptAsync("(()=>{const d=document.documentElement,w=d.style.width;d.style.width='max-content';const r=d.getBoundingClientRect();d.style.width=w;return [Math.ceil(r.width),Math.ceil(r.height)].join(',')})()");
     var parts=(size??"").Trim('"').Split(',');double width,height;
     if(parts.Length==2&&double.TryParse(parts[0],out width)&&double.TryParse(parts[1],out height)) {
      width=Math.Max(25,Math.Min(800,width))+2;height=Math.Max(25,Math.Min(600,height))+2;
      if(Math.Abs(popup.Width-width)>1||Math.Abs(popup.Height-height)>1){popup.Width=width;popup.Height=height;if(!smoke)place();}
     }
     await Task.Delay(i<3?60:250);
    }
   }catch(Exception e){if(alive)popup.Close();Status("Couldn't open the extension popup: "+e.Message);}
  }
  void CloseExtensionAction() {if(extensionActionWindow!=null)extensionActionWindow.Close();}
 }
}
