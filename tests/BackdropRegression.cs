using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mica {
 partial class BrowserApp {
  // Dialogs on a Mica window: frosted glass over the window behind, and a click on the glass closes the
  // dialog, except for the sign-in code card, which waits while you're in Discord.
  async Task RunBackdropTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"backdrop");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Width=1200;window.Height=800;window.Left=-6000;window.Top=-6000;
   try {
    await Task.Delay(800);
    Action clickGlass=()=>dialogBackdrop.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left) {RoutedEvent=UIElement.MouseLeftButtonDownEvent});
    ShowShortcuts();await WaitFor(()=>dialogBackdrop!=null&&dialogBackdrop.IsVisible,5000).ContinueWith(t=>{});await Task.Delay(400);
    RegressionCheck("a dialog frosts the window behind it",delegate {
     Require(dialogBackdrop!=null&&dialogBackdrop.IsVisible,"no backdrop");
     Require(Math.Abs(dialogBackdrop.Left-window.Left)<2&&Math.Abs(dialogBackdrop.Width-window.ActualWidth)<2&&Math.Abs(dialogBackdrop.Height-window.ActualHeight)<2,"covers "+dialogBackdrop.Left+","+dialogBackdrop.Width+"x"+dialogBackdrop.Height+" vs "+window.Left+","+window.ActualWidth+"x"+window.ActualHeight);
     var picture=LogicalDescendants<System.Windows.Controls.Image>(dialogBackdrop.Content).FirstOrDefault();
     Require(picture!=null&&picture.Source!=null&&((BitmapSource)picture.Source).PixelWidth<=window.ActualWidth/4,"no small blurred picture of the window");
    });
    if(dialogBackdrop!=null) {
     var content=(FrameworkElement)dialogBackdrop.Content;var shot=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);shot.Render(content);
     var dialogPart=(FrameworkElement)shortcutsWindow.Content;dialogPart.UpdateLayout();var over=new RenderTargetBitmap((int)dialogPart.ActualWidth,(int)dialogPart.ActualHeight,96,96,PixelFormats.Pbgra32);over.Render(dialogPart);
     var both=new DrawingVisual();using(var g=both.RenderOpen()){g.DrawImage(shot,new Rect(0,0,shot.Width,shot.Height));g.DrawImage(over,new Rect(shortcutsWindow.Left-window.Left,shortcutsWindow.Top-window.Top,over.Width,over.Height));}
     var page=new RenderTargetBitmap(shot.PixelWidth,shot.PixelHeight,96,96,PixelFormats.Pbgra32);page.Render(both);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(page));using(var f=File.Create(Path.Combine(dir,"dialog-backdrop.png")))png.Save(f);
    }
    var glass=dialogBackdrop;if(glass!=null)clickGlass();await Task.Delay(400);
    RegressionCheck("clicking outside the dialog closes it and its glass",delegate {Require(shortcutsWindow==null||!shortcutsWindow.IsVisible,"dialog still open");Require(dialogBackdrop==null&&(glass==null||!glass.IsVisible),"glass still up");});
    ShowShortcuts();await WaitFor(()=>dialogBackdrop!=null,5000).ContinueWith(t=>{});
    shortcutsWindow.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(shortcutsWindow),0,Key.Escape) {RoutedEvent=Keyboard.PreviewKeyDownEvent});await Task.Delay(400);
    RegressionCheck("Esc still closes the dialog, glass too",delegate {Require((shortcutsWindow==null||!shortcutsWindow.IsVisible)&&dialogBackdrop==null,"left open");});
    bool cancelled=false;var close=ShowSignInCode(window,"ABCD-EFGH",delegate {cancelled=true;});await WaitFor(()=>dialogBackdrop!=null,5000).ContinueWith(t=>{});
    if(dialogBackdrop!=null)clickGlass();await Task.Delay(400);
    RegressionCheck("a click outside doesn't cancel a Discord sign-in in progress",delegate {Require(signInCodeWindow!=null&&signInCodeWindow.IsVisible&&!cancelled,"sign-in card closed");});
    close();await Task.Delay(300);
    RegressionCheck("the glass goes when the sign-in card does",delegate {Require(dialogBackdrop==null,"glass left behind");});
    // A card that grows out of the island (Find, Print, Cast, a page's dialog) gets no glass.
    System.Windows.Controls.StackPanel toolBody,toolActions;var docked=CreateMicaDialog(window,"Island test card",420,200,out toolBody,out toolActions);DockTool(docked);docked.Show();await Task.Delay(600);
    RegressionCheck("island cards aren't frosted",delegate {Require(docked.IsVisible&&dialogBackdrop==null,"glass under an island card");});
    docked.Close();await Task.Delay(200);
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
