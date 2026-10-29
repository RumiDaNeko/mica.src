using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Shapes;

namespace Mica {
 // Vault layout in Mica's language: Georgia headings, small-caps eyebrows,
 // soft cards. The vault logic in BrowserVault.cs creates the controls and
 // command buttons (identified by Tag); this file only arranges them.
 partial class BrowserApp {
  StackPanel vaultBody;
  static IEnumerable<T> VaultElements<T>(DependencyObject node) where T:DependencyObject {
   foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) {var match=child as T;if(match!=null)yield return match;foreach(var nested in VaultElements<T>(child))yield return nested;}
  }
  Border VaultCard(string title,string glyph,out StackPanel content) {
   content=new StackPanel();
   if(title!=null) {var heading=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,14)};var icon=DialogText(glyph,15,"Accent");icon.FontFamily=new FontFamily("Segoe MDL2 Assets");icon.Margin=new Thickness(0,1,10,0);heading.Children.Add(icon);var text=DialogText(title,14,"Ink");text.FontWeight=FontWeights.SemiBold;heading.Children.Add(text);content.Children.Add(heading);}
   var card=new Border {Child=content,CornerRadius=new CornerRadius(14),Padding=new Thickness(18),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,0,12)};card.SetResourceReference(Border.BackgroundProperty,"Panel");card.SetResourceReference(Border.BorderBrushProperty,"Line");return card;
  }
  TextBlock VaultEyebrow(string text) {var label=DialogText(text,9,"Muted");label.FontWeight=FontWeights.SemiBold;label.Margin=new Thickness(0,0,0,8);return label;}
  static Button VaultPrimary(Button button) {button.SetResourceReference(Button.BackgroundProperty,"Hover");return button;}
  FrameworkElement VaultMonogram(string text,double size) {
   var badge=new Border {Width=size,Height=size,CornerRadius=new CornerRadius(size*0.3),VerticalAlignment=VerticalAlignment.Center};badge.SetResourceReference(Border.BackgroundProperty,"Hover");
   var letter=DialogText(string.IsNullOrEmpty(text)?"?":text.Substring(0,1).ToUpperInvariant(),size*0.42,"Accent");letter.FontFamily=Localizer.Serif;letter.HorizontalAlignment=HorizontalAlignment.Center;letter.VerticalAlignment=VerticalAlignment.Center;badge.Child=letter;return badge;
  }
  static string VaultSite(string origin) {Uri uri;return Uri.TryCreate(origin??"",UriKind.Absolute,out uri)?uri.Host:(origin??"");}
  void ArrangeVault(StackPanel root,StackPanel footer,TextBlock message,TextBlock accountLabel,PasswordBox master,ListBox list,TextBox origin,TextBox username,PasswordBox secret,TextBox totp) {
   vaultBody=root;var buttons=root.Children.OfType<Button>().ToDictionary(b=>(string)b.Content);root.Children.Clear();
   Func<string,string,string,Button> action=(key,title,glyph)=>{var button=buttons[key];button.Tag=key;System.Windows.Automation.AutomationProperties.SetName(button,key);button.Padding=new Thickness(12,9,14,9);button.Margin=new Thickness(0,0,6,6);var row=new StackPanel {Orientation=Orientation.Horizontal};var icon=DialogText(glyph,13,"Accent");icon.FontFamily=new FontFamily("Segoe MDL2 Assets");icon.VerticalAlignment=VerticalAlignment.Center;icon.Margin=new Thickness(0,0,8,0);row.Children.Add(icon);row.Children.Add(DialogText(title,12,"Ink"));button.Content=row;button.ToolTip=key;return button;};
   string fields="<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>";
   foreach(string type in new[]{"TextBox","PasswordBox"})fields+="<Style TargetType='"+type+"'><Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='Background' Value='{DynamicResource Canvas}'/><Setter Property='BorderBrush' Value='{DynamicResource Line}'/><Setter Property='CaretBrush' Value='{DynamicResource Accent}'/><Setter Property='BorderThickness' Value='1'/><Setter Property='FontSize' Value='13'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='"+type+"'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='9'><ScrollViewer x:Name='PART_ContentHost' Margin='12,9'/></Border><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>";
   root.Resources=(ResourceDictionary)XamlReader.Parse(fields+"</ResourceDictionary>");

   // Segmented switch between the two pages.
   var switcher=new Border {CornerRadius=new CornerRadius(12),Padding=new Thickness(4),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,18),BorderThickness=new Thickness(1)};switcher.SetResourceReference(Border.BackgroundProperty,"Panel");switcher.SetResourceReference(Border.BorderBrushProperty,"Line");
   var navigation=new StackPanel {Orientation=Orientation.Horizontal};switcher.Child=navigation;
   var passwordsTab=new Button {Content="Passwords",Padding=new Thickness(18,8,18,8),Margin=new Thickness(0,0,4,0)};var syncTab=new Button {Content="Account & sync",Padding=new Thickness(18,8,18,8)};navigation.Children.Add(passwordsTab);navigation.Children.Add(syncTab);
   // Header: page switch on the left, the vault's lock on the right.
   var top=new Grid();top.Children.Add(switcher);root.Children.Add(top);
   vaultLockButton=new Button {Padding=new Thickness(14,9,16,9),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Tag="vault-lock"};vaultLockButton.SetResourceReference(Button.BackgroundProperty,"Hover");
   vaultLockButton.Click+=delegate {if(vaultUnlocked){if(vaultLockNow!=null)vaultLockNow();}else if(vaultOpenUnlock!=null)vaultOpenUnlock();};top.Children.Add(vaultLockButton);
   var passwordsPage=new StackPanel();var syncPage=new StackPanel {Visibility=Visibility.Collapsed};root.Children.Add(passwordsPage);root.Children.Add(syncPage);
   Action<bool> select=showSync=>{passwordsPage.Visibility=showSync?Visibility.Collapsed:Visibility.Visible;syncPage.Visibility=showSync?Visibility.Visible:Visibility.Collapsed;foreach(var tab in new[]{passwordsTab,syncTab}){if((tab==syncTab)==showSync)tab.SetResourceReference(Button.BackgroundProperty,"Hover");else tab.Background=Brushes.Transparent;}if(showSync)AnimateArrival(syncPage);else AnimateArrival(passwordsPage);};passwordsTab.Click+=delegate {select(false);};syncTab.Click+=delegate {select(true);};select(false);
   PrepareVaultUnlock(root,master,buttons["Unlock / create local vault"],message);

   // ── Passwords: list on the left, details / editor on the right ──
   var columns=new Grid();columns.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});columns.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1.15,GridUnitType.Star)});passwordsPage.Children.Add(columns);
   StackPanel saved;var savedCard=VaultCard(null,null,out saved);savedCard.Margin=new Thickness(0,0,12,0);savedCard.MinHeight=440;columns.Children.Add(savedCard);
   var savedHeader=new Grid {Margin=new Thickness(0,0,0,12)};var savedTitles=new StackPanel();savedTitles.Children.Add(VaultEyebrow("YOUR LOGINS"));var savedCount=DialogText("Saved passwords",18,"Ink");savedCount.FontFamily=Localizer.Serif;savedTitles.Children.Add(savedCount);savedHeader.Children.Add(savedTitles);
   var newLogin=VaultPrimary(action("New credential","New","\uE710"));newLogin.HorizontalAlignment=HorizontalAlignment.Right;newLogin.VerticalAlignment=VerticalAlignment.Bottom;newLogin.Margin=new Thickness(0);savedHeader.Children.Add(newLogin);saved.Children.Add(savedHeader);
   list.Height=330;list.Background=Brushes.Transparent;list.BorderThickness=new Thickness(0);list.SetResourceReference(Control.ForegroundProperty,"Ink");ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
   list.ItemTemplate=(DataTemplate)XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Margin='-4,-3'><Grid.ColumnDefinitions><ColumnDefinition Width='42'/><ColumnDefinition/></Grid.ColumnDefinitions><Border Width='30' Height='30' CornerRadius='9' Background='{DynamicResource Canvas}' VerticalAlignment='Center' HorizontalAlignment='Left'><TextBlock Text='&#xE72E;' FontFamily='Segoe MDL2 Assets' FontSize='12' Foreground='{DynamicResource Accent}' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><StackPanel Grid.Column='1' VerticalAlignment='Center'><TextBlock Text='{Binding Origin}' FontWeight='SemiBold' FontSize='12' TextTrimming='CharacterEllipsis'/><TextBlock Text='{Binding Username}' FontSize='11' Foreground='{DynamicResource Muted}' Margin='0,3,0,0' TextTrimming='CharacterEllipsis'/></StackPanel></Grid></DataTemplate>");
   var empty=new StackPanel {Margin=new Thickness(4,40,4,10),HorizontalAlignment=HorizontalAlignment.Center};var emptyIcon=DialogText("\uE8D7",26,"Accent");emptyIcon.FontFamily=new FontFamily("Segoe MDL2 Assets");emptyIcon.HorizontalAlignment=HorizontalAlignment.Center;empty.Children.Add(emptyIcon);var emptyText=DialogText("Nothing saved yet.\nUnlock your vault, then add a login with New.",12,"Muted");emptyText.TextAlignment=TextAlignment.Center;emptyText.LineHeight=20;emptyText.Margin=new Thickness(0,12,0,0);empty.Children.Add(emptyText);
   saved.Children.Add(empty);saved.Children.Add(list);

   var detailHost=new Grid();Grid.SetColumn(detailHost,1);columns.Children.Add(detailHost);
   // Placeholder when nothing is selected.
   StackPanel idle;var idleCard=VaultCard(null,null,out idle);idleCard.MinHeight=440;idle.VerticalAlignment=VerticalAlignment.Center;idle.Children.Add(VaultMonogram("M",54));((FrameworkElement)idle.Children[0]).HorizontalAlignment=HorizontalAlignment.Center;
   var idleTitle=DialogText("Pick a login",20,"Ink");vaultIdleTitle=idleTitle;idleTitle.FontFamily=Localizer.Serif;idleTitle.HorizontalAlignment=HorizontalAlignment.Center;idleTitle.Margin=new Thickness(0,16,0,6);idle.Children.Add(idleTitle);
   var idleText=DialogText("Select one on the left to fill or copy it,\nor create a new one.",12,"Muted");idleText.TextAlignment=TextAlignment.Center;idleText.HorizontalAlignment=HorizontalAlignment.Center;idleText.LineHeight=20;idle.Children.Add(idleText);vaultIdleText=idleText;
   vaultIdleUnlock=VaultPrimary(new Button {Content=VaultIconLabel("\uE785","Unlock passwords"),Padding=new Thickness(16,10,18,10),Margin=new Thickness(0,18,0,0),HorizontalAlignment=HorizontalAlignment.Center,Tag="vault-unlock"});vaultIdleUnlock.Click+=delegate {if(vaultOpenUnlock!=null)vaultOpenUnlock();};idle.Children.Add(vaultIdleUnlock);
   detailHost.Children.Add(idleCard);
   // Read-only details of the selected login.
   StackPanel details;var detailsCard=VaultCard(null,null,out details);detailsCard.MinHeight=440;detailHost.Children.Add(detailsCard);
   var detailHead=new Grid {Margin=new Thickness(0,0,0,20)};detailHead.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(66)});detailHead.ColumnDefinitions.Add(new ColumnDefinition());var monogramHost=new ContentControl();detailHead.Children.Add(monogramHost);
   var detailTitles=new StackPanel {VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(detailTitles,1);detailHead.Children.Add(detailTitles);var detailSite=DialogText("",20,"Ink");detailSite.FontFamily=Localizer.Serif;detailSite.TextTrimming=TextTrimming.CharacterEllipsis;detailSite.TextWrapping=TextWrapping.NoWrap;detailTitles.Children.Add(detailSite);var detailOrigin=DialogText("",11,"Muted");detailOrigin.Margin=new Thickness(0,4,0,0);detailTitles.Children.Add(detailOrigin);details.Children.Add(detailHead);
   Func<string,TextBlock> detailRow=title=>{details.Children.Add(VaultEyebrow(title));var value=DialogText("",13,"Ink");value.Margin=new Thickness(0,0,0,18);details.Children.Add(value);return value;};
   var detailUser=detailRow("USERNAME");
   // The password stays hidden until you ask, and hides again on another login or when the vault locks.
   details.Children.Add(VaultEyebrow("PASSWORD"));var passwordRow=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,18)};details.Children.Add(passwordRow);
   var detailPassword=DialogText("••••••••••••",13,"Ink");detailPassword.Tag="vault-password-text";detailPassword.VerticalAlignment=VerticalAlignment.Center;detailPassword.TextWrapping=TextWrapping.Wrap;detailPassword.MaxWidth=300;passwordRow.Children.Add(detailPassword);
   var reveal=new Button {Padding=new Thickness(8,4,10,4),Margin=new Thickness(10,0,0,0),Content=VaultIconLabel("","Show"),Tag="vault-password-reveal",VerticalAlignment=VerticalAlignment.Center};passwordRow.Children.Add(reveal);
   bool revealed=false;
   Action hidePassword=delegate {revealed=false;detailPassword.Text="••••••••••••";detailPassword.FontFamily=window.FontFamily;reveal.Content=VaultIconLabel("","Show");};
   reveal.Click+=delegate {var entry=list.SelectedItem as VaultEntry;if(revealed||entry==null||!vaultUnlocked||string.IsNullOrEmpty(entry.Password)){hidePassword();return;}revealed=true;detailPassword.Text=entry.Password;detailPassword.FontFamily=new FontFamily("Consolas");reveal.Content=VaultIconLabel("","Hide");};
   root.Unloaded+=delegate {hidePassword();};
   // The current 2FA code, counting down; shown only for a login that has a 2FA key.
   var codeTitle=VaultEyebrow("2FA CODE");details.Children.Add(codeTitle);var codeRow=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,18)};details.Children.Add(codeRow);
   var detailCode=DialogText("",20,"Ink");detailCode.FontFamily=new FontFamily("Consolas");detailCode.Tag="vault-totp-code";detailCode.VerticalAlignment=VerticalAlignment.Center;codeRow.Children.Add(detailCode);
   var codeLeft=DialogText("",11,"Muted");codeLeft.Margin=new Thickness(10,0,12,0);codeLeft.VerticalAlignment=VerticalAlignment.Center;codeRow.Children.Add(codeLeft);
   var copyCode=new Button {Padding=new Thickness(10,6,12,6),Content=VaultIconLabel("\uE8C8","Copy code"),Tag="vault-totp-copy",VerticalAlignment=VerticalAlignment.Center};codeRow.Children.Add(copyCode);
   Action tickCode=delegate {var entry=list.SelectedItem as VaultEntry;string code=entry==null||string.IsNullOrEmpty(entry.Totp)?null:Totp.Code(entry.Totp);var visible=code!=null?Visibility.Visible:Visibility.Collapsed;codeTitle.Visibility=codeRow.Visibility=visible;if(code==null)return;detailCode.Text=code.Length==6?code.Substring(0,3)+" "+code.Substring(3):code;codeLeft.Text=Totp.SecondsLeft(entry.Totp)+"s";};
   var codeTimer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};codeTimer.Tick+=delegate {if(!root.IsLoaded){codeTimer.Stop();return;}tickCode();};codeTimer.Start();root.Unloaded+=delegate {codeTimer.Stop();};
   copyCode.Click+=delegate {var entry=list.SelectedItem as VaultEntry;string code=entry==null||string.IsNullOrEmpty(entry.Totp)?null:Totp.Code(entry.Totp);if(code!=null){CopyPassword(code);message.Text="2FA code copied.";}};
   var useActions=new WrapPanel {Margin=new Thickness(0,6,0,0)};useActions.Children.Add(VaultPrimary(action("Fill selected credential on current website","Fill on this site","\uE8A7")));useActions.Children.Add(action("Copy selected password","Copy password","\uE8C8"));details.Children.Add(useActions);
   var manageActions=new WrapPanel {Margin=new Thickness(0,4,0,0)};var edit=new Button {Padding=new Thickness(12,9,14,9),Margin=new Thickness(0,0,6,6),Content=VaultIconLabel("\uE70F","Edit")};manageActions.Children.Add(edit);manageActions.Children.Add(action("Delete selected credential","Delete","\uE74D"));details.Children.Add(manageActions);
   // Editor, opened by New or Edit.
   StackPanel editor;var editorCard=VaultCard(null,null,out editor);editorCard.MinHeight=440;detailHost.Children.Add(editorCard);
   editor.Children.Add(VaultEyebrow("CREDENTIAL"));var editorTitle=DialogText("New login",20,"Ink");editorTitle.FontFamily=Localizer.Serif;editorTitle.Margin=new Thickness(0,0,0,18);editor.Children.Add(editorTitle);
   Action<string,Control> field=(title,input)=>{var label=DialogText(title,11,"Muted");label.Margin=new Thickness(0,0,0,6);editor.Children.Add(label);input.Margin=new Thickness(0,0,0,14);System.Windows.Automation.AutomationProperties.SetName(input,title);editor.Children.Add(input);};
   field("Website (https://…)",origin);field("Username or email",username);field("Password",secret);field("2FA setup key (optional)",totp);
   var editActions=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,6,0,0)};var cancelEdit=new Button {Content="Cancel",Padding=new Thickness(16,9,16,9),Margin=new Thickness(0,0,6,0)};editActions.Children.Add(cancelEdit);var saveLogin=VaultPrimary(action("Save credential","Save login","\uE74E"));saveLogin.Margin=new Thickness(0);editActions.Children.Add(saveLogin);editor.Children.Add(editActions);

   string mode="idle";
   Action<string> show=next=>{mode=next;idleCard.Visibility=next=="idle"?Visibility.Visible:Visibility.Collapsed;detailsCard.Visibility=next=="details"?Visibility.Visible:Visibility.Collapsed;editorCard.Visibility=next=="editor"?Visibility.Visible:Visibility.Collapsed;AnimateArrival(next=="idle"?idleCard:next=="details"?detailsCard:editorCard);};
   Action showSelection=delegate {
    hidePassword();var entry=list.SelectedItem as VaultEntry;if(entry==null){if(mode!="editor")show("idle");return;}
    detailSite.Text=VaultSite(entry.Origin);detailOrigin.Text=entry.Origin;detailUser.Text=string.IsNullOrEmpty(entry.Username)?"—":entry.Username;tickCode();monogramHost.Content=VaultMonogram(VaultSite(entry.Origin),50);show("details");
   };
   list.SelectionChanged+=delegate {showSelection();};
   newLogin.Click+=delegate {if(!vaultUnlocked){if(vaultOpenUnlock!=null)vaultOpenUnlock();return;}if(list.ItemsSource!=null){editorTitle.Text="New login";show("editor");origin.Focus();}};
   edit.Click+=delegate {if(list.SelectedItem!=null){editorTitle.Text="Edit login";show("editor");}};
   cancelEdit.Click+=delegate {showSelection();if(list.SelectedItem==null)show("idle");};
   // Save and Delete rebuild the list; leave the editor once that happens.
   DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty,typeof(ListBox)).AddValueChanged(list,delegate {if(mode!="editor"||list.ItemsSource!=null)show("idle");});
   ((System.Collections.Specialized.INotifyCollectionChanged)list.Items).CollectionChanged+=delegate {bool none=list.Items.Count==0;empty.Visibility=none?Visibility.Visible:Visibility.Collapsed;list.Visibility=none?Visibility.Collapsed:Visibility.Visible;savedCount.Text=none?"Saved passwords":list.Items.Count+(list.Items.Count==1?" saved login":" saved logins");};
   list.Visibility=Visibility.Collapsed;show("idle");

   // ── Account & sync ──
   StackPanel profile;var profileCard=VaultCard(null,null,out profile);syncPage.Children.Add(profileCard);
   var profileRow=new Grid();profileRow.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(86)});profileRow.ColumnDefinitions.Add(new ColumnDefinition());profileRow.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});profile.Children.Add(profileRow);
   var avatarHost=new ContentControl {Width=68,Height=68,HorizontalAlignment=HorizontalAlignment.Left};profileRow.Children.Add(avatarHost);
   var who=new StackPanel {VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(who,1);profileRow.Children.Add(who);who.Children.Add(VaultEyebrow("MICA ACCOUNT"));var displayName=DialogText("Not signed in",22,"Ink");displayName.FontFamily=Localizer.Serif;who.Children.Add(displayName);
   accountLabel.FontSize=12;accountLabel.Margin=new Thickness(0,4,0,0);accountLabel.SetResourceReference(TextBlock.ForegroundProperty,"Muted");who.Children.Add(accountLabel);
   var authActions=new StackPanel {VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(authActions,2);profileRow.Children.Add(authActions);var signIn=VaultPrimary(action("Sign in with Discord","Sign in with Discord","\uE77B"));var signOut=action("Sign out","Sign out","\uE8AC");signIn.Margin=signOut.Margin=new Thickness(0);authActions.Children.Add(signIn);authActions.Children.Add(signOut);
   var privacy=DialogText("Discord only proves who you are. Your master password encrypts everything on this device and can't be recovered — not even by Mica.",11,"Muted");privacy.LineHeight=18;privacy.Margin=new Thickness(0,16,0,0);profile.Children.Add(privacy);
   Action refreshProfile=async delegate {
    var current=ReadVaultAccount();bool signedIn=accountLabel.Text!=null&&accountLabel.Text.StartsWith("Discord",StringComparison.Ordinal);
    displayName.Text=signedIn&&current!=null?current.username:"Not signed in";accountLabel.Text=signedIn?"Signed in with Discord · sync available":"Sign in to back up your encrypted vault";
    signIn.Visibility=signedIn?Visibility.Collapsed:Visibility.Visible;signOut.Visibility=signedIn?Visibility.Visible:Visibility.Collapsed;
    avatarHost.Content=VaultMonogram(signedIn&&current!=null?current.username:"?",68);
    if(signedIn&&current!=null){var image=await LoadAvatar(current);if(image!=null)avatarHost.Content=new Ellipse {Width=68,Height=68,Fill=new ImageBrush(image) {Stretch=Stretch.UniformToFill}};}
   };
   bool syncingLabel=false;DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock)).AddValueChanged(accountLabel,delegate {if(syncingLabel)return;
    // Its own two sentences, in English or translated, aren't news; reacting would undo the translation.
    string now=accountLabel.Text??"";foreach(var own in new[]{"Signed in with Discord · sync available","Sign in to back up your encrypted vault"})if(now==own||now==Localizer.Translate(own))return;syncingLabel=true;try{refreshProfile();}finally{syncingLabel=false;}});
   syncingLabel=true;try{refreshProfile();}finally{syncingLabel=false;}

   var tiles=new Grid();tiles.ColumnDefinitions.Add(new ColumnDefinition());tiles.ColumnDefinitions.Add(new ColumnDefinition());syncPage.Children.Add(tiles);
   StackPanel sync,snapshot;var syncCard=VaultCard("Encrypted sync","\uE753",out sync);syncCard.Margin=new Thickness(0,0,6,12);tiles.Children.Add(syncCard);var snapshotCard=VaultCard("Browser snapshot","\uE7F4",out snapshot);snapshotCard.Margin=new Thickness(6,0,0,12);Grid.SetColumn(snapshotCard,1);tiles.Children.Add(snapshotCard);
   var syncText=DialogText("Settings sync on their own while you're signed in. Passwords sync on their own too, encrypted: after you save one and when Mica closes. Sync now does it right away.",11,"Muted");syncText.LineHeight=18;syncText.MinHeight=54;sync.Children.Add(syncText);var syncActions=new WrapPanel {Margin=new Thickness(0,12,0,0)};syncActions.Children.Add(action("Pull remote vault (merge into local)","Pull","\uE896"));syncActions.Children.Add(VaultPrimary(action("Sync passwords now","Sync now","\uE895")));syncActions.Children.Add(action("Restore logins from before the last sync","Restore from before sync","\uE777"));sync.Children.Add(syncActions);
   var snapshotText=DialogText("Keep tabs, bookmarks, history and preferences inside your encrypted vault.",11,"Muted");snapshotText.LineHeight=18;snapshotText.MinHeight=54;snapshot.Children.Add(snapshotText);var snapshotActions=new WrapPanel {Margin=new Thickness(0,12,0,0)};snapshotActions.Children.Add(action("Capture browser snapshot","Capture","\uE722"));snapshotActions.Children.Add(action("Restore captured browser snapshot","Restore","\uE777"));snapshot.Children.Add(snapshotActions);
   StackPanel passkeys;var passkeyCard=VaultCard(null,null,out passkeys);syncPage.Children.Add(passkeyCard);var passkeyRow=new Grid();passkeyRow.ColumnDefinitions.Add(new ColumnDefinition());passkeyRow.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});passkeys.Children.Add(passkeyRow);
   var passkeyText=new StackPanel {VerticalAlignment=VerticalAlignment.Center};var passkeyTitle=DialogText("Passkeys",13,"Ink");passkeyTitle.FontWeight=FontWeights.SemiBold;passkeyText.Children.Add(passkeyTitle);var passkeyNote=DialogText("Managed by Windows Hello, not stored in this vault.",11,"Muted");passkeyNote.Margin=new Thickness(0,4,0,0);passkeyText.Children.Add(passkeyNote);passkeyRow.Children.Add(passkeyText);
   var passkeyButton=action("Open Windows passkey settings","Open settings","\uE8A7");passkeyButton.Margin=new Thickness(0);passkeyButton.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(passkeyButton,1);passkeyRow.Children.Add(passkeyButton);
   footer.HorizontalAlignment=HorizontalAlignment.Stretch;footer.Orientation=Orientation.Vertical;message.SetResourceReference(TextBlock.ForegroundProperty,"Muted");message.FontSize=11;message.Text="Encrypted on this device · unlock only when you need a password";footer.Children.Add(message);UpdateVaultLock(false);
  }
  Button vaultLockButton,vaultIdleUnlock;TextBlock vaultIdleTitle,vaultIdleText;bool vaultUnlocked;
  // The lock shows the vault's state and toggles it; the account page never needs it.
  void UpdateVaultLock(bool unlocked) {
   vaultUnlocked=unlocked;if(vaultLockButton==null)return;
   vaultLockButton.Content=VaultIconLabel(unlocked?"\uE785":"\uE72E",unlocked?"Unlocked · Lock":"Locked · Unlock");
   vaultLockButton.ToolTip=unlocked?"Lock your passwords":"Unlock your passwords";System.Windows.Automation.AutomationProperties.SetName(vaultLockButton,unlocked?"Lock vault":"Unlock vault");
   vaultIdleTitle.Text=unlocked?"Pick a login":"Your passwords are locked";
   vaultIdleText.Text=unlocked?"Select one on the left to fill or copy it,\nor create a new one.":"Unlock with your master password or Windows Hello\nwhen you need them. Your account works without it.";
   vaultIdleUnlock.Visibility=unlocked?Visibility.Collapsed:Visibility.Visible;
  }
  StackPanel VaultIconLabel(string glyph,string title) {var row=new StackPanel {Orientation=Orientation.Horizontal};var icon=DialogText(glyph,13,"Accent");icon.FontFamily=new FontFamily("Segoe MDL2 Assets");icon.VerticalAlignment=VerticalAlignment.Center;icon.Margin=new Thickness(0,0,8,0);row.Children.Add(icon);row.Children.Add(DialogText(title,12,"Ink"));return row;}
 }
}
