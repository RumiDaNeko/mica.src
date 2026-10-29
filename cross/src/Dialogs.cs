using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Mica {
 // Mica's dialogs, as on Windows (BrowserDialogs.cs CreateMicaDialog): a card in the middle of the window with an
 // icon tile, a title and a quiet line saying what kind of window it is, a close button, the body, and actions at
 // the bottom right. It is a popup, a window of its own, so it shows over the page.
 sealed partial class BrowserWindow {
  internal Popup openDialog;
  static TextBlock DialogText(string text,double size,string tone) {
   var t=new TextBlock {Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap};Res(t,TextBlock.ForegroundProperty,tone);return t;
  }
  static void SectionLabel(StackPanel body,string text) {var label=DialogText(text,10,"Muted");label.Margin=new Thickness(0,12,0,8);body.Children.Add(label);}
  static Border Card(Control child) {return Res(new Border {CornerRadius=new CornerRadius(10),Padding=new Thickness(0,4),Margin=new Thickness(0,0,0,10),Child=child},Border.BackgroundProperty,"Panel");}
  static string DialogGlyph(string title) {
   switch(title) {
    case "Keyboard shortcuts":return Glyphs.Keys;case "Clear browsing data":return Glyphs.Trash;case "Browser task manager":return Glyphs.Work;
    case "Personalize":return Glyphs.Palette;case "About Mica":return Glyphs.Info;case "Print this page":return Glyphs.Print;case "AI side panel":return Glyphs.Chat;case "Your own AI address":return Glyphs.Chat;
   }
   return Glyphs.Info;
  }
  static string DialogSubtitle(string title) {
   if(title=="Keyboard shortcuts")return "Quick reference";if(title=="About Mica")return "Version";if(title=="Print this page")return "Page tool";if(title=="Personalize")return "Look and feel";
   return "Your data";
  }
  // Built and shown at once; close() takes it down. Escape closes it too. A page tool (Print) sits in the island's
  // corner instead of the middle, as on Windows (DockToIsland).
  Popup MicaDialog(string title,double width,out StackPanel body,out StackPanel actions) {return MicaDialog(title,width,out body,out actions,false);}
  Popup MicaDialog(string title,double width,out StackPanel body,out StackPanel actions,bool corner) {
   CloseDialog();if(openMenu!=null)openMenu.IsOpen=false;
   var layout=new Grid {RowDefinitions=new RowDefinitions("Auto,*,Auto")};
   var card=Res(Res(new Border {Width=width,CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),Padding=new Thickness(22,18,22,20),Margin=new Thickness(12,8,12,18),Child=layout,BoxShadow=BoxShadows.Parse("0 5 26 0 #29252032")},Border.BackgroundProperty,"Canvas"),Border.BorderBrushProperty,"Line");
   var header=new Grid {Margin=new Thickness(0,0,0,14),ColumnDefinitions=new ColumnDefinitions("46,*,Auto")};
   var badge=Res(new Border {Width=34,Height=34,CornerRadius=new CornerRadius(11),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Child=Glyph(DialogGlyph(title),15,"accent")},Border.BackgroundProperty,"Hover");header.Children.Add(badge);
   var titles=new StackPanel {VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)};Grid.SetColumn(titles,1);
   var heading=DialogText(title,17,"Ink");heading.FontWeight=FontWeight.SemiBold;heading.TextWrapping=TextWrapping.NoWrap;heading.TextTrimming=TextTrimming.CharacterEllipsis;titles.Children.Add(heading);
   var subtitle=DialogText(DialogSubtitle(title),11,"Muted");subtitle.Margin=new Thickness(0,1,0,0);titles.Children.Add(subtitle);header.Children.Add(titles);
   var close=CaptionButton(Glyphs.Close,"Close");close.Width=30;close.Height=30;close.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(close,2);header.Children.Add(close);
   layout.Children.Add(header);
   body=new StackPanel();var scroll=new ScrollViewer {Content=body,MaxHeight=Math.Max(200,Bounds.Height-220),HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);layout.Children.Add(scroll);
   actions=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};Grid.SetRow(actions,2);layout.Children.Add(actions);
   var popup=corner?new Popup {Child=card,PlacementTarget=pages,Placement=PlacementMode.AnchorAndGravity,PlacementAnchor=Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.BottomRight,PlacementGravity=Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.TopLeft,IsLightDismissEnabled=false}
    :new Popup {Child=card,PlacementTarget=root,Placement=PlacementMode.Center,IsLightDismissEnabled=false};
   if(corner)card.Margin=new Thickness(26,22,8,14);
   close.Click+=delegate {popup.IsOpen=false;};
   card.KeyDown+=delegate(object s,KeyEventArgs e) {if(e.Key==Key.Escape){popup.IsOpen=false;e.Handled=true;}};
   popup.Closed+=delegate {root.Children.Remove(popup);if(openDialog==popup)openDialog=null;};
   root.Children.Add(popup);openDialog=popup;
   card.Opacity=0;card.RenderTransform=new TranslateTransform(0,8);
   card.Transitions=new Avalonia.Animation.Transitions {new Avalonia.Animation.DoubleTransition {Property=OpacityProperty,Duration=TimeSpan.FromMilliseconds(160)},new Avalonia.Animation.TransformOperationsTransition {Property=RenderTransformProperty,Duration=TimeSpan.FromMilliseconds(200),Easing=new Avalonia.Animation.Easings.CubicEaseOut()}};
   popup.Opened+=delegate {card.Opacity=1;card.RenderTransform=Avalonia.Media.Transformation.TransformOperations.Parse("translateY(0px)");};
   return popup;
  }
  // About Mica (the Mac app menu): the version, its engine and where it's from.
  internal void ShowAbout() {
   StackPanel body,actions;var popup=MicaDialog("About Mica",360,out body,out actions);
   var name=DialogText("Mica "+typeof(BrowserWindow).Assembly.GetName().Version.ToString(3),15,"Ink");name.FontWeight=FontWeight.SemiBold;body.Children.Add(name);
   var engine=DialogText("Chromium "+Xilium.CefGlue.CefRuntime.ChromeVersion,12,"Muted");engine.Margin=new Thickness(0,4,0,10);body.Children.Add(engine);
   body.Children.Add(DialogText("A light, calm web browser.",13,"Ink"));
   var done=DialogButton("Done",true);done.Click+=delegate {popup.IsOpen=false;};actions.Children.Add(done);
   popup.IsOpen=true;
  }
  static Button DialogButton(string text,bool primary) {
   var b=new Button {Content=text,Padding=new Thickness(18,9)};b.Classes.Add("flat");if(primary)b.Classes.Add("on");return b;
  }
  void CloseDialog() {if(openDialog!=null)openDialog.IsOpen=false;}
 }
}
