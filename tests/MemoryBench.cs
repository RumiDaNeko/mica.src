using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Mica {
 partial class BrowserApp {
  static readonly string[] BenchSites={
   "https://en.wikipedia.org/wiki/Web_browser","https://en.wikipedia.org/wiki/Vietnam","https://github.com/microsoft/vscode","https://news.ycombinator.com/",
   "https://www.bbc.com/news","https://www.reddit.com/r/programming/","https://www.youtube.com/","https://stackoverflow.com/questions",
   "https://developer.mozilla.org/en-US/docs/Web/JavaScript","https://www.theverge.com/","https://www.amazon.com/","https://edition.cnn.com/",
   "https://www.google.com/maps","https://docs.github.com/en","https://www.apple.com/","https://www.microsoft.com/en-us/",
   "https://vnexpress.net/","https://www.twitch.tv/","https://medium.com/","https://www.nytimes.com/"};
  // Twenty real sites open at once, sampled while the user stays active: the number the 2 GB goal is about.
  async Task RunMemoryBench() {
   var log=regressionLog;string dir=Path.Combine(profile,"memory-bench");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Width=1280;window.Height=900;window.Left=-6000;window.Top=-6000;
   try {
    bool first=true;
    foreach(var site in BenchSites){Navigate(site,!first);first=false;try {await WaitForPage(20000);}catch(Exception){log.Add("slow: "+site);}inactiveSince=null;}
    for(int second=0;second<=180;second+=15) {
     if(second>0)await Task.Delay(15000);inactiveSince=null;
     log.Add("after "+second+" s: "+BenchSample()+" · resting "+state.Tabs.Count(t=>!views.ContainsKey(t.Id)));
    }
    log.Add("loaded views: "+views.Count+", tabs: "+state.Tabs.Count+", rested by budget: "+budgetRested);
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
  string BenchSample() {
   long working=0,committed=0;int count=0;var sizes=new List<string>();
   var ids=EngineProcessIds();ids.Add(Process.GetCurrentProcess().Id);
   foreach(int id in ids)try {using(var p=Process.GetProcessById(id)){working+=p.WorkingSet64;committed+=p.PrivateMemorySize64;count++;sizes.Add((p.WorkingSet64/1048576)+"/"+(p.PrivateMemorySize64/1048576));}}catch(ArgumentException){}catch(InvalidOperationException){}
   return (working/1048576)+" MB working, "+(committed/1048576)+" MB private, "+count+" processes [ws/private: "+string.Join(" ",sizes)+"]";
  }
 }
}
