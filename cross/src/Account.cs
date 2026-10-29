using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using GlyphPath=Avalonia.Controls.Shapes.Path;

namespace Mica {
 sealed class ProfileInfo {public string Id {get;set;} public string Name {get;set;} public string Color {get;set;}}
 // The account card and profiles, as on Windows (BrowserAvatar.cs ShowAccount, BrowserProfiles.cs). Each profile is a
 // separate you: its own sign-ins, cookies, extensions, tabs and history, in Profiles/<id> beside the main one, listed
 // in profiles.json (the same layout as Windows). A profile opens in its own Mica.
 sealed partial class BrowserWindow {
  const string MainProfileId="main";
  // The folder holding profiles.json; for the main profile, its own folder.
  string MainRoot {get {var parent=Path.GetDirectoryName(profile.TrimEnd(Path.DirectorySeparatorChar));return !Program.Private&&string.Equals(Path.GetFileName(parent),"Profiles",StringComparison.OrdinalIgnoreCase)?Path.GetDirectoryName(parent):profile;}}
  string ProfileId {get {return Program.Private||MainRoot==profile?MainProfileId:Path.GetFileName(profile.TrimEnd(Path.DirectorySeparatorChar));}}
  string ProfilesPath {get {return Path.Combine(MainRoot,"profiles.json");}}
  static bool IsProfileId(string id) {return id!=null&&id.Length==32&&id.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');}
  string ProfileFolder(string id) {return id==MainProfileId?MainRoot:Path.Combine(MainRoot,"Profiles",id);}
  List<ProfileInfo> LoadProfiles() {
   List<ProfileInfo> list=null;try {if(File.Exists(ProfilesPath))list=new JavaScriptSerializer().Deserialize<List<ProfileInfo>>(File.ReadAllText(ProfilesPath));}catch(Exception){}
   list=(list??new List<ProfileInfo>()).Where(p=>p!=null&&(p.Id==MainProfileId||IsProfileId(p.Id))&&Directory.Exists(ProfileFolder(p.Id))).ToList();
   if(!list.Any(p=>p.Id==MainProfileId))list.Insert(0,new ProfileInfo {Id=MainProfileId,Name="Main"});
   return list;
  }
  void SaveProfiles(List<ProfileInfo> list) {
   try {string temp=ProfilesPath+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(list));if(File.Exists(ProfilesPath))File.Replace(temp,ProfilesPath,null);else File.Move(temp,ProfilesPath);}catch(Exception e){Status("Couldn't save profiles: "+e.Message);}
  }
  ProfileInfo CurrentProfile {get {return Program.Private?null:LoadProfiles().FirstOrDefault(p=>p.Id==ProfileId);}}
  // Switching: this window if it's already that profile, or a new Mica on it.
  void OpenProfile(string id) {
   if(id==ProfileId&&!Program.Private){Activate();return;}
   try {
    var start=new ProcessStartInfo(Environment.ProcessPath) {UseShellExecute=false};start.ArgumentList.Add("--profile");start.ArgumentList.Add(ProfileFolder(id));
    Process.Start(start);
   } catch(Exception e) {Status("Couldn't open that profile: "+e.Message);}
  }

