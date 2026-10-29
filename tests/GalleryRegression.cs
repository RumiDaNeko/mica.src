using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mica {
 partial class BrowserApp {
  // Renders every pop-up the menu can open, light and dark, so they can be reviewed side by side.
  async Task RunGalleryTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"gallery");Directory.CreateDirectory(dir);
   try {
    window.WindowState=WindowState.Normal;window.Width=1280;window.Height=840;window.Left=-6000;window.Top=-6000;
    Navigate("https://example.com/",false);try {await WaitForPage();}catch(Exception){log.Add("example.com didn't load; page tools may be missing");}
    await Task.Delay(600);
    var open=new List<KeyValuePair<string,Action>> {
     new KeyValuePair<string,Action>("shortcuts",ShowShortcuts),
     new KeyValuePair<string,Action>("clear-data",ShowClearBrowsingData),
     new KeyValuePair<string,Action>("site-permissions",ShowSitePermissions),
     new KeyValuePair<string,Action>("extensions",ShowExtensions),
     new KeyValuePair<string,Action>("task-manager",ShowTaskManager),
     new KeyValuePair<string,Action>("find",ShowFind),
     new KeyValuePair<string,Action>("updates",ShowUpdates),
     new KeyValuePair<string,Action>("print",ShowPrint),
     new KeyValuePair<string,Action>("cast",ShowCast),
     new KeyValuePair<string,Action>("passwords",ShowVault),
     new KeyValuePair<string,Action>("bookmarks",delegate {ShowLibrary(true);}),
    };
    foreach(var dark in new[]{false,true}) {
     state.Dark=dark;ApplyTheme();await Task.Delay(300);
     foreach(var item in open) {
      var before=Application.Current.Windows.Cast<Window>().ToList();
      try {item.Value();}catch(Exception e){log.Add(item.Key+": "+e.Message);continue;}
      await Task.Delay(900);
      var shown=Application.Current.Windows.Cast<Window>().Where(w=>!before.Contains(w)&&w.IsVisible).ToList();
      if(shown.Count==0){log.Add(item.Key+": nothing opened (maybe not a window)");continue;}
      foreach(var w in shown) {
       w.UpdateLayout();int width=(int)Math.Ceiling(w.ActualWidth),height=(int)Math.Ceiling(w.ActualHeight);if(width<10||height<10)continue;
       var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(w);
       var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));
       using(var file=File.Create(Path.Combine(dir,item.Key+(dark?"-dark":"")+".png")))png.Save(file);
       log.Add(item.Key+(dark?" dark":"")+": "+w.Title+" "+width+"x"+height);
       w.Close();
      }
      await Task.Delay(200);
     }
    }
    state.Dark=false;ApplyTheme();
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
