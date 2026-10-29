using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Xilium.CefGlue;

namespace Mica {
 // Print, as on Windows (BrowserTools.cs ShowPrint): Mica's own card in the island's corner, never Chrome's preview
 // or the system's print dialog. Save as PDF or any printer; the page's print layout either way. A printer gets a
 // PDF made the same way, sent with CUPS (lp), which macOS and Linux both have.
 sealed partial class BrowserWindow {
  // The self-test saves here instead of asking where.
  internal static string PrintSaveOverride;
  // window.print() from a page opens the card too (a console line, as the media bridge does).
  internal const string PrintScript="(()=>{try{Object.defineProperty(window,'print',{value:function(){console.debug('__mica_print')},configurable:false,writable:false});}catch(e){}})()";
  internal Popup printCard;internal TextBlock printNote;

  void Print() {
   var tab=Active;if(tab==null||tab.View==null||tab.Entry.Url=="mica://home")return;
   if(printCard!=null&&printCard.IsOpen)return;
   var view=tab.View;
   StackPanel body,actions;var popup=MicaDialog("Print this page",380,out body,out actions,true);printCard=popup;
   popup.Closed+=delegate {if(printCard==popup)printCard=null;};
   var title=DialogText(TabTitle(tab.Entry),14,"Ink");title.Classes.Add("user");body.Children.Add(title);
   var about=DialogText("Choose a printer or save a PDF. Uses the page's print layout.",12,"Muted");about.Margin=new Thickness(0,2,0,10);body.Children.Add(about);
   // Destination: Save as PDF, then every printer CUPS knows, the default marked.
   var destinations=new List<string>{"Save as PDF"};string defaultPrinter;destinations.AddRange(Printers(out defaultPrinter));
   body.Children.Add(DialogText("Destination",11,"Muted"));
   var chooser=new ComboBox {Margin=new Thickness(0,6,0,8),HorizontalAlignment=HorizontalAlignment.Stretch,Tag="print-destination"};
   foreach(var name in destinations)chooser.Items.Add(name==defaultPrinter?name+"  (default)":name);
   chooser.SelectedIndex=0;body.Children.Add(chooser);
   Func<string,string,TextBox> field=delegate(string label,string text) {
    body.Children.Add(DialogText(label,11,"Muted"));var box=new TextBox {Text=text,Margin=new Thickness(0,6,0,8)};body.Children.Add(box);return box;
   };
   var pages=field("Pages · blank for all, e.g. 1-3, 5","");var copies=field("Printer copies · 1–99 (PDF exports one document)","1");
   var landscape=new CheckBox {Content="Landscape",Margin=new Thickness(0,4)};var backgrounds=new CheckBox {Content="Include backgrounds",IsChecked=true,Margin=new Thickness(0,4)};var headers=new CheckBox {Content="Page headers and footers",Margin=new Thickness(0,4)};
   body.Children.Add(landscape);body.Children.Add(backgrounds);body.Children.Add(headers);
   var note=DialogText("Ready",12,"Muted");note.Margin=new Thickness(0,12,0,0);body.Children.Add(note);printNote=note;
   var go=DialogButton("Print / save",true);go.Tag="print-go";actions.Children.Add(go);
   go.Click+=async delegate {
    int number;if(!int.TryParse(copies.Text,out number)||number<1||number>99){note.Text="Choose 1–99 copies.";return;}
    string ranges=pages.Text??"";
    if(!Regex.IsMatch(ranges,@"^\s*(\d+\s*(-\s*\d+)?\s*(,\s*\d+\s*(-\s*\d+)?\s*)*)?$")){note.Text="Use page numbers or ranges such as 1-3, 5.";return;}
    var host=view.Host;if(host==null){note.Text="The page isn't ready.";return;}
    int destination=Math.Max(0,chooser.SelectedIndex);string path;
    if(destination==0) {
     path=PrintSaveOverride;
     if(path==null) {
      var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {Title="Save as PDF",SuggestedFileName=SafeName(TabTitle(tab.Entry))+".pdf",DefaultExtension="pdf",ShowOverwritePrompt=true,FileTypeChoices=new[]{new FilePickerFileType("PDF document") {Patterns=new[]{"*.pdf"}}}});
      if(file==null)return;path=file.TryGetLocalPath();if(path==null){note.Text="Choose a folder on this device.";return;}
     }
    }
    else path=Path.Combine(Path.GetTempPath(),"mica-print-"+Guid.NewGuid().ToString("N")+".pdf");
    var settings=new CefPdfPrintSettings {Landscape=landscape.IsChecked==true,PrintBackground=backgrounds.IsChecked==true,DisplayHeaderFooter=headers.IsChecked==true,PageRanges=Regex.Replace(ranges,@"\s+",""),PreferCssPageSize=true};
    body.IsEnabled=false;actions.IsEnabled=false;note.Text="Preparing your document…";
    var done=new PdfDone();host.PrintToPdf(path,settings,done);
    bool ok=await done.Task;
    if(destination==0)note.Text=ok?"PDF saved.":"PDF could not be saved.";
    else if(!ok)note.Text="Printing failed. Check the page, page range and printer.";
    else {
     string printer=destinations[destination],error=await Task.Run(()=>SendToPrinter(path,printer,number,landscape.IsChecked==true));
     note.Text=error==null?"Sent to printer.":"Printer returned: "+error;
    }
    body.IsEnabled=true;actions.IsEnabled=true;
    if(destination==0&&ok)Status("PDF saved.");
   };
   popup.IsOpen=true;
  }
  static string SafeName(string text) {foreach(char c in Path.GetInvalidFileNameChars())text=text.Replace(c,' ');text=text.Trim();return text.Length==0?"Mica page":text.Length>80?text.Substring(0,80):text;}
  sealed class PdfDone : CefPdfPrintCallback {
   readonly TaskCompletionSource<bool> done=new TaskCompletionSource<bool>();
   public Task<bool> Task {get {return done.Task;}}
   protected override void OnPdfPrintFinished(string path,bool ok) {done.TrySetResult(ok);}
  }
  // The printers CUPS knows (lpstat -e), and its default (lpstat -d). None on Windows' cross build.
  static IEnumerable<string> Printers(out string fallback) {
   fallback=null;if(OperatingSystem.IsWindows())return new string[0];
   string list=Run("lpstat","-e"),chosen=Run("lpstat","-d");
   if(chosen!=null){int colon=chosen.LastIndexOf(':');if(colon>0)fallback=chosen.Substring(colon+1).Trim();}
   return (list??"").Split('\n').Select(l=>l.Trim()).Where(l=>l.Length>0&&!l.Contains(' ')).ToList();
  }
  static string Run(string file,string args) {
   try {
    var start=new ProcessStartInfo(file,args) {RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
    using(var p=Process.Start(start)){string output=p.StandardOutput.ReadToEnd();p.WaitForExit(5000);return p.ExitCode==0?output:null;}
   } catch(Exception) {return null;}
  }
  static string SendToPrinter(string pdf,string printer,int copies,bool landscape) {
   try {
    var start=new ProcessStartInfo("lp") {RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true};
    foreach(var a in new[]{"-d",printer,"-n",copies.ToString()})start.ArgumentList.Add(a);
    if(landscape){start.ArgumentList.Add("-o");start.ArgumentList.Add("landscape");}
    start.ArgumentList.Add(pdf);
    using(var p=Process.Start(start)){string error=p.StandardError.ReadToEnd();p.WaitForExit(30000);return p.ExitCode==0?null:(error.Trim().Length>0?error.Trim():"error "+p.ExitCode);}
   } catch(Exception e) {return e.Message;}
   finally {var file=pdf;System.Threading.Tasks.Task.Delay(60000).ContinueWith(_=>{try {File.Delete(file);} catch(Exception) {}});}
  }
 }
}