  // ── The card: a banner in the profile's colour, the avatar ring, who you are, and the profiles.
  void ShowAccount(Control anchor) {
   var popup=MenuCard(anchor,290,PlacementMode.Top,delegate(StackPanel body,Popup card) {
    var current=CurrentProfile;
    var accent=this.FindResource("Accent") as ISolidColorBrush;Color tint=current!=null&&!string.IsNullOrEmpty(current.Color)?Color.Parse(current.Color):accent!=null?accent.Color:Colors.SlateBlue;
    const double tall=58;
    var header=new Grid();
    header.Children.Add(new Border {Height=tall,CornerRadius=new CornerRadius(9,9,4,4),VerticalAlignment=VerticalAlignment.Top,Background=new LinearGradientBrush {StartPoint=new RelativePoint(0,0,RelativeUnit.Relative),EndPoint=new RelativePoint(1,0,RelativeUnit.Relative),GradientStops={new GradientStop(tint,0),new GradientStop(Color.FromArgb(0xCC,tint.R,tint.G,tint.B),1)}}});
    var picture=Res(new Border {Width=62,Height=62,CornerRadius=new CornerRadius(31),Child=Glyph(Glyphs.Person,24,null)},Border.BackgroundProperty,"Hover");
    header.Children.Add(Res(new Border {Width=72,Height=72,CornerRadius=new CornerRadius(36),Margin=new Thickness(10,tall-36,0,0),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Padding=new Thickness(5),Child=picture},Border.BackgroundProperty,"Panel"));
    var names=new StackPanel {Margin=new Thickness(12,tall+42,12,4)};
    var who=DialogText("Not signed in",17,"Ink");who.FontWeight=FontWeight.SemiBold;who.TextWrapping=TextWrapping.NoWrap;who.TextTrimming=TextTrimming.CharacterEllipsis;names.Children.Add(who);
    var detail=DialogText((current==null?"Private window":current.Name+" profile")+" · This device",11,"Muted");detail.Margin=new Thickness(0,2,0,0);names.Children.Add(detail);
    header.Children.Add(names);body.Children.Add(header);
    var folder=new Button {Padding=new Thickness(14,8),Margin=new Thickness(12,8,12,10),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center};folder.Classes.Add("flat");folder.Classes.Add("on");
    var folderRow=new StackPanel {Orientation=Orientation.Horizontal};var fg=Glyph(Glyphs.Folder,13,null);fg.Margin=new Thickness(0,0,8,0);folderRow.Children.Add(fg);folderRow.Children.Add(new TextBlock {Text="Profile folder",FontWeight=FontWeight.SemiBold,VerticalAlignment=VerticalAlignment.Center});
    folder.Content=folderRow;folder.Click+=delegate {card.IsOpen=false;OpenPath(profile,false);};body.Children.Add(folder);
    MenuDivider(body);
    if(!Program.Private) {
     MenuHeading(body,"SWITCH PROFILE");
     foreach(var item in LoadProfiles()) {
      var entry=item;bool here=entry.Id==ProfileId;
      var row=MenuCommand(body,card,Glyphs.Dot,entry.Name,here?"This window":"",delegate {OpenProfile(entry.Id);});
      var dot=((Grid)row.Content).Children[0] as Viewbox;
      // A filled dot in the profile's colour (the accent when it has none), as on Windows.
      var path=dot==null?null:dot.Child as GlyphPath;
      if(path!=null){if(!string.IsNullOrEmpty(entry.Color)){path.Classes.Remove("accent");path.Fill=path.Stroke=new SolidColorBrush(Color.Parse(entry.Color));}else path.Bind(GlyphPath.FillProperty,path.GetResourceObservable("Accent"));}
      if(here)Res(row,BackgroundProperty,"Hover");
     }
     MenuCommand(body,card,Glyphs.Plus,"Add profile","",ShowAddProfile);
     MenuCommand(body,card,Glyphs.Gear,"Manage profiles","",ShowManageProfiles);
    }
   });
  }

