using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
namespace Mica {
 partial class BrowserApp {
  readonly Dictionary<string,Window> toolWindows=new Dictionary<string,Window>();
  Window ToolWindow(string key,string title,double width,double height,out StackPanel body,out StackPanel actions) {
   Window existing;if(toolWindows.TryGetValue(key,out existing)){existing.Activate();body=null;actions=null;return null;}
   var dialog=CreateMicaDialog(window,title,width,height,out body,out actions);toolWindows[key]=dialog;
   foreach(var text in VaultElements<TextBlock>(dialog))if((text.Tag as string)=="dialog-subtitle")text.Text="Browser tools";
   dialog.Closed+=delegate {toolWindows.Remove(key);};return dialog;
  }
  Button ToolAction(Panel panel,string text,string glyph,Action action) {
   var button=new Button {Padding=new Thickness(12,8,14,8),Margin=new Thickness(0,4,8,4),Tag=text};button.SetResourceReference(Control.BackgroundProperty,"Hover");
   var row=new StackPanel {Orientation=Orientation.Horizontal};var icon=ContextIcon(glyph);icon.Margin=new Thickness(0,0,8,0);row.Children.Add(icon);row.Children.Add(DialogText(text,12,"Ink"));button.Content=row;
   System.Windows.Automation.AutomationProperties.SetName(button,text);button.Click+=delegate {action();};panel.Children.Add(button);return button;
  }
  TextBox ToolField(Panel panel,string label,string value) {
   var heading=DialogText(label,11,"Muted");heading.Margin=new Thickness(0,10,0,6);panel.Children.Add(heading);
   var field=new TextBox {Text=value,Margin=new Thickness(0,0,0,8)};System.Windows.Automation.AutomationProperties.SetName(field,label);panel.Children.Add(field);return field;
  }
  WebView2 ToolPage() {var view=CurrentView();if(view==null)Status("Open a website to use this tool.");return view;}
  void ClosePageTools(bool navigation=false) {foreach(string key in navigation?new[]{"find","print","cast"}:new[]{"find","print","devtools","cast"}){Window w;if(toolWindows.TryGetValue(key,out w))w.Close();}}
  void CloseTools() {foreach(var w in toolWindows.Values.ToArray())w.Close();}
  // A regular tool card, made compact and grown out of the island like Find.
  void DockTool(Window dialog) {var card=(Border)dialog.Content;card.Padding=new Thickness(18,14,18,16);DockToIsland(dialog,card,(FrameworkElement)card.Child);}
  void CompactFind(Window dialog,StackPanel body,TextBox input,TextBlock count,StackPanel options,StackPanel actions) {
   var resources=body.Resources;body.Children.Remove(input);body.Children.Remove(count);body.Children.Remove(options);((Grid)actions.Parent).Children.Remove(actions);
   dialog.Width=430;dialog.Height=90;dialog.WindowStartupLocation=WindowStartupLocation.Manual;
   var surface=new Border {CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),Padding=new Thickness(12),Resources=resources};surface.Resources.MergedDictionaries.Add(DialogStyles.Create());surface.SetResourceReference(Border.BackgroundProperty,"Canvas");surface.SetResourceReference(Border.BorderBrushProperty,"Line");dialog.Content=surface;
   var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition());layout.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});surface.Child=layout;
   var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(60)});row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});layout.Children.Add(row);
   input.Margin=new Thickness(0);input.Height=32;input.VerticalAlignment=VerticalAlignment.Center;input.Padding=new Thickness(9,6,9,6);row.Children.Add(input);count.FontSize=11;count.Text="0 / 0";count.HorizontalAlignment=HorizontalAlignment.Center;count.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(count,1);row.Children.Add(count);
   actions.Margin=new Thickness(0);actions.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(actions,2);row.Children.Add(actions);
   int index=0;foreach(Button button in actions.Children){button.Content=ContextIcon(index++==0?"\uE70E":"\uE70D");button.ToolTip=button.Tag;button.Width=28;button.Height=28;button.Padding=new Thickness(0);button.Margin=new Thickness(1,0,1,0);}
   var close=new Button {Content=ContextIcon("\uE8BB"),Width=28,Height=28,Padding=new Thickness(0),ToolTip="Close find (Esc)"};close.Click+=delegate {dialog.Close();};actions.Children.Add(close);
   options.Margin=new Thickness(2,10,0,0);System.Windows.Documents.TextElement.SetFontSize(options,11);Grid.SetRow(options,1);layout.Children.Add(options);
   surface.SetResourceReference(Border.BackgroundProperty,"PopupPanel");surface.SetResourceReference(Border.BorderBrushProperty,"PopupLine");DockToIsland(dialog,surface,layout);
  }
  async void ShowFind() {
   var view=ToolPage();if(view==null)return;StackPanel body,actions;var dialog=ToolWindow("find","Find in page",470,335,out body,out actions);if(dialog==null)return;
   var input=ToolField(body,"Find text","");var count=DialogText("0 / 0",12,"Muted");body.Children.Add(count);
   var options=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,12,0,0)};body.Children.Add(options);
   var matchCase=new CheckBox {Content="Match case",Margin=new Thickness(0,0,18,0)};var word=new CheckBox {Content="Whole word"};options.Children.Add(matchCase);options.Children.Add(word);
   bool live=true,running=false;int revision=0;CoreWebView2Find finder=null;var delay=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(180)};
   Action update=delegate {if(live&&finder!=null)count.Text=finder.MatchCount==0?"0 / 0":Math.Max(1,finder.ActiveMatchIndex)+" / "+finder.MatchCount;};
   EventHandler<object> changed=delegate {update();};
   Func<Task> search=async delegate {
    if(!live||running||finder==null)return;running=true;int current=revision;
    try {var settings=environment.CreateFindOptions();settings.FindTerm=input.Text;settings.IsCaseSensitive=matchCase.IsChecked==true;settings.ShouldMatchWord=word.IsChecked==true;settings.ShouldHighlightAllMatches=true;settings.SuppressDefaultFindDialog=true;finder.Stop();if(input.Text.Length>0){await finder.StartAsync(settings);if(live)update();}else count.Text="0 / 0";}
    catch(Exception){if(live)count.Text="Search is unavailable on this page.";}
    finally {running=false;if(live&&current!=revision)delay.Start();}
   };
   delay.Tick+=async delegate {delay.Stop();await search();};Action schedule=delegate {revision++;delay.Stop();delay.Start();};input.TextChanged+=delegate {schedule();};matchCase.Click+=delegate {schedule();};word.Click+=delegate {schedule();};
   ToolAction(actions,"Previous","\uE70E",delegate {if(finder!=null)finder.FindPrevious();});ToolAction(actions,"Next","\uE70D",delegate {if(finder!=null)finder.FindNext();});
   input.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter&&finder!=null){if((Keyboard.Modifiers&ModifierKeys.Shift)!=0)finder.FindPrevious();else finder.FindNext();e.Handled=true;}};
   dialog.Closed+=delegate {live=false;delay.Stop();if(finder!=null)try{finder.MatchCountChanged-=changed;finder.ActiveMatchIndexChanged-=changed;finder.Stop();}catch(Exception){}};
   CompactFind(dialog,body,input,count,options,actions);dialog.Show();input.Focus();
   try {finder=view.CoreWebView2.Find;finder.MatchCountChanged+=changed;finder.ActiveMatchIndexChanged+=changed;await search();}catch(Exception){count.Text="Find requires a supported engine and a text page.";}
  }
  void ShowPrint() {
   var view=ToolPage();if(view==null)return;StackPanel body,actions;var dialog=ToolWindow("print","Print this page",380,620,out body,out actions);if(dialog==null)return;
   body.Children.Add(DialogText(view.CoreWebView2.DocumentTitle,14,"Ink"));body.Children.Add(DialogText("Choose a printer or save a PDF. Uses the page's print layout.",12,"Muted"));
   var destinations=new List<string>{"Save as PDF"};try{destinations.AddRange(System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>());}catch(Exception){}
   // Destination: a dropdown of Save as PDF and every installed printer, the system default marked.
   int destination=0;body.Children.Add(DialogText("Destination",11,"Muted"));var chooser=new ComboBox {Margin=new Thickness(0,6,0,8),Padding=new Thickness(10,7,10,7),Tag="print-destination"};System.Windows.Automation.AutomationProperties.SetName(chooser,"Destination");
   string defaultPrinter=null;try {defaultPrinter=new System.Drawing.Printing.PrinterSettings().PrinterName;}catch(Exception){}
   foreach(var name in destinations)chooser.Items.Add(name==defaultPrinter?name+"  (default)":name);
   chooser.SelectedIndex=0;chooser.SelectionChanged+=delegate {destination=Math.Max(0,chooser.SelectedIndex);};body.Children.Add(chooser);
   var pages=ToolField(body,"Pages · blank for all, e.g. 1-3, 5","");var copies=ToolField(body,"Printer copies · 1–99 (PDF exports one document)","1");
   var landscape=new CheckBox {Content="Landscape",Margin=new Thickness(0,8,0,8)};var backgrounds=new CheckBox {Content="Include backgrounds",IsChecked=true,Margin=new Thickness(0,8,0,8)};var headers=new CheckBox {Content="Page headers and footers",Margin=new Thickness(0,8,0,8)};body.Children.Add(landscape);body.Children.Add(backgrounds);body.Children.Add(headers);
   var note=DialogText("Ready",12,"Muted");note.Margin=new Thickness(0,12,0,0);body.Children.Add(note);bool live=true;dialog.Closed+=delegate {live=false;};
   ToolAction(actions,"Print / save","\uE74E",async delegate {
    int number;if(!int.TryParse(copies.Text,out number)||number<1||number>99){note.Text="Choose 1–99 copies.";return;}
    if(!System.Text.RegularExpressions.Regex.IsMatch(pages.Text,@"^\s*(\d+\s*(-\s*\d+)?\s*(,\s*\d+\s*(-\s*\d+)?\s*)*)?$")){note.Text="Use page numbers or ranges such as 1-3, 5.";return;}
    try {
     var settings=environment.CreatePrintSettings();settings.Copies=number;settings.PageRanges=pages.Text;settings.Orientation=landscape.IsChecked==true?CoreWebView2PrintOrientation.Landscape:CoreWebView2PrintOrientation.Portrait;settings.ShouldPrintBackgrounds=backgrounds.IsChecked==true;settings.ShouldPrintHeaderAndFooter=headers.IsChecked==true;
     string path=null;if(destination==0){var picker=new Microsoft.Win32.SaveFileDialog {Filter="PDF document|*.pdf",DefaultExt=".pdf",FileName="Mica page.pdf",OverwritePrompt=true};if(picker.ShowDialog(dialog)!=true)return;path=picker.FileName;}else settings.PrinterName=destinations[destination];
     body.IsEnabled=false;actions.IsEnabled=false;note.Text="Preparing your document…";
     if(path!=null){bool saved=await view.CoreWebView2.PrintToPdfAsync(path,settings);if(live)note.Text=saved?"PDF saved.":"PDF could not be saved.";}
     else {var result=await view.CoreWebView2.PrintAsync(settings);if(live)note.Text=result==CoreWebView2PrintStatus.Succeeded?"Sent to printer.":"Printer returned: "+result;}
    }catch(Exception){if(live)note.Text="Printing failed. Check the page, page range and printer.";}finally{if(live){body.IsEnabled=true;actions.IsEnabled=true;}}
   });DockTool(dialog);dialog.Show();
  }
  sealed class BrowserTaskRow {public string Id,Label,Title,Status;public override string ToString(){return Label;}}
  // A list that draws each tab row itself, while its items stay plain BrowserTaskRow values.
  sealed class TaskList:ListBox {
   internal Func<object,object> Render;
   protected override void PrepareContainerForItemOverride(DependencyObject element,object item) {base.PrepareContainerForItemOverride(element,item);if(Render!=null)((ListBoxItem)element).Content=Render(item);}
  }
  // A line chart of recent samples: a soft filled area under an accent line, scaled to its largest value.
  static void DrawHistory(Canvas canvas,List<double> values,double floor) {
   canvas.Children.Clear();double w=canvas.ActualWidth,h=canvas.ActualHeight;if(w<2||h<2||values.Count<2)return;
   double max=Math.Max(floor,values.Max())*1.15;int slots=60;double step=w/(slots-1);double x0=w-step*(values.Count-1);
   var line=new PathFigure {IsClosed=false};var area=new PathFigure {IsClosed=true,StartPoint=new Point(x0,h)};
   for(int i=0;i<values.Count;i++) {
    var point=new Point(x0+i*step,h-values[i]/max*(h-4)-2);
    if(i==0)line.StartPoint=point;else line.Segments.Add(new LineSegment(point,true));
    area.Segments.Add(new LineSegment(point,true));
   }
   area.Segments.Add(new LineSegment(new Point(w,h),true));
   var fill=new System.Windows.Shapes.Path {Data=new PathGeometry(new[]{area}),Opacity=0.18};fill.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,"Accent");canvas.Children.Add(fill);
   var stroke=new System.Windows.Shapes.Path {Data=new PathGeometry(new[]{line}),StrokeThickness=1.6,StrokeLineJoin=PenLineJoin.Round};stroke.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"Accent");canvas.Children.Add(stroke);
  }
  void ShowTaskManager() {
   StackPanel body,actions;var dialog=ToolWindow("tasks","Browser task manager",900,640,out body,out actions);if(dialog==null)return;
   var layout=new Grid();layout.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(280)});layout.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(20)});layout.ColumnDefinitions.Add(new ColumnDefinition());body.Children.Add(layout);
   // Left: the tabs in this window.
   var left=new StackPanel();layout.Children.Add(left);
   left.Children.Add(DialogText("TABS IN THIS WINDOW",10,"Muted"));
   var tabs=new TaskList {Height=420,Margin=new Thickness(0,8,0,8),Background=Brushes.Transparent,BorderThickness=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch};tabs.SetResourceReference(Control.ForegroundProperty,"Ink");
   ScrollViewer.SetHorizontalScrollBarVisibility(tabs,ScrollBarVisibility.Disabled);left.Children.Add(tabs);
   tabs.Render=delegate(object item) {
    var row=(BrowserTaskRow)item;var tab=state.Tabs.FirstOrDefault(t=>t.Id==row.Id);
    var grid=new Grid {Margin=new Thickness(0,2,0,2)};grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(26)});grid.ColumnDefinitions.Add(new ColumnDefinition());
    if(tab!=null){var icon=CreateTabIcon(tab,16);icon.VerticalAlignment=VerticalAlignment.Top;icon.Margin=new Thickness(0,2,0,0);grid.Children.Add(icon);}
    var text=new StackPanel();Grid.SetColumn(text,1);grid.Children.Add(text);
    var title=DialogText(row.Title,12,"Ink");title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(title);
    var status=DialogText(row.Status,10,"Muted");status.Margin=new Thickness(0,2,0,0);text.Children.Add(status);
    return grid;
   };
   var note=DialogText("Select a tab to rest or close it. Pinned tabs are protected.",11,"Muted");note.TextWrapping=TextWrapping.Wrap;left.Children.Add(note);
   // Right: totals with their recent history, then each engine process as a bar.
   var right=new StackPanel();Grid.SetColumn(right,2);layout.Children.Add(right);
   var memoryHistory=new List<double>();var cpuHistory=new List<double>();
   Func<string,string,Tuple<TextBlock,Canvas>> chart=delegate(string label,string tag) {
    var card=new Border {CornerRadius=new CornerRadius(12),Padding=new Thickness(14,12,14,10),Margin=new Thickness(0,0,0,10)};card.SetResourceReference(Border.BackgroundProperty,"Panel");
    var stack=new StackPanel();card.Child=stack;
    var head=new Grid();head.Children.Add(DialogText(label,10,"Muted"));var value=DialogText("—",18,"Ink");value.FontWeight=FontWeights.SemiBold;value.HorizontalAlignment=HorizontalAlignment.Right;value.Tag=tag;head.Children.Add(value);stack.Children.Add(head);
    var canvas=new Canvas {Height=64,Margin=new Thickness(0,8,0,0),ClipToBounds=true};stack.Children.Add(canvas);
    right.Children.Add(card);return Tuple.Create(value,canvas);
   };
   var memoryChart=chart("MEMORY · LAST 2 MINUTES","task-memory");var cpuChart=chart("CPU · LAST 2 MINUTES","task-cpu");
   memoryChart.Item2.SizeChanged+=delegate {DrawHistory(memoryChart.Item2,memoryHistory,200);};cpuChart.Item2.SizeChanged+=delegate {DrawHistory(cpuChart.Item2,cpuHistory,5);};
   var procHead=new Grid {Margin=new Thickness(2,4,2,6)};procHead.Children.Add(DialogText("PROCESSES",10,"Muted"));var procHint=DialogText("working set · CPU, not per-tab",10,"Muted");procHint.HorizontalAlignment=HorizontalAlignment.Right;procHead.Children.Add(procHint);right.Children.Add(procHead);
   var processes=new StackPanel {Tag="task-processes"};
   var procScroll=new ScrollViewer {Content=processes,MaxHeight=190,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};right.Children.Add(procScroll);
   var samples=new Dictionary<int,Tuple<TimeSpan,DateTime>>();
   Func<Process,double> cpuOf=p=>{var now=DateTime.UtcNow;var cpu=p.TotalProcessorTime;Tuple<TimeSpan,DateTime> previous;double percent=-1;if(samples.TryGetValue(p.Id,out previous)&&(now-previous.Item2).TotalSeconds>0)percent=Math.Max(0,Math.Min(100,(cpu-previous.Item1).TotalSeconds/(now-previous.Item2).TotalSeconds/Environment.ProcessorCount*100));samples[p.Id]=Tuple.Create(cpu,now);return percent;};
   Action refresh=delegate {
    string selected=(tabs.SelectedItem as BrowserTaskRow)==null?null:((BrowserTaskRow)tabs.SelectedItem).Id;
    tabs.ItemsSource=state.Tabs.Select(t=>new BrowserTaskRow {Id=t.Id,Title=(t.Pinned?"★ ":"")+t.Title,Status=(views.ContainsKey(t.Id)?"Running":"Resting / internal")+(t.Id==state.ActiveId?" · Active":""),Label=(t.Pinned?"★  ":"")+t.Title+"   ·   "+(views.ContainsKey(t.Id)?"Running":"Resting / internal")+(t.Id==state.ActiveId?"   ·   Active":"")}).ToList();tabs.SelectedItem=tabs.Items.Cast<BrowserTaskRow>().FirstOrDefault(t=>t.Id==selected);
    var rows=new List<Tuple<string,int,double,double>>();
    using(var app=Process.GetCurrentProcess())rows.Add(Tuple.Create("Mica UI",app.Id,app.WorkingSet64/1048576d,cpuOf(app)));
    if(environment!=null)try{foreach(var info in environment.GetProcessInfos())try{using(var p=Process.GetProcessById(info.ProcessId))rows.Add(Tuple.Create(info.Kind.ToString(),info.ProcessId,p.WorkingSet64/1048576d,cpuOf(p)));}catch(ArgumentException){}catch(InvalidOperationException){}}catch(Exception){}
    double total=rows.Sum(r=>r.Item3),cpu=rows.Where(r=>r.Item4>=0).Sum(r=>r.Item4);
    memoryHistory.Add(total);if(memoryHistory.Count>60)memoryHistory.RemoveAt(0);
    if(rows.Any(r=>r.Item4>=0)){cpuHistory.Add(cpu);if(cpuHistory.Count>60)cpuHistory.RemoveAt(0);}
    memoryChart.Item1.Text=total.ToString("F0")+" MB";cpuChart.Item1.Text=rows.Any(r=>r.Item4>=0)?cpu.ToString("F1")+"%":"—";
    DrawHistory(memoryChart.Item2,memoryHistory,200);DrawHistory(cpuChart.Item2,cpuHistory,5);
    processes.Children.Clear();double biggest=Math.Max(1,rows.Max(r=>r.Item3));
    foreach(var r in rows.OrderByDescending(r=>r.Item3)) {
     var line=new Grid {Margin=new Thickness(2,3,2,3),Tag="proc-row",ToolTip="PID "+r.Item2};
     line.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(84)});line.ColumnDefinitions.Add(new ColumnDefinition());line.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(132)});
     var name=DialogText(r.Item1,11,"Ink");name.VerticalAlignment=VerticalAlignment.Center;line.Children.Add(name);
     var track=new Grid {Height=8,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)};Grid.SetColumn(track,1);
     var rail=new Border {CornerRadius=new CornerRadius(4)};rail.SetResourceReference(Border.BackgroundProperty,"Hover");track.Children.Add(rail);
     var bar=new Border {CornerRadius=new CornerRadius(4),HorizontalAlignment=HorizontalAlignment.Left};bar.SetResourceReference(Border.BackgroundProperty,"Accent");track.Children.Add(bar);
     double share=r.Item3/biggest;track.SizeChanged+=delegate {bar.Width=Math.Max(6,track.ActualWidth*share);};line.Children.Add(track);
     var figures=DialogText(r.Item3.ToString("F0")+" MB · "+(r.Item4>=0?r.Item4.ToString("F1")+"%":"—")+" · PID "+r.Item2,10,"Muted");figures.VerticalAlignment=VerticalAlignment.Center;figures.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(figures,2);line.Children.Add(figures);
     processes.Children.Add(line);
    }
   };
   ToolAction(actions,"Rest tab","\uE708",delegate {var row=tabs.SelectedItem as BrowserTaskRow;if(row==null)return;WebView2 v;if(row.Id==state.ActiveId){note.Text="Switch to another tab before resting this one.";return;}if(views.TryGetValue(row.Id,out v)&&LiveCore(v)!=null&&LiveCore(v).IsDocumentPlayingAudio){note.Text="This tab is playing audio.";return;}DisposeView(row.Id);RenderTabs();refresh();});
   ToolAction(actions,"Close tab","\uE8BB",delegate {var row=tabs.SelectedItem as BrowserTaskRow;if(row==null)return;var tab=state.Tabs.FirstOrDefault(t=>t.Id==row.Id);if(tab!=null&&tab.Pinned){note.Text="Unpin this tab before closing it.";return;}CloseTab(row.Id);refresh();});
   var poll=new DispatcherTimer {Interval=TimeSpan.FromSeconds(2)};poll.Tick+=delegate {refresh();};dialog.Closed+=delegate {poll.Stop();};refresh();dialog.Show();poll.Start();
  }
 }
}
