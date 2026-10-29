using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Handlers;

namespace Mica {
 // Mica never shows Chromium's own UI (its download bubble, zoom bubble, permission prompts, page dialogs, password
 // and translate bubbles, print preview): everything goes through Mica's own surfaces, as on Windows.
 static class ChromiumUi {
  // Profile settings Chromium reads at start, written before it starts. The download bubble stays shut (Mica's
  // island and Downloads page show downloads), Mica's vault keeps passwords, and nothing offers to translate.
  internal static void SeedPreferences(string profileFolder) {
   try {
    Directory.CreateDirectory(profileFolder);string path=Path.Combine(profileFolder,"Preferences");
    JsonObject root=null;try {if(File.Exists(path))root=JsonNode.Parse(File.ReadAllText(path)) as JsonObject;} catch(Exception) {}
    if(root==null)root=new JsonObject();
    Action<string,JsonNode> set=delegate(string name,JsonNode value) {
     var parts=name.Split('.');var node=root;
     for(int i=0;i<parts.Length-1;i++){var next=node[parts[i]] as JsonObject;if(next==null){next=new JsonObject();node[parts[i]]=next;}node=next;}
     node[parts[parts.Length-1]]=value;
    };
    set("download_bubble.partial_view_enabled",false);set("download.prompt_for_download",false);
    set("credentials_enable_service",false);set("credentials_enable_autosignin",false);
    set("autofill.profile_enabled",false);set("autofill.credit_card_enabled",false);
    set("translate.enabled",false);
    File.WriteAllText(path,root.ToJsonString());
   } catch(Exception) {}
  }
  // Switches for the same: no print preview (the system's own print dialog instead).
  internal static readonly string[] Switches={"disable-print-preview"};

  // Chrome style puts icons and bubbles on its own toolbar; Mica has none of it.
  internal sealed class Commands : CefCommandHandler {
   protected override bool OnChromeCommand(CefBrowser browser,int commandId,CefWindowOpenDisposition disposition) {return false;}
   protected override bool IsChromePageActionIconVisible(CefChromePageActionIconType iconType) {return false;}
   protected override bool IsChromeToolbarButtonVisible(CefChromeToolbarButtonType buttonType) {return false;}
  }
  internal static readonly Commands CommandHandler=new Commands();
 }

 sealed partial class BrowserWindow {
  internal const string CameraGlyph="M4 8h3l2-2.5h6L17 8h3v11H4z M12 10.5a3 3 0 1 1 0 6a3 3 0 1 1 0-6z",MicGlyph="M12 3.5a3 3 0 0 1 3 3v5a3 3 0 0 1-6 0v-5a3 3 0 0 1 3-3z M6.5 11a5.5 5.5 0 0 0 11 0 M12 16.5V20",
   LocationGlyph="M12 21s-6-6-6-10.5a6 6 0 0 1 12 0C18 15 12 21 12 21z M12 8.5a2 2 0 1 1 0 4a2 2 0 1 1 0-4z",BellGlyph="M6.5 16v-5a5.5 5.5 0 0 1 11 0v5l1.5 2h-14z M10 20.5a2 2 0 0 0 4 0";

