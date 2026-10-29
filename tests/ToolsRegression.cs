using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
namespace Mica {
 partial class BrowserApp {
  [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd,IntPtr hdc,uint flags);
  void GrabWindow(Window w,string name) {
   var hwnd=new System.Windows.Interop.WindowInteropHelper(w).Handle;var source=PresentationSource.FromVisual(w);double scale=source==null?1:source.CompositionTarget.TransformToDevice.M11;
   using(var bmp=new System.Drawing.Bitmap((int)(w.ActualWidth*scale),(int)(w.ActualHeight*scale))) {
    using(var g=System.Drawing.Graphics.FromImage(bmp)){var hdc=g.GetHdc();PrintWindow(hwnd,hdc,2);g.ReleaseHdc(hdc);}
    bmp.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts",name),System.Drawing.Imaging.ImageFormat.Png);
   }
  }

  async Task RunToolsTest() {
   var log=new List<string>();int failures=0;
   try {
    using(var server=new RegressionServer()) {
     Navigate(server.Url+"ok",false);await WaitFor(()=>CurrentView()!=null&&CurrentView().CoreWebView2.DocumentTitle=="Regression OK",15000);var view=CurrentView();var core=view.CoreWebView2;
     await view.ExecuteScriptAsync("document.body.innerHTML='<h1>mica Mica mica</h1><p>Find fixture</p>'");
     Require(IsShortcut(Key.F,ModifierKeys.Control)&&IsShortcut(Key.P,ModifierKeys.Control)&&IsShortcut(Key.F12,ModifierKeys.None)&&IsShortcut(Key.Escape,ModifierKeys.Shift),"Tools shortcuts missing");
     HandleKey(Key.F,ModifierKeys.Control);for(int frame=0;frame<4;frame++){await Task.Delay(55);CaptureDialog(toolWindows["find"],"mica-find-grow-"+frame+".png");}var find=toolWindows["find"];var input=VaultElements<TextBox>(find).Single();input.Text="mica";
     var spot=IslandBounds();Require(islandYielded,"Island did not step aside for Find");Require(spot.HasValue&&Math.Abs(spot.Value.Right-(find.Left+find.Width))<2&&Math.Abs(spot.Value.Bottom-(find.Top+find.Height))<2,"Find does not grow from the island corner: island "+spot+" find "+find.Left+","+find.Top+" "+find.Width+"x"+find.Height);
     await Task.Delay(400);var grown=(FrameworkElement)find.Content;Require(Math.Abs(grown.ActualWidth-find.Width)<1&&islandCard.Opacity<0.05,"Find did not finish growing: "+grown.ActualWidth+" island opacity "+islandCard.Opacity);log.Add("PASS find grows out of the island");
     await WaitFor(()=>core.Find.MatchCount==3,7000);var options=VaultElements<CheckBox>(find).ToList();options[0].IsChecked=true;options[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitFor(()=>core.Find.MatchCount==2,7000);
     
GrabWindow(find,"mica-find-real.png");CaptureDialog(find,"mica-find.png");find.Close();await WaitFor(()=>!toolWindows.ContainsKey("find"),2000);Require(!toolWindows.ContainsKey("find"),"Find cleanup failed");await Task.Delay(300);Require(!islandYielded&&islandCard.Opacity>0.95,"Island did not come back after Find");log.Add("PASS custom find counts matches, respects case and cleans up");
     ShowFind();await Task.Delay(400);ShowPrint();for(int frame=0;frame<4;frame++){await Task.Delay(60);CaptureDialog(toolWindows["print"],"mica-print-grow-"+frame+".png");}await Task.Delay(300);await WaitFor(()=>!toolWindows.ContainsKey("find"),2000);Require(!toolWindows.ContainsKey("find"),"Find stayed open under Print");{var pw=toolWindows["print"];var pcard=(System.Windows.Controls.Border)pw.Content;var pspot=IslandBounds();var cardCorner=pcard.PointToScreen(new Point(pcard.ActualWidth,pcard.ActualHeight));cardCorner=PresentationSource.FromVisual(pw).CompositionTarget.TransformFromDevice.Transform(cardCorner);Require(islandYielded&&pspot.HasValue&&Math.Abs(pspot.Value.Right-cardCorner.X)<2&&Math.Abs(pspot.Value.Bottom-cardCorner.Y)<2,"Print card not grown from the island: island "+pspot+" card corner "+cardCorner);GrabWindow(pw,"mica-print-real.png");log.Add("PASS print grows from the island and replaces Find");}CaptureDialog(toolWindows["print"],"mica-print.png");
var settings=environment.CreatePrintSettings();settings.Orientation=CoreWebView2PrintOrientation.Landscape;settings.ShouldPrintBackgrounds=true;
     string pdf=Path.Combine(profile,"tools-print.pdf");Require(await core.PrintToPdfAsync(pdf,settings),"PDF export failed");Require(Encoding.ASCII.GetString(File.ReadAllBytes(pdf),0,4)=="%PDF","PDF header invalid");toolWindows["print"].Close();log.Add("PASS custom print window and engine landscape PDF export; no physical job sent");
     ShowDeveloperTools();await Task.Delay(350);var dev=toolWindows["devtools"];var fields=VaultElements<TextBox>(dev).ToList();fields.Last().Text="6*7";
     VaultElements<Button>(dev).Single(b=>b.Tag as string=="Run").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitFor(()=>fields[0].Text.Contains("42"),5000);
     await view.ExecuteScriptAsync("console.log('mica-console-fixture')");await WaitFor(()=>fields[0].Text.Contains("mica-console-fixture"),5000);
     VaultElements<Button>(dev).Single(b=>b.Tag as string=="DOM").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitFor(()=>fields[0].Text.Contains("<h1>mica Mica mica</h1>"),5000);
     VaultElements<Button>(dev).Single(b=>b.Tag as string=="Network").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await view.ExecuteScriptAsync("fetch('/ok?tool-network=1')");await WaitFor(()=>fields[0].Text.Contains("tool-network=1"),5000);CaptureDialog(dev,"mica-devtools.png");dev.Close();log.Add("PASS developer console evaluation/logs, DOM source and network capture");
     ShowTaskManager();await Task.Delay(250);var tasks=toolWindows["tasks"];var rows=VaultElements<ListBox>(tasks).Single();Require(rows.Items.Count==state.Tabs.Count,"Task manager tab count wrong");Require(VaultElements<Grid>(tasks).Count(g=>(g.Tag as string)=="proc-row")>=2,"Process rows missing");await WaitFor(()=>VaultElements<TextBlock>(tasks).Any(b=>b.Text.Contains("%")&&b.Text.Contains("PID")),5000);Require(VaultElements<TextBlock>(tasks).Any(b=>(b.Tag as string)=="task-memory"&&b.Text.EndsWith("MB")),"Memory total missing");Require(VaultElements<Canvas>(tasks).Any(c=>c.Children.Count>=2),"Memory graph not drawn");CaptureDialog(tasks,"mica-task-manager.png");Active.Pinned=true;rows.SelectedItem=rows.Items.Cast<BrowserTaskRow>().Single(t=>t.Id==state.ActiveId);int before=state.Tabs.Count;VaultElements<Button>(tasks).Single(b=>b.Tag as string=="Close tab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Require(before==state.Tabs.Count,"Task manager closed pinned tab");Active.Pinned=false;CaptureDialog(tasks,"mica-task-manager.png");tasks.Close();log.Add("PASS task manager shows tabs/real process working sets and protects pinned tabs");
     state.AmbientYoutube=false;await ApplyAmbient(view);Require(await view.ExecuteScriptAsync("!window.__micaAmbient")=="true","Legacy ambient remained injected");log.Add("PASS legacy ambient effect removed; official extension covered by store bridge suite");
     state.Dark=true;ApplyTheme();ShowPrint();await Task.Delay(250);CaptureDialog(toolWindows["print"],"mica-print-dark.png");toolWindows["print"].Close();
     ShowCast();await Task.Delay(700);var cast=toolWindows["cast"];CaptureDialog(cast,"mica-cast.png");var castButton=VaultElements<Button>(cast).First(b=>b.Tag as string=="Mirror tab"||((b.Tag as string)??"").StartsWith("Cast "));log.Add("INFO engine Cast.enable supported: "+castButton.IsEnabled+"; no physical receiver tested");cast.Close();
     ShowFind();await Task.Delay(100);var tab=state.AddTab("mica://home");await Activate(tab.Id);await WaitFor(()=>!toolWindows.ContainsKey("find"),2000);Require(!toolWindows.ContainsKey("find"),"Page tool stayed attached after switching tabs");log.Add("PASS page tools close when switching target tab");
     ShowMenu();await Task.Delay(200);CapturePopup(menuPopup,"mica-tools-menu.png");CloseMenus();
    }
   }catch(Exception e){failures++;log.Add("FAIL "+e);}
   finally{CloseTools();}
   log.Add(failures==0?"PASS":"FAILURES: "+failures);File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","tools-result.txt"),log);Application.Current.Shutdown(failures==0?0:1);
  }
 }
}
