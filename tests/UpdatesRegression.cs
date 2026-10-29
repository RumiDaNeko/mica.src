using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mica {
 partial class BrowserApp {
  // Mica's own updates against a fake GitHub release served from this machine.
  async Task RunUpdatesTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"updates");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;
   AppUpdater.RootOverride=Path.Combine(dir,"store");
   var server=new TcpListener(IPAddress.Loopback,0);server.Start();int port=((IPEndPoint)server.LocalEndpoint).Port;
   string site="http://127.0.0.1:"+port+"/";
   byte[] setupBytes=Encoding.ASCII.GetBytes("fake Mica setup "+Guid.NewGuid());
   string goodSha;using(var sha=System.Security.Cryptography.SHA256.Create())goodSha=BitConverter.ToString(sha.ComputeHash(setupBytes)).Replace("-","").ToLowerInvariant();
   var pages=new Dictionary<string,Tuple<int,byte[]>>();
   Action<string,int,string> page=delegate(string path,int code,string text) {lock(pages)pages[path]=Tuple.Create(code,Encoding.UTF8.GetBytes(text));};
   // Releases are signed with a key made for this test; the real one never leaves the publisher's PC.
   var signer=new System.Security.Cryptography.RSACryptoServiceProvider(2048);signer.PersistKeyInCsp=false;AppUpdater.ReleaseKeyOverride=signer.ToXmlString(false);
   var stranger=new System.Security.Cryptography.RSACryptoServiceProvider(2048);stranger.PersistKeyInCsp=false;
   Func<System.Security.Cryptography.RSACryptoServiceProvider,string,string,string> sign=delegate(System.Security.Cryptography.RSACryptoServiceProvider key,string name,string sha) {return name+" "+Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(name+"\n"+sha),"SHA256"));};
   bool signReleases=true;var signingKey=signer;
   Func<string,string,string> signatures=delegate(string version,string lines) {string path="sig-"+version+"-"+Guid.NewGuid().ToString("N");page(path,200,lines);return ",{\"name\":\"Mica-"+version+"-signatures.txt\",\"browser_download_url\":\""+site+path+"\",\"size\":"+lines.Length+"}";};
   Func<string,string,string,bool,string> release=delegate(string tag,string asset,string digest,bool pre) {
    string version=tag.TrimStart('v');
    return "{\"tag_name\":\""+tag+"\",\"draft\":false,\"prerelease\":"+(pre?"true":"false")+",\"body\":\"- Faster tabs\\r\\n- New Updates page\",\"assets\":[{\"name\":\"notes.txt\",\"browser_download_url\":\""+site+"notes.txt\",\"size\":3},{\"name\":\""+asset+"\",\"browser_download_url\":\""+site+asset+"\",\"size\":"+setupBytes.Length+(digest==null?"":",\"digest\":\"sha256:"+digest+"\"")+"}"
     +(signReleases&&digest!=null?signatures(version,sign(signingKey,asset,digest)):"")+"]}";
   };
   var serving=Task.Run(delegate {
    while(true) {
     TcpClient client;try {client=server.AcceptTcpClient();}catch(Exception){return;}
     using(client)using(var stream=client.GetStream()) {
      var reader=new StreamReader(stream,Encoding.ASCII);string first=reader.ReadLine()??"";string line;while(!string.IsNullOrEmpty(line=reader.ReadLine())){}
      string path=first.Split(' ').Length>1?first.Split(' ')[1].TrimStart('/'):"";
      Tuple<int,byte[]> found;byte[] content;int code;
      lock(pages){if(pages.TryGetValue(path,out found)){code=found.Item1;content=found.Item2;}else if(path.EndsWith("-Setup.exe")){code=200;content=setupBytes;}else {code=404;content=Encoding.ASCII.GetBytes("{}");}}
      var head=Encoding.ASCII.GetBytes("HTTP/1.1 "+code+(code==200?" OK":" Not Found")+"\r\nContent-Length: "+content.Length+"\r\nConnection: close\r\n\r\n");
      stream.Write(head,0,head.Length);stream.Write(content,0,content.Length);
     }
    }
   });
   try {
    RegressionCheck("release tags read as versions",delegate {Require(AppUpdater.ParseTag("v0.12.4")==new Version(0,12,4)&&AppUpdater.ParseTag("0.13.0-beta")==new Version(0,13,0)&&AppUpdater.ParseTag("nightly")==null,"parse");});
    RegressionCheck("pre-releases are skipped",delegate {Require(AppUpdater.FromJson(release("v1.0.0","Mica-1.0.0-Setup.exe",goodSha,true))==null,"took a prerelease");});
    var parsed=AppUpdater.FromJson(release("v1.0.0","Mica-1.0.0-Setup.exe",goodSha,false));
    RegressionCheck("the setup asset and its checksum are picked from the release",delegate {Require(parsed!=null&&parsed.SetupName=="Mica-1.0.0-Setup.exe"&&parsed.Sha256==goodSha&&parsed.Version==new Version(1,0,0),parsed==null?"null":parsed.SetupName);});
    RegressionCheck("the real feed is GitHub's latest-release API",delegate {Require(AppUpdater.Feed=="https://api.github.com/repositories/"+AppUpdater.ReleaseRepoId+"/releases/latest",AppUpdater.Feed??"none");});

    AppUpdater.FeedOverride=site+"missing";
    string none=await AppUpdater.Update(CurrentApp,true);
    RegressionCheck("no release yet says so",delegate {Require(none!=null&&none.Contains("No Mica release"),none);});

    AppUpdater.FeedOverride=site+"latest";
    lock(pages)page("latest",200,release("v"+AppVersion,"Mica-"+AppVersion+"-Setup.exe",goodSha,false));
    string same=await AppUpdater.Update(CurrentApp,true);
    RegressionCheck("the same version is up to date",delegate {Require(same!=null&&same.Contains("up to date")&&AppUpdater.ReadySetup(CurrentApp)==null,same);});

    lock(pages)page("latest",200,release("v99.0.0","Mica-99.0.0-Setup.exe",null,false));
    string unchecked_=await AppUpdater.Update(CurrentApp,true);
    RegressionCheck("a release without a checksum isn't downloaded",delegate {Require(unchecked_!=null&&unchecked_.Contains("checksum")&&AppUpdater.ReadySetup(CurrentApp)==null,unchecked_);});

    lock(pages)page("latest",200,release("v99.0.0","Mica-99.0.0-Setup.exe",new string('0',64),false));
    string bad=null;try {await AppUpdater.Update(CurrentApp,true,true);}catch(Exception e){bad=e.Message;}
    RegressionCheck("a download that doesn't match the checksum is thrown away",delegate {Require(bad!=null&&bad.Contains("checksum")&&AppUpdater.ReadySetup(CurrentApp)==null&&!Directory.GetFiles(AppUpdater.Root,"*.part").Any(),bad);});

    signReleases=false;page("latest",200,release("v99.0.0","Mica-99.0.0-Setup.exe",goodSha,false));signReleases=true;
    string unsigned=await AppUpdater.Update(CurrentApp,true);
    RegressionCheck("a release without Mica's signature isn't offered",delegate {Require(unsigned!=null&&unsigned.Contains("signed")&&AppUpdater.Available(CurrentApp)==null,unsigned);});
    signingKey=stranger;page("latest",200,release("v99.0.0","Mica-99.0.0-Setup.exe",goodSha,false));signingKey=signer;
    await AppUpdater.Update(CurrentApp,true);string forged=null;try {await AppUpdater.Download(CurrentApp,null);}catch(Exception e){forged=e.Message;}
    RegressionCheck("a release signed with another key is refused before downloading",delegate {Require(forged!=null&&forged.Contains("signature")&&AppUpdater.ReadySetup(CurrentApp)==null&&!Directory.GetFiles(AppUpdater.Root,"Mica-*").Any(),forged);});
    RegressionCheck("a signature only counts for its own file and version",delegate {
     string line=sign(signer,"Mica-99.0.0-Setup.exe",goodSha).Split(' ')[1];
     Require(AppUpdater.SignedBy("Mica-99.0.0-Setup.exe",goodSha,line,new Version(99,0,0)),"valid refused");
     Require(!AppUpdater.SignedBy("Mica-99.0.0-Setup.exe",new string('1',64),line,new Version(99,0,0)),"other file");
     Require(!AppUpdater.SignedBy("Mica-99.0.0-Setup.exe",goodSha,line,new Version(100,0,0)),"other version");
    });
    lock(pages)page("latest",200,release("v99.0.0","Mica-99.0.0-Setup.exe",goodSha,false));
    string found=await AppUpdater.Update(CurrentApp,true);
    RegressionCheck("a newer release is announced, not downloaded",delegate {Require(found!=null&&found.Contains("available")&&AppUpdater.ReadySetup(CurrentApp)==null&&AppUpdater.Available(CurrentApp)!=null&&!Directory.GetFiles(AppUpdater.Root,"Mica-*").Any(),found);});
    appUpdateNote=found;ShowUpdates();await Task.Delay(900);
    if(updatesWindow!=null) {
     var offered=updatesWindow;Func<string,FrameworkElement> part=delegate(string tag) {return VaultElements<FrameworkElement>(offered.Content as DependencyObject).FirstOrDefault(e=>(e.Tag as string)==tag);};
     RegressionCheck("Updates offers Download for an available release",delegate {Require(((TextBlock)part("updates-app-latest")).Text=="99.0.0 · available"&&part("updates-download").IsVisible&&!part("updates-restart").IsVisible&&!part("updates-progress-panel").IsVisible,((TextBlock)part("updates-app-latest")).Text);});
     RefreshUpdatesHint();await WaitFor(delegate {return UpdatesHint()=="Update available";},4000);
     RegressionCheck("the menu row says Update available",delegate {Require(UpdatesHint()=="Update available",UpdatesHint());});
     appDownloading=true;appReceived=1048576;appTotal=4194304;NotifyUpdates();await Task.Delay(300);
     RegressionCheck("a download shows a progress bar",delegate {var fill=part("updates-progress");var scale=fill==null?null:fill.RenderTransform as ScaleTransform;Require(part("updates-progress-panel").IsVisible&&scale!=null&&Math.Abs(scale.ScaleX-0.25)<0.01&&((TextBlock)part("updates-progress-text")).Text.Contains("25%")&&!part("updates-download").IsVisible,scale==null?"no bar":scale.ScaleX+" "+((TextBlock)part("updates-progress-text")).Text);});
     offered.UpdateLayout();{var shot=new RenderTargetBitmap((int)Math.Ceiling(offered.ActualWidth),(int)Math.Ceiling(offered.ActualHeight),96,96,PixelFormats.Pbgra32);shot.Render(offered);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(shot));using(var file=File.Create(Path.Combine(dir,"updates-downloading.png")))encoder.Save(file);}
     appDownloading=false;offered.Close();await Task.Delay(300);
    }
    var seen=new List<long>();
    string ok=await AppUpdater.Download(CurrentApp,delegate(long received,long total) {lock(seen)seen.Add(received);});string ready=AppUpdater.ReadySetup(CurrentApp);
    RegressionCheck("a newer release is downloaded and verified",delegate {Require(ok!=null&&ok.Contains("99.0.0")&&ready!=null&&File.ReadAllBytes(ready).SequenceEqual(setupBytes)&&AppUpdater.Available(CurrentApp)==null,ok);});
    RegressionCheck("the download reports its progress",delegate {Require(seen.Count>0,"no progress");});
    RegressionCheck("a ready setup is checked again, signature and all, before it runs",delegate {
     using(var held=AppUpdater.LockVerified(ready))Require(held!=null,"verified setup refused");
     string okFile=ready+".ok";var saved=File.ReadAllLines(okFile);
     File.WriteAllLines(okFile,new[]{saved[0],saved[1],sign(stranger,saved[1],saved[0]).Split(' ')[1]});
     using(var held=AppUpdater.LockVerified(ready))Require(held==null,"accepted a setup signed by someone else");
     File.WriteAllLines(okFile,saved);var original=File.ReadAllBytes(ready);File.WriteAllBytes(ready,Encoding.ASCII.GetBytes("swapped"));
     using(var held=AppUpdater.LockVerified(ready))Require(held==null,"accepted a changed setup");
     File.WriteAllBytes(ready,original);
    });
    string again=await AppUpdater.Update(CurrentApp,false);
    RegressionCheck("the daily check doesn't run twice in a day",delegate {Require(again==null,again);});

    appUpdateNote=ok;ShowUpdates();await Task.Delay(900);var dialog=updatesWindow;
    RegressionCheck("Updates shows the new version, its notes and Restart to update",delegate {
     Require(dialog!=null,"no window");
     var texts=VaultElements<TextBlock>(dialog.Content as DependencyObject).ToList();var buttons=VaultElements<Button>(dialog.Content as DependencyObject).ToList();
     var latest=texts.First(t=>(t.Tag as string)=="updates-app-latest");var notes=texts.First(t=>(t.Tag as string)=="updates-app-notes");var restart=buttons.First(b=>(b.Tag as string)=="updates-restart");
     Require(latest.Text=="99.0.0 · ready","latest "+latest.Text);Require(notes.IsVisible&&notes.Text.Contains("Faster tabs"),"notes "+notes.Text);Require(restart.IsVisible,"restart hidden");
    });
    if(dialog!=null) {
     Func<string,FrameworkElement> part=delegate(string tag) {return VaultElements<FrameworkElement>(dialog.Content as DependencyObject).FirstOrDefault(e=>(e.Tag as string)==tag);};
     var scroller=VaultElements<ScrollViewer>(dialog.Content as DependencyObject).FirstOrDefault();
     RegressionCheck("Updates hides its scroll bar",delegate {Require(scroller!=null&&scroller.VerticalScrollBarVisibility==ScrollBarVisibility.Hidden,scroller==null?"no scroller":scroller.VerticalScrollBarVisibility.ToString());});
     appChecking=true;NotifyUpdates();await Task.Delay(200);
     RegressionCheck("while checking, the latest release spins instead of showing the old answer",delegate {Require(part("updates-app-latest-spinner").IsVisible&&!part("updates-app-latest").IsVisible,"spinner "+part("updates-app-latest-spinner").IsVisible);});
     appChecking=false;NotifyUpdates();await Task.Delay(200);
     RegressionCheck("the answer comes back when the check ends",delegate {Require(!part("updates-app-latest-spinner").IsVisible&&part("updates-app-latest").IsVisible,"spinner "+part("updates-app-latest-spinner").IsVisible);});
    }
    RefreshUpdatesHint();await WaitFor(delegate {return UpdatesHint()=="Restart to update";},4000);
    RegressionCheck("the menu row says Restart to update",delegate {Require(UpdatesHint()=="Restart to update",UpdatesHint());});
    if(dialog!=null) {
     dialog.UpdateLayout();int width=(int)Math.Ceiling(dialog.ActualWidth),height=(int)Math.Ceiling(dialog.ActualHeight);
     var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(dialog);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));
     using(var file=File.Create(Path.Combine(dir,"updates-ready.png")))png.Save(file);
     dialog.Close();await Task.Delay(300);
    }
    File.WriteAllText(Path.Combine(AppUpdater.Root,"available.json"),release("v99.0.0","Mica-99.0.0-Setup.exe",goodSha,false));
    AppUpdater.Prune(new Version(99,0,0),true);
    RegressionCheck("once installed, the old setup is cleaned up",delegate {Require(AppUpdater.ReadySetup(CurrentApp)==null&&!Directory.GetFiles(AppUpdater.Root,"Mica-*").Any(),string.Join(",",Directory.GetFiles(AppUpdater.Root)));});
    RegressionCheck("once installed, the found-release note is cleaned up too",delegate {Require(!File.Exists(Path.Combine(AppUpdater.Root,"available.json")),"available.json left behind");});

    // A release also carries an app-only update labelled with its engine; a copy with that engine takes it.
    string patchText="fake app-only update "+Guid.NewGuid();string patchSha;using(var sha=System.Security.Cryptography.SHA256.Create())patchSha=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(patchText))).Replace("-","").ToLowerInvariant();
    Func<string,string> twoAssets=delegate(string version) {
     return "{\"tag_name\":\"v"+version+"\",\"draft\":false,\"prerelease\":false,\"body\":\"- Small\",\"assets\":[{\"name\":\"Mica-"+version+"-Setup.exe\",\"browser_download_url\":\""+site+"Mica-"+version+"-Setup.exe\",\"size\":260000000,\"digest\":\"sha256:"+goodSha+"\"},"
      +"{\"name\":\"Mica-"+version+"-Update.exe\",\"label\":\"engine 1.2.3\",\"browser_download_url\":\""+site+"Mica-"+version+"-Update.exe\",\"size\":"+Encoding.UTF8.GetByteCount(patchText)+",\"digest\":\"sha256:"+patchSha+"\"}"
      +signatures(version,sign(signer,"Mica-"+version+"-Setup.exe",goodSha)+"\n"+sign(signer,"Mica-"+version+"-Update.exe",patchSha)+"\n")+"]}";
    };
    lock(pages){page("Mica-100.0.0-Update.exe",200,patchText);page("latest",200,twoAssets("100.0.0"));}
    AppUpdater.LocalEngineOverride="1.2.3";await AppUpdater.Update(CurrentApp,true);var small=AppUpdater.Available(CurrentApp);
    RegressionCheck("with the same engine, the small app-only update is chosen",delegate {Require(small!=null&&small.UsesPatch&&small.DownloadSize==Encoding.UTF8.GetByteCount(patchText)&&small.PatchEngine=="1.2.3",small==null?"none":small.UsesPatch+" "+small.DownloadSize);});
    await AppUpdater.Download(CurrentApp,null);string smallReady=AppUpdater.ReadySetup(CurrentApp);
    RegressionCheck("the app-only update downloads and checks out",delegate {Require(smallReady!=null&&File.ReadAllText(smallReady)==patchText,smallReady??"none");});
    lock(pages)page("latest",200,twoAssets("101.0.0"));
    AppUpdater.LocalEngineOverride="9.9.9";await AppUpdater.Update(CurrentApp,true);var full=AppUpdater.Available(CurrentApp);
    RegressionCheck("with a different engine, the full installer is chosen",delegate {Require(full!=null&&!full.UsesPatch&&full.DownloadSize==260000000,full==null?"none":full.UsesPatch+" "+full.DownloadSize);});
    AppUpdater.LocalEngineOverride=null;
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   finally {server.Stop();AppUpdater.ReleaseKeyOverride=null;AppUpdater.FeedOverride=null;AppUpdater.RootOverride=null;AppUpdater.LocalEngineOverride=null;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