  // ── Site permissions (BrowserPermissions.cs WatchSitePermissions): a card in the island's corner asks, Allow or
  // Block; Chromium keeps the choice for the site, as Chrome does. Requests queue; a prompt the page gave up on
  // (it navigated) goes away.
  sealed class SitePermissions : CefPermissionHandler {
   readonly BrowserWindow window;readonly Func<bool> alive;
   public SitePermissions(BrowserWindow w,Func<bool> a) {window=w;alive=a;}
   protected override bool OnShowPermissionPrompt(CefBrowser browser,ulong promptId,string requestingOrigin,CefPermissionRequestTypes requested,CefPermissionPromptCallback callback) {
    Dispatcher.UIThread.Post(delegate {window.AskPermission(alive,promptId,requestingOrigin,requested,callback);});
    return true;
   }
   protected override void OnDismissPermissionPrompt(CefBrowser browser,ulong promptId,CefPermissionRequestResult result) {
    Dispatcher.UIThread.Post(delegate {window.DropPermission(promptId);});
   }
  }
  static string PermissionQuestion(CefPermissionRequestTypes kind) {
   var words=new List<string>();
   if((kind&CefPermissionRequestTypes.CameraStream)!=0||(kind&CefPermissionRequestTypes.CameraPanTiltZoom)!=0)words.Add("use your camera");
   if((kind&CefPermissionRequestTypes.MicStream)!=0)words.Add("use your microphone");
   if((kind&CefPermissionRequestTypes.Geolocation)!=0)words.Add("know your location");
   if((kind&CefPermissionRequestTypes.Notifications)!=0)words.Add("show notifications");
   if((kind&CefPermissionRequestTypes.Clipboard)!=0)words.Add("see text and images copied to the clipboard");
   if((kind&CefPermissionRequestTypes.MultipleDownloads)!=0)words.Add("download multiple files");
   if((kind&CefPermissionRequestTypes.FileSystemAccess)!=0)words.Add("edit files on your device");
   if((kind&CefPermissionRequestTypes.LocalFonts)!=0)words.Add("use fonts installed on your device");
   if((kind&CefPermissionRequestTypes.MidiSysex)!=0)words.Add("control and reprogram MIDI devices");
   if((kind&CefPermissionRequestTypes.WindowManagement)!=0)words.Add("manage windows on all your displays");
   if((kind&(CefPermissionRequestTypes.StorageAccess|CefPermissionRequestTypes.TopLevelStorageAccess))!=0)words.Add("use cookies and data it saved on other sites");
   if((kind&CefPermissionRequestTypes.IdleDetection)!=0)words.Add("know when you're using this device");
   if(words.Count==0)words.Add("use a feature of this device");
   return words.Count==1?words[0]:string.Join(", ",words.GetRange(0,words.Count-1))+" and "+words[words.Count-1];
  }
  static string PermissionGlyph(CefPermissionRequestTypes kind) {
   if((kind&(CefPermissionRequestTypes.CameraStream|CefPermissionRequestTypes.CameraPanTiltZoom))!=0)return CameraGlyph;
   if((kind&CefPermissionRequestTypes.MicStream)!=0)return MicGlyph;
   if((kind&CefPermissionRequestTypes.Geolocation)!=0)return LocationGlyph;
   if((kind&CefPermissionRequestTypes.Notifications)!=0)return BellGlyph;
   return Glyphs.Lock;
  }
  static string OriginLabel(string origin) {Uri uri;return Uri.TryCreate(origin,UriKind.Absolute,out uri)&&!string.IsNullOrEmpty(uri.Host)?uri.Host:origin;}

  readonly Queue<Action> sitePromptQueue=new Queue<Action>();
  readonly Dictionary<ulong,Action> sitePromptClosers=new Dictionary<ulong,Action>();
  internal Popup sitePrompt;internal string sitePromptText;
  void AskPermission(Func<bool> pageAlive,ulong promptId,string origin,CefPermissionRequestTypes kind,CefPermissionPromptCallback callback) {
   bool answered=false;
   Action<CefPermissionRequestResult> answer=delegate(CefPermissionRequestResult result) {if(answered)return;answered=true;sitePromptClosers.Remove(promptId);try {callback.Continue(result);} catch(Exception) {}};
   if(!pageAlive()){answer(CefPermissionRequestResult.Ignore);return;}
   sitePromptClosers[promptId]=delegate {answer(CefPermissionRequestResult.Ignore);};
   AskSite(PermissionGlyph(kind),OriginLabel(origin)+" wants to",PermissionQuestion(kind),"Allow","Block",
    ()=>!answered&&pageAlive(),
    delegate(bool? allow) {answer(allow==true?CefPermissionRequestResult.Accept:allow==false?CefPermissionRequestResult.Deny:CefPermissionRequestResult.Dismiss);});
  }
  void DropPermission(ulong promptId) {Action close;if(sitePromptClosers.TryGetValue(promptId,out close))close();}

