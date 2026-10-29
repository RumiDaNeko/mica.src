using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xilium.CefGlue;

namespace Mica {
 // Checks engine selection and the updater's install path without the network or a real 300 MB download:
 //   Mica --engine-self-test <result file> [a saved copy of https://cef-builds.spotifycdn.com/index.json]
 // Fake engines decide which one wins and when it falls back; a tiny archive shaped like a CEF minimal distribution
 // goes through checksum, unpack and layout; a small made-up index stands in for the CDN's.
 static class CefEngineSelfTest {
  const string V14="156.0.14+g4fa4ab5+chromium-156.0.8078.48",V16="156.0.16+gaaaaaaa+chromium-156.0.8078.55",V18="156.0.18+gbbbbbbb+chromium-156.0.8078.60",
   V20="156.0.20+gccccccc+chromium-156.0.8078.66",V21="156.0.21+gddddddd+chromium-156.0.8078.70",V25="156.0.25+geeeeeee+chromium-156.0.8078.80",
   V10="156.0.10+gfffffff+chromium-156.0.8078.20",V157="157.0.1+g1234567+chromium-157.0.8100.10";
  static readonly StringBuilder report=new StringBuilder();
  static int failures;
  static void Check(string name,bool ok,string detail=null) {
   report.AppendLine((ok?"PASS ":"FAIL ")+name+(ok||detail==null?"":": "+detail));if(!ok)failures++;
  }
  static string Engine(string root,string version,bool ready=true,bool bad=false) {
   string folder=Path.Combine(root,version);Directory.CreateDirectory(folder);
   File.WriteAllText(Path.Combine(folder,CefRuntime.LibCefFileName),"fake");
   if(ready)File.WriteAllText(Path.Combine(folder,".ready"),"test");
   if(bad)File.WriteAllText(Path.Combine(folder,"bad"),"");
   return folder;
  }
  static string Name(string folder) {return folder==null?"(none)":Path.GetFileName(folder);}

  internal static int Run(string result,string savedIndex=null) {
   string temp=Path.Combine(Path.GetTempPath(),"mica-engine-test-"+Guid.NewGuid().ToString("N").Substring(0,8));
   try {
    Choosing(Path.Combine(temp,"choose"));
    Verifying(Path.Combine(temp,"verify"));
    Index();
    if(savedIndex!=null)RealIndex(savedIndex);
    Installing(Path.Combine(temp,"install"));
    Pruning(Path.Combine(temp,"prune"));
   } catch(Exception e) {Check("engine self-test ran to the end",false,e.ToString());}
   finally {try {Directory.Delete(temp,true);} catch(Exception) {}}
   report.AppendLine(failures==0?"ALL PASSED":failures+" FAILED");
   Directory.CreateDirectory(Path.GetDirectoryName(result));
   File.WriteAllText(result,report.ToString());
   return failures==0?0:1;
  }

  static void Choosing(string root) {
   string bundled=Path.Combine(root,"..","bundled");Directory.CreateDirectory(root);
   var bundledVersion=CefEngine.ParseVersion(V14);
   var verified=new List<string>();
   Func<string,bool> verify=f=>{verified.Add(Path.GetFileName(f));return !File.Exists(Path.Combine(f,"bad"));};
   Func<string> choose=()=>CefEngine.Choose(root,bundled,bundledVersion,156,verify);

   Check("parses a CEF version",CefEngine.ParseVersion(V21)==new Version(156,0,21)&&CefEngine.ParseVersion("x")==null);
   Check("no downloaded engine: the bundled one",choose()==bundled);
   Engine(root,V10);
   Check("an engine older than the bundled one is ignored",choose()==bundled);
   string e16=Engine(root,V16),e20=Engine(root,V20);
   Check("the newest verified engine wins",Name(choose())==V20,Name(choose()));
   Engine(root,V157);
   Check("another CEF major is ignored (bindings are per branch)",Name(choose())==V20);
   Engine(root,V25,ready:false);
   Check("an engine that didn't finish installing is ignored",Name(choose())==V20);
   File.WriteAllText(Path.Combine(e20,"bad"),"");
   verified.Clear();
   Check("an engine that fails to load falls back to the next one",Name(choose())==V16);
   Check("the failed engine is set aside",CefEngine.IsFailed(e20));
   verified.Clear();choose();
   Check("a set-aside engine isn't tried again",!verified.Contains(V20),string.Join(",",verified));
   CefEngine.RecordStart(e16);
   Check("one unconfirmed start is tolerated",Name(choose())==V16&&CefEngine.StartCount(e16)==1);
   CefEngine.RecordStart(e16);
   Check("two starts that didn't stay up fall back to the bundled engine",choose()==bundled&&CefEngine.IsFailed(e16));
   string e18=Engine(root,V18);
   CefEngine.RecordStart(e18);CefEngine.Confirm(e18);
   Check("a confirmed start clears the count",CefEngine.StartCount(e18)==0&&Name(choose())==V18);
  }

