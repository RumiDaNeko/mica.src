using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 partial class BrowserApp {
  List<DownloadEntry> downloads=new List<DownloadEntry>();
  sealed class LiveDownload {
   public DownloadEntry Entry;public WebView2 Owner;public CoreWebView2DownloadOperation Operation;
   public EventHandler<object> BytesChanged,StateChanged;
   public bool PauseRequested,CancelRequested,OpenWhenDone;
  }
  readonly Dictionary<string,LiveDownload> liveDownloads=new Dictionary<string,LiveDownload>();
  readonly HashSet<WebView2> retainedDownloadViews=new HashSet<WebView2>();
  readonly Dictionary<WebView2,int> pendingDownloadPrompts=new Dictionary<WebView2,int>();
  DispatcherTimer downloadsTimer;bool downloadUiDirty,downloadStoreDirty,downloadsShuttingDown;
  DateTime lastDownloadSave=DateTime.MinValue;
  string DownloadsPath {get {return Path.Combine(profile,"downloads.json");}}

  void InitializeDownloads() {
   try {var other=Program.Browsers.FirstOrDefault(b=>b!=this&&b.CanShareProfile(profile));downloads=privateMode?new List<DownloadEntry>():other!=null?other.downloads:DownloadStore.Load(DownloadsPath);}
   catch(Exception e) {downloads=new List<DownloadEntry>();Status("Couldn't read download history: "+e.Message);}
   downloadsTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(300)};
   downloadsTimer.Tick+=delegate {
    if(downloadUiDirty){downloadUiDirty=false;RefreshDownloadsUi();}
    if(downloadStoreDirty&&DateTime.UtcNow-lastDownloadSave>=TimeSpan.FromSeconds(1))SaveDownloads();
    if(!downloadUiDirty&&!downloadStoreDirty)downloadsTimer.Stop();
   };
   downloadsTimer.Start();downloadUiDirty=true;
  }
  void DownloadChanged(bool saveNow) {
   downloadUiDirty=true;downloadStoreDirty=true;
   foreach(var other in Program.Browsers.Where(b=>b!=this&&!b.closing&&b.profile==profile)){other.downloadUiDirty=true;if(other.downloadsTimer!=null)other.downloadsTimer.Start();}
   if(saveNow)SaveDownloads();
   if(!downloadsShuttingDown&&downloadsTimer!=null)downloadsTimer.Start();
  }
  void SaveDownloads() {
   if(privateMode){downloadStoreDirty=false;lastDownloadSave=DateTime.UtcNow;return;}
   try {DownloadStore.TrimHistory(downloads,IsDownloadLive);DownloadStore.Save(DownloadsPath,downloads);downloadStoreDirty=false;lastDownloadSave=DateTime.UtcNow;}
   catch(Exception e) {downloadStoreDirty=false;Status("Couldn't save download history: "+e.Message);}
  }
  async void TrackDownload(WebView2 owner,CoreWebView2DownloadStartingEventArgs e) {
   e.Handled=true;
   var deferral=e.GetDeferral();
   int pending;pendingDownloadPrompts.TryGetValue(owner,out pending);pendingDownloadPrompts[owner]=pending+1;
   try {
    // Leave the WebView event stack before opening a modal native file picker.
    await Task.Yield();
    if(downloadsShuttingDown||closing||!views.Values.Contains(owner)){e.Cancel=true;return;}
    string suggested=Path.GetFileName(e.ResultFilePath);
    if(string.IsNullOrWhiteSpace(suggested))suggested="download";
    string destination;bool openWhenDone=false;
    if(smoke&&Environment.GetEnvironmentVariable("MICA_TEST_DOWNLOAD_PROMPT")!="1") {
     string directory=Path.Combine(profile,"test-downloads");Directory.CreateDirectory(directory);
     destination=Path.GetFullPath(Path.Combine(directory,Guid.NewGuid().ToString("N")+"-"+suggested));
    }else {
     var total=e.DownloadOperation.TotalBytesToReceive;
     string choice=await AskDownload(suggested,total.HasValue?(long)Math.Min(total.Value,(ulong)long.MaxValue):-1,DownloadSource(e.DownloadOperation.Uri));
     if(choice==null||downloadsShuttingDown||closing){e.Cancel=true;if(choice==null)Status("Download cancelled.");return;}
     if(choice=="saveas") {
      var picker=new SaveFileDialog {Title="Save download",FileName=suggested,InitialDirectory=DownloadsFolder(),Filter="All files (*.*)|*.*",OverwritePrompt=true,AddExtension=false,CheckPathExists=true};
      if(picker.ShowDialog(window)!=true){e.Cancel=true;return;}
      destination=Path.GetFullPath(picker.FileName);
     }else {destination=UniqueDownloadPath(DownloadsFolder(),suggested);openWhenDone=choice=="open";}
    }
    if(downloadsShuttingDown||closing||!views.Values.Contains(owner)){e.Cancel=true;return;}
    e.ResultFilePath=destination;
    var operation=e.DownloadOperation;
    var entry=new DownloadEntry {Id=Guid.NewGuid().ToString("N"),Url=operation.Uri,FilePath=destination,FileName=Path.GetFileName(destination),StartedUtc=DateTime.UtcNow,Received=0,Total=-1,Status="Downloading",Detail=""};
    var live=new LiveDownload {Entry=entry,Owner=owner,Operation=operation,OpenWhenDone=openWhenDone};
    live.BytesChanged=delegate {UpdateDownload(live,false);};live.StateChanged=delegate {UpdateDownload(live,true);};
    downloads.Insert(0,entry);liveDownloads.Add(entry.Id,live);
    operation.BytesReceivedChanged+=live.BytesChanged;operation.StateChanged+=live.StateChanged;
    UpdateDownload(live,true);
    Status("Downloading "+entry.FileName+".");
   }catch(Exception error) {
    try {e.Cancel=true;}catch { }
    if(!downloadsShuttingDown)Status("Couldn't start download: "+error.Message);
   }finally {
    // Complete before releasing a controller retained while the picker was open.
    try {deferral.Complete();}catch(Exception error) {if(!downloadsShuttingDown)Status("Couldn't finish download setup: "+error.Message);}
    int count;if(pendingDownloadPrompts.TryGetValue(owner,out count)){if(count<=1)pendingDownloadPrompts.Remove(owner);else pendingDownloadPrompts[owner]=count-1;}
    ReleaseDownloadView(owner);
   }
  }
  void UpdateDownload(LiveDownload live,bool stateChanged) {
   if(downloadsShuttingDown||!liveDownloads.ContainsKey(live.Entry.Id))return;
   bool terminal=false;
   try {
    var operation=live.Operation;var entry=live.Entry;
    entry.Received=operation.BytesReceived;var total=operation.TotalBytesToReceive;
    entry.Total=total.HasValue?(long)Math.Min(total.Value,(ulong)long.MaxValue):-1;
    if(operation.State==CoreWebView2DownloadState.Completed){entry.Status="Completed";entry.Detail="";terminal=true;if(live.OpenWhenDone){live.OpenWhenDone=false;var done=entry;window.Dispatcher.BeginInvoke(new Action(delegate {OpenDownload(done);}));}}
    else if(live.CancelRequested){entry.Status="Cancelled";entry.Detail="Cancelled by you.";terminal=true;}
    else if(operation.State==CoreWebView2DownloadState.Interrupted) {
     bool resumable=operation.CanResume;
     bool paused=live.PauseRequested||operation.InterruptReason==CoreWebView2DownloadInterruptReason.UserPaused;
     entry.Status=paused&&resumable?"Paused":operation.InterruptReason==CoreWebView2DownloadInterruptReason.UserCanceled&&!resumable?"Cancelled":"Interrupted";
     entry.Detail=entry.Status=="Paused"?"Paused. Resume whenever you're ready.":entry.Status=="Cancelled"?"Cancelled by you.":DownloadInterruptText(operation.InterruptReason);
     terminal=!resumable;
    }else {entry.Status="Downloading";entry.Detail="";}
   }catch(Exception e) {live.Entry.Status="Interrupted";live.Entry.Detail="The download engine stopped: "+e.Message;terminal=true;}
   DownloadChanged(stateChanged||terminal);
   if(terminal)DetachDownload(live);
  }
  static string DownloadInterruptText(CoreWebView2DownloadInterruptReason reason) {
   switch(reason) {
    case CoreWebView2DownloadInterruptReason.FileNoSpace:return "There isn't enough disk space. Free some space before trying again.";
    case CoreWebView2DownloadInterruptReason.FileAccessDenied:return "Mica couldn't write to the selected location.";
    case CoreWebView2DownloadInterruptReason.FileMalicious:return "The browser blocked this file as unsafe.";
    case CoreWebView2DownloadInterruptReason.FileBlockedByPolicy:return "This download was blocked by your device's policy.";
    default:return "Download interrupted: "+reason+".";
   }
  }
  void DetachDownload(LiveDownload live) {
   liveDownloads.Remove(live.Entry.Id);
   try {live.Operation.BytesReceivedChanged-=live.BytesChanged;live.Operation.StateChanged-=live.StateChanged;}catch { }
   // Disposal in the WebView callback itself can re-enter the controller.
   window.Dispatcher.BeginInvoke(new Action(delegate {ReleaseDownloadView(live.Owner);}));
  }
  bool HasDownloads(WebView2 owner) {return pendingDownloadPrompts.ContainsKey(owner)||liveDownloads.Values.Any(d=>d.Owner==owner);}
  bool RetainDownloadView(WebView2 owner) {
   if(downloadsShuttingDown||!HasDownloads(owner))return false;
   owner.Visibility=Visibility.Collapsed;retainedDownloadViews.Add(owner);return true;
  }
  void ReleaseDownloadView(WebView2 owner) {
   if(!retainedDownloadViews.Contains(owner)||HasDownloads(owner))return;
   retainedDownloadViews.Remove(owner);Find<Grid>("WebHost").Children.Remove(owner);owner.Dispose();
  }
  BrowserApp DownloadOwner(DownloadEntry entry) {return entry==null?null:Program.Browsers.FirstOrDefault(b=>b.profile==profile&&b.liveDownloads.ContainsKey(entry.Id));}
  bool IsDownloadLive(DownloadEntry entry) {return DownloadOwner(entry)!=null;}
  bool CanResumeDownload(DownloadEntry entry) {
   var owner=DownloadOwner(entry);if(owner!=null&&owner!=this)return owner.CanResumeDownload(entry);
   LiveDownload live;if(entry==null||!liveDownloads.TryGetValue(entry.Id,out live))return false;
   try {return live.Operation.CanResume;}catch {return false;}
  }
  void PauseDownload(DownloadEntry entry) {
   var owner=DownloadOwner(entry);if(owner!=null&&owner!=this){owner.PauseDownload(entry);return;}
   LiveDownload live;if(entry==null||!liveDownloads.TryGetValue(entry.Id,out live)||entry.Status!="Downloading")return;
   try {live.PauseRequested=true;live.Operation.Pause();UpdateDownload(live,true);}
   catch(Exception e) {live.PauseRequested=false;Status("Couldn't pause download: "+e.Message);}
  }
  void ResumeDownload(DownloadEntry entry) {
   var owner=DownloadOwner(entry);if(owner!=null&&owner!=this){owner.ResumeDownload(entry);return;}
   LiveDownload live;if(entry==null||!liveDownloads.TryGetValue(entry.Id,out live)||!CanResumeDownload(entry))return;
   try {live.PauseRequested=false;live.Operation.Resume();UpdateDownload(live,true);}
   catch(Exception e) {Status("Couldn't resume download: "+e.Message);}
  }
  void CancelDownload(DownloadEntry entry) {
   var owner=DownloadOwner(entry);if(owner!=null&&owner!=this){owner.CancelDownload(entry);return;}
   LiveDownload live;if(entry==null||!liveDownloads.TryGetValue(entry.Id,out live))return;
   try {live.CancelRequested=true;live.Operation.Cancel();UpdateDownload(live,true);}
   catch(Exception e) {live.CancelRequested=false;Status("Couldn't cancel download: "+e.Message);}
  }
  void RemoveDownload(DownloadEntry entry) {
   if(entry==null)return;
   if(IsDownloadLive(entry)){Status("Cancel the download before removing it from the list.");return;}
   downloads.Remove(entry);DownloadChanged(true);
  }
  void ClearFinishedDownloads() {downloads.RemoveAll(d=>!IsDownloadLive(d));DownloadChanged(true);Status("Finished download records cleared. Your files were kept.");}
  void OpenDownload(DownloadEntry entry) {
   if(entry==null||entry.Status!="Completed")return;
   try {if(!File.Exists(entry.FilePath)){Status("This file has moved or was deleted.");return;}OpenDownloadSafely(Path.GetFullPath(entry.FilePath));}
   catch(Exception e) {Status("Couldn't open file: "+e.Message);}
  }
  void RevealDownload(DownloadEntry entry) {
   if(entry==null)return;
   try {
    string full=Path.GetFullPath(entry.FilePath);
    if(File.Exists(full))Process.Start(new ProcessStartInfo {FileName="explorer.exe",Arguments="/select,\""+full+"\"",UseShellExecute=true});
    else {string directory=Path.GetDirectoryName(full);if(Directory.Exists(directory))Process.Start(new ProcessStartInfo {FileName=directory,UseShellExecute=true});else Status("The download folder is no longer available.");}
   }catch(Exception e) {Status("Couldn't show file: "+e.Message);}
  }
  void ShutdownDownloads() {
   if(downloadsShuttingDown)return;downloadsShuttingDown=true;
   if(downloadsTimer!=null)downloadsTimer.Stop();
   foreach(var live in liveDownloads.Values.ToList()) {
    live.Entry.Status="Interrupted";live.Entry.Detail="Mica closed before the download finished. Start it again from its website.";
    try {live.Operation.BytesReceivedChanged-=live.BytesChanged;live.Operation.StateChanged-=live.StateChanged;live.Operation.Cancel();}catch { }
   }
   liveDownloads.Clear();SaveDownloads();
   foreach(var view in retainedDownloadViews.ToList()){Find<Grid>("WebHost").Children.Remove(view);view.Dispose();}
   retainedDownloadViews.Clear();
  }
 }
}
