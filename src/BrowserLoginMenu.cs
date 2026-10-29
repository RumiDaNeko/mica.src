using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // Mica's own dropdown under a login field: saved accounts, a passkey, and a way to the
 // Passwords window. It replaces the engine's grey autofill list, which can't be styled.
 partial class BrowserApp {
  Popup loginMenu;WebView2 loginMenuView,loginMenuDismissed;DateTime loginMenuDismissedAt;
  // The page decides where the menu appears, so it could put it under a click meant for the page:
  // a row only fills once the menu has been on screen long enough to be seen.
  DateTime loginMenuArmedAt;
  readonly List<Border> loginMenuRows=new List<Border>();
  readonly List<Action> loginMenuActions=new List<Action>();
  int loginMenuIndex=-1;

  static double Number(Dictionary<string,object> m,string key) {object v;if(!m.TryGetValue(key,out v)||v==null)return 0;try {return Convert.ToDouble(v,System.Globalization.CultureInfo.InvariantCulture);}catch(Exception){return 0;}}
  static bool Flag(Dictionary<string,object> m,string key) {object v;return m.TryGetValue(key,out v)&&v is bool&&(bool)v;}

  async void ShowLoginMenu(Func<bool> live,WebView2 view,string origin,Dictionary<string,object> m) {
   object kindValue,prefixValue;m.TryGetValue("kind",out kindValue);m.TryGetValue("prefix",out prefixValue);
   string kind=kindValue as string,prefix=(prefixValue as string)??"";bool passkey=Flag(m,"passkey");
   object whyValue;m.TryGetValue("why",out whyValue);string why=whyValue as string;
   // After a pick, the fill's own typing mustn't reopen it; focusing or clicking the field does.
   if(loginMenuDismissed==view&&(why=="input"||why=="move"||DateTime.UtcNow-loginMenuDismissedAt<TimeSpan.FromMilliseconds(600)))return;if(why=="focus"||why=="click")loginMenuDismissed=null;
   if(kind==null||(kind=="password"&&Flag(m,"typed"))){HideLoginMenu(view);return;}
   if(sessionVault==null)await OpenAutofillVault();
   if(closing||!live()||!view.IsVisible){HideLoginMenu(view);return;}
   var saved=SavedLogins(origin).Where(e=>kind!="username"||prefix.Length==0||(e.Username??"").StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).ToList();
   if(kind=="username"&&saved.Count==1&&string.Equals(saved[0].Username,prefix,StringComparison.Ordinal))saved.Clear(); // Already typed in full.
   bool locked=sessionVault==null&&File.Exists(VaultFile);
   if(saved.Count==0&&!passkey&&!locked){HideLoginMenu(view);return;}

   loginMenuRows.Clear();loginMenuActions.Clear();loginMenuIndex=-1;
   double zoom=view.ZoomFactor<=0?1:view.ZoomFactor,width=Math.Max(280,Math.Min(420,Number(m,"w")*zoom));
   var list=new StackPanel();
   var card=new Border {Width=width,CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),Padding=new Thickness(6),Margin=new Thickness(12,4,12,18),Resources=window.Resources,Child=list,Tag="login-menu"};
   card.SetResourceReference(Border.BackgroundProperty,"PopupPanel");card.SetResourceReference(Border.BorderBrushProperty,"PopupLine");
   card.Effect=new System.Windows.Media.Effects.DropShadowEffect {BlurRadius=24,ShadowDepth=5,Opacity=0.16,Color=Color.FromRgb(0x25,0x20,0x32)};
   var heading=DialogText("Saved in Mica",11,"Muted");heading.Margin=new Thickness(10,6,10,4);list.Children.Add(heading);
   foreach(var entry in saved.Take(5)) {
    var e=entry;string name=string.IsNullOrEmpty(e.Username)?"(no username)":e.Username;
    AddLoginMenuRow(list,"",name,"••••••••",delegate {HideLoginMenu(view);view.Focus();FillLogin(view,origin,e,true);});
   }
   if(saved.Count>5)list.Children.Add(MenuNote("+"+(saved.Count-5)+" more in Passwords"));
   if(locked)AddLoginMenuRow(list,"","Fill your older saved passwords","Enter your master password once; this device remembers it",async delegate {
    HideLoginMenu(view);if(await UnlockSessionVault(live,null)&&live()){view.Focus();try {await view.CoreWebView2.ExecuteScriptAsync("window.__micaRetell&&window.__micaRetell()");}catch(Exception){}}
   });
   if(passkey) {
    if(list.Children.Count>1)list.Children.Add(MenuDivider());
    AddLoginMenuRow(list,"","Use a passkey","Windows Hello, a phone or a security key",async delegate {
     HideLoginMenu(view);view.Focus();try {await view.CoreWebView2.ExecuteScriptAsync("window.__micaUsePasskey&&window.__micaUsePasskey()");}catch(Exception){}
    });
   }
   list.Children.Add(MenuDivider());
   var manage=new Button {Content="Manage passwords…",HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,0,0,2),FontSize=12,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Cursor=Cursors.Hand,Focusable=false};
   manage.SetResourceReference(Control.ForegroundProperty,"Accent");manage.Click+=delegate {HideLoginMenu(view);ShowVault();};list.Children.Add(manage);

   if(loginMenu==null) {
    loginMenu=new Popup {AllowsTransparency=true,StaysOpen=true,Placement=PlacementMode.Relative,PopupAnimation=PopupAnimation.None,Focusable=false};
    loginMenu.Opened+=delegate {
     IntPtr handle=PopupHandle(loginMenu);var owner=loginMenuView==null?null:Window.GetWindow(loginMenuView);if(handle==IntPtr.Zero||owner==null)return;
     SetWindowLongPtr(handle,-8,new System.Windows.Interop.WindowInteropHelper(owner).Handle);SetWindowPos(handle,new IntPtr(-2),0,0,0,0,0x0001|0x0002|0x0010);
    };
    window.LocationChanged+=delegate {HideLoginMenu(null);};window.SizeChanged+=delegate {HideLoginMenu(null);};window.Deactivated+=delegate {if(loginMenu!=null&&!loginMenu.IsMouseOver)HideLoginMenu(null);};
   }
   if(loginMenuView!=view&&loginMenuView!=null)HideLoginMenu(null);
   if(loginMenuView!=view){view.IsVisibleChanged+=LoginViewHidden;}
   loginMenuView=view;
   loginMenu.IsOpen=false;loginMenu.Child=card;loginMenu.PlacementTarget=view;
   loginMenu.HorizontalOffset=Number(m,"x")*zoom-12;loginMenu.VerticalOffset=(Number(m,"y")+Number(m,"h"))*zoom;
   loginMenuArmedAt=DateTime.UtcNow.AddMilliseconds(PromptArmDelay);loginMenu.IsOpen=true;
   SetMenuFlags(view,true);
  }
  void LoginViewHidden(object sender,DependencyPropertyChangedEventArgs e) {if(!(bool)e.NewValue)HideLoginMenu((WebView2)sender);}

  void AddLoginMenuRow(StackPanel list,string glyph,string title,string detail,Action action) {
   int index=loginMenuRows.Count;
   var row=new Border {CornerRadius=new CornerRadius(8),Padding=new Thickness(8,7,10,7),Background=Brushes.Transparent,Cursor=Cursors.Hand,Tag="login-row"};
   var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(40)});grid.ColumnDefinitions.Add(new ColumnDefinition());row.Child=grid;
   var badge=new Border {Width=30,Height=30,CornerRadius=new CornerRadius(15),HorizontalAlignment=HorizontalAlignment.Left};badge.SetResourceReference(Border.BackgroundProperty,"Hover");
   var icon=new TextBlock {Text=glyph,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=13,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};icon.SetResourceReference(TextBlock.ForegroundProperty,"Accent");badge.Child=icon;grid.Children.Add(badge);
   var text=new StackPanel {VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(text,1);grid.Children.Add(text);
   var main=DialogText(title,13,"Ink");main.FontWeight=FontWeights.SemiBold;main.TextWrapping=TextWrapping.NoWrap;main.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(main);
   var sub=DialogText(detail,11,"Muted");sub.TextWrapping=TextWrapping.NoWrap;sub.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(sub);
   row.MouseEnter+=delegate {HighlightLoginRow(index);};
   Action pick=delegate {loginMenuDismissed=loginMenuView;loginMenuDismissedAt=DateTime.UtcNow;action();};
   row.MouseLeftButtonUp+=delegate(object s,MouseButtonEventArgs e) {e.Handled=true;if(DateTime.UtcNow>=loginMenuArmedAt||simulatedLoginKeys)pick();};
   loginMenuRows.Add(row);loginMenuActions.Add(pick);list.Children.Add(row);
  }
  TextBlock MenuNote(string text) {var t=DialogText(text,11,"Muted");t.Margin=new Thickness(10,2,10,4);return t;}
  Border MenuDivider() {var line=new Border {Height=1,Margin=new Thickness(6,5,6,5)};line.SetResourceReference(Border.BackgroundProperty,"Line");return line;}
  void HighlightLoginRow(int index) {
   loginMenuIndex=index;
   for(int i=0;i<loginMenuRows.Count;i++){if(i==index)loginMenuRows[i].SetResourceReference(Border.BackgroundProperty,"Hover");else loginMenuRows[i].Background=Brushes.Transparent;}
   if(loginMenuView!=null)SetMenuFlags(loginMenuView,true);
  }
  // Keys arrive from the page's own script, which the page could fake. Picking a login fills a
  // password the page can then read, so choosing a row (arrows) and picking it (Enter) only count while
  // that key is held down right now, in this window. "Pressed at some point" isn't enough:
  // an Enter typed into a chat box earlier would otherwise do.
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
  internal bool simulatedLoginKeys; // Tests drive the menu without a keyboard.
  bool RealKeyDown(WebView2 view,int virtualKey) {return simulatedLoginKeys||window.IsActive&&(GetAsyncKeyState(virtualKey)&0x8000)!=0;}
  void LoginMenuKey(WebView2 view,string key) {
   if(loginMenu==null||!loginMenu.IsOpen||loginMenuView!=view||loginMenuRows.Count==0){if(key=="Escape")HideLoginMenu(view);return;}
   if(key=="ArrowDown"&&RealKeyDown(view,0x28))HighlightLoginRow((loginMenuIndex+1)%loginMenuRows.Count);
   else if(key=="ArrowUp"&&RealKeyDown(view,0x26))HighlightLoginRow(loginMenuIndex<=0?loginMenuRows.Count-1:loginMenuIndex-1);
   else if(key=="Enter"&&loginMenuIndex>=0&&RealKeyDown(view,0x0D))loginMenuActions[loginMenuIndex]();
   else if(key=="Escape"||key=="Tab"){loginMenuDismissed=view;loginMenuDismissedAt=DateTime.UtcNow;HideLoginMenu(view);}
  }
  async void SetMenuFlags(WebView2 view,bool open) {
   var core=LiveCore(view);if(core==null)return;
   try {await core.ExecuteScriptAsync("window.__micaMenuOpen="+(open?"true":"false")+";window.__micaMenuPick="+(open&&loginMenuIndex>=0?"true":"false"));}catch(Exception){}
  }
  // view == null hides it whatever page it belongs to.
  void HideLoginMenu(WebView2 view) {
   if(loginMenu==null||!loginMenu.IsOpen||(view!=null&&view!=loginMenuView))return;
   loginMenu.IsOpen=false;loginMenuIndex=-1;var was=loginMenuView;
   if(was!=null){was.IsVisibleChanged-=LoginViewHidden;SetMenuFlags(was,false);}
   loginMenuView=null;
  }
 }
}
