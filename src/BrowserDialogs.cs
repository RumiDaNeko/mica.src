using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Mica {
 partial class BrowserApp {
  Window shortcutsWindow;
  TextBlock DialogText(string text,double size,string color) {
   var label=new TextBlock {Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap};label.SetResourceReference(TextBlock.ForegroundProperty,color);return label;
  }
  // When an owned window closes, Windows activates the next window in z-order,
  // which can be another app: Mica then drops behind it and looks hidden.
  // Hand activation back to the owner while the dialog is still open.
  static void KeepOwnerInFront(Window dialog) {
   dialog.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e) {
    var owner=dialog.Owner;if(e.Cancel||owner==null||!dialog.IsActive||!owner.IsVisible)return;
    owner.IsEnabled=true;owner.Activate();
   };
  }
  Window CreateMicaDialog(Window owner,string title,double width,double height,out StackPanel body,out StackPanel actions) {
   double grow=UiScale;var dialog=new Window {Owner=owner,Title=title,Width=Math.Min(width*grow+24,SystemParameters.WorkArea.Width-16),Height=Math.Min(height*grow+26,SystemParameters.WorkArea.Height-16),WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,FontFamily=window.FontFamily,FontSize=13,Resources=window.Resources};
   dialog.SetResourceReference(Window.ForegroundProperty,"Ink");KeepOwnerInFront(dialog);
   // Short dialogs end where their content ends; long ones scroll inside the old size.
   dialog.MaxHeight=dialog.Height;dialog.SizeToContent=SizeToContent.Height;
   var card=new Border {CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),Padding=new Thickness(22,18,22,20),Margin=new Thickness(12,8,12,18)};Scaled(card);card.SetResourceReference(Border.BackgroundProperty,"Canvas");card.SetResourceReference(Border.BorderBrushProperty,"Line");card.Effect=new System.Windows.Media.Effects.DropShadowEffect {BlurRadius=26,ShadowDepth=5,Opacity=0.16,Color=Color.FromRgb(0x25,0x20,0x32)};card.Resources.MergedDictionaries.Add(DialogStyles.Create());dialog.Content=card;
   var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition());layout.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});card.Child=layout;
   // Header: an icon badge, the title, and a quiet line saying what kind of window this is.
   var header=new Grid {Margin=new Thickness(0,0,0,14),Background=Brushes.Transparent};header.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(46)});header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
   var badge=new Border {Width=34,Height=34,CornerRadius=new CornerRadius(11),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};badge.SetResourceReference(Border.BackgroundProperty,"Hover");var glyph=DialogText(DialogGlyph(title),15,"Accent");glyph.FontFamily=new FontFamily("Segoe MDL2 Assets");glyph.HorizontalAlignment=HorizontalAlignment.Center;glyph.VerticalAlignment=VerticalAlignment.Center;badge.Child=glyph;header.Children.Add(badge);
   var titles=new StackPanel {VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)};Grid.SetColumn(titles,1);var heading=DialogText(title,17,"Ink");heading.FontWeight=FontWeights.SemiBold;heading.TextWrapping=TextWrapping.NoWrap;heading.TextTrimming=TextTrimming.CharacterEllipsis;titles.Children.Add(heading);var subtitle=DialogText(DialogSubtitle(title),11,"Muted");subtitle.Tag="dialog-subtitle";subtitle.Margin=new Thickness(0,1,0,0);titles.Children.Add(subtitle);header.Children.Add(titles);
   var close=new Button {Content="\uE8BB",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=11,Padding=new Thickness(0),Width=30,Height=30,VerticalAlignment=VerticalAlignment.Center,ToolTip="Close"};close.SetResourceReference(Control.ForegroundProperty,"Muted");Grid.SetColumn(close,2);System.Windows.Automation.AutomationProperties.SetName(close,"Close");close.Click+=delegate {dialog.Close();};header.Children.Add(close);header.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(e.ChangedButton==MouseButton.Left&&e.ButtonState==MouseButtonState.Pressed)dialog.DragMove();};layout.Children.Add(header);
   body=new StackPanel();var scroll=new ScrollViewer {Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};scroll.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]=System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'><Setter Property='Width' Value='8'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'><Track x:Name='PART_Track' IsDirectionReversed='True' Orientation='Vertical' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}'><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='{DynamicResource Muted}' Opacity='0.35' CornerRadius='3' Margin='2,0'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></ControlTemplate></Setter.Value></Setter></Style>");
   Grid.SetRow(scroll,1);layout.Children.Add(scroll);
   actions=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};Grid.SetRow(actions,2);layout.Children.Add(actions);
   dialog.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){dialog.Close();e.Handled=true;}};
   dialog.Loaded+=delegate {AnimateArrival(card);};return dialog;
  }
  static string DialogGlyph(string title) {
   switch(title) {
    case "Keyboard shortcuts":return "\uE765";case "Clear browsing data":return "\uE74D";case "Site permissions":return "\uE72E";
    case "Browser task manager":return "\uE9D9";case "Find in page":return "\uE721";case "Print this page":return "\uE749";
    case "Cast":case "Cast media":return "\uE88A";case "Passwords & sync":case "Unlock your vault":case "Create your vault":case "Choose your master password":case "Unlock cloud data":return "\uE72E";
    case "Sign in":return "\uE77B";case "Developer tools":return "\uE943";case "Web engine":case "Updates":return "\uE895";case "Personalize":return "\uE790";case "Ad blocking":return "\uE8F8";
   }
   return title.EndsWith(" says")||title=="Leave this page?"?"\uE8BD":"\uE946";
  }
  static string DialogSubtitle(string title) {
   if(title=="Keyboard shortcuts")return "Quick reference";if(title=="Updates")return "Mica and its web engine"; if(title=="Personalize")return "Look and feel";if(title=="Site permissions"||title=="Ad blocking")return "Privacy";
   if(title.EndsWith(" says")||title=="Leave this page?"||title=="Sign in")return "From this page";return "Your data";
  }  void ShortcutGroup(StackPanel body,string title,string[,] entries) {
   var heading=DialogText(title,10,"Muted");heading.Margin=new Thickness(0,8,0,9);body.Children.Add(heading);
   var rows=new StackPanel();
   for(int i=0;i<entries.GetLength(0);i++) {
    var row=new Grid {Margin=new Thickness(12,6,12,6)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});var label=DialogText(entries[i,0],12,"Ink");label.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(label);
    var key=new Border {CornerRadius=new CornerRadius(5),BorderThickness=new Thickness(1),Padding=new Thickness(8,4,8,4)};key.SetResourceReference(Border.BackgroundProperty,"Canvas");key.SetResourceReference(Border.BorderBrushProperty,"Line");key.Child=DialogText(entries[i,1],10,"Accent");Grid.SetColumn(key,1);row.Children.Add(key);rows.Children.Add(row);
   }
   var group=new Border {CornerRadius=new CornerRadius(10),Padding=new Thickness(0,5,0,5),Margin=new Thickness(0,0,0,12),Child=rows};group.SetResourceReference(Border.BackgroundProperty,"Panel");body.Children.Add(group);
  }
  void ShowShortcuts() {
   if(shortcutsWindow!=null){shortcutsWindow.Activate();return;}
   StackPanel body,actions;var dialog=CreateMicaDialog(window,"Keyboard shortcuts",560,740,out body,out actions);shortcutsWindow=dialog;
   ShortcutGroup(body,"FIND & NAVIGATE",new[,] {{"Spotlight search","Ctrl+K"},{"Edit address","Ctrl+L"},{"Back / forward","Alt+← / →"},{"Reload page","Ctrl+R"}});
   ShortcutGroup(body,"TABS & WINDOWS",new[,] {{"New tab","Ctrl+T"},{"Search tabs","Ctrl+Shift+A"},{"Close unpinned tab","Ctrl+W"},{"Next tab","Ctrl+Tab"},{"Go to tab 1–9 / 10th","Ctrl+1…9 / 0"},{"New window","Ctrl+N"},{"Private window","Ctrl+Shift+N"}});
   ShortcutGroup(body,"YOUR LIBRARY",new[,] {{"Bookmark page","Ctrl+D"},{"History","Ctrl+H"},{"Downloads","Ctrl+J"},{"Focus mode","Ctrl+Shift+B"}});
   ShortcutGroup(body,"PAGE TOOLS",new[,] {{"Find in page","Ctrl+F"},{"Print / save PDF","Ctrl+P"},{"Developer tools","F12 / Ctrl+Shift+I"},{"Browser task manager","Shift+Esc"},{"scrubnote (page animations)","Alt+Shift+S"}});
   var spotlight=DialogText("In Spotlight: ↑ ↓ to select · Enter to open · Esc to close",11,"Muted");spotlight.Margin=new Thickness(0,4,0,12);body.Children.Add(spotlight);
   var note=DialogText("Inactive tabs rest after 2 minutes and reload when reopened. Audio-playing tabs stay awake. Unsaved forms may be lost when a tab rests.",11,"Muted");note.LineHeight=18;body.Children.Add(note);
   var done=new Button {Content="Done",Padding=new Thickness(22,10,22,10),IsDefault=true};done.SetResourceReference(Button.BackgroundProperty,"Hover");done.Click+=delegate {dialog.Close();};actions.Children.Add(done);dialog.Closed+=delegate {shortcutsWindow=null;};dialog.Loaded+=delegate {done.Focus();};dialog.Show();
  }
  bool ConfirmMica(Window owner,string title,string message,string acceptLabel) {
   StackPanel body,actions;var dialog=CreateMicaDialog(owner,title,520,310,out body,out actions);var detail=DialogText(message,13,"Muted");detail.LineHeight=22;body.Children.Add(detail);
   var cancel=new Button {Content="Cancel",Name="CancelAction",IsDefault=true,Padding=new Thickness(18,10,18,10),Margin=new Thickness(0,0,8,0)};cancel.Click+=delegate {dialog.DialogResult=false;};actions.Children.Add(cancel);
   var accept=new Button {Content=acceptLabel,Name="ConfirmAction",Padding=new Thickness(18,10,18,10)};accept.SetResourceReference(Button.BackgroundProperty,"Hover");accept.Click+=delegate {dialog.DialogResult=true;};actions.Children.Add(accept);dialog.Loaded+=delegate {cancel.Focus();};return dialog.ShowDialog()==true;
  }
 }
}