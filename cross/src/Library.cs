using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Avalonia.Threading;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Handlers;

namespace Mica {
 // Downloads and the library pages (History, Downloads), as on Windows (BrowserDownloads.cs, BrowserLibrary.cs).
 // Files go straight to the Downloads folder; the records use the Windows build's DownloadStore (downloads.json).
 // The library is a page of Mica's own on library.mica.invalid; what it asks for comes back here through
 // BuiltInPages.Act.
 sealed partial class BrowserWindow {
  List<DownloadEntry> downloads=new List<DownloadEntry>();
  readonly Dictionary<string,CefDownloadItemCallback> downloadControls=new Dictionary<string,CefDownloadItemCallback>();
  readonly HashSet<string> cancelRequested=new HashSet<string>();
  DownloadHandler downloadHandler;
  string DownloadsPath {get {return Path.Combine(profile,"downloads.json");}}
  static bool IsDownloadLive(DownloadEntry d) {return d.Status=="InProgress"||d.Status=="Starting"||d.Status=="Paused";}

  void LoadDownloads() {
   downloadHandler=new Downloads(this);
   try {downloads=DownloadStore.Load(DownloadsPath);} catch(Exception) {downloads=new List<DownloadEntry>();}
   BuiltInPages.Act=delegate(string action,Dictionary<string,string> args) {Dispatcher.UIThread.Post(delegate {LibraryAction(action,args);});};
  }
  void SaveDownloads() {
   try {DownloadStore.TrimHistory(downloads,IsDownloadLive);DownloadStore.Save(DownloadsPath,downloads);} catch(IOException) {} catch(UnauthorizedAccessException) {}
  }
  // The self-test keeps its downloads in its throwaway profile.
  internal static string DownloadFolderOverride;
  static string DownloadFolder() {
   if(DownloadFolderOverride!=null){Directory.CreateDirectory(DownloadFolderOverride);return DownloadFolderOverride;}
   string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
   string folder=Path.Combine(home,"Downloads");Directory.CreateDirectory(folder);return folder;
  }
  // "report.pdf", then "report (1).pdf": never over a file that is already there.
  static string FreePath(string folder,string name) {
   foreach(char c in Path.GetInvalidFileNameChars())name=name.Replace(c,'_');
   name=name.Trim().TrimStart('.');if(name.Length==0)name="download";
   string stem=Path.GetFileNameWithoutExtension(name),ext=Path.GetExtension(name),path=Path.Combine(folder,name);
   for(int n=1;File.Exists(path)||File.Exists(path+".crdownload");n++)path=Path.Combine(folder,stem+" ("+n+")"+ext);
   return path;
  }

  sealed class Downloads : DownloadHandler {
   readonly BrowserWindow window;
   public Downloads(BrowserWindow w) {window=w;}
   protected override bool OnBeforeDownload(CefBrowser browser,CefDownloadItem item,string suggestedName,CefBeforeDownloadCallback callback) {
    string id=item.Id.ToString(),url=item.Url,path=FreePath(DownloadFolder(),suggestedName);long total=item.TotalBytes;
    callback.Continue(path,false);
    Dispatcher.UIThread.Post(delegate {window.DownloadStarted(id,url,path,total);});
    return true;
   }
   protected override void OnDownloadUpdated(CefBrowser browser,CefDownloadItem item,CefDownloadItemCallback callback) {
    if(item.Id==0)return;
    string id=item.Id.ToString(),path=item.FullPath;long received=item.ReceivedBytes,total=item.TotalBytes;
    string status=item.IsComplete?"Completed":item.IsCanceled?"Cancelled":item.IsInterrupted?"Interrupted":"InProgress";
    string reason=item.IsInterrupted?item.InterruptReason.ToString():null;
    lock(window.downloadControls) {if(status=="InProgress")window.downloadControls[id]=callback;else window.downloadControls.Remove(id);}
    bool cancel;lock(window.cancelRequested)cancel=window.cancelRequested.Remove(id);
    if(cancel&&status=="InProgress")callback.Cancel();
    Dispatcher.UIThread.Post(delegate {window.DownloadUpdated(id,path,received,total,status,reason);});
   }
  }
  // CEF numbers downloads from 1 each run, so the record gets a key of its own.
  readonly Dictionary<string,DownloadEntry> liveDownloads=new Dictionary<string,DownloadEntry>();
  void DownloadStarted(string engineId,string url,string path,long total) {
   var record=new DownloadEntry {Id=Guid.NewGuid().ToString("N"),Url=url,FilePath=path,FileName=Path.GetFileName(path),StartedUtc=DateTime.UtcNow,Total=total>0?total:-1,Status="InProgress"};
   liveDownloads[engineId]=record;downloads.Insert(0,record);
   UpdateLibrary();SaveDownloads();RefreshIslandDownloads();
  }
  void DownloadUpdated(string engineId,string path,long received,long total,string status,string reason) {
   DownloadEntry record;if(!liveDownloads.TryGetValue(engineId,out record))return;
   if(!string.IsNullOrEmpty(path)){record.FilePath=path;record.FileName=Path.GetFileName(path);}
   record.Received=Math.Max(0,received);record.Total=total>0?total:-1;
   bool changed=record.Status!=status;record.Status=status;
   if(status=="Interrupted")record.Detail="Stopped: "+(reason??"the connection dropped")+". Start it again from its website.";
   if(status!="InProgress"){liveDownloads.Remove(engineId);if(status=="Completed")Status(record.FileName+" is in your Downloads folder.");}
   UpdateLibrary();if(changed)SaveDownloads();RefreshIslandDownloads();
  }
  string EngineIdOf(DownloadEntry record) {foreach(var pair in liveDownloads)if(pair.Value==record)return pair.Key;return null;}

