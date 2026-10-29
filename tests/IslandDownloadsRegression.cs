using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Mica {
 partial class BrowserApp {
  // Downloads in the island: at most two, newest first, staying put under the pointer, and leaving after their linger.
  async Task RunIslandDownloadsTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"island-downloads");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;
   try {
    await Task.Delay(800);
    var now=DateTime.UtcNow;
    foreach(var item in new[]{new[]{"a","old.zip","Completed"},new[]{"b","report.pdf","Completed"},new[]{"c","photos.zip","Interrupted"}}) {
     var entry=new DownloadEntry {Id=item[0],FileName=item[1],FilePath=Path.Combine(dir,item[1]),Url="https://example.com/"+item[1],Status=item[2],Received=600,Total=1000,Detail=item[2]=="Interrupted"?"Download interrupted: NetworkFailed.":"",StartedUtc=now.AddSeconds(item[0]=="a"?-30:item[0]=="b"?-20:-10)};
     downloads.Insert(0,entry);islandSeenLive.Add(entry.Id);islandDoneAt[entry.Id]=now;
    }
    RefreshIslandDownloads();await Task.Delay(400);
    var rows=islandDownloads.Children.OfType<Border>().Select(b=>(string)b.Tag).ToList();
    log.Add("rows: "+string.Join(", ",rows));
    RegressionCheck("the island shows at most two downloads, newest first",delegate {Require(rows.SequenceEqual(new[]{"island-download:c","island-download:b"}),string.Join(", ",rows));});
    var note=(TextBlock)((Grid)((Border)islandDownloads.Children[1]).Child).Children[2];
    RegressionCheck("a finished one offers to open",delegate {Require(note.Text=="Open",note.Text);});
    CaptureElement(islandCard,"island-downloads.png");
    // The pointer check says "near", but with downloads up it stays.
    islandAway=false;WatchIslandPointer();
    RegressionCheck("downloads keep the island up under the pointer",delegate {Require(!islandAway&&islandCard.IsHitTestVisible,"stepped aside");});
    islandAway=true;RefreshIslandDownloads();
    RegressionCheck("a hidden island comes back for downloads",delegate {Require(!islandAway&&islandCard.IsHitTestVisible,"still away");});
    foreach(var id in new[]{"a","b","c"})islandDoneAt[id]=now-TimeSpan.FromSeconds(9);
    RefreshIslandDownloads();
    RegressionCheck("finished downloads leave after their linger",delegate {Require(!IslandHasDownloads,islandDownloads.Children.Count+" left");});
    downloads.RemoveAll(d=>d.Id.Length==1);
    // Speed: nothing on the first sample, then bytes per second, smoothed.
    var t0=DateTime.UtcNow;double r0=IslandDownloadSpeed("speed",0,t0),r1=IslandDownloadSpeed("speed",2000000,t0.AddSeconds(1)),r2=IslandDownloadSpeed("speed",3000000,t0.AddSeconds(2));
    log.Add("speeds: "+r0+", "+r1+", "+r2+" shown as "+FormatBytes((long)r2)+"/s");
    RegressionCheck("downloads show a smoothed speed",delegate {Require(r0<0&&Math.Abs(r1-2000000)<1&&Math.Abs(r2-1600000)<1,r0+", "+r1+", "+r2);});
    islandSpeeds.Remove("speed");
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
