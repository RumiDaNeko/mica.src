using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace Mica {
 public sealed class DownloadEntry {
  public string Id {get;set;}
  public string Url {get;set;}
  public string FilePath {get;set;}
  public string FileName {get;set;}
  public DateTime StartedUtc {get;set;}
  public long Received {get;set;}
  public long Total {get;set;}
  public string Status {get;set;}
  public string Detail {get;set;}
 }
 public static class DownloadStore {
  public const int HistoryLimit=200;
  static JavaScriptSerializer Serializer() {return new JavaScriptSerializer {MaxJsonLength=int.MaxValue};}
  public static void TrimHistory(List<DownloadEntry> records,Func<DownloadEntry,bool> isLive) {
   int count=0;long characters=0;var keep=new HashSet<DownloadEntry>();
   foreach(var record in records.OrderByDescending(d=>d.StartedUtc)) {
    if(isLive(record)){keep.Add(record);continue;}
    long size=256L+6L*((record.Id??"").Length+(record.Url??"").Length+(record.FilePath??"").Length+(record.FileName??"").Length+(record.Detail??"").Length);
    if(count<HistoryLimit&&characters+size<=1048576){keep.Add(record);count++;characters+=size;}
   }
   records.RemoveAll(d=>!keep.Contains(d));
  }
  public static List<DownloadEntry> Load(string path) {
   var records=File.Exists(path)?Serializer().Deserialize<List<DownloadEntry>>(File.ReadAllText(path)):null;
   if(records==null)return new List<DownloadEntry>();
   var ids=new HashSet<string>(StringComparer.Ordinal);
   records.RemoveAll(d=>d==null||string.IsNullOrEmpty(d.Id)||!ids.Add(d.Id));
   foreach(var record in records) {
    record.Received=Math.Max(0,record.Received);record.Total=record.Total<0?-1:record.Total;
    if(record.Status!="Completed"&&record.Status!="Cancelled") {
     record.Status="Interrupted";
     record.Detail="Mica restarted. Start this download again from its website.";
    }
   }
   TrimHistory(records,d=>false);return records.OrderByDescending(d=>d.StartedUtc).ToList();
  }
  public static void Save(string path,List<DownloadEntry> records) {
   Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
   string temporary=path+".tmp";
   File.WriteAllText(temporary,Serializer().Serialize(records));
   if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
  }
 }
}
