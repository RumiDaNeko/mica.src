using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Mica {
 partial class BrowserApp {
  // Off-screen frame timing for the shell's animations.
  async Task<string> MeasureFrames(string name,Action start,int milliseconds,Func<bool> visible=null) {
   var intervals=new List<double>();var seen=new List<double>();var clock=Stopwatch.StartNew();double last=-1;
   EventHandler onFrame=delegate {double now=clock.Elapsed.TotalMilliseconds;if(last>=0){intervals.Add(now-last);if(visible!=null&&visible())seen.Add(now-last);}last=now;};
   // Names any dispatcher operation that holds the UI thread long enough to cost a frame.
   var hooks=window.Dispatcher.Hooks;var began=new Dictionary<System.Windows.Threading.DispatcherOperation,double>();var heavy=new List<string>();
   var methodField=typeof(System.Windows.Threading.DispatcherOperation).GetField("_method",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
   System.Windows.Threading.DispatcherHookEventHandler opStart=delegate(object s,System.Windows.Threading.DispatcherHookEventArgs e) {began[e.Operation]=clock.Elapsed.TotalMilliseconds;};
   System.Windows.Threading.DispatcherHookEventHandler opDone=delegate(object s,System.Windows.Threading.DispatcherHookEventArgs e) {
    double t;if(!began.TryGetValue(e.Operation,out t))return;began.Remove(e.Operation);double took=clock.Elapsed.TotalMilliseconds-t;if(took<10)return;
    var method=methodField==null?null:methodField.GetValue(e.Operation) as Delegate;
    heavy.Add((method==null?e.Operation.Priority.ToString():(method.Method.DeclaringType.DeclaringType!=null?method.Method.DeclaringType.DeclaringType.FullName+"/":method.Method.DeclaringType.Namespace+"/")+method.Method.DeclaringType.Name+"."+method.Method.Name+(method.Target!=null&&!(method.Target.GetType().Name.StartsWith("<"))?"@"+method.Target.GetType().Name:"")+" "+e.Operation.Priority)+" "+took.ToString("0"));
   };
   await Task.Delay(300);hooks.OperationStarted+=opStart;hooks.OperationCompleted+=opDone;CompositionTarget.Rendering+=onFrame;var work=Stopwatch.StartNew();start();double startCost=work.Elapsed.TotalMilliseconds;
   await Task.Delay(milliseconds+120);CompositionTarget.Rendering-=onFrame;hooks.OperationStarted-=opStart;hooks.OperationCompleted-=opDone;
   if(intervals.Count==0)return name+": no frames";
   int slow=intervals.Count(i=>i>25);
   return string.Format("{0}: start {1:0.0} ms, {2} frames, avg {3:0.0} ms, max {4:0.0} ms, {5} over 25 ms{6}",name,startCost,intervals.Count,intervals.Average(),intervals.Max(),slow,heavy.Count==0?"":" | "+string.Join(", ",heavy))
    +(visible==null?"":seen.Count==0?" | never visible":string.Format(" | while visible: {0} frames, max {1:0.0} ms, {2} over 25 ms",seen.Count,seen.Max(),seen.Count(i=>i>25)));
  }
  async Task RunMotionTest() {
   var log=regressionLog;string dir=Path.Combine(profile,"motion");Directory.CreateDirectory(dir);
   window.WindowState=WindowState.Normal;window.Width=1280;window.Height=840;window.Left=-6000;window.Top=-6000;
   log.Add("render tier "+(RenderCapability.Tier>>16)+", animation "+SystemParameters.ClientAreaAnimation);
   await Task.Delay(1500);
   log.Add(await MeasureFrames("idle home",delegate {},400));
   log.Add(await MeasureFrames("idle home 3s",delegate {},3000));
   log.Add(await MeasureFrames("sidebar collapse (home)",ToggleSidebar,220));
   log.Add(await MeasureFrames("sidebar expand (home)",ToggleSidebar,220));
   var button=Find<Button>("NewTabButton");
   log.Add(await MeasureFrames("button hover",delegate {AnimateButton(button,1.025);},140));
   AnimateButton(button,1);
   var searchClock=Stopwatch.StartNew();Spotlight.Search(state,"");double searchCost=searchClock.Elapsed.TotalMilliseconds;
   log.Add("spotlight search alone "+searchCost.ToString("0.0")+" ms, "+state.History.Count+" history, "+state.Tabs.Count+" tabs");
   log.Add(await MeasureFrames("spotlight open",delegate {OpenSpotlight(false);},220));
   CloseSpotlight();await Task.Delay(300);
   log.Add(await MeasureFrames("spotlight open again",delegate {OpenSpotlight(false);},220));
   log.Add(await MeasureFrames("spotlight close",CloseSpotlight,160));
   Func<bool> menuShowing=delegate {var card=(UIElement)menuPopup.Child;return menuPopup.IsOpen&&card.Opacity>0.01;};
   log.Add(await MeasureFrames("menu open",ShowMenu,240,menuShowing));
   CloseMenus();await Task.Delay(300);
   log.Add(await MeasureFrames("menu open again",ShowMenu,240,menuShowing));
   var tracker=VaultElements<Border>(menuPopup.Child).FirstOrDefault(b=>(b.Tag as string)=="tracker-row");
   if(tracker!=null)log.Add(await MeasureFrames("tracker slider open",delegate {tracker.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) {RoutedEvent=UIElement.MouseEnterEvent});},220));
   CloseMenus();await Task.Delay(300);
   // Numbers roll digit by digit; other text slides into the new words.
   tickerInTests=true;var headline=Find<TextBlock>("HomeHeadline");headline.Text="9:59";await Task.Delay(100);
   log.Add(await MeasureFrames("clock rolls 9:59 to 10:00",delegate {RollText(headline,"10:00");},80));
   RegressionCheck("a changing number rolls only its changed digits",delegate {Require(headline.Inlines.OfType<InlineUIContainer>().Count()==4,"rollers "+headline.Inlines.OfType<InlineUIContainer>().Count());});
   CaptureElement(headline,"ticker-mid.png");await Task.Delay(470);CaptureElement(headline,"ticker-landed.png");await Task.Delay(160);
   RegressionCheck("the roll settles on the new number",delegate {Require(headline.Text=="10:00"&&!headline.Inlines.OfType<InlineUIContainer>().Any(),"text "+headline.Text);});
   CaptureElement(headline,"ticker-end.png");
   var greeting=Find<TextBlock>("Greeting");RollText(greeting,"Good evening, tester.");await Task.Delay(40);
   RegressionCheck("words slide instead of rolling",delegate {Require(greeting.Opacity<1&&!greeting.Inlines.OfType<InlineUIContainer>().Any(),"opacity "+greeting.Opacity);});
   await Task.Delay(400);RegressionCheck("slid text lands on the new words",delegate {Require(greeting.Text=="Good evening, tester."&&greeting.Opacity==1,greeting.Text+" "+greeting.Opacity);});
   RollText(headline,"10:01");RollText(headline,"10:02");await Task.Delay(600);
   RegressionCheck("a roll interrupted by another lands on the latest value",delegate {Require(headline.Text=="10:02",headline.Text);});
   tickerInTests=false;UpdateHomeClock();
   // The menu: one list grouped under headings, opening above its button.
   log.Add(await MeasureFrames("menu open",ShowMenu,260));
   var topNames=PopupButtons(menuPopup).Select(b=>System.Windows.Automation.AutomationProperties.GetName(b)).ToList();
   var headings=((StackPanel)((ScrollViewer)((Border)menuPopup.Child).Child).Content).Children.OfType<TextBlock>().Select(x=>x.Text).ToList();
   RegressionCheck("the menu lists every command under its heading, with no submenus",delegate {
    foreach(var n in new[]{"Find in page","Reading mode","Print","Cast media","YouTube ambient","Site permissions","Developer tools","Extensions","Clear browsing data","Ad blocking","Task manager","Keyboard shortcuts","Updates","Language"})Require(topNames.Contains(n),"missing "+n+": "+string.Join(",",topNames));
    Require(headings.SequenceEqual(new[]{"SITES","PRIVACY","TOOLS"}),"headings "+string.Join(",",headings));
    Require(topNames.IndexOf("Find in page")>topNames.IndexOf("Profiles")&&topNames.IndexOf("Task manager")>topNames.IndexOf("Extensions"),"order "+string.Join(",",topNames));
   });
   RegressionCheck("the menu opens above its button",delegate {
    // The test window sits off-screen, where WPF pulls popups back onto the screen, so the placement is checked rather than the position.
    Require(menuPopup.IsOpen&&menuPopup.Placement==System.Windows.Controls.Primitives.PlacementMode.Top&&menuPopup.PlacementTarget==Find<Button>("MenuButton")&&menuPopup.VerticalOffset==MenuShadowRoom.Bottom-8,menuPopup.Placement+" offset "+menuPopup.VerticalOffset);
   });
   RegressionCheck("the menu is no taller than the window",delegate {var card=(FrameworkElement)menuPopup.Child;Require(card.ActualHeight<=window.ActualHeight-40+1,card.ActualHeight+" > "+(window.ActualHeight-40));});
   CaptureElement((FrameworkElement)menuPopup.Child,"grouped-menu.png");
   CloseMenus();await Task.Delay(100);
   RegressionCheck("the menu closes",delegate {Require(!menuPopup.IsOpen,"left open");});   await Task.Delay(200);
   // The close animation, drawn at moments along its timeline as one strip.
   {
    window.UpdateLayout();var still=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);still.Render(window);still.Freeze();
    double[] moments={0,150,260,330,380,450,560,700};double fw=window.ActualWidth,fh=window.ActualHeight,cell=0.3;
    var strip=new System.Windows.Media.Imaging.RenderTargetBitmap((int)(fw*cell*4),(int)(fh*cell*2),96,96,PixelFormats.Pbgra32);
    var drawing=new DrawingVisual();
    using(var dc=drawing.RenderOpen()) {
     dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2B,0x3A,0x4A)),null,new Rect(0,0,fw*cell*4,fh*cell*2));
     for(int i=0;i<moments.Length;i++) {
      Action<double> seek;var scene=FarewellScene(still,new Size(fw,fh),((SolidColorBrush)window.Resources["Accent"]).Color,out seek);seek(moments[i]);
      scene.Measure(new Size(fw,fh));scene.Arrange(new Rect(0,0,fw,fh));scene.UpdateLayout();
      var frame=new System.Windows.Media.Imaging.RenderTargetBitmap((int)fw,(int)fh,96,96,PixelFormats.Pbgra32);frame.Render(scene);
      dc.DrawImage(frame,new Rect((i%4)*fw*cell,(i/4)*fh*cell,fw*cell,fh*cell));
     }
    }
    strip.Render(drawing);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(strip));
    using(var file=File.Create(Path.Combine(dir,"farewell-strip.png")))encoder.Save(file);
    // Played for real (bypassing the active-window check): the overlay must appear, run and close itself.
    farewellNote=null;PlayFarewell();string started=farewellNote;bool shown=farewellOverlay!=null&&farewellOverlay.IsVisible;
    if(farewellShot!=null){var shotPng=new System.Windows.Media.Imaging.PngBitmapEncoder();shotPng.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(farewellShot));using(var file=File.Create(Path.Combine(dir,"farewell-capture.png")))shotPng.Save(file);}
    await Task.Delay((int)FarewellLength+400);string ended=farewellNote;
    log.Add("farewell: "+started+" -> "+ended);
    RegressionCheck("the close animation plays in its own window and cleans up",delegate {Require(started=="playing"&&shown,"start "+started+", shown "+shown);Require(ended!=null&&ended.StartsWith("finished")&&!farewellOverlay.IsVisible,"end "+ended);});
    var cost=Stopwatch.StartNew();Action<double> s2;FarewellScene(still,new Size(fw,fh),Colors.Purple,out s2);s2(400);log.Add("farewell scene build+frame "+cost.Elapsed.TotalMilliseconds.ToString("0.0")+" ms");
    // Every closing animation you can pick: it starts as the window and ends with nothing left on screen.
    Func<FrameworkElement,int> opaque=delegate(FrameworkElement scene) {
     scene.Measure(new Size(fw,fh));scene.Arrange(new Rect(0,0,fw,fh));scene.UpdateLayout();
     var frame=new System.Windows.Media.Imaging.RenderTargetBitmap((int)fw,(int)fh,96,96,PixelFormats.Pbgra32);frame.Render(scene);
     var pixels=new byte[(int)fw*(int)fh*4];frame.CopyPixels(pixels,(int)fw*4,0);int count=0;for(int i=3;i<pixels.Length;i+=4)if(pixels[i]>8)count++;return count;
    };
    foreach(var item in CloseEffects.Where(e=>e[0]!="off")) {
     Action<double> seekEffect;double length;var timer=Stopwatch.StartNew();var scene=FarewellScene(item[0],still,new Size(fw,fh),Colors.Purple,out seekEffect,out length);
     seekEffect(0);int first=opaque(scene);seekEffect(length*0.5);int middle=opaque(scene);seekEffect(length);int last=opaque(scene);
     log.Add("close effect "+item[0]+": "+length+" ms, opaque pixels "+first+" -> "+middle+" -> "+last+", built in "+timer.Elapsed.TotalMilliseconds.ToString("0")+" ms");
     string key=item[0];RegressionCheck("the "+key+" close animation starts as the window and leaves nothing behind",delegate {Require(first>fw*fh*0.9&&last<fw*fh*0.002&&length<=900,first+" -> "+last+" in "+length);});
    }
    // Picked in Personalize: the choice is kept and the preview plays it.
    ShowPersonalize();await Task.Delay(500);
    var personalize=Application.Current.Windows.Cast<Window>().FirstOrDefault(w=>w!=window&&w.Title=="Personalize");
    Func<string,Button> effectButton=delegate(string key) {return personalize==null?null:VaultElements<Button>(personalize).FirstOrDefault(b=>(b.Tag as string)=="close-effect:"+key);};
    var drop=effectButton("drop");if(drop!=null)drop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(400);
    var stage=personalize==null?null:VaultElements<Border>(personalize).FirstOrDefault(b=>(b.Tag as string)=="close-preview");
    RegressionCheck("Personalize picks the closing animation and previews it",delegate {Require(drop!=null&&state.CloseEffect=="drop"&&CloseEffect()=="drop"&&stage!=null&&stage.Child!=null,"effect "+state.CloseEffect+", stage "+(stage!=null&&stage.Child!=null));});
    var off=effectButton("off");if(off!=null)off.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(200);
    bool wantedWhenOff=FarewellWanted();
    RegressionCheck("Off means no closing animation",delegate {Require(state.CloseEffect=="off"&&!wantedWhenOff,"effect "+state.CloseEffect);});
    var stars=effectButton("stars");if(stars!=null)stars.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(200);
    RegressionCheck("Stars is the default",delegate {Require(state.CloseEffect==null&&CloseEffect()=="stars","effect "+state.CloseEffect);});
    if(personalize!=null)personalize.Close();await Task.Delay(200);
   }
   var buildClock=Stopwatch.StartNew();ShowMenu();double built=buildClock.Elapsed.TotalMilliseconds;CloseMenus();await Task.Delay(300);
   log.Add(await MeasureFrames("menu reopen without rebuilding (ShowMenu alone "+built.ToString("0")+" ms)",delegate {menuPopup.IsOpen=true;},240));
   CloseMenus();await Task.Delay(300);
   log.Add(await MeasureFrames("find grows from island",ShowFind,360));
   log.Add(await MeasureFrames("find shrinks into island",delegate {ClosePageTools();},280));
   await Task.Delay(300);
   // The same moves with a personalised look: a home background with frosted glass and a custom frame.
   state.HomeBackground="Lagoon";state.HomeGlass=60;state.FrameColor="#5E4334";ApplyPersonalization();RenderTabs();await Task.Delay(600);
   log.Add(await MeasureFrames("idle home (personalised)",delegate {},400));
   log.Add(await MeasureFrames("sidebar collapse (personalised)",ToggleSidebar,220));
   log.Add(await MeasureFrames("sidebar expand (personalised)",ToggleSidebar,220));
   log.Add(await MeasureFrames("spotlight open (personalised)",delegate {OpenSpotlight(false);},220));
   log.Add(await MeasureFrames("spotlight close (personalised)",CloseSpotlight,160));
   log.Add(await MeasureFrames("menu open (personalised)",ShowMenu,240));
   CloseMenus();await Task.Delay(300);
   log.Add(await MeasureFrames("home scroll (personalised)",delegate {Find<ScrollViewer>("Home").ScrollToVerticalOffset(40);},200));
   state.HomeBackground=null;state.FrameColor=null;ApplyPersonalization();RenderTabs();await Task.Delay(300);
   var memory=Stopwatch.StartNew();EngineProcessIds();log.Add("memory poll UI part "+memory.Elapsed.TotalMilliseconds.ToString("0.0")+" ms");
   Navigate("https://example.com/",false);
   await WaitFor(delegate {NavigationProgress p;return CurrentView()!=null&&CurrentView().CoreWebView2!=null&&navigations.TryGetValue(state.ActiveId,out p)&&!p.IsLoading;},25000);
   await Task.Delay(800);
   memory=Stopwatch.StartNew();EngineProcessIds();log.Add("memory poll UI part with page "+memory.Elapsed.TotalMilliseconds.ToString("0.0")+" ms");
   log.Add(await MeasureFrames("sidebar collapse (page)",ToggleSidebar,220));
   log.Add(await MeasureFrames("sidebar expand (page)",ToggleSidebar,220));
   var tabs=Stopwatch.StartNew();RenderTabs();log.Add("RenderTabs "+tabs.Elapsed.TotalMilliseconds.ToString("0.0")+" ms");
   File.WriteAllLines(Path.Combine(dir,"result.txt"),log);window.Close();
  }
 }
}
