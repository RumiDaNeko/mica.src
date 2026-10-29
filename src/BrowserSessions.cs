using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Mica {
 // Session snapshots (a named set of every window's tabs, restored as a resting group), the address-bar
 // site shortcuts editor, and the password check (weak, reused and, if you ask, leaked passwords).
 partial class BrowserApp {
  // ---- Sessions ----
  internal sealed class SavedSession {public string Name {get;set;} public DateTime Saved {get;set;} public List<PageEntry> Tabs {get;set;}}
  string SessionsFolder {get {return Path.Combine(profile,"Sessions");}}
  internal List<SavedSession> LoadSessions() {
   var list=new List<SavedSession>();if(!Directory.Exists(SessionsFolder))return list;
   foreach(var file in Directory.GetFiles(SessionsFolder,"*.json"))try {var s=new JavaScriptSerializer {MaxJsonLength=int.MaxValue}.Deserialize<SavedSession>(File.ReadAllText(file));if(s!=null&&s.Tabs!=null)list.Add(s);}catch(Exception){}
   return list.OrderByDescending(s=>s.Saved).ToList();
  }
  static string SessionFile(string name) {var safe=new string(name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).ToArray()).Trim();return (safe.Length==0?"Session":safe)+".json";}
  internal SavedSession SaveSessionAs(string name) {
   var tabs=Program.Browsers.Where(b=>!b.closing&&b.profile==profile&&!b.privateMode).SelectMany(b=>b.state.Tabs).Where(t=>Navigation.IsWeb(t.Url))
    .Select(t=>new PageEntry {Url=t.Url,Title=t.Title,Space=t.Space,Pinned=t.Pinned,Jar=t.Jar}).ToList();
   if(tabs.Count==0){Status("There are no website tabs to save.");return null;}
   var session=new SavedSession {Name=name,Saved=DateTime.UtcNow,Tabs=tabs};
   try {Directory.CreateDirectory(SessionsFolder);File.WriteAllText(Path.Combine(SessionsFolder,SessionFile(name)),new JavaScriptSerializer {MaxJsonLength=int.MaxValue}.Serialize(session));}
   catch(Exception e){Status("Couldn't save the session: "+e.Message);return null;}
   Status("Saved “"+name+"”: "+tabs.Count+(tabs.Count==1?" tab.":" tabs."));return session;
  }
  // Back as one group of resting tabs per space: nothing loads until you open one.
  internal int RestoreSession(SavedSession session) {
   int added=0;
   foreach(var space in session.Tabs.GroupBy(t=>state.KnownSpace(t.Space))) {
    var tabs=new List<PageEntry>();
    foreach(var saved in space){if(!Navigation.IsWeb(saved.Url))continue;var tab=new PageEntry {Id=Guid.NewGuid().ToString("N"),Url=saved.Url,Title=saved.Title,Space=space.Key,Jar=saved.Jar!=null&&state.Jars.Any(j=>j.Id==saved.Jar)?saved.Jar:null,LastUsed=DateTime.UtcNow.AddMinutes(-1)};state.Tabs.Add(tab);tabs.Add(tab);added++;}
    if(tabs.Count==0)continue;
    // The group is made around its first tab: an empty group would be tidied away.
    var group=state.CreateGroup(tabs[0]);group.Name=session.Name;group.Collapsed=true;foreach(var tab in tabs)tab.Group=group.Id;
   }
   state.NormalizeGroups();RenderTabs();MarkRestingTabs(false);Save();
   Status("“"+session.Name+"” is back: "+added+(added==1?" tab":" tabs")+" in a folded group. Open the group to start.");return added;
  }
  Popup sessionsPopup;
  internal void SaveSession() {
   if(privateMode){Status("Private windows don't keep sessions.");return;}
   CloseMenus();if(sessionsPopup==null){sessionsPopup=CreatePopup("SessionsPopup",340);sessionsPopup.Placement=PlacementMode.Center;}
   var body=PopupBody(sessionsPopup);
   var head=DialogText("Save this session",15,"Ink");head.FontWeight=FontWeights.SemiBold;head.Margin=new Thickness(14,12,14,2);body.Children.Add(head);
   int count=Program.Browsers.Where(b=>!b.closing&&b.profile==profile&&!b.privateMode).Sum(b=>b.state.Tabs.Count(t=>Navigation.IsWeb(t.Url)));
   var about=DialogText(count+(count==1?" tab":" tabs")+" from every window. Restore it any time from the palette (> restore).",11,"Muted");about.TextWrapping=TextWrapping.Wrap;about.Margin=new Thickness(14,0,14,8);body.Children.Add(about);
   var name=new TextBox {Text="Session "+DateTime.Now.ToString("d MMM HH:mm"),Margin=new Thickness(14,2,14,8),Padding=new Thickness(6,4,6,4),MaxLength=60,Tag="session-name"};body.Children.Add(name);
   var save=new Button {Content="Save",Margin=new Thickness(14,0,14,12),Padding=new Thickness(14,5,14,5),HorizontalAlignment=HorizontalAlignment.Left,Tag="session-save"};
   Action go=delegate {string n=name.Text.Trim();if(n.Length==0)return;if(SaveSessionAs(n)!=null)sessionsPopup.IsOpen=false;};
   save.Click+=delegate {go();};name.KeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e) {if(e.Key==System.Windows.Input.Key.Enter){e.Handled=true;go();}};body.Children.Add(save);
   sessionsPopup.PlacementTarget=Find<Grid>("Root");sessionsPopup.IsOpen=true;name.Focus();name.SelectAll();
  }
  internal void ShowSessions() {
   CloseMenus();if(sessionsPopup==null){sessionsPopup=CreatePopup("SessionsPopup",340);sessionsPopup.Placement=PlacementMode.Center;}
   RenderSessions();sessionsPopup.PlacementTarget=Find<Grid>("Root");sessionsPopup.IsOpen=true;
  }
  void RenderSessions() {
   var body=PopupBody(sessionsPopup);var list=LoadSessions();
   var head=DialogText("Sessions",15,"Ink");head.FontWeight=FontWeights.SemiBold;head.Margin=new Thickness(14,12,14,2);body.Children.Add(head);
   var about=DialogText(list.Count==0?"None saved yet. Save one from the palette: > save session.":"Restoring adds the tabs as a folded group; they load when you open them.",11,"Muted");about.TextWrapping=TextWrapping.Wrap;about.Margin=new Thickness(14,0,14,8);body.Children.Add(about);
   foreach(var session in list) {
    var s=session;var row=new Grid {Margin=new Thickness(6,1,6,1),Tag="session:"+s.Name};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
    var text=new StackPanel();var title=DialogText(s.Name,12,"Ink");title.FontWeight=FontWeights.SemiBold;text.Children.Add(title);text.Children.Add(DialogText(s.Tabs.Count+(s.Tabs.Count==1?" tab":" tabs")+" · "+s.Saved.ToLocalTime().ToString("d MMM HH:mm"),10,"Muted"));
    var open=new Button {Content=text,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(8,6,8,6),ToolTip="Restore",Tag="session-restore:"+s.Name};open.Click+=delegate {sessionsPopup.IsOpen=false;RestoreSession(s);};row.Children.Add(open);
    var remove=new Button {Content="",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=11,Width=28,Height=28,Padding=new Thickness(0),ToolTip="Delete the saved session",Tag="session-delete:"+s.Name,VerticalAlignment=VerticalAlignment.Center};
    remove.Click+=delegate {try {File.Delete(Path.Combine(SessionsFolder,SessionFile(s.Name)));}catch(Exception){}RenderSessions();};Grid.SetColumn(remove,1);row.Children.Add(remove);body.Children.Add(row);
   }
   body.Children.Add(new Border {Height=8});
  }

  // ---- Site shortcuts: "yt cats" searches YouTube ----
  Popup keywordsPopup;
  internal void ShowKeywords() {
   CloseMenus();if(keywordsPopup==null){keywordsPopup=CreatePopup("KeywordsPopup",420);keywordsPopup.Placement=PlacementMode.Center;}
   RenderKeywords();keywordsPopup.PlacementTarget=Find<Grid>("Root");keywordsPopup.IsOpen=true;
  }
  void RenderKeywords() {
   var body=PopupBody(keywordsPopup);
   var head=DialogText("Site shortcuts",15,"Ink");head.FontWeight=FontWeights.SemiBold;head.Margin=new Thickness(14,12,14,2);body.Children.Add(head);
   var about=DialogText("Type a shortcut, a space and your words in the address bar: “yt lofi” searches YouTube. %s marks where the words go.",11,"Muted");about.TextWrapping=TextWrapping.Wrap;about.Margin=new Thickness(14,0,14,8);body.Children.Add(about);
   foreach(var pair in state.SiteKeywords.OrderBy(p=>p.Key).ToList()) {
    string key=pair.Key;var row=new Grid {Margin=new Thickness(14,2,8,2),Tag="keyword:"+key};
    row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(64)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
    var word=new TextBox {Text=key,Padding=new Thickness(4,3,4,3),MaxLength=12,Margin=new Thickness(0,0,6,0),Tag="keyword-word:"+key};row.Children.Add(word);
    var address=new TextBox {Text=pair.Value,Padding=new Thickness(4,3,4,3),Tag="keyword-url:"+key};Grid.SetColumn(address,1);row.Children.Add(address);
    Action commit=delegate {
     string w=word.Text.Trim().ToLowerInvariant(),u=address.Text.Trim();if(w==key&&u==state.SiteKeywords[key])return;
     if(w.Length==0||w.Contains(" ")||!u.Contains("%s")||!Navigation.IsWeb(u.Replace("%s","x"))){Status("A shortcut is one word, and its address needs %s where your words go.");word.Text=key;address.Text=state.SiteKeywords[key];return;}
     state.SiteKeywords.Remove(key);state.SiteKeywords[w]=u;Save();RenderKeywords();
    };
    word.LostKeyboardFocus+=delegate {commit();};address.LostKeyboardFocus+=delegate {commit();};
    var remove=new Button {Content="",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=10,Width=26,Height=26,Padding=new Thickness(0),Margin=new Thickness(4,0,0,0),ToolTip="Remove",Tag="keyword-remove:"+key};
    remove.Click+=delegate {state.SiteKeywords.Remove(key);Save();RenderKeywords();};Grid.SetColumn(remove,2);row.Children.Add(remove);body.Children.Add(row);
   }
   var add=new Button {Content="Add a shortcut",Margin=new Thickness(14,8,14,12),HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(12,5,12,5),Tag="keyword-add"};
   add.Click+=delegate {string w="new";int n=2;while(state.SiteKeywords.ContainsKey(w))w="new"+(n++);state.SiteKeywords[w]="https://www.google.com/search?q=site%3Aexample.com+%s";Save();RenderKeywords();};body.Children.Add(add);
  }

  // ---- Password check ----
  sealed class PasswordFinding {public VaultEntry Entry;public bool Weak,Reused;public int Leaked=-1;}
  internal static bool WeakPassword(string p) {
   if(string.IsNullOrEmpty(p)||p.Length<8)return true;
   int kinds=(p.Any(char.IsLower)?1:0)+(p.Any(char.IsUpper)?1:0)+(p.Any(char.IsDigit)?1:0)+(p.Any(c=>!char.IsLetterOrDigit(c))?1:0);
   return p.Length<12&&kinds<3||p.Distinct().Count()<=3;
  }
  List<PasswordFinding> CheckPasswordsLocally() {
   var all=new List<VaultEntry>();if(sessionVault!=null)all.AddRange(sessionVault.Entries);
   foreach(var e in DeviceLogins.Load(profile))if(!all.Any(a=>VaultCrypto.Matches(a.Origin,e.Origin)&&a.Username==e.Username))all.Add(e);
   var counts=all.Where(e=>!string.IsNullOrEmpty(e.Password)).GroupBy(e=>e.Password).ToDictionary(g=>g.Key,g=>g.Select(e=>e.Origin).Distinct().Count());
   return all.Where(e=>!string.IsNullOrEmpty(e.Password)).Select(e=>new PasswordFinding {Entry=e,Weak=WeakPassword(e.Password),Reused=counts[e.Password]>1}).ToList();
  }
  // Have I Been Pwned's range API: only the first 5 characters of each password's SHA-1 leave this PC.
  internal static int LeakCount(string password,Func<string,string> fetch) {
   string hex;using(var sha=SHA1.Create())hex=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(password))).Replace("-","");
   string body=fetch(hex.Substring(0,5));if(body==null)return -1;
   foreach(var line in body.Split('\n')){var parts=line.Trim().Split(':');int n;if(parts.Length==2&&string.Equals(parts[0],hex.Substring(5),StringComparison.OrdinalIgnoreCase)&&int.TryParse(parts[1],out n))return n;}
   return 0;
  }
  static string PwnedRange(string prefix) {
   var request=(HttpWebRequest)WebRequest.Create("https://api.pwnedpasswords.com/range/"+prefix);request.Timeout=10000;request.UserAgent="Micarium-password-check";request.Headers["Add-Padding"]="true";
   using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream()))return reader.ReadToEnd();
  }
  internal Func<string,string> pwnedForTest;
  Popup passwordCheckPopup;List<PasswordFinding> lastFindings;
  internal async void ShowPasswordCheck() {
   if(privateMode)return;CloseMenus();
   if(sessionVault==null)await OpenAutofillVault();
   if(passwordCheckPopup==null){passwordCheckPopup=CreatePopup("PasswordCheckPopup",400);passwordCheckPopup.Placement=PlacementMode.Center;}
   lastFindings=CheckPasswordsLocally();RenderPasswordCheck(false);passwordCheckPopup.PlacementTarget=Find<Grid>("Root");passwordCheckPopup.IsOpen=true;
  }
  void RenderPasswordCheck(bool checking) {
   var body=PopupBody(passwordCheckPopup);var findings=lastFindings??new List<PasswordFinding>();
   var head=DialogText("Password check",15,"Ink");head.FontWeight=FontWeights.SemiBold;head.Margin=new Thickness(14,12,14,2);body.Children.Add(head);
   int weak=findings.Count(f=>f.Weak),reused=findings.Count(f=>f.Reused),leaked=findings.Count(f=>f.Leaked>0);bool online=findings.Any(f=>f.Leaked>=0);
   string summary=findings.Count==0?"No saved passwords to check.":findings.Count+" saved · "+weak+" weak · "+reused+" reused"+(online?" · "+leaked+" found in leaks":"");
   var about=DialogText(summary,11,"Muted");about.Tag="password-summary";about.Margin=new Thickness(14,0,14,8);body.Children.Add(about);
   foreach(var f in findings.OrderByDescending(f=>(f.Leaked>0?4:0)+(f.Reused?2:0)+(f.Weak?1:0))) {
    var problems=new List<string>();if(f.Leaked>0)problems.Add("seen in "+f.Leaked.ToString("N0")+" leaks");if(f.Reused)problems.Add("used on other sites");if(f.Weak)problems.Add("easy to guess");
    if(problems.Count==0&&findings.Count>8)continue;
    var row=new StackPanel {Margin=new Thickness(14,3,14,3),Tag="password:"+f.Entry.Origin};
    var site=DialogText(Navigation.AddressLabel(f.Entry.Origin)+(string.IsNullOrEmpty(f.Entry.Username)?"":" · "+f.Entry.Username),12,"Ink");site.TextTrimming=TextTrimming.CharacterEllipsis;row.Children.Add(site);
    row.Children.Add(DialogText(problems.Count==0?"Looks fine":char.ToUpper(problems[0][0])+string.Join(", ",problems).Substring(1),10,problems.Count==0?"Muted":"Accent"));body.Children.Add(row);
   }
   if(findings.Count>0) {
    var leakNote=DialogText("Checking for leaks sends the first 5 characters of each password's SHA-1 fingerprint to Have I Been Pwned; never the password.",10,"Muted");leakNote.TextWrapping=TextWrapping.Wrap;leakNote.Margin=new Thickness(14,10,14,4);body.Children.Add(leakNote);
    var check=new Button {Content=checking?"Checking…":online?"Check again":"Check for leaks",IsEnabled=!checking,Margin=new Thickness(14,4,14,12),Padding=new Thickness(12,5,12,5),HorizontalAlignment=HorizontalAlignment.Left,Tag="password-leaks"};
    check.Click+=async delegate {
     RenderPasswordCheck(true);var fetch=pwnedForTest??PwnedRange;var list=lastFindings;
     await Task.Run(delegate {foreach(var group in list.GroupBy(x=>x.Entry.Password)){int n;try {n=LeakCount(group.Key,fetch);}catch(Exception){n=-1;}foreach(var x in group)x.Leaked=n;}});
     if(list.All(x=>x.Leaked<0))Status("Couldn't reach Have I Been Pwned.");
     RenderPasswordCheck(false);
    };
    body.Children.Add(check);
   }
  }
 }
}
