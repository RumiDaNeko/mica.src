using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Mica {
 // Shows the signed-in Mica account (Discord) in the sidebar. Images come only
 // from Discord's public CDN and are cached beside the encrypted session.
 partial class BrowserApp {
  static bool avatarRefreshed;
  string avatarError;
  string VaultSessionPath {get {return System.IO.Path.Combine(profile,"vault-session.bin");}}
  VaultAccount ReadVaultAccount() {
   try {if(File.Exists(VaultSessionPath))return VaultCrypto.Json.Deserialize<VaultAccount>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(VaultSessionPath),null,DataProtectionScope.CurrentUser)));}catch(Exception){}
   return null;
  }
  void WriteVaultAccount(VaultAccount account) {File.WriteAllBytes(VaultSessionPath,ProtectedData.Protect(Encoding.UTF8.GetBytes(VaultCrypto.Json.Serialize(account)),null,DataProtectionScope.CurrentUser));}
  static string AvatarUrl(VaultAccount account) {
   if(account.avatar!=null&&Regex.IsMatch(account.avatar,"^(a_)?[0-9a-f]{32}$"))return "https://cdn.discordapp.com/avatars/"+account.id+"/"+account.avatar+".png?size=128";
   ulong id;return "https://cdn.discordapp.com/embed/avatars/"+(ulong.TryParse(account.id,out id)?(id>>22)%6:0)+".png";
  }
  void InitializeAvatar() {
   Click("AvatarButton",ShowAccount);Click("MiniAvatar",ShowAccount);
   RenderAvatar();
   if(smoke||privateMode||avatarRefreshed)return;avatarRefreshed=true;
   window.Dispatcher.BeginInvoke(new Action(async delegate {
    // Pick up a changed Discord picture/name for an existing session.
    var account=ReadVaultAccount();if(account==null)return;
    try {
     var verified=VaultCrypto.Json.Deserialize<AccountResult>(await VaultRequest("/v1/account","GET",null,account));
     var user=verified==null?null:verified.user;
     if(user==null||user.id!=account.id||(user.avatar==account.avatar&&user.username==account.username&&user.banner==account.banner&&user.accent_color==account.accent_color))return;
     account.avatar=user.avatar;account.username=user.username;account.banner=user.banner;account.accent_color=user.accent_color;WriteVaultAccount(account);RefreshAvatars();
    }catch(Exception){}
   }),System.Windows.Threading.DispatcherPriority.ApplicationIdle);
  }
  void RefreshAvatars() {foreach(var browser in Program.Browsers.Where(b=>!b.closing&&b.profile==profile))browser.RenderAvatar();}
  async void RenderAvatar() {
   var account=privateMode?null:ReadVaultAccount();
   UpdateHomeClock();
   foreach(var name in new[]{"AvatarButton","MiniAvatar"}) {
    var button=Find<Button>(name);button.ToolTip=account==null?"Account and profiles":account.username+"\nAccount and profiles";
    button.Content=AvatarPlaceholder();button.Visibility=privateMode?Visibility.Collapsed:Visibility.Visible;
   }
   if(account==null)return;
   var image=await LoadAvatar(account);if(image==null||closing)return;
   // The download can finish off the window's thread; the buttons are only touched on it.
   if(!window.Dispatcher.CheckAccess()){if(image.CanFreeze&&!image.IsFrozen)image.Freeze();window.Dispatcher.BeginInvoke(new Action(delegate {if(!closing)foreach(var name in new[]{"AvatarButton","MiniAvatar"})Find<Button>(name).Content=new Ellipse {Width=24,Height=24,Fill=new ImageBrush(image) {Stretch=Stretch.UniformToFill}};}));return;}
   foreach(var name in new[]{"AvatarButton","MiniAvatar"})Find<Button>(name).Content=new Ellipse {Width=24,Height=24,Fill=new ImageBrush(image) {Stretch=Stretch.UniformToFill}};
  }
  // The avatar opens your account: who you are (as Discord shows you), this window's profile, Passwords,
  // and the other profiles to switch to.
  Popup accountPopup;
  internal async void ShowAccount() {
   if(accountPopup==null)accountPopup=CreatePopup("AccountPopup",290);
   if(accountPopup.IsOpen){accountPopup.IsOpen=false;return;}
   CloseMenus();if(profilesPopup!=null)profilesPopup.IsOpen=false;
   var body=PopupBody(accountPopup);var account=privateMode?null:ReadVaultAccount();var current=CurrentProfile;
   Color tint=current!=null&&!string.IsNullOrEmpty(current.Color)?ParseColor(current.Color):((SolidColorBrush)window.FindResource("Accent")).Color;
   // Header: your Discord banner (or its colour, else the profile's), the picture overlapping its lower edge, then the names.
   string bannerUrl=account==null?null:BannerUrl(account);double tall=bannerUrl!=null?96:58;
   if(account!=null&&account.accent_color.HasValue&&account.accent_color>=0&&account.accent_color<=0xFFFFFF){int c=account.accent_color.Value;tint=Color.FromRgb((byte)(c>>16),(byte)(c>>8),(byte)c);}
   var header=new Grid {Tag="account-card"};
   var banner=new Border {Height=tall,Tag="account-banner",CornerRadius=new CornerRadius(9,9,4,4),VerticalAlignment=VerticalAlignment.Top,Background=new LinearGradientBrush(tint,Color.FromArgb(0xCC,tint.R,tint.G,tint.B),0)};header.Children.Add(banner);
   var ring=new Border {Width=72,Height=72,CornerRadius=new CornerRadius(36),Margin=new Thickness(10,tall-36,0,0),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Padding=new Thickness(5)};ring.SetResourceReference(Border.BackgroundProperty,"PopupPanel");
   // A fixed circle, clipped round, so the picture can't show square whatever it is.
   var picture=new Border {Width=62,Height=62,CornerRadius=new CornerRadius(31),Tag="account-picture",Clip=new EllipseGeometry(new Point(31,31),31,31)};picture.SetResourceReference(Border.BackgroundProperty,"Hover");
   picture.Child=new TextBlock {Text="",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=24,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
   ring.Child=picture;header.Children.Add(ring);
   var names=new StackPanel {Margin=new Thickness(12,tall+42,12,4)};
   var shown=new TextBlock {Text=account!=null?account.username:"Not signed in",FontSize=17,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,Tag="account-name"};shown.SetResourceReference(TextBlock.ForegroundProperty,"Ink");names.Children.Add(shown);
   var detail=DialogText((current!=null?current.Name+" profile":"Private window")+" · "+(account!=null?"Discord":"Sign in to sync"),11,"Muted");detail.Margin=new Thickness(0,2,0,0);detail.Tag="account-detail";names.Children.Add(detail);
   header.Children.Add(names);body.Children.Add(header);
   // Passwords get their own button, right on the card.
   var label=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};
   label.Children.Add(new TextBlock {Text="",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=13,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)});
   label.Children.Add(new TextBlock {Text=account!=null?"Passwords":"Sign in with Discord",FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
   var passwords=new Button {Content=label,Padding=new Thickness(14,8,14,8),Margin=new Thickness(12,8,12,10),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center,Tag="account-passwords"};
   passwords.SetResourceReference(Button.BackgroundProperty,"Hover");passwords.Click+=delegate {accountPopup.IsOpen=false;ShowVault();};body.Children.Add(passwords);
   MenuDivider(body);MenuHeading(body,"SWITCH PROFILE");
   foreach(var item in LoadProfiles()) {
    var entry=item;bool here=current!=null&&entry.Id==current.Id;
    var row=MenuCommand(body,accountPopup,"",entry.Name,here?"This window":"",delegate {OpenProfile(entry.Id);});row.Tag="account-profile:"+entry.Id;
    var symbol=(TextBlock)((Grid)row.Content).Children[0];symbol.Text="●";symbol.FontFamily=new FontFamily("Segoe UI");symbol.FontSize=15;
    if(!string.IsNullOrEmpty(entry.Color))symbol.Foreground=new SolidColorBrush(ParseColor(entry.Color));
    if(here)row.SetResourceReference(Button.BackgroundProperty,"Hover");
   }
   MenuCommand(body,accountPopup,"","Add profile","",ShowAddProfile).Tag="account-add";
   MenuCommand(body,accountPopup,"","Manage profiles","",ShowManageProfiles).Tag="account-manage";
   var anchor=Find<Button>(sidebarHidden?"MiniAvatar":"AvatarButton");
   accountPopup.Placement=sidebarHidden?PlacementMode.Right:PlacementMode.Top;accountPopup.PlacementTarget=anchor;accountPopup.VerticalOffset=sidebarHidden?0:-8;accountPopup.HorizontalOffset=sidebarHidden?8:-120;accountPopup.IsOpen=true;
   if(account==null)return;
   if(bannerUrl!=null) {
    var cover=await LoadCdnImage(bannerUrl,"Banners",600);
    Action paint=delegate {if(cover!=null&&accountPopup.IsOpen)banner.Background=new ImageBrush(cover) {Stretch=Stretch.UniformToFill};};
    if(window.Dispatcher.CheckAccess())paint();else window.Dispatcher.BeginInvoke(paint);
   }
   var image=await LoadAvatar(account);if(image==null)return;
   // Same as the sidebar: a sized circle filled with the picture, set on the window's thread.
   Action show=delegate {if(accountPopup.IsOpen&&picture.Tag as string=="account-picture")picture.Child=new Ellipse {Width=62,Height=62,Fill=new ImageBrush(image) {Stretch=Stretch.UniformToFill}};};
   if(window.Dispatcher.CheckAccess())show();else window.Dispatcher.BeginInvoke(show);
  }
  FrameworkElement AvatarPlaceholder() {var glyph=new TextBlock {Text="",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=15,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};return glyph;}
  static string BannerUrl(VaultAccount account) {return account.banner!=null&&Regex.IsMatch(account.banner,"^(a_)?[0-9a-f]{32}$")&&Regex.IsMatch(account.id??"","^[0-9]{1,20}$")?"https://cdn.discordapp.com/banners/"+account.id+"/"+account.banner+".png?size=600":null;}
  Task<BitmapSource> LoadAvatar(VaultAccount account) {return LoadCdnImage(AvatarUrl(account),"Avatars",128);}
  // One cached picture per folder from Discord's CDN; a new one replaces the old.
  async Task<BitmapSource> LoadCdnImage(string url,string folder,int width) {
   string directory=System.IO.Path.Combine(profile,folder),key;
   using(var sha=SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(url))).Replace("-","").ToLowerInvariant();
   string path=System.IO.Path.Combine(directory,key+".png");
   try {
    if(!File.Exists(path)) {
     Directory.CreateDirectory(directory);
     byte[] data;using(var client=new WebClient())data=await client.DownloadDataTaskAsync(url);
     if(data.Length==0||data.Length>2*1024*1024)return null;
     File.WriteAllBytes(path,data);
     foreach(var old in Directory.GetFiles(directory,"*.png"))if(!string.Equals(old,path,StringComparison.OrdinalIgnoreCase))try{File.Delete(old);}catch(IOException){}
    }
    var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=width;bitmap.UriSource=new Uri(path);bitmap.EndInit();bitmap.Freeze();return bitmap;
   }catch(Exception error){avatarError=error.Message;try{if(File.Exists(path))File.Delete(path);}catch(IOException){}return null;}
  }
 }
}
