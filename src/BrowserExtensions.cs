using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 partial class BrowserApp {
  WebView2 extensionView;
  Task<CoreWebView2Profile> extensionProfileTask;
  Window extensionsWindow;
  StackPanel extensionRows,extensionCommands;
  TextBlock extensionStatus;
  bool extensionsBusy;
  static Window activeExtensionsWindow;
  Grid extensionSurface;
  List<CoreWebView2BrowserExtension> displayedExtensions=new List<CoreWebView2BrowserExtension>();
  string extensionFilter="All";
  PreparedExtension pendingExtension;
  sealed class ExtensionRecord {public string Id {get;set;}public string Folder {get;set;}public string Source {get;set;}public string Version {get;set;}}
  readonly Dictionary<string,ExtensionRecord> extensionRecords=new Dictionary<string,ExtensionRecord>();
  string ExtensionRecordsPath {get{return Path.Combine(profile,"extensions.json");}}
  T ExtensionUi<T>(string name) where T:FrameworkElement {return (T)extensionSurface.FindName(name);}
  // Ambient Light is driven by the "YouTube ambient" menu toggle, so it stays out of the extension list.
  // The built-in blockers live under Ad blocking instead.
  static bool IsHiddenExtension(string id) {return IsRuntimeExtension(id)||id==AmbientExtensionId||IsBuiltInBlocker(id)||IsAiPanelExtension(id);}
  static bool IsBuiltInBlocker(string id) {lock(builtInIds)return builtInIds.Contains(id);}
  static bool IsRuntimeExtension(string id) {return id=="dgiklkfkllikcanfonkcabmbdfmgleag"||id=="mhjfbmdgcfjbbpaeojofohoefgiehjai";}

  async Task<CoreWebView2Profile> EnsureExtensionProfile() {
   if(extensionProfileTask==null)extensionProfileTask=CreateExtensionProfile();
   CoreWebView2Profile ready;
   try {ready=await extensionProfileTask;}
   catch {extensionProfileTask=null;throw;}
   if(!privateMode&&!aiExtensionsRepaired){aiExtensionsRepaired=true;RepairAiExtensions();}
   return ready;
  }
  // Claude Code drives Claude for Chrome from its worker, often with no panel open, so the AI extensions get Mica's
  // current shim at start, not only when the panel shows them.
  bool aiExtensionsRepaired;
  async void RepairAiExtensions() {foreach(var ai in AiExtensions)await AiExtensionInstalled(ai);}
  async Task<CoreWebView2Profile> CreateExtensionProfile() {
   var sharedEnvironment=await EnsureEnvironment();
   if(closing)throw new OperationCanceledException("The browser is closing.");
   // A profile must have a live controller, including when every user tab is home/asleep.
   var view=new WebView2 {Width=1,Height=1,Visibility=Visibility.Hidden,IsHitTestVisible=false,Focusable=false};
   extensionView=view;Find<Grid>("Root").Children.Add(view);
   System.ComponentModel.CancelEventHandler onClosing=null;
   onClosing=delegate {ReplaceTargetWatcher(view);view.Dispose();window.Closing-=onClosing;};
   window.Closing+=onClosing;
   try {
    await view.EnsureCoreWebView2Async(sharedEnvironment,ViewOptions(sharedEnvironment));
    if(closing)throw new OperationCanceledException("The browser is closing.");
    view.CoreWebView2.Settings.IsWebMessageEnabled=false;
    view.CoreWebView2.Settings.AreHostObjectsAllowed=false;
    KeepWindowOpen(view);RegisterOwnTarget(view.CoreWebView2);
    return view.CoreWebView2.Profile;
   }catch {
    window.Closing-=onClosing;Find<Grid>("Root").Children.Remove(view);view.Dispose();
    if(extensionView==view)extensionView=null;
    throw;
   }
  }
  async Task<CoreWebView2BrowserExtension> InstallExtension(string folder) {
   if(string.IsNullOrWhiteSpace(folder))throw new ArgumentException("Choose the extension's manifest.json file.");
   string fullPath=Path.GetFullPath(folder);
   if(!Directory.Exists(fullPath)||!File.Exists(Path.Combine(fullPath,"manifest.json")))
    throw new ArgumentException("This folder does not contain manifest.json. Extract the extension first, then select its manifest.json file.");
   var browserProfile=await EnsureExtensionProfile();
   var installed=await browserProfile.AddBrowserExtensionAsync(fullPath);
   RememberExtension(installed.Id,fullPath,"Local folder",null);return installed;
  }
  async Task<List<CoreWebView2BrowserExtension>> GetExtensions() {
   var browserProfile=await EnsureExtensionProfile();
   return (await browserProfile.GetBrowserExtensionsAsync()).OrderBy(e=>e.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
  }

  void LoadExtensionRecords() {
   extensionRecords.Clear();try {if(File.Exists(ExtensionRecordsPath)){var records=new JavaScriptSerializer().Deserialize<List<ExtensionRecord>>(File.ReadAllText(ExtensionRecordsPath));if(records!=null)foreach(var r in records.Where(r=>r!=null&&!string.IsNullOrEmpty(r.Id)))extensionRecords[r.Id]=r;}}catch(Exception e){if(!(e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is InvalidOperationException))throw;}
  }
  void RememberExtension(string id,string folder,string source,string version) {
   LoadExtensionRecords();ExtensionRecord previous;extensionRecords.TryGetValue(id,out previous);extensionRecords[id]=new ExtensionRecord {Id=id,Folder=folder,Source=source,Version=version};
   if(SaveExtensionRecords()&&previous!=null&&!string.Equals(previous.Folder,folder,StringComparison.OrdinalIgnoreCase))CleanManagedExtension(previous);
  }
  bool SaveExtensionRecords() {
   string temp=ExtensionRecordsPath+".tmp";
   try {File.WriteAllText(temp,new JavaScriptSerializer().Serialize(extensionRecords.Values.ToList()));if(File.Exists(ExtensionRecordsPath))File.Replace(temp,ExtensionRecordsPath,null);else File.Move(temp,ExtensionRecordsPath);return true;}catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
  }
  void CleanManagedExtension(ExtensionRecord record) {
   if(record==null||record.Source!="Chrome Web Store"||string.IsNullOrEmpty(record.Folder))return;
   try {
    string root=Path.GetFullPath(Path.Combine(profile,"StoreExtensions")).TrimEnd(Path.DirectorySeparatorChar);
    string folder=Path.GetFullPath(record.Folder).TrimEnd(Path.DirectorySeparatorChar);
    if(!string.Equals(Path.GetDirectoryName(folder),root,StringComparison.OrdinalIgnoreCase)||!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(folder),"^package-[0-9a-f]{32}$"))return;
    ExtensionPackage.CheckParents(folder);ExtensionPackage.DeleteStage(folder);
   }catch(Exception e){if(!FaviconCache.IsExpected(e))throw;}
  }
  async Task RemoveManagedExtension(CoreWebView2BrowserExtension extension) {
   await extension.RemoveAsync();if(extension.Id==AmbientExtensionId){foreach(var browser in Program.Browsers.Where(b=>!b.privateMode&&b.profile==profile)){browser.state.AmbientYoutube=false;browser.Save();}}LoadExtensionRecords();ExtensionRecord record;extensionRecords.TryGetValue(extension.Id,out record);extensionRecords.Remove(extension.Id);
   if(SaveExtensionRecords())CleanManagedExtension(record);
  }
  void ShowExtensions() {
   if(privateMode){Status("Extensions are disabled in private windows.");return;}
   if(activeExtensionsWindow!=null){activeExtensionsWindow.Activate();return;}
   var dialog=new Window {Owner=window,Title="Extensions — Mica",Width=780,Height=720,MinWidth=690,MinHeight=590,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brushes.Transparent,Foreground=Brush("Ink"),FontFamily=window.FontFamily,FontSize=13,Resources=window.Resources};KeepOwnerInFront(dialog);
   extensionsWindow=dialog;activeExtensionsWindow=dialog;extensionFilter="All";
   foreach(var key in new[]{"Ink","Muted","Canvas","Panel","Line","Hover","Accent"})dialog.Resources[key]=Brush(key);
   dialog.Resources[typeof(Button)]=window.Resources[typeof(Button)];
   using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Mica.Extensions.xaml"))extensionSurface=(Grid)XamlReader.Load(stream);
   // Same card as every other Mica pop-up: rounded, soft shadow, the shared control styles.
   var chrome=new Border {CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),Margin=new Thickness(12,8,12,18)};chrome.SetResourceReference(Border.BackgroundProperty,"Canvas");chrome.SetResourceReference(Border.BorderBrushProperty,"Line");chrome.Effect=new System.Windows.Media.Effects.DropShadowEffect {BlurRadius=26,ShadowDepth=5,Opacity=0.16,Color=Color.FromRgb(0x25,0x20,0x32)};chrome.Resources.MergedDictionaries.Add(DialogStyles.Create());
   var frame=new Grid();chrome.Child=frame;dialog.Content=chrome;
   extensionSurface.Margin=new Thickness(22,18,22,20);frame.Children.Add(extensionSurface);
   // The heading lives in Extensions.xaml; this strip only drags the window and holds Close.
   var header=new Grid {Background=Brushes.Transparent,Height=62,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,0,14,0)};
   var close=new Button {Content="\uE8BB",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=11,Width=30,Height=30,Padding=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,ToolTip="Close"};close.SetResourceReference(Control.ForegroundProperty,"Muted");System.Windows.Automation.AutomationProperties.SetName(close,"Close");close.Click+=delegate {dialog.Close();};header.Children.Add(close);header.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(e.LeftButton==MouseButtonState.Pressed)dialog.DragMove();};frame.Children.Add(header);
   dialog.Loaded+=delegate {AnimateArrival(chrome);};
   extensionRows=ExtensionUi<StackPanel>("ExtensionRows");extensionCommands=ExtensionUi<StackPanel>("ExtensionCommands");extensionStatus=ExtensionUi<TextBlock>("ExtensionStatus");
   ExtensionUi<Button>("BrowseStore").Click+=delegate {dialog.Close();Navigate("https://chromewebstore.google.com/category/extensions",true);};
   ExtensionUi<Button>("ReviewStore").Click+=async delegate {await ReviewStoreExtension();};
   ExtensionUi<TextBox>("StoreLink").KeyDown+=async delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter){e.Handled=true;await ReviewStoreExtension();}};
   ExtensionUi<TextBox>("ExtensionSearch").TextChanged+=delegate {RenderExtensions(displayedExtensions);};
   foreach(string filter in new[]{"All","Enabled","Disabled"}){string chosen=filter;ExtensionUi<Button>("Filter"+filter+"Extensions").Click+=delegate {extensionFilter=chosen;RenderExtensions(displayedExtensions);};}
   ExtensionUi<Button>("RefreshExtensions").Click+=async delegate {await RunExtensionAction(null,null);};
   ExtensionUi<Button>("LoadUnpacked").Click+=async delegate {
    var picker=new OpenFileDialog {Title="Choose the extension's manifest.json",Filter="Extension manifest|manifest.json",CheckFileExists=true,Multiselect=false};
    if(picker.ShowDialog(dialog)==true){if(!string.Equals(Path.GetFileName(picker.FileName),"manifest.json",StringComparison.OrdinalIgnoreCase)){extensionStatus.Text="Select the extension's manifest.json file.";return;}await RunExtensionAction(async delegate {await InstallExtension(Path.GetDirectoryName(picker.FileName));},"Extension added. Reload open pages to apply it.");}
   };
   ExtensionUi<Button>("CancelExtension").Click+=delegate {CancelExtensionReview();};
   ExtensionUi<Button>("ConfirmExtension").Click+=async delegate {await ConfirmStoreExtension();};
   try {if(Active!=null){ExtensionPackage.ParseStoreId(Active.Url);ExtensionUi<TextBox>("StoreLink").Text=Active.Url;}}catch(FormatException){}
   dialog.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){if(pendingExtension!=null)CancelExtensionReview();else dialog.Close();e.Handled=true;}};
   dialog.Closed+=delegate {CancelExtensionReview();if(extensionsWindow==dialog){extensionsWindow=null;activeExtensionsWindow=null;}};
   dialog.Loaded+=async delegate {await RunExtensionAction(null,null);};dialog.Show();
  }
  void SetExtensionBusy(bool busy,string message) {
   extensionsBusy=busy;if(extensionsWindow==null)return;
   ExtensionUi<Grid>("ExtensionMain").IsEnabled=!busy;ExtensionUi<Border>("ExtensionReview").IsEnabled=!busy;
   ExtensionUi<ProgressBar>("ExtensionProgress").Visibility=busy?Visibility.Visible:Visibility.Collapsed;
   if(message!=null)extensionStatus.Text=message;
  }
  async Task ReviewStoreExtension() {
   if(extensionsBusy||extensionsWindow==null)return;var dialog=extensionsWindow;string input=ExtensionUi<TextBox>("StoreLink").Text;
   try {
    string id=ExtensionPackage.ParseStoreId(input);if(IsRuntimeExtension(id))throw new InvalidOperationException("This component is managed by the browser.");
    SetExtensionBusy(true,"Downloading and checking extension…");
    string version=(await EnsureEnvironment()).BrowserVersionString.Split(' ')[0];
    var prepared=await ExtensionPackage.PrepareAsync(input,Path.Combine(profile,"StoreExtensions"),version);
    if(closing||extensionsWindow!=dialog){DisposePrepared(prepared);return;}
    PresentExtensionReview(prepared);
   }catch(Exception e){if(extensionsWindow==dialog)extensionStatus.Text=ExtensionError(e);}
   finally {SetExtensionBusy(false,null);}
  }
  static string PermissionDescription(string permission) {
   switch(permission) {
    case "<all_urls>": return "Read and change data on all websites (<all_urls>)";
    case "tabs": return "Read information about your tabs (tabs)";
    case "storage": return "Store extension settings (storage)";
    case "scripting": return "Run scripts on permitted websites (scripting)";
    case "contextMenus": return "Add commands to website menus (contextMenus)";
    case "activeTab": return "Access the active tab when you invoke the extension (activeTab)";
    case "downloads": return "Manage downloads (downloads)";
    case "history": return "Read and change browsing history (history)";
    case "cookies": return "Access cookies on permitted websites (cookies)";
    case "alarms": return "Schedule background tasks (alarms)";
    default: return permission;
   }
  }
  void PresentExtensionReview(PreparedExtension prepared) {
   CancelExtensionReview();pendingExtension=prepared;
   ExtensionUi<TextBlock>("ReviewName").Text=prepared.Name;
   ExtensionUi<TextBlock>("ReviewVersion").Text="Chrome Web Store · Version "+prepared.Version;
   ExtensionUi<TextBlock>("ReviewPermissions").Text=prepared.Permissions.Length==0?"No additional permissions declared.":string.Join("\n",prepared.Permissions.Select(p=>"•  "+PermissionDescription(p)));
   ExtensionUi<Button>("ConfirmExtension").Content=displayedExtensions.Any(e=>e.Id==prepared.Id)?"Replace installed version":"Add extension";
   ExtensionUi<Grid>("ExtensionMain").Visibility=Visibility.Collapsed;ExtensionUi<Border>("ExtensionReview").Visibility=Visibility.Visible;AnimateArrival(ExtensionUi<Border>("ExtensionReview"));ExtensionUi<Button>("CancelExtension").Focus();
  }
  void DisposePrepared(PreparedExtension prepared) {if(prepared==null)return;try {prepared.Dispose();}catch(IOException){}catch(UnauthorizedAccessException){}}
  void CancelExtensionReview() {
   DisposePrepared(pendingExtension);pendingExtension=null;
   if(extensionSurface!=null){ExtensionUi<Border>("ExtensionReview").Visibility=Visibility.Collapsed;ExtensionUi<Grid>("ExtensionMain").Visibility=Visibility.Visible;}
  }
  async Task ConfirmStoreExtension() {
   if(extensionsBusy||pendingExtension==null)return;var prepared=pendingExtension;pendingExtension=null;
   ExtensionUi<Border>("ExtensionReview").Visibility=Visibility.Collapsed;ExtensionUi<Grid>("ExtensionMain").Visibility=Visibility.Visible;
   try {await RunExtensionAction(async delegate {
    var browserProfile=await EnsureExtensionProfile();var installed=await browserProfile.AddBrowserExtensionAsync(prepared.Folder);
    if(installed.Id!=prepared.Id){await installed.RemoveAsync();throw new InvalidOperationException("Extension identity did not match the store package.");}
    prepared.Commit();RememberExtension(installed.Id,prepared.Folder,"Chrome Web Store",prepared.Version);
   },"Extension added from Chrome Web Store. Reload open pages to apply it.");}finally {DisposePrepared(prepared);}
  }
  async Task RunExtensionAction(Func<Task> action,string success) {
   if(extensionsBusy)return;var dialog=extensionsWindow;SetExtensionBusy(true,action==null?"Loading your extensions…":"Updating extension…");
   try {if(action!=null)await action();var installed=await GetExtensions();if(extensionsWindow!=null){LoadExtensionRecords();RenderExtensions(installed);extensionStatus.Text=success??"Some Chrome features and toolbar popups aren't supported yet.";}}
   catch(Exception e){if(extensionsWindow!=null&&extensionsWindow==dialog)extensionStatus.Text=ExtensionError(e);}
   finally {SetExtensionBusy(false,null);}
  }
  static string ExtensionError(Exception e) {
   if(e is NotImplementedException||e is MissingMethodException||e is NotSupportedException)return "This extension needs features that aren't available in the current browser runtime.";
   var network=e as System.Net.WebException;if(network!=null){if(network.Response!=null)network.Response.Close();return "Couldn't download this extension from Chrome Web Store. Check your connection and whether it is still available.";}
   if(e.HResult==unchecked((int)0x80070002))return "The package has an invalid or unsupported manifest.";
   if(e.HResult==unchecked((int)0x80070005))return "The runtime couldn't load this extension's files. Its package may use unsupported features.";
   return e.Message;
  }
  FrameworkElement ExtensionIcon(CoreWebView2BrowserExtension item) {
   ExtensionRecord record;try {
    if(extensionRecords.TryGetValue(item.Id,out record)&&Directory.Exists(record.Folder)) {
     var manifest=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(record.Folder,"manifest.json"))) as Dictionary<string,object>;object value;
     if(manifest!=null&&manifest.TryGetValue("icons",out value)){var icons=value as Dictionary<string,object>;if(icons!=null)foreach(var pair in icons.OrderBy(p=>p.Key=="48"?0:p.Key=="32"?1:2)){
      string relative=pair.Value as string;if(string.IsNullOrEmpty(relative))continue;string root=Path.GetFullPath(record.Folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;string path=Path.GetFullPath(Path.Combine(root,relative));
      if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!File.Exists(path)||new FileInfo(path).Length>1024*1024)continue;
      ExtensionPackage.CheckParents(Path.GetDirectoryName(path));if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)continue;
      using(var stream=File.OpenRead(path)){var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=32;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();return new Image {Source=bitmap,Width=30,Height=30,Stretch=Stretch.Uniform};}
     }}
    }
   }catch(Exception e){if(!FaviconCache.IsExpected(e))throw;}
   return new TextBlock {Text="\uEA86",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=22,Foreground=Brush("Accent"),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
  }
  void RenderExtensions(List<CoreWebView2BrowserExtension> installed) {
   displayedExtensions=installed.Where(e=>!IsHiddenExtension(e.Id)).ToList();if(extensionRows==null)return;extensionRows.Children.Clear();
   ExtensionUi<TextBlock>("ExtensionCount").Text=displayedExtensions.Count+" installed";
   foreach(string filter in new[]{"All","Enabled","Disabled"})ExtensionUi<Button>("Filter"+filter+"Extensions").Background=filter==extensionFilter?Brush("Hover"):Brushes.Transparent;
   string query=ExtensionUi<TextBox>("ExtensionSearch").Text.Trim();
   var visible=displayedExtensions.Where(e=>e.Name.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0&&(extensionFilter=="All"||(extensionFilter=="Enabled"?e.IsEnabled:!e.IsEnabled))).ToList();
   if(visible.Count==0){var empty=new StackPanel {Margin=new Thickness(24,27,24,27)};empty.Children.Add(new TextBlock {Text="\uEA86",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=28,Foreground=Brush("Accent"),HorizontalAlignment=HorizontalAlignment.Center});empty.Children.Add(new TextBlock {Text=displayedExtensions.Count==0?"Room for your favorite tools.":"No matching extensions.",FontSize=17,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,14,0,8)});empty.Children.Add(new TextBlock {Text=displayedExtensions.Count==0?"Browse the store and choose Add to Mica.":"Try another name or choose All.",Foreground=Brush("Muted"),FontSize=12,HorizontalAlignment=HorizontalAlignment.Center});extensionRows.Children.Add(new Border {Background=Brush("Panel"),CornerRadius=new CornerRadius(14),Child=empty});return;}
   foreach(var item in visible) {
    var extension=item;ExtensionRecord record;extensionRecords.TryGetValue(item.Id,out record);
    var row=new Grid {Margin=new Thickness(16)};row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(54)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
    row.Children.Add(new Border {Width=40,Height=40,Background=Brush("Hover"),CornerRadius=new CornerRadius(10),Child=ExtensionIcon(item),HorizontalAlignment=HorizontalAlignment.Left});
    var detail=new StackPanel {Margin=new Thickness(0,0,12,0),VerticalAlignment=VerticalAlignment.Center};detail.Children.Add(new TextBlock {Text=item.Name,FontSize=13,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=item.Name});detail.Children.Add(new TextBlock {Text=(item.IsEnabled?"Enabled":"Disabled")+" · "+(record==null?"Added to Mica":record.Source)+(record==null||string.IsNullOrEmpty(record.Version)?"":" · "+record.Version),FontSize=10,Foreground=Brush("Muted"),Margin=new Thickness(0,6,0,0),TextTrimming=TextTrimming.CharacterEllipsis});Grid.SetColumn(detail,1);row.Children.Add(detail);
    var controls=new StackPanel {Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};var toggle=new Button {Content=item.IsEnabled?"On":"Off",Background=item.IsEnabled?Brush("Hover"):Brush("Canvas"),Foreground=item.IsEnabled?Brush("Accent"):Brush("Muted"),MinWidth=48,Padding=new Thickness(10,7,10,7),ToolTip=(item.IsEnabled?"Disable ":"Enable ")+item.Name};System.Windows.Automation.AutomationProperties.SetName(toggle,(item.IsEnabled?"Disable ":"Enable ")+item.Name);bool enabled=!item.IsEnabled;toggle.Click+=async delegate {await RunExtensionAction(async delegate {if(extension.Id==AmbientExtensionId)await SetAmbientExtension(enabled);else await extension.EnableAsync(enabled);},"Extension "+(enabled?"enabled":"disabled")+". Reload open pages to apply the change.");};controls.Children.Add(toggle);
    var remove=new Button {Content="\uE74D",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=13,Foreground=Brush("Muted"),Margin=new Thickness(8,0,0,0),Padding=new Thickness(9),ToolTip="Remove "+item.Name};System.Windows.Automation.AutomationProperties.SetName(remove,"Remove "+item.Name);remove.Click+=async delegate {await RunExtensionAction(async delegate {await RemoveManagedExtension(extension);},"Extension removed. Reload pages to clear its changes.");};controls.Children.Add(remove);Grid.SetColumn(controls,2);row.Children.Add(controls);
    extensionRows.Children.Add(new Border {Background=Brush("Panel"),BorderBrush=Brush("Line"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Margin=new Thickness(0,0,0,9),Child=row,Tag=item.Id});
   }
  }
 }
}