  // Opens History or Downloads: the tab that already shows it, else here (on a new tab page) or a new tab.
  void OpenLibraryPage(string url) {
   var open=state.Tabs.FirstOrDefault(t=>t.Space==state.Space&&t.Url==url);
   if(open!=null){Show(open.Id);return;}
   var tab=Active;Navigate(url,tab==null||!(tab.Entry.Url=="mica://home"||Navigation.IsLibrary(tab.Entry.Url)));
  }
  // What the library page shows, kept current as JSON for the page to read.
  void UpdateLibrary() {
   var history=state.History.Where(h=>h!=null&&Navigation.IsWeb(h.Url)).Select(h=>new Dictionary<string,object> {{"t",h.Title??""},{"u",h.Url},{"d",Ms(h.LastUsed)}}).ToList();
   var files=downloads.Select(d=>new Dictionary<string,object> {{"id",d.Id},{"n",d.FileName??""},{"u",d.Url??""},{"d",Ms(d.StartedUtc)},{"r",d.Received},{"t",d.Total},{"s",d.Status??""},{"x",d.Detail??""},{"e",d.Status=="Completed"&&File.Exists(d.FilePath??"")}}).ToList();
   BuiltInPages.LibraryJson=new JavaScriptSerializer().Serialize(new Dictionary<string,object> {{"history",history},{"downloads",files}});
  }
  static long Ms(DateTime utc) {return (long)(DateTime.SpecifyKind(utc,DateTimeKind.Utc)-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;}
  void LibraryAction(string action,Dictionary<string,string> args) {
   string id,url;args.TryGetValue("id",out id);args.TryGetValue("u",out url);
   var record=id==null?null:downloads.FirstOrDefault(d=>d.Id==id);
   switch(action) {
    case "forget":if(url!=null){state.History.RemoveAll(h=>h.Url==url);UpdateHomeSites();Save();}break;
    case "clear-history":state.History.Clear();UpdateHomeSites();Save();break;
    case "open":if(record!=null&&record.Status=="Completed"&&File.Exists(record.FilePath))OpenPath(record.FilePath,false);break;
    case "reveal":if(record!=null&&File.Exists(record.FilePath))OpenPath(record.FilePath,true);else OpenPath(DownloadFolder(),false);break;
    case "cancel":
     if(record!=null){string engine=EngineIdOf(record);if(engine!=null){CefDownloadItemCallback control;lock(downloadControls)downloadControls.TryGetValue(engine,out control);if(control!=null)control.Cancel();else lock(cancelRequested)cancelRequested.Add(engine);}}
     break;
    case "remove":if(record!=null&&!IsDownloadLive(record)){downloads.Remove(record);SaveDownloads();}break;
    case "clear-finished":downloads.RemoveAll(d=>!IsDownloadLive(d));SaveDownloads();break;
    case "retry":if(record!=null&&Navigation.IsWeb(record.Url)){var view=ActiveView();var host=view==null?null:view.Host;if(host!=null)host.StartDownload(record.Url);}break;
   }
   UpdateLibrary();
  }
 }
}