  static void Verifying(string root) {
   string fake=Engine(root,V21);
   Check("a file that isn't a library fails verification",!CefEngine.VerifyLibrary(fake));
   string bundled=CefEngine.BundledFolder();
   Check("the bundled libcef reports CefGlue's API hash",bundled!=null&&CefEngine.VerifyLibrary(bundled),bundled??"no bundled engine");
   Check("the bundled engine records its version",CefEngine.FolderVersion(bundled??"")==CefRuntime.BindingsCefVersion,CefEngine.FolderVersion(bundled??""));
  }

  static string IndexJson(string platform) {
   Func<string,string,string,string,string> version=(cef,channel,file,sha)=>
    "{\"cef_version\":\""+cef+"\",\"channel\":\""+channel+"\",\"files\":[{\"type\":\"standard\",\"name\":\"x.tar.bz2\",\"sha1\":\"0000000000000000000000000000000000000000\",\"size\":1},"+
    "{\"type\":\"minimal\",\"name\":\""+file+"\",\"sha1\":\""+sha+"\",\"size\":42}]}";
   Func<string,string> name=cef=>"cef_binary_"+cef+"_"+platform+"_minimal.tar.bz2";
   string sha=new string('a',40);
   return "{\""+platform+"\":{\"versions\":["+string.Join(",",
    version(V157,"stable",name(V157),sha),
    version(V25,"beta",name(V25),sha),
    version("156.0.30+g9999999+chromium-156.0.8078.99","stable","../../evil.tar.bz2",sha),
    version("156.0.31+g9999999+chromium-156.0.8078.99","stable",name("156.0.31+g9999999+chromium-156.0.8078.99"),"not-a-sha"),
    version(V20,"stable",name(V20),sha),
    version(V16,"stable",name(V16),sha))+"]},\"other\":{\"versions\":[]}}";
  }
  static void Index() {
   string platform=CefEngineUpdater.Platform;
   var latest=CefEngineUpdater.LatestFromIndex(new MemoryStream(Encoding.UTF8.GetBytes(IndexJson(platform))),platform,156);
   Check("the index gives the newest stable build of the same major",latest!=null&&latest.Version==V20&&latest.Size==42&&latest.Sha1==new string('a',40),latest?.Version);
   Check("the index file name is exactly the build's",latest!=null&&latest.File=="cef_binary_"+V20+"_"+platform+"_minimal.tar.bz2");
   Check("no build for a platform the index lacks",CefEngineUpdater.LatestFromIndex(new MemoryStream(Encoding.UTF8.GetBytes(IndexJson(platform))),"nowhere",156)==null);
  }

  // The CDN's real index lists this build (or a newer one of the same major) for every platform the shell ships on.
  static void RealIndex(string path) {
   foreach(var platform in new[]{"macosx64","macosarm64","linux64","linuxarm64","windows64"}) {
    CefEngineUpdater.Build latest;
    using(var stream=File.OpenRead(path))latest=CefEngineUpdater.LatestFromIndex(stream,platform,CefRuntime.BindingsCefVersionMajor);
    Check("the CDN index has a stable "+CefRuntime.BindingsCefVersionMajor+" build for "+platform,latest!=null&&latest.Parsed>=CefEngine.ParseVersion(CefRuntime.BindingsCefVersion)&&latest.Size>0,latest?.Version);
   }
  }

