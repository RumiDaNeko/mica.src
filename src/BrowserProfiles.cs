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
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 sealed class ProfileInfo {public string Id {get;set;} public string Name {get;set;} public string Color {get;set;}}
 // Profiles: each has its own logins, cookies, site data and extensions (a named profile inside the one
 // shared engine, so a second profile costs no second engine), and its own Mica data (tabs, history,
 // bookmarks, passwords, ad blocking, look) in Profiles\<id>. The main profile is the one Mica always had.
 partial class BrowserApp {
  const string MainProfileId="main";
  // The folder holding profiles.json and the shared engine; for the main profile, its own folder.
  internal string MainRoot {get {var parent=Path.GetDirectoryName(profile.TrimEnd('\\'));return !privateMode&&string.Equals(Path.GetFileName(parent),"Profiles",StringComparison.OrdinalIgnoreCase)?Path.GetDirectoryName(parent):profile;}}
  internal string ProfileId {get {return privateMode||MainRoot==profile?MainProfileId:Path.GetFileName(profile.TrimEnd('\\'));}}
  internal string EngineFolder {get {return Path.Combine(privateMode?profile:MainRoot,"WebView");}}
  string EngineProfileName {get {return ProfileId==MainProfileId?null:"mica-"+ProfileId;}}
  internal CoreWebView2ControllerOptions ViewOptions(CoreWebView2Environment engine,string jar=null) {
   var options=engine.CreateCoreWebView2ControllerOptions();options.IsInPrivateModeEnabled=privateMode;
   if(jar!=null&&!privateMode)options.ProfileName=JarProfileName(jar);else if(EngineProfileName!=null)options.ProfileName=EngineProfileName;return options;
  }
  internal static bool IsProfileId(string id) {return id!=null&&id.Length==32&&id.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');}

  string ProfilesPath {get {return Path.Combine(MainRoot,"profiles.json");}}
  internal List<ProfileInfo> LoadProfiles() {
   List<ProfileInfo> list=null;try {if(File.Exists(ProfilesPath))list=new JavaScriptSerializer().Deserialize<List<ProfileInfo>>(File.ReadAllText(ProfilesPath));}catch(Exception){}
   list=(list??new List<ProfileInfo>()).Where(p=>p!=null&&(p.Id==MainProfileId||IsProfileId(p.Id))&&Directory.Exists(ProfileFolder(p.Id))).ToList();
   if(!list.Any(p=>p.Id==MainProfileId))list.Insert(0,new ProfileInfo {Id=MainProfileId,Name="Main"});
   return list;
  }
  void SaveProfiles(List<ProfileInfo> list) {
   try {string temp=ProfilesPath+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(list));if(File.Exists(ProfilesPath))File.Replace(temp,ProfilesPath,null);else File.Move(temp,ProfilesPath);}catch(Exception e){Status("Couldn't save profiles: "+e.Message);}
  }
  string ProfileFolder(string id) {return id==MainProfileId?MainRoot:Path.Combine(MainRoot,"Profiles",id);}
  internal ProfileInfo CurrentProfile {get {return privateMode?null:LoadProfiles().FirstOrDefault(p=>p.Id==ProfileId);}}
  internal string ProfileTitleSuffix {get {var current=CurrentProfile;return current==null||(current.Id==MainProfileId&&LoadProfiles().Count==1)?"":" · "+current.Name;}}

  // Switching: focus a window already on that profile, or open one.
  internal BrowserApp OpenProfile(string id) {
   var open=Program.Browsers.FirstOrDefault(b=>!b.closing&&!b.privateMode&&b.MainRoot==MainRoot&&b.ProfileId==id);
   if(open!=null){if(open.window.WindowState==WindowState.Minimized)open.window.WindowState=WindowState.Normal;open.window.Activate();return open;}
   return Program.OpenWindow(id==MainProfileId?new[]{"--new-window"}:new[]{"--profile",id},smoke,smoke?MainRoot:null);
  }

  Popup profilesPopup;TextBlock profilesHint;
  internal void ShowProfiles() {
   if(profilesPopup==null){profilesPopup=CreatePopup("ProfilesPopup",260);}
   if(profilesPopup.IsOpen){profilesPopup.IsOpen=false;return;}
   CloseMenus();var body=PopupBody(profilesPopup);MenuHeading(body,"PROFILES");
   foreach(var item in LoadProfiles()) {
    var entry=item;bool here=entry.Id==ProfileId&&!privateMode;
    var row=MenuCommand(body,profilesPopup,"",entry.Name,here?"This window":"",delegate {OpenProfile(entry.Id);});row.Tag="profile:"+entry.Id;
    var symbol=(TextBlock)((Grid)row.Content).Children[0];symbol.Text="●";symbol.FontFamily=new FontFamily("Segoe UI");symbol.FontSize=15;
    if(!string.IsNullOrEmpty(entry.Color))symbol.Foreground=new SolidColorBrush(ParseColor(entry.Color));
    if(here)row.SetResourceReference(Button.BackgroundProperty,"Hover");
   }
   MenuDivider(body);
   MenuCommand(body,profilesPopup,"","Add profile","",ShowAddProfile).Tag="profile-add";
   MenuCommand(body,profilesPopup,"","Manage profiles","",ShowManageProfiles).Tag="profile-manage";
   var note=DialogText("Each profile keeps its own logins, cookies, extensions, tabs and history.",10,"Muted");note.TextWrapping=TextWrapping.Wrap;note.Margin=new Thickness(12,4,12,10);body.Children.Add(note);
   var anchor=Find<Button>(sidebarHidden?"MiniMenu":"MenuButton");profilesPopup.Placement=PlacementMode.Top;profilesPopup.PlacementTarget=anchor;profilesPopup.VerticalOffset=-8;profilesPopup.HorizontalOffset=0;profilesPopup.IsOpen=true;
  }

  Window addProfileWindow;
  internal void ShowAddProfile() {
   if(addProfileWindow!=null){addProfileWindow.Activate();return;}
   StackPanel body,actions;var dialog=CreateMicaDialog(window,"Add profile",440,440,out body,out actions);addProfileWindow=dialog;dialog.Closed+=delegate {addProfileWindow=null;};
   body.Children.Add(DialogText("A profile is a separate you: its own sign-ins, cookies, extensions, tabs and history. Nothing is shared with your other profiles.",12,"Muted"));
   SectionLabel(body,"NAME");
   var name=new TextBox {Padding=new Thickness(8,6,8,6),Tag="profile-name",MaxLength=40};System.Windows.Automation.AutomationProperties.SetName(name,"Profile name");body.Children.Add(name);
   SectionLabel(body,"COLOUR");
   string chosen=FrameSwatches[(LoadProfiles().Count*3)%FrameSwatches.Length];
   var swatches=new WrapPanel();body.Children.Add(swatches);var rings=new List<Border>();
   foreach(var hex in FrameSwatches) {
    string colour=hex;var ring=new Border {Width=30,Height=30,CornerRadius=new CornerRadius(15),BorderThickness=new Thickness(2),Margin=new Thickness(0,0,8,8),Cursor=Cursors.Hand,Tag="profile-colour:"+colour,Background=Brushes.Transparent,Padding=new Thickness(3)};
    ring.Child=new Border {CornerRadius=new CornerRadius(11),Background=new SolidColorBrush(ParseColor(colour))};
    Action mark=delegate {foreach(var r in rings)r.BorderBrush=Brushes.Transparent;ring.SetResourceReference(Border.BorderBrushProperty,"Accent");};
    ring.MouseLeftButtonUp+=delegate {chosen=colour;mark();};rings.Add(ring);swatches.Children.Add(ring);if(colour==chosen)ring.Loaded+=delegate {mark();};
   }
   var problem=DialogText("",11,"Muted");body.Children.Add(problem);
   var create=new Button {Content="Create and open",Padding=new Thickness(18,9,18,9),IsDefault=true,Tag="profile-create"};create.SetResourceReference(Button.BackgroundProperty,"Hover");actions.Children.Add(create);
   create.Click+=delegate {
    string title=(name.Text??"").Trim();if(title.Length==0){problem.Text="Give it a name, like Work or School.";name.Focus();return;}
    var created=CreateProfile(title,chosen);if(created==null)return;dialog.Close();OpenProfile(created.Id);
   };
   dialog.Show();name.Focus();
  }
  internal ProfileInfo CreateProfile(string name,string colour) {
   var list=LoadProfiles();var info=new ProfileInfo {Id=Guid.NewGuid().ToString("N"),Name=name,Color=colour};
   try {
    string folder=ProfileFolder(info.Id);Directory.CreateDirectory(folder);
    // It starts in its own colour, in the theme you're using now.
    var start=new BrowserState {Dark=state.Dark,FrameColor=colour};start.AddTab("mica://home");StateStore.Save(Path.Combine(folder,"session.json"),start);
   }catch(Exception e){Status("Couldn't create the profile: "+e.Message);return null;}
   list.Add(info);SaveProfiles(list);RefreshProfileTitles();return info;
  }

  Window manageProfilesWindow;
  internal void ShowManageProfiles() {
   if(manageProfilesWindow!=null){manageProfilesWindow.Activate();return;}
   StackPanel body,actions;var dialog=CreateMicaDialog(window,"Profiles",480,560,out body,out actions);manageProfilesWindow=dialog;dialog.Closed+=delegate {manageProfilesWindow=null;};
   var done=new Button {Content="Done",Padding=new Thickness(22,10,22,10),IsDefault=true};done.SetResourceReference(Button.BackgroundProperty,"Hover");done.Click+=delegate {dialog.Close();};actions.Children.Add(done);
   foreach(var item in LoadProfiles()) {
    var entry=item;var row=new Grid {Margin=new Thickness(12,8,8,8)};
    row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(26)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
    var dot=DialogText("●",16,"Accent");dot.VerticalAlignment=VerticalAlignment.Center;if(!string.IsNullOrEmpty(entry.Color))dot.Foreground=new SolidColorBrush(ParseColor(entry.Color));row.Children.Add(dot);
    var field=new TextBox {Text=entry.Name,Padding=new Thickness(6,4,6,4),MaxLength=40,Tag="profile-rename:"+entry.Id,VerticalContentAlignment=VerticalAlignment.Center};Grid.SetColumn(field,1);row.Children.Add(field);
    Action rename=delegate {string title=(field.Text??"").Trim();if(title.Length==0||title==entry.Name){field.Text=entry.Name;return;}var list=LoadProfiles();var stored=list.First(p=>p.Id==entry.Id);stored.Name=title;entry.Name=title;SaveProfiles(list);RefreshProfileTitles();};
    field.LostFocus+=delegate {rename();};field.KeyDown+=delegate(object sender,KeyEventArgs e) {if(e.Key==Key.Enter){e.Handled=true;rename();}};
    var card=Card(row);body.Children.Add(card);
    if(entry.Id==MainProfileId)continue;
    // Removing takes two clicks: it deletes that profile's logins, cookies and tabs.
    var remove=new Button {Content="Remove",Padding=new Thickness(12,6,12,6),Margin=new Thickness(8,0,0,0),Tag="profile-remove:"+entry.Id};Grid.SetColumn(remove,2);row.Children.Add(remove);
    bool armed=false;
    remove.Click+=async delegate {
     if(!armed){armed=true;remove.Content="Delete everything?";var timer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromSeconds(4)};timer.Tick+=delegate {timer.Stop();if(armed&&remove.IsEnabled){armed=false;remove.Content="Remove";}};timer.Start();return;}
     remove.IsEnabled=false;remove.Content="Removing…";
     if(await RemoveProfile(entry.Id))FoldAway(card);else {remove.IsEnabled=true;remove.Content="Remove";armed=false;}
    };
   }
   var note=DialogText("Removing a profile deletes its sign-ins, cookies, extensions, tabs, history and saved passwords on this PC.",11,"Muted");note.Margin=new Thickness(0,12,0,4);body.Children.Add(note);
   dialog.Show();
  }
  internal async Task<bool> RemoveProfile(string id) {
   if(!IsProfileId(id))return false;
   var jars=StateStore.Load(Path.Combine(ProfileFolder(id),"session.json")).Jars;
   foreach(var open in Program.Browsers.Where(b=>!b.closing&&!b.privateMode&&b.MainRoot==MainRoot&&b.ProfileId==id).ToList())open.window.Close();
   // The engine deletes the profile's own data (cookies, storage, extensions) once nothing uses it.
   var view=new WebView2 {Width=1,Height=1,Visibility=Visibility.Hidden,IsHitTestVisible=false,Focusable=false};Find<Grid>("Root").Children.Add(view);
   try {
    var engine=await EnsureEnvironment();var options=engine.CreateCoreWebView2ControllerOptions();options.ProfileName="mica-"+id;
    await view.EnsureCoreWebView2Async(engine,options);RegisterOwnTarget(view.CoreWebView2);view.CoreWebView2.Profile.Delete();
   }catch(Exception e){if(smoke)Status("Engine profile: "+e.Message);}
   finally {Find<Grid>("Root").Children.Remove(view);try {view.Dispose();}catch(Exception){}}
   foreach(var jar in jars)await DeleteEngineProfile("mica-"+id+"-jar-"+jar.Id);
   var list=LoadProfiles();list.RemoveAll(p=>p.Id==id);SaveProfiles(list);
   try {Directory.Delete(ProfileFolder(id),true);}catch(Exception){}
   RefreshProfileTitles();Status("Profile removed.");return true;
  }
  void RefreshProfileTitles() {
   foreach(var b in Program.Browsers.Where(b=>!b.closing&&!b.privateMode&&b.MainRoot==MainRoot)) {
    var tab=b.Active;if(tab!=null)b.window.Title=(tab.Url=="mica://home"?"A fresh start":tab.Title)+" — Mica"+b.ProfileTitleSuffix;
    if(b.profilesHint!=null){var current=b.CurrentProfile;b.profilesHint.Text=current==null?"":current.Name;}
   }
  }
 }
}
