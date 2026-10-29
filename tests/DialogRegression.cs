using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Mica {
 partial class BrowserApp {
  void CaptureDialog(Window dialog,string name) {
   dialog.UpdateLayout();var shot=new RenderTargetBitmap((int)dialog.ActualWidth,(int)dialog.ActualHeight,96,96,PixelFormats.Pbgra32);shot.Render(dialog);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(shot));using(var file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts",name)))png.Save(file);
  }
  async Task RunBrandTest() {
   var log=new System.Collections.Generic.List<string>();int failure=0;
   try {
    var wordmark=Find<TextBlock>("BrandWordmark");
    GlyphTypeface glyph;
    Require(new Typeface(wordmark.FontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal).TryGetGlyphTypeface(out glyph),"Brand font did not resolve");
    Require(glyph.FontUri.ToString().ToLowerInvariant().Contains("caveat.ttf"),"Brand silently fell back to another font: "+glyph.FontUri);
    Require(wordmark.Text=="Mica."&&window.Icon!=null,"Wordmark or app icon missing");
    await Task.Delay(350);CaptureElement(Find<Grid>("BrandBar"),"mica-brand-wordmark.png");CaptureElement(Find<Grid>("Root"),"mica-brand-home.png");
    log.Add("PASS embedded Caveat font resolves offline without fallback; Mica. wordmark and app icon loaded");
   }catch(Exception e){failure++;log.Add("FAIL "+e);}
   log.Add(failure==0?"PASS":"FAIL");File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","brand-result.txt"),log);Application.Current.Shutdown(failure);
  }
  async Task RunDialogTest() {
   var log=new System.Collections.Generic.List<string>();int failures=0;
   try {
    state.Dark=false;ApplyTheme();ShowShortcuts();await Task.Delay(350);var first=shortcutsWindow;
    Require(first!=null&&first.WindowStyle==WindowStyle.None&&first.Owner==window,"Shortcuts did not use the custom owned dialog");CaptureDialog(first,"mica-shortcuts-light.png");
    ShowShortcuts();Require(first==shortcutsWindow,"Duplicate shortcut window");first.Close();Require(shortcutsWindow==null,"Shortcut window reference retained");log.Add("PASS custom shortcuts open once and clean up on close");
    state.Dark=true;ApplyTheme();ShowShortcuts();await Task.Delay(350);CaptureDialog(shortcutsWindow,"mica-shortcuts-dark.png");shortcutsWindow.Close();log.Add("PASS light and dark shortcuts render");
    foreach(bool accept in new[]{false,true}) {
     bool expected=accept;
     var pending=window.Dispatcher.BeginInvoke(new Action(async delegate {
      await Task.Delay(350);
      var prompt=window.OwnedWindows.Cast<Window>().Single(w=>w.Title=="Replace local vault");
      var grid=(Grid)((Border)prompt.Content).Child;var actions=grid.Children.OfType<StackPanel>().Single();var cancel=actions.Children.OfType<Button>().Single(b=>b.Name=="CancelAction");Require(cancel.IsDefault,"Destructive confirmation is the default action");
      CaptureDialog(prompt,"mica-confirmation.png");actions.Children.OfType<Button>().Single(b=>b.Name==(expected?"ConfirmAction":"CancelAction")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
     }),DispatcherPriority.ApplicationIdle);
     Require(ConfirmMica(window,"Replace local vault","Replace the local vault with the remote encrypted vault? Unsynced local changes will be lost.","Replace local vault")==expected,"Confirmation returned wrong decision");
     await pending;
    }
    log.Add("PASS confirmation preserves separate cancel/accept decisions and defaults to cancel");
   }catch(Exception e){failures++;log.Add("FAIL "+e);}
   log.Add(failures==0?"PASS":"FAILURES: "+failures);File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts","dialogs-result.txt"),log);Application.Current.Shutdown(failures==0?0:1);
  }
 }
}
