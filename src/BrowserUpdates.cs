using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mica {
 // Updates: one place for what Mica runs on and what it fetches over the air. Mica itself
 // comes from GitHub Releases and the web engine from Microsoft; both are checked once a day
 // in the background, and this page shows them and lets you check now.
 partial class BrowserApp {
  internal const string AppVersion="0.12.35";
  static readonly Version CurrentApp=Version.Parse(AppVersion);
  Window updatesWindow;
  static string engineUpdateNote,appUpdateNote;
  static bool appChecking;
  static event Action EngineUpdateChanged;
  static void SetEngineNote(string text) {engineUpdateNote=text;NotifyUpdates();}
  static void NotifyUpdates() {var changed=EngineUpdateChanged;if(changed!=null)changed();}

  string RunningEngine() {return BundledRuntime.ChosenVersion??(environment!=null?environment.BrowserVersionString.Split(' ')[0]:"System");}
  Version WaitingEngine() {
   var running=EngineUpdater.ParseVersion(RunningEngine());
   var best=EngineUpdater.Installed(EngineUpdater.Root).Select(i=>i.Item1).FirstOrDefault();
   return best!=null&&(running==null||best>running)?best:null;
  }
  // Short status for the menu row. Working it out reads the update folders, so it is done off the
  // UI thread and kept; opening the menu only reads the kept answer.
  static string updatesHint;
  void RefreshUpdatesHint() {
   var running=EngineUpdater.ParseVersion(RunningEngine());
   Task.Run(delegate {
    string hint;
    try {
     var best=EngineUpdater.Installed(EngineUpdater.Root).Select(i=>i.Item1).FirstOrDefault();
     hint=AppUpdater.ReadySetup(CurrentApp)!=null?"Restart to update":AppUpdater.Available(CurrentApp)!=null?"Update available":best!=null&&(running==null||best>running)?"Restart to finish":"Up to date";
    }catch(Exception){hint="Up to date";}
    updatesHint=hint;
    try {window.Dispatcher.BeginInvoke(new Action(delegate {if(updatesHintText!=null&&menuPopup!=null&&menuPopup.IsOpen)RollText(updatesHintText,UpdatesHint());}));}catch(Exception){}
   });
  }
  string UpdatesHint() {
   if(appDownloading)return "Downloading…";if(engineChecking||appChecking)return "Checking…";
   return updatesHint??"Up to date";
  }

  async void CheckAppUpdate(bool force) {
   if(appChecking)return;appChecking=true;appUpdateNote=null;NotifyUpdates();
   try {
    string message=await AppUpdater.Update(CurrentApp,force);
    if(message!=null&&!closing){appUpdateNote=message;if(!force&&(AppUpdater.ReadySetup(CurrentApp)!=null||AppUpdater.Available(CurrentApp)!=null))Status(message);}
   }catch(Exception e){if(force&&!closing)appUpdateNote="Couldn't update Mica: "+e.Message;}
   finally {appChecking=false;NotifyUpdates();RefreshUpdatesHint();}
  }
  // Only runs when the user presses Download: a found release is never fetched on its own.
  static bool appDownloading;static long appReceived,appTotal;
  static event Action AppDownloadProgress;
  async void DownloadAppUpdate() {
   if(appDownloading||AppUpdater.Available(CurrentApp)==null)return;appDownloading=true;appReceived=0;appTotal=AppUpdater.Available(CurrentApp).DownloadSize;appUpdateNote=null;NotifyUpdates();
   try {
    string message=await AppUpdater.Download(CurrentApp,delegate(long received,long total) {appReceived=received;appTotal=total;var moved=AppDownloadProgress;if(moved!=null)moved();});
    if(message!=null&&!closing){appUpdateNote=message;Status(message);}
   }catch(Exception e){if(!closing)appUpdateNote="Couldn't download the update: "+e.Message;}
   finally {appDownloading=false;NotifyUpdates();RefreshUpdatesHint();}
  }
  static string Megabytes(long bytes) {return (bytes/1048576.0).ToString("0.0")+" MB";}
  // The installer closes every Mica window (saving tabs), upgrades and reopens Mica.
  void RestartToUpdate() {
   string setup=AppUpdater.ReadySetup(CurrentApp);if(setup==null)return;
   try {
    using(var locked=AppUpdater.LockVerified(setup)) {
     if(locked==null){appUpdateNote="The downloaded update changed since it was checked, so Mica won't run it. Check for updates again.";try {File.Delete(setup+".ok");}catch(Exception){}NotifyUpdates();return;}
     Process.Start(new ProcessStartInfo(setup,"--update") {UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(setup)});
    }
   }catch(Exception e){appUpdateNote="Couldn't start the update: "+e.Message;NotifyUpdates();}
  }

  // The copy an update replaced, kept beside this one so the update can be undone (see the installer).
  static string PreviousFolder {get {return Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\')+".previous";}}
  internal static string PreviousVersion {get {
   try {
    string folder=PreviousFolder;if(!File.Exists(Path.Combine(folder,"Mica.exe"))||!File.Exists(Path.Combine(folder,"mica-install.txt"))||!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MicaUninstall.exe")))return null;
    string file=Path.Combine(folder,"version.txt");return File.Exists(file)?File.ReadAllText(file).Trim():"the earlier version";
   }catch(Exception){return null;}
  }}
  // The installer closes Mica (saving tabs), swaps the two copies and opens Mica again.
  void UndoUpdate() {
   try {Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MicaUninstall.exe"),"--rollback") {UseShellExecute=true,WorkingDirectory=Path.GetTempPath()});}
   catch(Exception e){appUpdateNote="Couldn't undo the update: "+e.Message;NotifyUpdates();}
  }
  const int UpdateSpinMinimum=700;
  // A small turning arc in the accent colour; it only animates while shown.
  FrameworkElement CheckSpinner(string tag) {
   var arc=new System.Windows.Shapes.Path {Data=Geometry.Parse("M 7,1 A 6,6 0 1 1 1,7"),StrokeThickness=2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Width=14,Height=14,Tag=tag,
    HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed,RenderTransformOrigin=new Point(0.5,0.5)};
   arc.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"Accent");var turn=new RotateTransform();arc.RenderTransform=turn;
   arc.IsVisibleChanged+=delegate {turn.BeginAnimation(RotateTransform.AngleProperty,arc.IsVisible?new System.Windows.Media.Animation.DoubleAnimation(0,360,TimeSpan.FromMilliseconds(800)) {RepeatBehavior=System.Windows.Media.Animation.RepeatBehavior.Forever}:null);};
   return arc;
  }
  void ShowUpdates() {
   CloseMenus();if(updatesWindow!=null){updatesWindow.Activate();return;}
   StackPanel body,actions;var dialog=CreateMicaDialog(window,"Updates",460,560,out body,out actions);updatesWindow=dialog;
   // It still scrolls with the wheel when the window is short; the bar itself isn't shown.
   var scroll=body.Parent as ScrollViewer;if(scroll!=null)scroll.VerticalScrollBarVisibility=ScrollBarVisibility.Hidden;
   // While a check runs, the values it can change spin instead of showing the old answer.
   var spinners=new Dictionary<TextBlock,FrameworkElement>();
   Func<string,string,string,TextBlock> row=delegate(string label,string value,string tag) {
    var grid=new Grid {Margin=new Thickness(12,9,12,9)};grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
    var name=DialogText(label,12,"Ink");name.VerticalAlignment=VerticalAlignment.Center;grid.Children.Add(name);
    var shown=DialogText(value,12,"Muted");shown.Tag=tag;shown.TextWrapping=TextWrapping.NoWrap;shown.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(shown,1);grid.Children.Add(shown);
    var spinner=CheckSpinner(tag+"-spinner");Grid.SetColumn(spinner,1);grid.Children.Add(spinner);spinners[shown]=spinner;
    body.Children.Add(Card(grid));return shown;
   };
   Action<TextBlock,bool> spin=delegate(TextBlock value,bool on) {value.Visibility=on?Visibility.Hidden:Visibility.Visible;spinners[value].Visibility=on?Visibility.Visible:Visibility.Collapsed;};
   DateTime spinUntil=DateTime.MinValue;var settle=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(UpdateSpinMinimum)};
   Func<string,TextBlock> note=delegate(string tag) {var text=DialogText("",12,"Muted");text.Tag=tag;text.Margin=new Thickness(2,6,2,0);text.LineHeight=18;body.Children.Add(text);return text;};
   SectionLabel(body,"MICA");
   row("Version",AppVersion,"updates-app");
   var appLatest=row("Latest release","—","updates-app-latest");
   var whatsNew=note("updates-app-notes");
   // Shown while a release downloads: how far along it is, in bytes.
   var progressPanel=new StackPanel {Margin=new Thickness(2,8,2,0),Tag="updates-progress-panel"};
   var bar=new Grid {Height=6,ClipToBounds=true};var track=new Border {CornerRadius=new CornerRadius(3)};track.SetResourceReference(Border.BackgroundProperty,"Hover");bar.Children.Add(track);
   var filled=new ScaleTransform(0,1);var fill=new Border {CornerRadius=new CornerRadius(3),Tag="updates-progress",RenderTransform=filled};fill.SetResourceReference(Border.BackgroundProperty,"Accent");bar.Children.Add(fill);
   var barText=DialogText("",11,"Muted");barText.Tag="updates-progress-text";barText.Margin=new Thickness(0,5,0,0);
   progressPanel.Children.Add(bar);progressPanel.Children.Add(barText);
   var appNote=note("updates-app-note");body.Children.Add(progressPanel);
   SectionLabel(body,"WEB ENGINE");
   var engine=row("Running",RunningEngine(),"updates-engine");
   var waiting=row("Next version","—","updates-waiting");
   var checkedAt=row("Last checked","—","updates-checked");
   var engineNote=note("updates-note");
   var about=DialogText("Mica checks for a new version of itself and of its web engine once a day. A new Mica is downloaded only when you press Download, and installed when you restart it; a new engine downloads in the background and starts the next time you open Mica.",11,"Muted");
   about.Margin=new Thickness(2,12,2,4);about.LineHeight=17;body.Children.Add(about);
   var check=new Button {Content="Check now",Padding=new Thickness(18,9,18,9),Tag="updates-check"};
   check.Click+=delegate {spinUntil=DateTime.UtcNow.AddMilliseconds(UpdateSpinMinimum);settle.Stop();settle.Start();CheckAppUpdate(true);CheckEngineUpdate(true);NotifyUpdates();};actions.Children.Add(check);
   var restart=new Button {Content="Restart to update",Padding=new Thickness(18,9,18,9),Margin=new Thickness(8,0,0,0),Tag="updates-restart"};restart.SetResourceReference(Button.BackgroundProperty,"Hover");
   restart.Click+=delegate {RestartToUpdate();};actions.Children.Add(restart);
   // Undo asks once more before it restarts Mica.
   string previous=PreviousVersion;bool undoAsked=false;
   var undo=new Button {Content="Undo update",Padding=new Thickness(18,9,18,9),Margin=new Thickness(0,0,8,0),Tag="updates-undo",Visibility=previous==null?Visibility.Collapsed:Visibility.Visible,
    ToolTip=previous==null?null:"Go back to Mica "+previous+". Your tabs and data stay as they are."};
   undo.Click+=delegate {if(!undoAsked){undoAsked=true;undo.Content="Restart on "+previous+"?";return;}UndoUpdate();};
   actions.Children.Insert(0,undo);
   var download=new Button {Content="Download",Padding=new Thickness(18,9,18,9),Margin=new Thickness(8,0,0,0),Tag="updates-download"};download.SetResourceReference(Button.BackgroundProperty,"Hover");
   download.Click+=delegate {DownloadAppUpdate();};actions.Children.Add(download);
   Action moved=delegate {
    double share=appTotal>0?Math.Min(1,(double)appReceived/appTotal):0;filled.ScaleX=share;
    barText.Text=appTotal>0?Megabytes(appReceived)+" of "+Megabytes(appTotal)+" · "+(int)(share*100)+"%":Megabytes(appReceived);
   };
   // Progress arrives many times a second; the bar follows at most every 100 ms.
   DateTime lastMove=DateTime.MinValue;
   Action progressed=delegate {window.Dispatcher.BeginInvoke(new Action(delegate {if(updatesWindow!=dialog||DateTime.UtcNow-lastMove<TimeSpan.FromMilliseconds(100))return;lastMove=DateTime.UtcNow;moved();}));};
   AppDownloadProgress+=progressed;dialog.Closed+=delegate {AppDownloadProgress-=progressed;};
   Action<TextBlock,string> show=delegate(TextBlock text,string value) {text.Text=value??"";text.Visibility=text.Text.Length==0?Visibility.Collapsed:Visibility.Visible;};
   Action refresh=delegate {
    if(updatesWindow!=dialog)return;
    string ready=AppUpdater.ReadySetup(CurrentApp);
    string appStamp=Path.Combine(AppUpdater.Root,"last-check");
    var found=ready==null?AppUpdater.Available(CurrentApp):null;
    appLatest.Text=ready!=null?AppUpdater.SetupVersion(ready)+" · ready":found!=null?found.Version+" · available":AppUpdater.Feed==null?"Not set up":File.Exists(appStamp)?"Up to date":"Not checked yet";
    string notes=ready!=null?AppUpdater.Notes(ready):found!=null?found.Notes:null;
    if(notes!=null){var lines=notes.Replace("\r","").Split('\n').Select(l=>l.Trim()).Where(l=>l.Length>0).Take(5).ToList();notes=lines.Count==0?null:"What's new\n"+string.Join("\n",lines);}
    show(whatsNew,notes);
    show(appNote,appChecking?"Checking GitHub for a newer Mica…":appDownloading&&found!=null?"Downloading Mica "+found.Version+"…":appUpdateNote);
    progressPanel.Visibility=appDownloading?Visibility.Visible:Visibility.Collapsed;if(appDownloading)moved();
    download.Visibility=found!=null&&!appDownloading?Visibility.Visible:Visibility.Collapsed;
    if(found!=null&&found.DownloadSize>0)download.Content="Download · "+Megabytes(found.DownloadSize);
    engine.Text=RunningEngine();
    var next=WaitingEngine();waiting.Text=next==null?"None waiting":next+" · ready";
    string stamp=Path.Combine(EngineUpdater.Root,"last-check");
    checkedAt.Text=File.Exists(stamp)?File.GetLastWriteTime(stamp).ToString("MMM d, HH:mm"):"Not yet";
    show(engineNote,engineChecking?"Checking Microsoft for a newer engine… a download can take a few minutes.":engineUpdateNote);
    // A check that answers at once still spins for a moment, so pressing Check now visibly does something.
    bool held=DateTime.UtcNow<spinUntil;
    spin(appLatest,appChecking||held);spin(waiting,engineChecking||held);spin(checkedAt,engineChecking||held);
    check.IsEnabled=!engineChecking&&!appChecking&&!appDownloading&&!held;
    restart.Visibility=ready!=null?Visibility.Visible:Visibility.Collapsed;
   };
   settle.Tick+=delegate {settle.Stop();refresh();};dialog.Closed+=delegate {settle.Stop();};
   Action changed=delegate {window.Dispatcher.BeginInvoke(refresh);};
   EngineUpdateChanged+=changed;dialog.Closed+=delegate {EngineUpdateChanged-=changed;updatesWindow=null;};
   refresh();dialog.Show();
  }
 }
}
