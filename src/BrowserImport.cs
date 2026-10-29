using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mica {
 // Read-only access to SQLite databases through the copy of SQLite that ships with Windows.
 static class Sqlite {
  [DllImport("winsqlite3.dll",EntryPoint="sqlite3_open_v2")] static extern int Open(byte[] path,out IntPtr db,int flags,IntPtr vfs);
  [DllImport("winsqlite3.dll",EntryPoint="sqlite3_close")] static extern int Close(IntPtr db);
  [DllImport("winsqlite3.dll",EntryPoint="sqlite3_prepare_v2")] static extern int Prepare(IntPtr db,byte[] sql,int length,out IntPtr statement,IntPtr tail);
  [DllImport("winsqlite3.dll",EntryPoint="sqlite3_step")] static extern int Step(IntPtr statement);
  [DllImport("winsqlite3.dll",EntryPoint="sqlite3_finalize")] static extern int Finalize(IntPtr statement);
  [DllImport("winsqlite3.dll",EntryPoint="sqlite3_column_text")] static extern IntPtr ColumnText(IntPtr statement,int column);
  [DllImport("winsqlite3.dll",EntryPoint="sqlite3_column_bytes")] static extern int ColumnBytes(IntPtr statement,int column);
  [DllImport("winsqlite3.dll",EntryPoint="sqlite3_column_int64")] static extern long ColumnLong(IntPtr statement,int column);
  [DllImport("winsqlite3.dll",EntryPoint="sqlite3_exec")] static extern int Exec(IntPtr db,byte[] sql,IntPtr callback,IntPtr argument,IntPtr error);
  static byte[] Utf8(string s) {return Encoding.UTF8.GetBytes(s+"\0");}
  // Rows as arrays of strings (numbers come back as text too).
  internal static List<string[]> Query(string path,string sql,int columns) {
   var rows=new List<string[]>();IntPtr db;
   if(Open(Utf8(path),out db,0x01,IntPtr.Zero)!=0){Close(db);throw new IOException("Couldn't open "+Path.GetFileName(path));}
   try {
    IntPtr statement;if(Prepare(db,Utf8(sql),-1,out statement,IntPtr.Zero)!=0)throw new IOException("Unexpected database layout in "+Path.GetFileName(path));
    try {
     while(Step(statement)==100) {
      var row=new string[columns];
      for(int i=0;i<columns;i++){var text=ColumnText(statement,i);int size=ColumnBytes(statement,i);if(text==IntPtr.Zero){row[i]=null;continue;}var bytes=new byte[size];Marshal.Copy(text,bytes,0,size);row[i]=Encoding.UTF8.GetString(bytes);}
      rows.Add(row);
     }
    }finally {Finalize(statement);}
   }finally {Close(db);}
   return rows;
  }
  // For tests: builds a database from SQL.
  internal static void Create(string path,string sql) {
   IntPtr db;if(Open(Utf8(path),out db,0x02|0x04,IntPtr.Zero)!=0){Close(db);throw new IOException("Couldn't create "+path);}
   try {if(Exec(db,Utf8(sql),IntPtr.Zero,IntPtr.Zero,IntPtr.Zero)!=0)throw new IOException("Fixture SQL failed");}finally {Close(db);}
  }
 }

 // A browser profile Mica can import from.
 sealed class ImportSource {
  internal string Browser,Profile,Folder;internal bool Firefox;
  internal string Name {get {return Profile==null?Browser:Browser+" · "+Profile;}}
 }
 sealed class ImportResult {internal int Bookmarks,History,Tabs;internal List<string> Notes=new List<string>();}

 // Finds other browsers' profiles and reads bookmarks, history and open tabs from them.
 // Everything is read from copies, so a browser that is running keeps working.
 static class BrowserImporter {
  static readonly string[][] Chromium={
   new[]{"Microsoft Edge","Local","Microsoft\\Edge\\User Data"},new[]{"Google Chrome","Local","Google\\Chrome\\User Data"},
   new[]{"Brave","Local","BraveSoftware\\Brave-Browser\\User Data"},new[]{"Vivaldi","Local","Vivaldi\\User Data"},
   new[]{"Opera","Roaming","Opera Software\\Opera Stable"},new[]{"Opera GX","Roaming","Opera Software\\Opera GX Stable"},
  };
  internal static string LocalRoot=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),RoamingRoot=Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

  internal static List<ImportSource> Find() {
   var found=new List<ImportSource>();
   foreach(var browser in Chromium) {
    string root=Path.Combine(browser[1]=="Local"?LocalRoot:RoamingRoot,browser[2]);if(!Directory.Exists(root))continue;
    var names=ProfileNames(root);
    if(File.Exists(Path.Combine(root,"Bookmarks"))||File.Exists(Path.Combine(root,"History")))found.Add(new ImportSource {Browser=browser[0],Folder=root});
    foreach(var folder in Directory.GetDirectories(root)) {
     string name=Path.GetFileName(folder);if(name!="Default"&&!name.StartsWith("Profile "))continue;
     if(!File.Exists(Path.Combine(folder,"Bookmarks"))&&!File.Exists(Path.Combine(folder,"History")))continue;
     string label;names.TryGetValue(name,out label);
     found.Add(new ImportSource {Browser=browser[0],Profile=label??name,Folder=folder});
    }
   }
   string firefox=Path.Combine(RoamingRoot,"Mozilla","Firefox");
   if(File.Exists(Path.Combine(firefox,"profiles.ini"))) {
    string name=null,path=null;bool relative=true;
    Action add=delegate {
     if(path==null)return;string folder=relative?Path.Combine(firefox,path.Replace('/','\\')):path;
     if(File.Exists(Path.Combine(folder,"places.sqlite")))found.Add(new ImportSource {Browser="Firefox",Profile=name,Folder=folder,Firefox=true});
    };
    foreach(var raw in File.ReadAllLines(Path.Combine(firefox,"profiles.ini"))) {
     string line=raw.Trim();
     if(line.StartsWith("[")){add();name=null;path=null;relative=true;continue;}
     if(line.StartsWith("Name="))name=line.Substring(5);else if(line.StartsWith("Path="))path=line.Substring(5);else if(line.StartsWith("IsRelative="))relative=line.EndsWith("1");
    }
    add();
   }
   return found;
  }
  static Dictionary<string,string> ProfileNames(string root) {
   var names=new Dictionary<string,string>();
   try {
    var state=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(root,"Local State"))) as Dictionary<string,object>;object value;
    var profile=state!=null&&state.TryGetValue("profile",out value)?value as Dictionary<string,object>:null;
    var cache=profile!=null&&profile.TryGetValue("info_cache",out value)?value as Dictionary<string,object>:null;
    if(cache!=null)foreach(var pair in cache){var info=pair.Value as Dictionary<string,object>;if(info!=null&&info.TryGetValue("name",out value)&&value is string)names[pair.Key]=(string)value;}
   }catch(Exception){}
   return names;
  }

  // Copies a file (and its journal) that another program may hold open, into a private temp folder.
  static string Snapshot(string file,string temp) {
   string copy=Path.Combine(temp,Path.GetFileName(file));
   foreach(var suffix in new[]{"","-wal","-journal"}) {
    if(!File.Exists(file+suffix))continue;
    using(var source=new FileStream(file+suffix,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var target=File.Create(copy+suffix))source.CopyTo(target);
   }
   return copy;
  }
  static DateTime FromChromeTime(string value) {long micro;return long.TryParse(value,out micro)&&micro>0?new DateTime(1601,1,1,0,0,0,DateTimeKind.Utc).AddTicks(micro*10):DateTime.UtcNow;}
  static DateTime FromUnixMicro(string value) {long micro;return long.TryParse(value,out micro)&&micro>0?new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddTicks(micro*10):DateTime.UtcNow;}

  internal static List<PageEntry> Bookmarks(ImportSource source,string temp) {
   var list=new List<PageEntry>();
   if(source.Firefox) {
    string db=Snapshot(Path.Combine(source.Folder,"places.sqlite"),temp);
    foreach(var row in Sqlite.Query(db,"SELECT b.title,p.url,b.dateAdded FROM moz_bookmarks b JOIN moz_places p ON p.id=b.fk WHERE b.type=1 ORDER BY b.dateAdded",3))
     list.Add(new PageEntry {Title=string.IsNullOrEmpty(row[0])?row[1]:row[0],Url=row[1],LastUsed=FromUnixMicro(row[2])});
   }else {
    string file=Path.Combine(source.Folder,"Bookmarks");if(!File.Exists(file))return list;
    var data=new JavaScriptSerializer {MaxJsonLength=int.MaxValue}.DeserializeObject(File.ReadAllText(file)) as Dictionary<string,object>;object value;
    var roots=data!=null&&data.TryGetValue("roots",out value)?value as Dictionary<string,object>:null;
    Action<object> walk=null;
    walk=delegate(object node) {
     var item=node as Dictionary<string,object>;if(item==null)return;object v;
     string type=item.TryGetValue("type",out v)?v as string:null;
     if(type=="url"&&item.TryGetValue("url",out v)&&v is string)list.Add(new PageEntry {Title=item.TryGetValue("name",out v)&&v is string&&((string)v).Length>0?(string)v:(string)item["url"],Url=(string)item["url"],LastUsed=FromChromeTime(item.TryGetValue("date_added",out v)?v as string:null)});
     if(item.TryGetValue("children",out v)&&v is object[])foreach(var child in (object[])v)walk(child);
    };
    if(roots!=null)foreach(var root in roots.Values)walk(root);
   }
   return list.Where(b=>Navigation.IsWeb(b.Url)).ToList();
  }
  internal static List<PageEntry> History(ImportSource source,string temp) {
   var list=new List<PageEntry>();
   if(source.Firefox) {
    string db=Snapshot(Path.Combine(source.Folder,"places.sqlite"),temp);
    foreach(var row in Sqlite.Query(db,"SELECT url,title,visit_count,last_visit_date FROM moz_places WHERE last_visit_date IS NOT NULL AND hidden=0 ORDER BY last_visit_date DESC LIMIT 2000",4))
     list.Add(new PageEntry {Url=row[0],Title=string.IsNullOrEmpty(row[1])?row[0]:row[1],VisitCount=Math.Max(1,ParseInt(row[2])),LastUsed=FromUnixMicro(row[3])});
   }else {
    string file=Path.Combine(source.Folder,"History");if(!File.Exists(file))return list;
    string db=Snapshot(file,temp);
    foreach(var row in Sqlite.Query(db,"SELECT url,title,visit_count,last_visit_time FROM urls WHERE hidden=0 ORDER BY last_visit_time DESC LIMIT 2000",4))
     list.Add(new PageEntry {Url=row[0],Title=string.IsNullOrEmpty(row[1])?row[0]:row[1],VisitCount=Math.Max(1,ParseInt(row[2])),LastUsed=FromChromeTime(row[3])});
   }
   return list.Where(h=>Navigation.IsWeb(h.Url)).ToList();
  }
  static int ParseInt(string s) {int v;return int.TryParse(s,out v)?v:0;}

  // The pages that were open when the browser last closed. Best effort: session files are internal formats.
  internal static List<PageEntry> OpenTabs(ImportSource source) {
   try {return (source.Firefox?FirefoxTabs(source):ChromiumTabs(source)).Where(t=>Navigation.IsWeb(t.Url)).GroupBy(t=>t.Url).Select(g=>g.First()).Take(60).ToList();}
   catch(Exception){return new List<PageEntry>();}
  }
  static List<PageEntry> FirefoxTabs(ImportSource source) {
   var list=new List<PageEntry>();
   string file=new[]{"sessionstore-backups\\recovery.jsonlz4","sessionstore.jsonlz4","sessionstore-backups\\previous.jsonlz4"}.Select(f=>Path.Combine(source.Folder,f)).FirstOrDefault(File.Exists);
   if(file==null)return list;
   byte[] raw;using(var s=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var m=new MemoryStream()){s.CopyTo(m);raw=m.ToArray();}
   if(raw.Length<12||Encoding.ASCII.GetString(raw,0,8)!="mozLz40\0")return list;
   string json=Encoding.UTF8.GetString(Lz4Block(raw,12,BitConverter.ToInt32(raw,8)));
   var data=new JavaScriptSerializer {MaxJsonLength=int.MaxValue}.DeserializeObject(json) as Dictionary<string,object>;object v;
   var windows=data!=null&&data.TryGetValue("windows",out v)?v as object[]:null;if(windows==null)return list;
   foreach(var w in windows.OfType<Dictionary<string,object>>()) {
    var tabs=w.TryGetValue("tabs",out v)?v as object[]:null;if(tabs==null)continue;
    foreach(var t in tabs.OfType<Dictionary<string,object>>()) {
     var entries=t.TryGetValue("entries",out v)?v as object[]:null;if(entries==null||entries.Length==0)continue;
     int index=t.TryGetValue("index",out v)&&v is int?(int)v:entries.Length;index=Math.Max(1,Math.Min(entries.Length,index));
     var entry=entries[index-1] as Dictionary<string,object>;if(entry==null||!entry.TryGetValue("url",out v)||!(v is string))continue;
     string url=(string)v;string title=entry.TryGetValue("title",out v)&&v is string?(string)v:url;
     list.Add(new PageEntry {Url=url,Title=title});
    }
   }
   return list;
  }
  // Decodes one LZ4 block (Firefox's mozLz4 session files).
  internal static byte[] Lz4Block(byte[] input,int offset,int size) {
   var output=new byte[size];int ip=offset,op=0;
   while(ip<input.Length) {
    int token=input[ip++];int literal=token>>4;
    if(literal==15){int b;do {b=input[ip++];literal+=b;}while(b==255);}
    Buffer.BlockCopy(input,ip,output,op,literal);ip+=literal;op+=literal;
    if(ip>=input.Length)break;
    int back=input[ip]|input[ip+1]<<8;ip+=2;int match=token&15;
    if(match==15){int b;do {b=input[ip++];match+=b;}while(b==255);}
    match+=4;int from=op-back;for(int i=0;i<match;i++)output[op++]=output[from+i];
   }
   return output;
  }
  // Chromium's session file ("SNSS"): navigation records per tab plus the selected entry.
  static List<PageEntry> ChromiumTabs(ImportSource source) {
   var list=new List<PageEntry>();
   string folder=Path.Combine(source.Folder,"Sessions");string file=null;
   if(Directory.Exists(folder))file=Directory.GetFiles(folder,"Session_*").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
   if(file==null&&File.Exists(Path.Combine(source.Folder,"Current Session")))file=Path.Combine(source.Folder,"Current Session");
   if(file==null)return list;
   byte[] data;using(var s=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var m=new MemoryStream()){s.CopyTo(m);data=m.ToArray();}
   if(data.Length<8||Encoding.ASCII.GetString(data,0,4)!="SNSS")return list;
   var navigations=new Dictionary<int,SortedDictionary<int,Tuple<string,string>>>();var selected=new Dictionary<int,int>();var closed=new HashSet<int>();var order=new List<int>();
   int pos=8;
   while(pos+3<=data.Length) {
    int size=BitConverter.ToUInt16(data,pos);pos+=2;if(size==0||pos+size>data.Length)break;
    int id=data[pos];int start=pos+1,end=pos+size;pos=end;
    try {
     if(id==6) { // update tab navigation: pickle(size, tab, index, url, title, ...)
      int p=start+4;int tab=BitConverter.ToInt32(data,p);int index=BitConverter.ToInt32(data,p+4);p+=8;
      int urlLength=BitConverter.ToInt32(data,p);p+=4;if(urlLength<0||p+urlLength>end)continue;
      string url=Encoding.UTF8.GetString(data,p,urlLength);p+=(urlLength+3)&~3;string title=url;
      if(p+4<=end){int titleLength=BitConverter.ToInt32(data,p);p+=4;if(titleLength>=0&&p+titleLength*2<=end)title=Encoding.Unicode.GetString(data,p,titleLength*2);}
      SortedDictionary<int,Tuple<string,string>> entries;if(!navigations.TryGetValue(tab,out entries)){entries=new SortedDictionary<int,Tuple<string,string>>();navigations[tab]=entries;order.Add(tab);}
      entries[index]=Tuple.Create(url,title);
     }else if(id==7&&end-start>=8)selected[BitConverter.ToInt32(data,start)]=BitConverter.ToInt32(data,start+4); // selected navigation: tab, index
     else if(id==16&&end-start>=4)closed.Add(BitConverter.ToInt32(data,start)); // tab closed
    }catch(ArgumentException){}
   }
   foreach(int tab in order) {
    if(closed.Contains(tab))continue;var entries=navigations[tab];int index;Tuple<string,string> shown;
    if(!selected.TryGetValue(tab,out index)||!entries.TryGetValue(index,out shown))shown=entries.Last().Value;
    list.Add(new PageEntry {Url=shown.Item1,Title=string.IsNullOrEmpty(shown.Item2)?shown.Item1:shown.Item2});
   }
   return list;
  }

  // Extensions installed in a Chromium profile. They are ordinary files: Chrome Web Store ones are
  // fetched again from the store (signed, current), others are copied. Themes, apps and the browser's
  // own components are left out. Firefox add-ons are a different format, so they aren't offered.
  internal static List<ImportExtension> Extensions(ImportSource source) {
   var list=new List<ImportExtension>();if(source.Firefox)return list;
   string root=Path.Combine(source.Folder,"Extensions");if(!Directory.Exists(root))return list;
   var locations=ExtensionLocations(source.Folder);
   foreach(var folder in Directory.GetDirectories(root)) {
    string id=Path.GetFileName(folder);if(id.Length!=32||id.Any(c=>c<'a'||c>'p'))continue;
    int location;if(locations.TryGetValue(id,out location)&&(location==5||location==10))continue; // the browser's own component
    var version=Directory.GetDirectories(folder).Where(d=>File.Exists(Path.Combine(d,"manifest.json"))).OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault();if(version==null)continue;
    try {
     var manifest=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(version,"manifest.json"))) as Dictionary<string,object>;if(manifest==null)continue;
     if(manifest.ContainsKey("theme")||manifest.ContainsKey("app"))continue;
     object value;string name=manifest.TryGetValue("name",out value)?value as string:null;if(string.IsNullOrEmpty(name))continue;
     string update=manifest.TryGetValue("update_url",out value)?value as string:null;
     list.Add(new ImportExtension {Id=id,Name=LocalizedName(name,manifest,version),Version=manifest.TryGetValue("version",out value)?value as string:"",Folder=version,
      Store=string.IsNullOrEmpty(update)||update.IndexOf("clients2.google.com/service/update2/crx",StringComparison.OrdinalIgnoreCase)>=0});
    }catch(Exception){}
   }
   return list.OrderBy(e=>e.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
  }
  // Where each extension came from (Chromium's "location": 5 and 10 are the browser's own components).
  static Dictionary<string,int> ExtensionLocations(string folder) {
   var found=new Dictionary<string,int>();
   foreach(var file in new[]{"Secure Preferences","Preferences"}) {
    try {
     string path=Path.Combine(folder,file);if(!File.Exists(path))continue;
     var data=new JavaScriptSerializer {MaxJsonLength=int.MaxValue}.DeserializeObject(File.ReadAllText(path)) as Dictionary<string,object>;object value;
     var extensions=data!=null&&data.TryGetValue("extensions",out value)?value as Dictionary<string,object>:null;
     var settings=extensions!=null&&extensions.TryGetValue("settings",out value)?value as Dictionary<string,object>:null;if(settings==null)continue;
     foreach(var pair in settings){var item=pair.Value as Dictionary<string,object>;if(item!=null&&item.TryGetValue("location",out value)&&value is int&&!found.ContainsKey(pair.Key))found[pair.Key]=(int)value;}
    }catch(Exception){}
   }
   return found;
  }
  // "__MSG_appName__" names come from the extension's own translations.
  static string LocalizedName(string name,Dictionary<string,object> manifest,string folder) {
   if(!name.StartsWith("__MSG_")||!name.EndsWith("__")||name.Length<9)return name;string key=name.Substring(6,name.Length-8);
   object value;string locale=manifest.TryGetValue("default_locale",out value)?value as string:null;
   foreach(var candidate in new[]{locale,"en","en_US"}) {
    if(string.IsNullOrEmpty(candidate))continue;
    try {
     string path=Path.Combine(folder,"_locales",candidate,"messages.json");if(!File.Exists(path))continue;
     var messages=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)) as Dictionary<string,object>;if(messages==null)continue;
     foreach(var pair in messages){var entry=pair.Value as Dictionary<string,object>;if(string.Equals(pair.Key,key,StringComparison.OrdinalIgnoreCase)&&entry!=null&&entry.TryGetValue("message",out value)&&value is string)return (string)value;}
    }catch(Exception){}
   }
   return key;
  }

  // A passwords file you exported yourself (Chrome, Edge, Brave, Opera, Vivaldi, Firefox, Safari and
  // most password managers write the same kind of CSV). Mica never reads another browser's password store.
  internal static List<VaultEntry> PasswordsCsv(string text,out int skipped) {
   var list=new List<VaultEntry>();skipped=0;var rows=CsvRows(text);if(rows.Count==0)return list;
   var header=rows[0].Select(h=>h.Trim().ToLowerInvariant()).ToList();
   Func<string[],int> column=delegate(string[] names) {foreach(var n in names){int i=header.IndexOf(n);if(i>=0)return i;}return -1;};
   int url=column(new[]{"url","login_uri","website","origin","web site"}),user=column(new[]{"username","login_username","user name","login","email"}),secret=column(new[]{"password","login_password"});
   if(url<0||secret<0)throw new FormatException("This file doesn't look like a passwords export: it needs url and password columns.");
   foreach(var row in rows.Skip(1)) {
    if(row.Length==1&&row[0].Trim().Length==0)continue;
    string address=url<row.Length?row[url].Trim():"",name=user>=0&&user<row.Length?row[user]:"",password=secret<row.Length?row[secret]:"";
    string origin;try {origin=VaultCrypto.Origin(address);}catch(ArgumentException){skipped++;continue;} // Only HTTPS sites can be filled.
    if(password.Length==0){skipped++;continue;}
    if(list.Any(e=>e.Origin==origin&&e.Username==name))continue;
    list.Add(new VaultEntry {Id=Guid.NewGuid().ToString("N"),Origin=origin,Username=name,Password=password});
   }
   return list;
  }
  internal static List<string[]> CsvRows(string text) {
   var rows=new List<string[]>();var row=new List<string>();var field=new StringBuilder();bool quoted=false;
   if(text.Length>0&&text[0]==(char)0xFEFF)text=text.Substring(1);
   for(int i=0;i<text.Length;i++) {
    char c=text[i];
    if(quoted){if(c=='"'){if(i+1<text.Length&&text[i+1]=='"'){field.Append('"');i++;}else quoted=false;}else field.Append(c);continue;}
    if(c=='"')quoted=true;else if(c==','){row.Add(field.ToString());field.Clear();}
    else if(c=='\r'||c=='\n'){if(c=='\r'&&i+1<text.Length&&text[i+1]=='\n')i++;row.Add(field.ToString());field.Clear();rows.Add(row.ToArray());row.Clear();}
    else field.Append(c);
   }
   if(field.Length>0||row.Count>0){row.Add(field.ToString());rows.Add(row.ToArray());}
   return rows;
  }
 }
 sealed class ImportExtension {internal string Id,Name,Version,Folder;internal bool Store;}
}
