using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;

namespace Mica {
 static class InstallerCloseRegression {
  [STAThread] static int Main(string[] args) {
   if(args.Length==3&&args[0]=="--fixture") {
    var app=new Application();int closed=0;
    for(int i=0;i<2;i++){var w=new Window {Title="Mica shutdown test",Width=240,Height=140};w.Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e){if(args[2]=="refuse")e.Cancel=true;else File.WriteAllText(args[1]+".closed",(++closed).ToString());};w.Show();}
    File.WriteAllText(args[1]+".ready","ready");return app.Run();
   }
   string folder=Path.Combine(Path.GetTempPath(),"MicaCloseTest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string exe=Path.Combine(folder,"Mica.exe");File.Copy(System.Reflection.Assembly.GetExecutingAssembly().Location,exe);
   var log=new System.Collections.Generic.List<string>();int code=0;
   try {
    foreach(string mode in new[]{"normal","refuse"}) {
     string prefix=Path.Combine(folder,mode);
     using(var process=Process.Start(new ProcessStartInfo(exe,"--fixture \""+prefix+"\" "+mode) {UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden})) {
      try {
       for(int i=0;i<100&&!File.Exists(prefix+".ready");i++)Thread.Sleep(50);
       if(!File.Exists(prefix+".ready"))throw new Exception("Fixture did not start");
       bool timedOut=false;try{CloseBrowser.RequestAndWait(process.Id,1200);}catch(IOException){timedOut=true;}
       if(mode=="normal") {if(timedOut||!process.HasExited||File.ReadAllText(prefix+".closed")!="2")throw new Exception("Legacy multi-window WM_CLOSE failed");log.Add("PASS legacy browser receives normal close for both windows");}
       else {if(!timedOut||process.HasExited)throw new Exception("Cancelled close was not preserved");log.Add("PASS cancelled close times out without force-killing browser");}
      }finally {if(!process.HasExited){process.Kill();process.WaitForExit();}}
     }
    }
   }catch(Exception e){code=1;log.Add("FAIL "+e);}
   finally {File.WriteAllLines(args[0],log);File.Delete(exe);foreach(string file in Directory.GetFiles(folder))File.Delete(file);Directory.Delete(folder);}
   return code;
  }
 }
}