  void ShowAddProfile() {
   StackPanel body,actions;var dialog=MicaDialog("Add profile",440,out body,out actions);
   body.Children.Add(DialogText("A profile is a separate you: its own sign-ins, cookies, extensions, tabs and history. Nothing is shared with your other profiles.",12,"Muted"));
   SectionLabel(body,"NAME");
   var name=new TextBox {Padding=new Thickness(8,6),MaxLength=40};name.Classes.Add("field");body.Children.Add(name);
   SectionLabel(body,"COLOUR");
   string chosen=FrameSwatches[(LoadProfiles().Count*3)%FrameSwatches.Length];
   var swatches=new WrapPanel();body.Children.Add(swatches);var rings=new List<Border>();
   foreach(var hex in FrameSwatches) {
    string colour=hex;var ring=new Border {Width=30,Height=30,CornerRadius=new CornerRadius(15),BorderThickness=new Thickness(2),Margin=new Thickness(0,0,8,8),Cursor=new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),Background=Brushes.Transparent,Padding=new Thickness(3),BorderBrush=Brushes.Transparent};
    ring.Child=new Border {CornerRadius=new CornerRadius(11),Background=new SolidColorBrush(Color.Parse(colour))};
    Action mark=delegate {foreach(var r in rings)r.BorderBrush=Brushes.Transparent;Res(ring,Border.BorderBrushProperty,"Accent");};
    ring.PointerReleased+=delegate {chosen=colour;mark();};rings.Add(ring);swatches.Children.Add(ring);if(colour==chosen)mark();
   }
   var problem=DialogText("",11,"Muted");body.Children.Add(problem);
   var create=DialogButton("Create and open",true);actions.Children.Add(create);
   create.Click+=delegate {
    string title=(name.Text??"").Trim();if(title.Length==0){problem.Text="Give it a name, like Work or School.";name.Focus();return;}
    var list=LoadProfiles();var info=new ProfileInfo {Id=Guid.NewGuid().ToString("N"),Name=title,Color=chosen};
    try {Directory.CreateDirectory(ProfileFolder(info.Id));} catch(Exception e) {problem.Text="Couldn't create it: "+e.Message;return;}
    list.Add(info);SaveProfiles(list);dialog.IsOpen=false;OpenProfile(info.Id);
   };
   dialog.IsOpen=true;name.Focus();
  }
  void ShowManageProfiles() {
   StackPanel body,actions;var dialog=MicaDialog("Profiles",480,out body,out actions);
   body.Children.Add(DialogText("Rename a profile, or remove one and everything in it. The main profile stays.",12,"Muted"));
   var rows=new StackPanel {Margin=new Thickness(0,12,0,0)};body.Children.Add(rows);
   Action render=null;render=delegate {
    rows.Children.Clear();
    foreach(var item in LoadProfiles()) {
     var entry=item;var row=new Grid {Margin=new Thickness(12,6),ColumnDefinitions=new ColumnDefinitions("*,Auto,Auto")};
     var box=new TextBox {Text=entry.Name,MaxLength=40,Padding=new Thickness(8,5)};box.Classes.Add("field");row.Children.Add(box);
     box.LostFocus+=delegate {string t=(box.Text??"").Trim();if(t.Length==0||t==entry.Name)return;var list=LoadProfiles();var found=list.FirstOrDefault(p=>p.Id==entry.Id);if(found!=null){found.Name=t;SaveProfiles(list);}};
     if(entry.Id!=MainProfileId&&entry.Id!=ProfileId) {
      var remove=DialogButton("Remove",false);remove.Margin=new Thickness(8,0,0,0);Grid.SetColumn(remove,2);row.Children.Add(remove);
      bool armed=false;
      remove.Click+=delegate {
       if(!armed){armed=true;remove.Content="Remove for good?";return;}
       try {Directory.Delete(ProfileFolder(entry.Id),true);} catch(Exception e) {Status("Couldn't remove it: "+e.Message);return;}
       SaveProfiles(LoadProfiles().Where(p=>p.Id!=entry.Id).ToList());render();
      };
     }
     else if(entry.Id==ProfileId){var note=DialogText("This window",11,"Muted");note.Margin=new Thickness(10,0,0,0);note.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(note,1);row.Children.Add(note);}
     rows.Children.Add(Card(row));
    }
   };
   render();
   var done=DialogButton("Done",true);done.Click+=delegate {dialog.IsOpen=false;};actions.Children.Add(done);
   dialog.IsOpen=true;
  }

  // Language: kept in the profile ("en" or "vi"); without it Mica follows the system's language.
  void SwitchLanguage(string language) {
   try {File.WriteAllText(Path.Combine(profile,"language"),language);} catch(Exception) {}
   Localizer.Use(language);Localizer.Redraw(this);
   RenderTabs();ShowAddress();RefreshHomePages();
   foreach(var t in tabs.Values)if(t.View!=null&&t.View.IsBrowserInitialized&&Navigation.IsLibrary(t.Entry.Url))t.View.Reload();
   Status(Localizer.Vietnamese?"Mica giờ dùng tiếng Việt.":"Mica is in English now.");
  }
 }
}