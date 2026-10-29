using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Mica {
 partial class BrowserApp {
  async Task<DownloadEntry> StartDownloadFixture(DownloadRegressionServer server,string file) {
   if(CurrentView()==null) {
    Navigate(server.Url,false);
    await WaitFor(delegate {return CurrentView()!=null&&CurrentView().CoreWebView2.DocumentTitle=="Download fixture";},15000);
   }
   int before=downloads.Count;CurrentView().CoreWebView2.Navigate(server.Url+file);
   await WaitFor(delegate {return downloads.Count>before;},10000);
   return downloads.First(d=>d.Url==server.Url+file);
  }
  async Task RunDownloadsTest() {
   var log=new List<string>();int failures=0;
   try {
    Require(smoke&&Path.GetFileName(profile).StartsWith("smoke-",StringComparison.Ordinal),"Download tests require an isolated smoke profile");
    Require(downloads.Count==0,"Fresh profile has unexpected download records");
    using(var server=new DownloadRegressionServer()) {
     var completed=await StartDownloadFixture(server,"complete.bin");
     await WaitFor(delegate {return completed.Status=="Completed";},20000);
     Require(completed.Received==DownloadRegressionServer.CompleteSize&&completed.Total==DownloadRegressionServer.CompleteSize,"Actual progress totals do not match response");
     Require(File.Exists(completed.FilePath),"Completed file missing");
     byte[] data=File.ReadAllBytes(completed.FilePath);
     Require(data.Length==DownloadRegressionServer.CompleteSize&&data.Select((value,index)=>value==(byte)(index%251)).All(value=>value),"Completed payload bytes are incorrect");
     log.Add("PASS real HTTP download completes with correct bytes, file and progress totals");
     RemoveDownload(completed);Require(File.Exists(completed.FilePath)&&!downloads.Contains(completed),"Removing record deleted the file or kept the record");
     log.Add("PASS removing completed record preserves downloaded file");

     var retained=await StartDownloadFixture(server,"retained.bin");
     var owner=CurrentView();string ownerId=state.ActiveId;
     await WaitFor(delegate {return retained.Received>=65536&&retained.Status=="Downloading";},10000);
     OpenLibraryPage("mica://downloads");await Task.Delay(450);window.UpdateLayout();
     Require(libraryPage.IsVisible&&Library<TextBlock>("LibraryCount").Text=="1 file","Live download is missing from Downloads page");
     var pauseButton=FindAction(libraryPage,"pause:"+retained.Id);Require(pauseButton!=null&&pauseButton.IsVisible,"Live download has no visible Pause action");
     SleepIdleTabs(true);
     Require(views.ContainsKey(ownerId)&&views[ownerId]==owner&&HasDownloads(owner),"Resting background tabs disposed a live download owner");
     pauseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitFor(delegate {return retained.Status=="Paused"&&CanResumeDownload(retained);},10000);
     long pausedBytes=retained.Received;await Task.Delay(700);
     Require(retained.Status=="Paused"&&retained.Received==pausedBytes&&IsDownloadLive(retained),"Paused download kept receiving or lost its resumable operation");
     Require(HasDownloads(owner),"Paused owner is not protected from sleeping");
     log.Add("PASS native Downloads page shows live progress, protects background owner, and Pause stops real bytes");
     window.UpdateLayout();Capture("mica-downloads-live.png");
     var resumeButton=FindAction(libraryPage,"resume:"+retained.Id);Require(resumeButton!=null&&resumeButton.IsVisible,"Paused download has no visible Resume action");
     resumeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitFor(delegate {return retained.Status=="Downloading"&&retained.Received>pausedBytes;},10000);
     // WebView2 can resume the existing buffered response instead of opening a range request.
     // Both paths must advance real received bytes and produce the complete, correct payload.
     log.Add("PASS resume continues real received bytes (range requests so far: "+server.RangeRequests+")");
     CloseTab(ownerId);
     Require(!views.Values.Contains(owner)&&retainedDownloadViews.Contains(owner)&&HasDownloads(owner),"Closing tab disposed its live download controller");
     int retainedTabCount=state.Tabs.Count,retainedRecordCount=downloads.Count;
     await owner.CoreWebView2.ExecuteScriptAsync("window.open('/popup', '_blank'); var a=document.createElement('a'); a.href='/complete.bin'; a.download='extra.bin'; document.body.appendChild(a); a.click();");
     await Task.Delay(600);
     Require(state.Tabs.Count==retainedTabCount&&downloads.Count==retainedRecordCount,"Detached download owner created a tab or started a new download");
     log.Add("PASS retained closed page cannot open popups or start extra downloads");
     await WaitFor(delegate {return retained.Status=="Completed";},30000);
     await WaitFor(delegate {return !retainedDownloadViews.Contains(owner);},5000);
     Require(new FileInfo(retained.FilePath).Length==DownloadRegressionServer.RetainedSize,"Retained download file has incorrect size");
     byte[] resumedPayload=File.ReadAllBytes(retained.FilePath);
     Require(resumedPayload.Select((value,index)=>value==(byte)(index%251)).All(value=>value),"Paused/resumed payload has missing, duplicated or corrupt bytes");
     log.Add("PASS closed tab's retained controller completes download and is released");

     var cancelled=await StartDownloadFixture(server,"cancel.bin");
     await WaitFor(delegate {return cancelled.Received>=65536&&cancelled.Status=="Downloading";},10000);
     RemoveDownload(cancelled);Require(downloads.Contains(cancelled),"Active record could be removed without cancellation");
     CancelDownload(cancelled);await WaitFor(delegate {return cancelled.Status=="Cancelled"&&!IsDownloadLive(cancelled);},10000);
     Require(!CanResumeDownload(cancelled),"Cancelled download can still resume");
     log.Add("PASS cancel stops real download; active record removal is guarded");

     string restartPath=Path.Combine(profile,"restart-downloads.json");
     var restartFixture=new List<DownloadEntry>();
     foreach(string status in new[]{"Downloading","Paused","Interrupted","Completed","Cancelled"})restartFixture.Add(new DownloadEntry {Id=status,FilePath=retained.FilePath,FileName="fixture.bin",Url=server.Url+"retained.bin",StartedUtc=DateTime.UtcNow,Received=123,Total=1000,Status=status});
     DownloadStore.Save(restartPath,restartFixture);var restored=DownloadStore.Load(restartPath);
     Require(restored.Count==5&&restored.Where(d=>d.Id=="Downloading"||d.Id=="Paused"||d.Id=="Interrupted").All(d=>d.Status=="Interrupted"),"Incomplete records did not become interrupted after restart");
     Require(restored.Single(d=>d.Id=="Completed").Status=="Completed"&&restored.Single(d=>d.Id=="Cancelled").Status=="Cancelled","Terminal restart states changed");
     Require(restored.All(d=>!IsDownloadLive(d)&&!CanResumeDownload(d)),"Persisted metadata fabricated resumable operations");
     log.Add("PASS persisted metadata restores terminal states and interrupts incomplete records without fake resume");
     var largeHistory=Enumerable.Range(0,240).Select(i=>new DownloadEntry {Id="large-"+i,Url="https://example.test/"+new string('x',12000),StartedUtc=DateTime.UtcNow.AddMinutes(-i),Status="Completed"}).ToList();
     var protectedLive=new DownloadEntry {Id="protected",Url="https://example.test/active",Status="Paused",StartedUtc=DateTime.MinValue};largeHistory.Add(protectedLive);
     DownloadStore.Save(restartPath,largeHistory);
     Require(new FileInfo(restartPath).Length>2097152,"Large history fixture did not exceed the old serializer limit");
     var bounded=DownloadStore.Load(restartPath);Require(bounded.Count>0&&bounded.Count<=DownloadStore.HistoryLimit,"Loaded large history was not bounded");
     DownloadStore.TrimHistory(largeHistory,d=>d==protectedLive);
     Require(largeHistory.Contains(protectedLive)&&largeHistory.Count<=DownloadStore.HistoryLimit+1,"History pruning removed live operation or exceeded bound");
     DownloadStore.Save(restartPath,largeHistory);Require(new FileInfo(restartPath).Length<1048576,"History metadata budget was not enforced");
     log.Add("PASS history beyond old JSON limit loads, prunes safely and preserves live transfers");
     ClearFinishedDownloads();Require(downloads.Count==0&&File.Exists(retained.FilePath)&&File.Exists(completed.FilePath),"Clearing records removed files");
     Require(DownloadStore.Load(DownloadsPath).Count==0,"Clearing records did not persist");
     log.Add("PASS clear finished records is persistent and never deletes downloaded files");
     await Task.Delay(650);Require(!downloadsTimer.IsEnabled,"Download refresh timer keeps waking at idle");
     log.Add("PASS download refresh timer stops after pending work is saved and rendered");
    }
   }catch(Exception e) {failures++;log.Add("FAIL downloads regression: "+e);}
   log.Add(failures==0?"PASS":"FAILURES: "+failures);
   string artifacts=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts"));Directory.CreateDirectory(artifacts);
   File.WriteAllLines(Path.Combine(artifacts,"downloads-result.txt"),log);Application.Current.Shutdown(failures==0?0:1);
  }
 }
 sealed class DownloadRegressionServer:IDisposable {
  public const int CompleteSize=524288,RetainedSize=8388608;
  readonly TcpListener listener;volatile bool disposed;int rangeRequests;
  public readonly string Url;
  public int RangeRequests {get {return rangeRequests;}}
  public DownloadRegressionServer() {listener=new TcpListener(IPAddress.Loopback,0);listener.Start();Url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/";Accept();}
  async void Accept() {
   while(!disposed)try {var client=await listener.AcceptTcpClientAsync();Serve(client);}
   catch(ObjectDisposedException){break;}catch(SocketException){if(disposed)break;throw;}
  }
  async void Serve(TcpClient client) {
   using(client)try {
    var stream=client.GetStream();var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
    string request=await reader.ReadLineAsync();if(request==null)return;
    string path=request.Split(' ')[1],line;long start=0;bool range=false;
    while(!string.IsNullOrEmpty(line=await reader.ReadLineAsync()))if(line.StartsWith("Range: bytes=",StringComparison.OrdinalIgnoreCase)) {long.TryParse(line.Substring(13).Split('-')[0],out start);range=true;Interlocked.Increment(ref rangeRequests);}
    if(!path.EndsWith(".bin",StringComparison.Ordinal)) {
     byte[] html=Encoding.UTF8.GetBytes("<!doctype html><title>Download fixture</title><h1>Local download test</h1>");
     byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: "+html.Length+"\r\nConnection: close\r\n\r\n");
     await stream.WriteAsync(response,0,response.Length);await stream.WriteAsync(html,0,html.Length);return;
    }
    int length=path=="/complete.bin"?CompleteSize:path=="/retained.bin"?RetainedSize:16777216;
    if(start<0||start>=length)return;
    string header="HTTP/1.1 "+(range?"206 Partial Content":"200 OK")+"\r\nContent-Type: application/octet-stream\r\nContent-Disposition: attachment; filename=\""+path.Substring(1)+"\"\r\nAccept-Ranges: bytes\r\nETag: \"mica-download-fixture\"\r\nLast-Modified: Wed, 21 Oct 2026 00:00:00 GMT\r\nContent-Length: "+(length-start)+"\r\n"+(range?"Content-Range: bytes "+start+"-"+(length-1)+"/"+length+"\r\n":"")+"Connection: close\r\n\r\n";
    byte[] headers=Encoding.ASCII.GetBytes(header);await stream.WriteAsync(headers,0,headers.Length);
    byte[] buffer=new byte[32768];
    for(long offset=start;offset<length&&!disposed;offset+=buffer.Length) {
     int count=(int)Math.Min(buffer.Length,length-offset);for(int i=0;i<count;i++)buffer[i]=(byte)((offset+i)%251);
     await stream.WriteAsync(buffer,0,count);await stream.FlushAsync();if(path!="/complete.bin")await Task.Delay(30);
    }
   }catch(IOException){}catch(SocketException){}catch(ObjectDisposedException){}
  }
  public void Dispose() {disposed=true;listener.Stop();}
 }
}