  // The card: as on Windows (AskSiteBubble), 340 wide, radius 14, an icon tile, who's asking, what, and the two
  // choices. Block sits in the corner where a page can lure a click; clicks in its first 700 ms are ignored.
  void AskSite(string glyph,string title,string subject,string acceptLabel,string declineLabel,Func<bool> alive,Action<bool?> done) {
   sitePromptQueue.Enqueue(delegate {
    if(!alive()){done(null);NextSitePrompt();return;}
    var grid=new Grid {ColumnDefinitions=new ColumnDefinitions("44,*")};
    var badge=Res(new Border {Width=32,Height=32,CornerRadius=new CornerRadius(10),VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Left,Child=Glyph(glyph,15,"accent")},Border.BackgroundProperty,"Hover");grid.Children.Add(badge);
    var stack=new StackPanel();Grid.SetColumn(stack,1);grid.Children.Add(stack);
    var heading=DialogText(title,11,"Muted");heading.Margin=new Thickness(0,0,26,2);stack.Children.Add(heading);
    var what=DialogText(subject,14,"Ink");what.FontWeight=FontWeight.SemiBold;stack.Children.Add(what);
    var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};stack.Children.Add(buttons);
    var accept=DialogButton(acceptLabel,true);accept.Margin=new Thickness(0,0,6,0);accept.Tag="site-accept";buttons.Children.Add(accept);
    var decline=DialogButton(declineLabel,false);decline.Tag="site-decline";buttons.Children.Add(decline);
    var dismiss=CaptionButton(Glyphs.Close,"Dismiss");dismiss.Width=24;dismiss.Height=24;dismiss.HorizontalAlignment=HorizontalAlignment.Right;dismiss.VerticalAlignment=VerticalAlignment.Top;dismiss.Margin=new Thickness(0,-6,-6,0);Grid.SetColumn(dismiss,1);grid.Children.Add(dismiss);
    var card=Res(Res(new Border {Width=340,CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),Padding=new Thickness(16,14,14,14),Margin=new Thickness(14,4,8,14),Child=grid,BoxShadow=BoxShadows.Parse("0 6 28 0 #2E252032")},Border.BackgroundProperty,"Panel"),Border.BorderBrushProperty,"Line");
    var popup=new Popup {Child=card,PlacementTarget=pages,Placement=PlacementMode.AnchorAndGravity,PlacementAnchor=Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.BottomRight,PlacementGravity=Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.TopLeft,IsLightDismissEnabled=false,ShouldUseOverlayLayer=false};
    var armed=DateTime.UtcNow.AddMilliseconds(700);bool finished=false;
    Action<bool?> finish=delegate(bool? choice) {
     if(finished)return;finished=true;popup.IsOpen=false;root.Children.Remove(popup);
     if(sitePrompt==popup){sitePrompt=null;sitePromptText=null;}
     islandCard.Opacity=1;done(choice);NextSitePrompt();
    };
    accept.Click+=delegate {if(DateTime.UtcNow>=armed)finish(true);};
    decline.Click+=delegate {if(DateTime.UtcNow>=armed)finish(false);};
    dismiss.Click+=delegate {finish(null);};
    card.KeyDown+=delegate(object s,KeyEventArgs e) {if(e.Key==Key.Escape){finish(null);e.Handled=true;}};
    popup.Tag=(Action)delegate {finish(null);};
    // It grows out of the island's corner and stands in for it while it asks.
    card.Opacity=0;card.RenderTransform=Avalonia.Media.Transformation.TransformOperations.Parse("scale(0.96) translateY(6px)");
    card.Transitions=new Avalonia.Animation.Transitions {new Avalonia.Animation.DoubleTransition {Property=OpacityProperty,Duration=TimeSpan.FromMilliseconds(160)},new Avalonia.Animation.TransformOperationsTransition {Property=RenderTransformProperty,Duration=TimeSpan.FromMilliseconds(220),Easing=new Avalonia.Animation.Easings.CubicEaseOut()}};
    popup.Opened+=delegate {card.Opacity=1;card.RenderTransform=Avalonia.Media.Transformation.TransformOperations.Parse("scale(1) translateY(0px)");};
    root.Children.Add(popup);sitePrompt=popup;sitePromptText=title+" "+subject;islandCard.Opacity=0;popup.IsOpen=true;
    // The page can give up on it (navigating away), or the tab can go: then it goes too.
    DispatcherTimer watch=null;watch=new DispatcherTimer(TimeSpan.FromMilliseconds(500),DispatcherPriority.Background,delegate {if(finished){watch.Stop();return;}if(!alive()){watch.Stop();finish(null);}});watch.Start();
   });
   if(sitePrompt==null&&sitePromptQueue.Count==1)NextSitePrompt();
  }
  void NextSitePrompt() {if(sitePrompt!=null||sitePromptQueue.Count==0)return;sitePromptQueue.Dequeue()();}
  // The self-test answers the card as a click would (after its 700 ms).
  internal void AnswerSitePrompt(bool allow) {
   if(sitePrompt==null)return;var card=(Border)sitePrompt.Child;
   foreach(var b in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(card))if(b is Button&&(string)((Button)b).Tag==(allow?"site-accept":"site-decline")){((Button)b).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));return;}
  }

  // ── Page dialogs (BrowserPermissions.cs WatchPageDialogs): alert, confirm, prompt and "leave this page?" as Mica
  // dialogs. Only the page in front may show one, and a page that keeps opening them is silenced until it navigates.
  sealed class PageDialogs : JSDialogHandler {
   readonly BrowserWindow window;readonly CrossTab tab;
   readonly List<DateTime> recent=new List<DateTime>();bool silenced;
   public PageDialogs(BrowserWindow w,CrossTab t) {window=w;tab=t;}
   protected override bool OnJSDialog(CefBrowser browser,string originUrl,CefJSDialogType dialogType,string message,string defaultText,CefJSDialogCallback callback,out bool suppress) {
    suppress=false;string origin=OriginLabel(originUrl);
    Dispatcher.UIThread.Post(delegate {
     if(tab!=window.Active||window.SpotlightOpen){window.Status("A background tab ("+origin+") tried to show a dialog. Mica dismissed it.");callback.Continue(false,null);return;}
     var now=DateTime.UtcNow;recent.RemoveAll(t=>now-t>TimeSpan.FromSeconds(10));recent.Add(now);
     if(silenced||recent.Count>3){if(!silenced)window.Status("Mica stopped "+origin+" from opening more dialogs.");silenced=true;callback.Continue(false,null);return;}
     window.PageDialog(dialogType,origin,message,defaultText,false,callback);
    });
    return true;
   }
   protected override bool OnBeforeUnloadDialog(CefBrowser browser,string messageText,bool isReload,CefJSDialogCallback callback) {
    Dispatcher.UIThread.Post(delegate {window.PageDialog(CefJSDialogType.Confirm,null,null,null,true,callback);});
    return true;
   }
   protected override void OnResetDialogState(CefBrowser browser) {Dispatcher.UIThread.Post(delegate {silenced=false;recent.Clear();window.ClosePageDialog();});}
   protected override void OnDialogClosed(CefBrowser browser) {}
  }
  Action pageDialogCancel;
  internal void ClosePageDialog() {var cancel=pageDialogCancel;pageDialogCancel=null;if(cancel!=null)cancel();}
  void PageDialog(CefJSDialogType kind,string origin,string message,string defaultText,bool leave,CefJSDialogCallback callback) {
   ClosePageDialog();
   StackPanel body,actions;var popup=MicaDialog(leave?"Leave this page?":"This page says",380,out body,out actions);
   bool answered=false;TextBox input=null;
   Action<bool> answer=delegate(bool ok) {if(answered)return;answered=true;pageDialogCancel=null;try {callback.Continue(ok,input==null?null:input.Text??"");} catch(Exception) {}if(popup.IsOpen)popup.IsOpen=false;};
   if(!string.IsNullOrEmpty(origin)){var from=DialogText(origin,11,"Muted");from.TextWrapping=TextWrapping.NoWrap;from.TextTrimming=TextTrimming.CharacterEllipsis;from.Margin=new Thickness(0,0,0,12);from.Classes.Add("user");body.Children.Add(from);}
   var detail=DialogText(leave?"Changes you made may not be saved.":message??"",13,leave?"Muted":"Ink");detail.LineHeight=22;if(!leave)detail.Classes.Add("user");body.Children.Add(detail);
   if(kind==CefJSDialogType.Prompt){input=new TextBox {Text=defaultText??"",Margin=new Thickness(0,14,0,0)};body.Children.Add(input);}
   if(kind!=CefJSDialogType.Alert){var cancel=DialogButton(leave?"Stay":"Cancel",false);cancel.Margin=new Thickness(0,0,8,0);cancel.Tag="dialog-cancel";cancel.Click+=delegate {answer(false);};actions.Children.Add(cancel);}
   var ok=DialogButton(leave?"Leave":"OK",true);ok.Tag="dialog-ok";ok.Click+=delegate {answer(true);};actions.Children.Add(ok);
   // Closing it any other way (Escape, the close button) is Cancel; an alert has only OK.
   popup.Closed+=delegate {answer(kind==CefJSDialogType.Alert&&!leave);};
   pageDialogCancel=delegate {answer(false);};
   popup.IsOpen=true;
   Dispatcher.UIThread.Post(delegate {if(input!=null){input.Focus();input.SelectAll();}else ok.Focus();},DispatcherPriority.Background);
  }
 }
}
