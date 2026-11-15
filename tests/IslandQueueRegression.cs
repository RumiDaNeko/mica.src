using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Mica {
 partial class BrowserApp {
  // Island prompts: a new one takes the place of one left untouched, which comes back afterwards unanswered;
  // ignored prompts don't keep swapping; a login form puts nothing on the island.
  async Task RunIslandQueueTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"islandqueue");Directory.CreateDirectory(dir);
   try {
    var answers=new List<string>();
    Func<string> showing=()=>siteBubble==null||!siteBubble.IsOpen?"none":LogicalDescendants<TextBlock>(siteBubble.Child).Select(t=>t.Text).FirstOrDefault(t=>t!=null&&t.StartsWith("Prompt ",StringComparison.Ordinal))??"?";
    Action<string> ask=name=>ShowChoiceBubble(Find<Button>("AddressButton"),"","Island test",name,null,null,new[]{new[]{"No","no",""},new[]{"Yes","yes","primary"}},()=>!closing,choice=>answers.Add(name+"="+(choice??"null")));
    ask("Prompt A");await WaitFor(()=>showing()=="Prompt A",5000);
    ask("Prompt B");await Task.Delay(600);
    RegressionCheck("a prompt you only just saw isn't replaced",delegate {Require(showing()=="Prompt A",showing());});
    await WaitFor(()=>showing()=="Prompt B",5000).ContinueWith(t=>{});
    RegressionCheck("a new prompt takes the island from one left alone, without answering it",delegate {Require(showing()=="Prompt B"&&answers.Count==0,showing()+" / "+string.Join(",",answers));});
    await Task.Delay(BubbleIdleMs+1500);
    RegressionCheck("the replaced prompt doesn't push back in",delegate {Require(showing()=="Prompt B",showing());});
    Choose(siteBubble,"yes");await WaitFor(()=>showing()=="Prompt A",5000).ContinueWith(t=>{});
    RegressionCheck("after answering, the replaced prompt comes back",delegate {Require(showing()=="Prompt A"&&answers.SequenceEqual(new[]{"Prompt B=yes"}),showing()+" / "+string.Join(",",answers));});
    Choose(siteBubble,"no");await WaitFor(()=>siteBubble==null,5000).ContinueWith(t=>{});
    RegressionCheck("and its answer still reaches whoever asked",delegate {Require(siteBubble==null&&answers.SequenceEqual(new[]{"Prompt B=yes","Prompt A=no"}),string.Join(",",answers));});
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
