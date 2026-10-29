using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Mica {
 partial class BrowserApp {
  // Home backgrounds, frosted glass, the frame colour and the colour picker, rendered off-screen.
  async Task RunPersonalizeTest() {
   var log=regressionLog;string dir=System.IO.Path.Combine(profile,"personalize");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Left=-6000;window.Top=-6000;window.Width=1280;window.Height=840;
   Action<string> shoot=delegate(string name) {
    window.UpdateLayout();var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);
    var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(System.IO.Path.Combine(dir,name+".png")))png.Save(file);
   };
   try {
    await Activate(state.Tabs.First(t=>t.Url=="mica://home").Id);await Task.Delay(400);
    state.HomeBackground="Dusk";state.HomeGlass=60;ApplyPersonalization();await Task.Delay(500);
    var layers=Find<Grid>("HomeBackdropLayers");var blurred=Find<Rectangle>("HomeWallpaperBlur");var glass=Find<System.Windows.Shapes.Path>("HomeGlass");
    RegressionCheck("a preset fills the home background and hides the line art",delegate {Require(layers.IsVisible&&Find<Rectangle>("HomeWallpaper").Fill!=null&&Find<Viewbox>("HomeArtwork").Visibility==Visibility.Collapsed,"layers "+layers.IsVisible);});
    RegressionCheck("frosted glass sits behind the home column",delegate {
     var column=Find<StackPanel>("HomeColumn");var area=column.TransformToVisual(Find<Grid>("HomeBackdrop")).TransformBounds(new Rect(0,0,column.ActualWidth,column.ActualHeight));
     Require(blurred.Clip!=null&&blurred.Effect==null&&blurred.Fill is ImageBrush&&glass.Data!=null,"no glass");
     var bounds=glass.Data.Bounds;Require(bounds.Contains(area),"glass "+bounds+" column "+area);
    });
    shoot("home-dusk");
    state.HomeGlass=0;UpdateHomeGlass();
    RegressionCheck("glass at 0% removes the card but keeps the top bar frosted",delegate {var clip=blurred.Clip as GeometryGroup;Require(!glass.IsVisible&&clip!=null&&clip.Children.Count==1&&clip.Bounds.Height<=44.5,"clip "+(clip==null?"none":clip.Bounds.ToString()));});
    RegressionCheck("over a home background the top bar is see-through frost",delegate {Require(Find<Border>("ToolbarBar").Background==Brushes.Transparent&&Find<Rectangle>("HomeBarTint").Fill!=null,"bar "+Find<Border>("ToolbarBar").Background);});
    state.HomeGlass=60;UpdateHomeGlass();

    // A photo of your own: drawn here so the test needs no files.
    var draw=new DrawingVisual();using(var dc=draw.RenderOpen()){dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x3A,0x6E,0xA5),Color.FromRgb(0xF2,0xB5,0x6B),35),null,new Rect(0,0,1600,1000));for(int i=0;i<40;i++)dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(90,255,255,255)),null,new Point((i*137)%1600,(i*251)%1000),20+i%5*14,20+i%5*14);}
    var photo=new RenderTargetBitmap(1600,1000,96,96,PixelFormats.Pbgra32);photo.Render(draw);string source=System.IO.Path.Combine(dir,"source.png");
    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(photo));using(var file=File.Create(source))encoder.Save(file);
    bool copied=SetHomePhoto(source);state.HomeBackground="photo";ApplyPersonalization();await Task.Delay(500);
    RegressionCheck("your own photo is copied into the profile and shown",delegate {Require(copied&&HomePhotoPath()!=null&&Find<Rectangle>("HomeWallpaper").Fill is ImageBrush,"photo "+HomePhotoPath());});
    shoot("home-photo");

    // Frame colour: a deep colour in the light theme switches sidebar text to light.
    state.HomeBackground="Mist";state.FrameColor="#1F3B45";ApplyPersonalization();RenderTabs();await Task.Delay(300);
    RegressionCheck("a custom frame colour paints the window frame",delegate {Require(((SolidColorBrush)window.Background).Color==ParseColor("#1F3B45"),"frame "+window.Background);});
    RegressionCheck("on a dark frame the sidebar text turns light",delegate {var ink=((SolidColorBrush)SideBrush("Ink")).Color;Require(Luminance(ink)>0.6,"ink "+ink);});
    RegressionCheck("sidebar cards are raised surfaces made from the frame colour",delegate {
     var card=SideBrush("SideCard") as SolidColorBrush;Require(card!=null,"cards should be flat: "+SideBrush("SideCard"));
     var top=card.Color;var frameColor=ParseColor("#1F3B45");
     Require(Luminance(top)>Luminance(frameColor)&&Math.Abs(top.B-top.R)>=Math.Abs(frameColor.B-frameColor.R)/2,"top "+top);
     Require(Find<Button>("SpaceButton").Effect==null,"cards should not cast shadows");
    });
    RegressionCheck("icons take the frame's hue",delegate {var accent=((SolidColorBrush)Brush("Accent")).Color;double h,s,v;ToHsv(accent,out h,out s,out v);double fh,fs,fv;ToHsv(ParseColor("#1F3B45"),out fh,out fs,out fv);Require(Math.Abs(h-fh)<12,"accent "+accent+" hue "+h+" vs "+fh);});
    RegressionCheck("the page keeps a clear edge against the frame",delegate {var edge=((SolidColorBrush)Find<Border>("ContentFrame").BorderBrush).Color;Require(edge.A==255&&edge!=((SolidColorBrush)window.Background).Color,"edge "+edge);});
    RegressionCheck("the theme switch shows a moon in light mode",delegate {Require((string)Find<Button>("ThemeButton").Content=="","glyph "+(int)((string)Find<Button>("ThemeButton").Content)[0]);});
    shoot("frame-deep");
    Navigate("https://example.com/?bar",false);await WaitFor(delegate {return Find<ScrollViewer>("Home").Visibility!=Visibility.Visible;},15000);await Task.Delay(300);
    RegressionCheck("on web pages the top bar takes a tint of the frame",delegate {var tinted=Find<Border>("ToolbarBar").Background as SolidColorBrush;Require(tinted!=null&&tinted.Color.A==255,"bar "+Find<Border>("ToolbarBar").Background);});
    RenderTabs();
    RegressionCheck("in light mode on a dark frame, sidebar letter icons are light",delegate {
     var letters=VaultElements<TextBlock>(Find<StackPanel>("TabItems")).Where(t=>t.Text.Length==1&&char.IsLetter(t.Text[0])&&t.FontSize<=16).ToList();
     Require(letters.Count>0,"no letter icon");foreach(var l in letters){var c=((SolidColorBrush)l.Foreground).Color;Require(Luminance(c)>0.35,"icon "+l.Text+" "+c);}
     var home=VaultElements<TextBlock>(Find<StackPanel>("TabItems")).Where(t=>t.Text=="").ToList();
     foreach(var g in home){var c=((SolidColorBrush)g.Foreground).Color;Require(Luminance(c)>0.35,"home icon "+c);}
    });
    RegressionCheck("the tinted top bar follows the page's rounded corners",delegate {var r=Find<Border>("ToolbarBar").CornerRadius;Require(r.TopLeft>=10&&r.TopRight>=10&&r.BottomLeft==0,"radius "+r);});
    shoot("frame-deep-page");
    await Activate(state.AddTab("mica://home").Id);await Task.Delay(300);
    state.FrameColor="#F0D6DE";ApplyPersonalization();RenderTabs();
    RegressionCheck("on a light frame the sidebar keeps dark text",delegate {var ink=((SolidColorBrush)SideBrush("Ink")).Color;Require(Luminance(ink)<0.2,"ink "+ink);});
    state.Dark=true;state.HomeBackground="Lagoon";ApplyTheme();await Task.Delay(400);
    RegressionCheck("dark mode deepens the chosen colour",delegate {Require(Luminance(((SolidColorBrush)window.Background).Color)<Luminance(ParseColor("#F0D6DE"))*0.6,"frame "+window.Background);});
    shoot("dark-lagoon");
    // Dark mode with a dark warm frame (the case from the report): cards must take the frame's hue, not the default purple.
    state.FrameColor="#5E4334";state.HomeBackground=null;ApplyTheme();RenderTabs();await Task.Delay(300);
    RegressionCheck("in dark mode a dark frame still tints the sidebar cards",delegate {
     var card=SideBrush("SideCard") as SolidColorBrush;Require(card!=null,"cards keep the theme colour: "+SideBrush("SideCard"));
     var top=card.Color;Require(top.R>top.B,"card "+top+" is not warm like the frame "+window.Background);
     var active=SideBrush("Panel") as SolidColorBrush;Require(active!=null&&active.Color.R>active.Color.B,"active tab "+SideBrush("Panel"));
     var space=Find<Button>("SpaceButton");Require(space.FocusVisualStyle!=null,"no keyboard focus ring");
     var surface=space.Template.FindName("Surface",space) as Border;space.Focus();space.UpdateLayout();
     Require(surface==null||surface.BorderBrush==null||surface.BorderBrush==Brushes.Transparent||((SolidColorBrush)surface.BorderBrush).Color.A==0,"focus draws an outline on the card: "+(surface==null?"":surface.BorderBrush.ToString()));
    });
    RegressionCheck("the theme switch shows a sun in dark mode",delegate {Require((string)Find<Button>("ThemeButton").Content=="","glyph "+(int)((string)Find<Button>("ThemeButton").Content)[0]);});
    shoot("dark-brown");
    Navigate("https://example.com/?dark",false);await WaitFor(delegate {return Find<ScrollViewer>("Home").Visibility!=Visibility.Visible;},15000);await Task.Delay(500);
    shoot("dark-brown-page");
    state.Dark=false;ApplyTheme();await Task.Delay(300);shoot("light-brown-page");
    // A near-black frame, in both modes.
    state.FrameColor="#0B0B0C";state.Dark=true;ApplyTheme();RenderTabs();await Task.Delay(300);shoot("dark-black-page");
    state.Dark=false;ApplyTheme();RenderTabs();await Task.Delay(300);shoot("light-black-page");
    RegressionCheck("a black frame keeps the bar neutral grey, not lavender",delegate {
     foreach(var dark in new[]{false,true}){state.Dark=dark;var c=BarTone(0);Require(Math.Max(c.R,Math.Max(c.G,c.B))-Math.Min(c.R,Math.Min(c.G,c.B))<=2,(dark?"dark ":"light ")+"bar "+c);}
     state.Dark=false;
    });
    RegressionCheck("a black frame turns white in light mode, with dark sidebar text",delegate {
     Require(Luminance(((SolidColorBrush)window.Background).Color)>0.8,"frame "+window.Background);
     var ink=SideBrush("Ink") as SolidColorBrush;Require(ink!=null&&Luminance(ink.Color)<0.2,"sidebar text "+SideBrush("Ink"));
    });
    RegressionCheck("popups take the frame's tone",delegate {
     Require(((SolidColorBrush)window.Resources["PopupPanel"]).Color==BarTone(0),"popup "+window.Resources["PopupPanel"]);
     Require(((Border)menuPopup.Child).Background==window.Resources["PopupPanel"],"menu card "+((Border)menuPopup.Child).Background);
     Require(islandCard==null||islandCard.Background==window.Resources["PopupPanel"],"island "+(islandCard==null?"":islandCard.Background.ToString()));
    });
    ShowMenu();await Task.Delay(300);CaptureElement((FrameworkElement)menuPopup.Child,"black-menu-light.png");CloseMenus();
    state.Dark=true;ApplyTheme();RenderTabs();await Task.Delay(300);
    ShowMenu();await Task.Delay(300);CaptureElement((FrameworkElement)menuPopup.Child,"black-menu-dark.png");CloseMenus();
    if(islandCard!=null)CaptureElement(islandCard,"black-island-dark.png");
    state.Dark=false;ApplyTheme();RenderTabs();await Task.Delay(300);shoot("light-black-page2");
    state.FrameColor="#5E4334";
    state.Dark=true;ApplyTheme();await Activate(state.AddTab("mica://home").Id);await Task.Delay(300);
    state.Dark=false;ApplyTheme();

    // The dialog and its picker.
    ShowPersonalize();await Task.Delay(900);var dialog=personalizeWindow;
    RegressionCheck("Personalize opens",delegate {Require(dialog!=null&&dialog.IsVisible,"no dialog");});
    if(dialog!=null) {
     var buttons=VaultElements<Button>(dialog.Content as DependencyObject).ToList();
     buttons.First(b=>(b.Tag as string)=="swatch-#6E5A8E").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Task.Delay(200);
     var hex=VaultElements<TextBox>(dialog.Content as DependencyObject).First(t=>(t.Tag as string)=="hex");
     RegressionCheck("a swatch sets the frame colour and the hex field",delegate {Require(state.FrameColor=="#6E5A8E"&&hex.Text=="#6E5A8E"&&((SolidColorBrush)window.Background).Color==ParseColor("#6E5A8E"),state.FrameColor+" "+hex.Text);});
     hex.Focus();hex.Text="#3E7A6E";await Task.Delay(200);
     RegressionCheck("typing a hex colour applies it",delegate {Require(state.FrameColor=="#3E7A6E",state.FrameColor+" focused "+hex.IsKeyboardFocused);});
     buttons.First(b=>(b.Tag as string)=="bg-Blossom").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Task.Delay(300);
     RegressionCheck("a background tile switches the home background",delegate {Require(state.HomeBackground=="Blossom","bg "+state.HomeBackground);});
     dialog.UpdateLayout();int w=(int)Math.Ceiling(dialog.ActualWidth),h=(int)Math.Ceiling(dialog.ActualHeight);
     var image=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);image.Render(dialog);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(System.IO.Path.Combine(dir,"dialog.png")))png.Save(file);
     shoot("home-blossom");
     buttons.First(b=>(b.Tag as string)=="frame-reset").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Task.Delay(200);
     RegressionCheck("Use theme colour goes back to the default frame",delegate {Require(state.FrameColor==null&&((SolidColorBrush)window.Background).Color==ParseColor("#DDD7E5")&&Find<Grid>("Sidebar").Resources["Ink"]==null,"frame "+state.FrameColor);});
     dialog.Close();
    }
    var scroller=Find<ScrollViewer>("ExpandedTabScroller");scroller.UpdateLayout();await Task.Delay(200);
    RegressionCheck("the sidebar hides its scrollbar when the tabs fit",delegate {Require(scroller.ExtentHeight-scroller.ViewportHeight>4||scroller.ComputedVerticalScrollBarVisibility!=Visibility.Visible,"extent "+scroller.ExtentHeight+" viewport "+scroller.ViewportHeight+" bar "+scroller.ComputedVerticalScrollBarVisibility);});
    Navigate("https://example.com/",false);
    await WaitFor(delegate {NavigationProgress p;return CurrentView()!=null&&CurrentView().CoreWebView2!=null&&CurrentView().CoreWebView2.DocumentTitle=="Example Domain"&&navigations.TryGetValue(state.ActiveId,out p)&&!p.IsLoading;},30000);
    string bar=await CurrentView().CoreWebView2.ExecuteScriptAsync("(function(){var s=document.querySelector('style[data-mica=scrollbar]');return !!s+'/'+getComputedStyle(document.documentElement).scrollbarWidth+'/'+typeof window.__micaScrollbar+'/'+typeof window.__micaFullscreen+'/'+document.querySelectorAll('style').length})()");
    RegressionCheck("web pages get Mica's scrollbar",delegate {Require(bar.StartsWith("\"true/auto"),bar);});
    // Tracker blocking: hovering the menu row opens a slider that stays open while dragging.
    ShowMenu();await Task.Delay(400);
    var trackerRow=VaultElements<Border>(menuPopup.Child).FirstOrDefault(b=>(b.Tag as string)=="tracker-row");
    RegressionCheck("the menu has a tracker blocking row",delegate {Require(trackerRow!=null,"no row");});
    if(trackerRow!=null) {
     var drawer=VaultElements<Grid>(trackerRow).First(g=>(g.Tag as string)=="tracker-drawer");
     var slider=VaultElements<FrameworkElement>(trackerRow).First(e=>System.Windows.Automation.AutomationProperties.GetAutomationId(e)=="tracker-slider");
     RegressionCheck("the slider is hidden until hover",delegate {Require(drawer.ActualHeight<1,"height "+drawer.ActualHeight);});
     trackerRow.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) {RoutedEvent=UIElement.MouseEnterEvent});await Task.Delay(350);
     RegressionCheck("hovering the row slides the slider open",delegate {Require(drawer.ActualHeight>30,"height "+drawer.ActualHeight);});
     var down=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left) {RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent};slider.RaiseEvent(down);
     trackerRow.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) {RoutedEvent=UIElement.MouseLeaveEvent});await Task.Delay(350);
     RegressionCheck("leaving the row while dragging keeps the slider open",delegate {Require(drawer.ActualHeight>30,"height "+drawer.ActualHeight);});
     slider.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) {RoutedEvent=UIElement.LostMouseCaptureEvent});await Task.Delay(350);
     RegressionCheck("letting go outside the row closes it",delegate {Require(drawer.ActualHeight<1,"height "+drawer.ActualHeight);});
     int before=TrackingLevel();
     trackerRow.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(trackerRow),0,before==3?System.Windows.Input.Key.Left:System.Windows.Input.Key.Right) {RoutedEvent=UIElement.KeyDownEvent});
     RegressionCheck("arrow keys move between Off, Basic, Balanced and Strict",delegate {Require(TrackingLevel()==(before==3?2:before+1)&&state.StrictTracking==(TrackingLevel()==3),"level "+TrackingLevel());});
     trackerRow.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) {RoutedEvent=UIElement.MouseEnterEvent});await Task.Delay(350);
     var menuCard=(FrameworkElement)menuPopup.Child;menuCard.UpdateLayout();
     var shot=new RenderTargetBitmap((int)menuCard.ActualWidth,(int)menuCard.ActualHeight,96,96,PixelFormats.Pbgra32);shot.Render(menuCard);var menuPng=new PngBitmapEncoder();menuPng.Frames.Add(BitmapFrame.Create(shot));using(var file=File.Create(System.IO.Path.Combine(dir,"menu-tracker.png")))menuPng.Save(file);
    }
    CloseMenus();
    var saved=StateStore.Load(statePath);
    RegressionCheck("choices are saved",delegate {Require(saved.HomeBackground=="Blossom"&&saved.HomeGlass==60,"saved "+saved.HomeBackground+" "+saved.HomeGlass);});
    // A white sidebar in dark mode is deepened to a mid grey; its text must still be the dark, readable one.
    string frameBefore=state.FrameColor;bool darkBefore=state.Dark;state.Dark=true;state.FrameColor="#FFFFFF";ApplyTheme();
    var whiteInk=((SolidColorBrush)Find<Grid>("Sidebar").Resources["Ink"]).Color;var behind=FrameColor();
    RegressionCheck("a white sidebar in dark mode gets dark text",delegate {Require(Luminance(whiteInk)<Luminance(behind),"ink "+Hex(whiteInk)+" on "+Hex(behind));});
    state.FrameColor="#202024";ApplyTheme();var darkInk=((SolidColorBrush)Find<Grid>("Sidebar").Resources["Ink"]).Color;
    RegressionCheck("a dark sidebar keeps light text",delegate {Require(Luminance(darkInk)>Luminance(FrameColor()),"ink "+Hex(darkInk));});
    state.FrameColor=frameBefore;state.Dark=darkBefore;ApplyTheme();

    // Text size: choosing Larger in Personalize grows Mica's own interface and opens pages larger.
    Navigate("https://example.com/?text-size",false);await WaitForPage();await Task.Delay(500);
    ShowPersonalize();await Task.Delay(500);var personalize=personalizeWindow;
    var larger=personalize==null?null:VaultElements<Button>(personalize.Content as DependencyObject).FirstOrDefault(b=>(b.Tag as string)=="text-size:1.3");
    if(larger!=null)larger.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));await Task.Delay(800);
    RegressionCheck("Larger text grows the sidebar, its column and the page",delegate {
     Require(larger!=null,"no Larger button");Require(Math.Abs(UiScale-1.3)<0.01&&Math.Abs(state.TextScale-1.3)<0.01,"scale "+UiScale);
     var sidebar=Find<Grid>("Sidebar");Require(sidebar.LayoutTransform is ScaleTransform&&Math.Abs(((ScaleTransform)sidebar.LayoutTransform).ScaleX-1.3)<0.01,"sidebar transform");
     Require(Math.Abs(Find<Grid>("AppLayout").ColumnDefinitions[0].ActualWidth-238*1.3)<2,"column "+Find<Grid>("AppLayout").ColumnDefinitions[0].ActualWidth);
     Require(Math.Abs(CurrentView().ZoomFactor-1.3)<0.01,"page zoom "+CurrentView().ZoomFactor);
     Require(Find<Border>("ZoomBadge").Visibility!=Visibility.Visible,"the zoom badge treats the chosen size as normal");
    });
    if(personalize!=null)personalize.Close();await Task.Delay(300);shoot("text-larger");
    SetTextScale(1);await Task.Delay(500);
    RegressionCheck("Default text puts everything back",delegate {Require(Math.Abs(UiScale-1)<0.01&&Math.Abs(Find<Grid>("AppLayout").ColumnDefinitions[0].ActualWidth-238)<2&&Math.Abs(CurrentView().ZoomFactor-1)<0.01,"scale "+UiScale+" zoom "+CurrentView().ZoomFactor);});
   }catch(Exception e){log.Add("ERROR "+e);regressionFailures++;}
   log.Add(regressionFailures==0?"ALL PASS":regressionFailures+" FAILURE(S)");
   File.WriteAllLines(System.IO.Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
