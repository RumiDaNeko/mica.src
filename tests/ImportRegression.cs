using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Mica {
 partial class BrowserApp {
  // Import from another browser against made-up Chrome and Firefox profiles: what is found, what comes
  // over (once), what is left out, and passwords from an exported file.
  async Task RunImportTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"import");Directory.CreateDirectory(dir);
   window.Left=-6000;window.Top=-6000;
   try {
    string local=Path.Combine(dir,"local"),roaming=Path.Combine(dir,"roaming");BrowserImporter.LocalRoot=local;BrowserImporter.RoamingRoot=roaming;
    string data=Path.Combine(local,"Google","Chrome","User Data"),chrome=Path.Combine(data,"Default");Directory.CreateDirectory(chrome);
    File.WriteAllText(Path.Combine(data,"Local State"),"{\"profile\":{\"info_cache\":{\"Default\":{\"name\":\"Person 1\"}}}}");
    File.WriteAllText(Path.Combine(chrome,"Bookmarks"),"{\"roots\":{\"bookmark_bar\":{\"type\":\"folder\",\"children\":[{\"type\":\"url\",\"name\":\"Example\",\"url\":\"https://example.com/\",\"date_added\":\"13350000000000000\"},{\"type\":\"folder\",\"children\":[{\"type\":\"url\",\"name\":\"Nested\",\"url\":\"https://nested.test/\"}]},{\"type\":\"url\",\"name\":\"Settings\",\"url\":\"chrome://settings\"}]}}}");
    Sqlite.Create(Path.Combine(chrome,"History"),"CREATE TABLE urls(id INTEGER PRIMARY KEY,url TEXT,title TEXT,visit_count INTEGER,last_visit_time INTEGER,hidden INTEGER);INSERT INTO urls VALUES(1,'https://history.test/a','History A',3,13350000000000000,0);INSERT INTO urls VALUES(2,'https://hidden.test/','Hidden',1,13350000000000000,1);");
    Directory.CreateDirectory(Path.Combine(chrome,"Sessions"));File.WriteAllBytes(Path.Combine(chrome,"Sessions","Session_13350000000000000"),SessionFile(new[]{Tuple.Create(1,"https://tab.test/open","Open tab"),Tuple.Create(2,"https://closed.test/","Closed")},2));
    // Extensions: one from another store (copied), the browser's own component and a theme (both left out).
    string copied=Path.Combine(chrome,"Extensions","aaaabbbbccccddddeeeeffffgggghhhh","1.0_0");Directory.CreateDirectory(Path.Combine(copied,"_locales","en"));Directory.CreateDirectory(Path.Combine(copied,"_metadata"));
    File.WriteAllText(Path.Combine(copied,"manifest.json"),"{\"manifest_version\":3,\"name\":\"__MSG_name__\",\"default_locale\":\"en\",\"version\":\"1.0\",\"update_url\":\"https://edge.microsoft.com/extensionwebstorebase/v1/crx\"}");
    File.WriteAllText(Path.Combine(copied,"_locales","en","messages.json"),"{\"name\":{\"message\":\"Imported Test\"}}");File.WriteAllText(Path.Combine(copied,"_metadata","verified_contents.json"),"[]");
    string component=Path.Combine(chrome,"Extensions","bbbbccccddddeeeeffffgggghhhhiiii","1.0");Directory.CreateDirectory(component);File.WriteAllText(Path.Combine(component,"manifest.json"),"{\"manifest_version\":3,\"name\":\"Browser part\",\"version\":\"1.0\"}");
    string theme=Path.Combine(chrome,"Extensions","ccccddddeeeeffffgggghhhhiiiijjjj","1.0");Directory.CreateDirectory(theme);File.WriteAllText(Path.Combine(theme,"manifest.json"),"{\"manifest_version\":3,\"name\":\"A theme\",\"version\":\"1.0\",\"theme\":{}}");
    File.WriteAllText(Path.Combine(chrome,"Secure Preferences"),"{\"extensions\":{\"settings\":{\"bbbbccccddddeeeeffffgggghhhhiiii\":{\"location\":5}}}}");
    // Firefox: bookmarks and history in places.sqlite.
    string fox=Path.Combine(roaming,"Mozilla","Firefox"),foxProfile=Path.Combine(fox,"Profiles","abc.default");Directory.CreateDirectory(foxProfile);
    File.WriteAllText(Path.Combine(fox,"profiles.ini"),"[Profile0]\r\nName=default-release\r\nIsRelative=1\r\nPath=Profiles/abc.default\r\n");
    Sqlite.Create(Path.Combine(foxProfile,"places.sqlite"),"CREATE TABLE moz_places(id INTEGER PRIMARY KEY,url TEXT,title TEXT,visit_count INTEGER,last_visit_date INTEGER,hidden INTEGER);CREATE TABLE moz_bookmarks(id INTEGER PRIMARY KEY,type INTEGER,fk INTEGER,title TEXT,dateAdded INTEGER);INSERT INTO moz_places VALUES(1,'https://fox.test/','Fox page',2,1700000000000000,0);INSERT INTO moz_bookmarks VALUES(1,1,1,'Fox bookmark',1700000000000000);");

    var sources=BrowserImporter.Find();log.Add("found: "+string.Join(", ",sources.Select(s=>s.Name)));
    var source=sources.FirstOrDefault(s=>s.Browser=="Google Chrome");var firefox=sources.FirstOrDefault(s=>s.Firefox);
    RegressionCheck("other browsers' profiles are found, by their own names",delegate {Require(source!=null&&source.Name=="Google Chrome · Person 1"&&firefox!=null&&firefox.Name=="Firefox · default-release",string.Join(", ",sources.Select(s=>s.Name)));});
    var extensions=BrowserImporter.Extensions(source);
    RegressionCheck("extensions are listed by name; components and themes are left out",delegate {Require(extensions.Count==1&&extensions[0].Name=="Imported Test"&&!extensions[0].Store,string.Join(", ",extensions.Select(e=>e.Name+(e.Store?" (store)":""))));});
    var tabs=BrowserImporter.OpenTabs(source);
    RegressionCheck("the open tabs come from the last session, without closed ones",delegate {Require(tabs.Count==1&&tabs[0].Url=="https://tab.test/open"&&tabs[0].Title=="Open tab",string.Join(", ",tabs.Select(t=>t.Url)));});

    int bookmarksBefore=state.Bookmarks.Count,tabsBefore=state.Tabs.Count;
    string summary=await ImportFrom(source,true,true,true,extensions);log.Add("import: "+summary);
    RegressionCheck("bookmarks, history and tabs come over",delegate {
     Require(state.Bookmarks.Any(b=>b.Url=="https://example.com/"&&b.Title=="Example")&&state.Bookmarks.Any(b=>b.Url=="https://nested.test/")&&!state.Bookmarks.Any(b=>b.Url.StartsWith("chrome:")),"bookmarks "+state.Bookmarks.Count);
     Require(state.History.Any(h=>h.Url=="https://history.test/a"&&h.VisitCount==3)&&!state.History.Any(h=>h.Url=="https://hidden.test/"),"history");
     Require(state.Tabs.Count==tabsBefore+1&&state.Tabs.Any(t=>t.Url=="https://tab.test/open"),"tabs "+state.Tabs.Count);
    });
    var installed=await GetExtensions();string copy=Path.Combine(profile,"ImportedExtensions","aaaabbbbccccddddeeeeffffgggghhhh");
    RegressionCheck("an extension from another store is copied in and runs",delegate {Require(installed.Any(e=>e.Name=="Imported Test"),"installed: "+string.Join(", ",installed.Select(e=>e.Name)));Require(File.Exists(Path.Combine(copy,"manifest.json"))&&!Directory.Exists(Path.Combine(copy,"_metadata")),"copy");});
    RegressionCheck("the summary says what came over",delegate {Require(summary.Contains("2 bookmarks")&&summary.Contains("1 page of history")&&summary.Contains("1 tab")&&summary.Contains("1 extension"),summary);});
    string again=await ImportFrom(source,true,true,true,new List<ImportExtension>());
    RegressionCheck("importing twice adds nothing twice",delegate {Require(state.Bookmarks.Count==bookmarksBefore+2&&state.Tabs.Count==tabsBefore+1&&state.History.Count(h=>h.Url=="https://history.test/a")==1,again);});
    string fromFox=await ImportFrom(firefox,true,true,false,new List<ImportExtension>());log.Add("firefox: "+fromFox);
    RegressionCheck("Firefox bookmarks and history come over",delegate {Require(state.Bookmarks.Any(b=>b.Url=="https://fox.test/"&&b.Title=="Fox bookmark")&&state.History.Any(h=>h.Url=="https://fox.test/"),fromFox);});

    // Passwords: only from a file you exported. Chrome's and Firefox's column layouts, quotes, commas and newlines.
    int skipped;var parsed=BrowserImporter.PasswordsCsv("\"url\",\"username\",\"password\",\"httpRealm\",\"formActionOrigin\",\"guid\"\r\n\"https://fox-login.test\",\"me\",\"a,b\"\"c\nd\",,\"\",\"{1}\"\r\n",out skipped);
    RegressionCheck("Firefox's export format is read, quotes and all",delegate {Require(parsed.Count==1&&parsed[0].Origin=="https://fox-login.test"&&parsed[0].Password=="a,b\"c\nd"&&skipped==0,parsed.Count+" "+(parsed.Count>0?parsed[0].Password:""));});
    string csv=Path.Combine(dir,"Chrome Passwords.csv");File.WriteAllText(csv,"name,url,username,password,note\r\nlogin.test,https://login.test/signin,user1,secret1,\r\ninsecure,http://insecure.test/,user2,secret2,\r\nempty,https://empty.test/,user3,,\r\n",new UTF8Encoding(true));
    string added=await ImportPasswords(csv);log.Add("passwords: "+added);
    var saved=SavedLogins("https://login.test");
    RegressionCheck("an exported passwords file fills in later",delegate {Require(saved.Any(e=>e.Username=="user1"&&e.Password=="secret1")&&added.StartsWith("Added 1 password.")&&added.Contains("Skipped 2"),added);});
    RegressionCheck("non-https entries aren't saved",delegate {Require(SavedLogins("http://insecure.test").Count==0,"saved");});
    EraseFile(csv);RegressionCheck("the exported file can be deleted afterwards",delegate {Require(!File.Exists(csv),"still there");});

    // The window itself.
    ShowImport();await Task.Delay(500);
    RegressionCheck("the import window lists the browsers found",delegate {Require(importWindow!=null&&VaultElements<Button>(importWindow).Any(b=>(b.Tag as string)=="import-source:Google Chrome · Person 1"),"window "+(importWindow!=null));});
    if(importWindow!=null){importWindow.UpdateLayout();GrabWindow(importWindow,"mica-import.png");importWindow.Close();}
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
  // A Chromium session file ("SNSS"): each tab's navigation, the selected one, and which tab was closed.
  static byte[] SessionFile(Tuple<int,string,string>[] tabs,int closedTab) {
   var file=new List<byte>(Encoding.ASCII.GetBytes("SNSS"));file.AddRange(BitConverter.GetBytes(3));
   Action<byte,List<byte>> record=delegate(byte id,List<byte> body) {file.AddRange(BitConverter.GetBytes((ushort)(body.Count+1)));file.Add(id);file.AddRange(body);};
   foreach(var tab in tabs) {
    var payload=new List<byte>();payload.AddRange(BitConverter.GetBytes(tab.Item1));payload.AddRange(BitConverter.GetBytes(0));
    var url=Encoding.UTF8.GetBytes(tab.Item2);payload.AddRange(BitConverter.GetBytes(url.Length));payload.AddRange(url);while(payload.Count%4!=0)payload.Add(0);
    var title=Encoding.Unicode.GetBytes(tab.Item3);payload.AddRange(BitConverter.GetBytes(tab.Item3.Length));payload.AddRange(title);while(payload.Count%4!=0)payload.Add(0);
    var pickle=new List<byte>(BitConverter.GetBytes(payload.Count));pickle.AddRange(payload);record(6,pickle);
    var selected=new List<byte>(BitConverter.GetBytes(tab.Item1));selected.AddRange(BitConverter.GetBytes(0));record(7,selected);
   }
   var closed=new List<byte>(BitConverter.GetBytes(closedTab));closed.AddRange(BitConverter.GetBytes(0L));record(16,closed);
   return file.ToArray();
  }
 }
}