  // A distribution laid out like the real one for this OS, with stand-in files.
  static string FakeDistribution(string parent,string version) {
   string top="cef_binary_"+version+"_"+CefEngineUpdater.Platform+"_minimal",root=Path.Combine(parent,top);
   Action<string> touch=path=>{Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,Path.GetFileName(path));};
   if(OperatingSystem.IsMacOS()) {
    string framework=Path.Combine(root,"Release","Chromium Embedded Framework.framework");
    touch(Path.Combine(framework,"Chromium Embedded Framework"));
    touch(Path.Combine(framework,"Libraries","libEGL.dylib"));
    touch(Path.Combine(framework,"Resources","resources.pak"));
    touch(Path.Combine(framework,"Resources","en.lproj","locale.pak"));
   } else {
    touch(Path.Combine(root,"Release",CefRuntime.LibCefFileName));
    touch(Path.Combine(root,"Release","v8_context_snapshot.bin"));
    touch(Path.Combine(root,"Release","libcef.lib"));
    touch(Path.Combine(root,"Release","bootstrap.exe"));
    touch(Path.Combine(root,"Resources","resources.pak"));
    touch(Path.Combine(root,"Resources","locales","en-US.pak"));
   }
   touch(Path.Combine(root,"include","cef_version.h"));
   return top;
  }
  static void Installing(string root) {
   string source=Path.Combine(root,"..","source");Directory.CreateDirectory(root);Directory.CreateDirectory(source);
   string top=FakeDistribution(source,V21),archive=Path.Combine(root,"cef_binary_"+V21+"_"+CefEngineUpdater.Platform+"_minimal.tar.bz2");
   CefEngineUpdater.RunTar("-cjf\n"+archive+"\n-C\n"+source+"\n"+top);
   var build=new CefEngineUpdater.Build {Version=V21,Parsed=CefEngine.ParseVersion(V21),File=Path.GetFileName(archive),Sha1=new string('0',40)};
   bool refused=false;
   try {CefEngineUpdater.Install(archive,build,root);} catch(InvalidOperationException) {refused=true;}
   Check("a download with the wrong SHA-1 is refused",refused&&!Directory.Exists(Path.Combine(root,V21)));
   build.Sha1=CefEngineUpdater.Sha1Of(archive);
   string engine=CefEngineUpdater.Install(archive,build,root);
   Check("a verified download unpacks into Engines/<version>",Name(engine)==V21&&File.Exists(Path.Combine(engine,".ready"))&&CefEngine.FolderVersion(engine)==V21);
   Check("the engine has libcef where CefGlue looks",File.Exists(Path.Combine(engine,CefRuntime.LibCefFileName)));
   if(OperatingSystem.IsMacOS())
    Check("macOS layout: libraries beside libcef, the English locale in Resources",File.Exists(Path.Combine(engine,"libEGL.dylib"))&&File.Exists(Path.Combine(engine,"Resources","locale.pak"))&&File.Exists(Path.Combine(engine,"Resources","resources.pak")));
   else
    Check("layout: resources and locales beside libcef, no import library or bootstrap",File.Exists(Path.Combine(engine,"resources.pak"))&&File.Exists(Path.Combine(engine,"locales","en-US.pak"))&&!File.Exists(Path.Combine(engine,"libcef.lib"))&&!File.Exists(Path.Combine(engine,"bootstrap.exe")));
   Check("nothing is left from unpacking",!Directory.Exists(engine+".unpack")&&!Directory.Exists(engine+".layout")&&!Directory.Exists(Path.Combine(engine,"include")));
   Check("the installed engine is a candidate",CefEngine.Installed(root).Any(e=>e.Item2==engine));
  }

  static void Pruning(string root) {
   Directory.CreateDirectory(root);
   string inUse=Engine(root,V20),older=Engine(root,V16),newer=Engine(root,V21),other=Engine(root,V157),failed=Engine(root,V25);
   CefEngine.MarkFailed(failed,"test");
   Directory.CreateDirectory(Path.Combine(root,V18+".unpack"));
   File.WriteAllText(Path.Combine(root,"partial.tar.bz2"),"");
   CefEngineUpdater.Prune(root,inUse,V20,156);
   Check("pruning keeps the engine in use and a newer one",Directory.Exists(inUse)&&Directory.Exists(newer));
   Check("pruning removes older, other-major, set-aside and half-unpacked engines",!Directory.Exists(older)&&!Directory.Exists(other)&&!Directory.Exists(failed)&&!Directory.Exists(Path.Combine(root,V18+".unpack"))&&!File.Exists(Path.Combine(root,"partial.tar.bz2")));
   Check("a set-aside version is remembered so it isn't downloaded again",File.Exists(Path.Combine(root,"failed.txt"))&&File.ReadAllLines(Path.Combine(root,"failed.txt")).Contains(V25));
  }
 }
}
