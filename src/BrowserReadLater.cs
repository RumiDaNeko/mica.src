using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // Read later: the reading-mode version of an article kept on this device, so it reads without a connection
 // (pictures still need one). A saved article opens as its own tab, mica://read/<id>, drawn by the reader
 // with no page behind it; the original is one click away.
 partial class BrowserApp {
  internal sealed class SavedArticle {public string Id {get;set;} public string Url {get;set;} public string Title {get;set;} public string Site {get;set;} public int Words {get;set;} public DateTime Saved {get;set;}}
  string ReadLaterFolder {get {return Path.Combine(profile,"ReadLater");}}
  string ReadLaterIndex {get {return Path.Combine(ReadLaterFolder,"index.json");}}
  static JavaScriptSerializer ArticleJson() {return new JavaScriptSerializer {MaxJsonLength=int.MaxValue};}
  internal List<SavedArticle> LoadReadLater() {
   try {if(File.Exists(ReadLaterIndex))return (ArticleJson().Deserialize<List<SavedArticle>>(File.ReadAllText(ReadLaterIndex))??new List<SavedArticle>()).Where(a=>a!=null&&BrowserProfiles_IsHex(a.Id)).ToList();}catch(Exception){}
   return new List<SavedArticle>();
  }
  static bool BrowserProfiles_IsHex(string id) {return IsProfileId(id);}
  void SaveReadLater(List<SavedArticle> list) {
   Directory.CreateDirectory(ReadLaterFolder);string temp=ReadLaterIndex+".tmp";File.WriteAllText(temp,ArticleJson().Serialize(list));
   if(File.Exists(ReadLaterIndex))File.Replace(temp,ReadLaterIndex,null);else File.Move(temp,ReadLaterIndex);
  }
  Dictionary<string,object> LoadArticle(string id) {
   try {string path=Path.Combine(ReadLaterFolder,id+".json");if(BrowserProfiles_IsHex(id)&&File.Exists(path))return ArticleJson().Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}catch(Exception){}
   return null;
  }

  // From the reader's bookmark button, the palette, or a page you're on (read as an article first).
  internal async void SaveForLater() {
   if(privateMode){Status("A private window doesn't keep anything, including saved articles.");return;}
   var tab=Active;var article=ReaderOpen&&readerTab==(tab==null?null:tab.Id)?readerArticle:null;
   if(tab!=null&&Navigation.IsReading(tab.Url)){Status("This article is already saved.");return;}
   if(article==null) {
    var view=CurrentView();var core=view==null?null:LiveCore(view);
    if(tab==null||!Navigation.IsWeb(tab.Url)||core==null){Status("Open an article to save it.");return;}
    try {
     string raw=await core.ExecuteScriptAsync(ReaderScript());article=ArticleJson().Deserialize<Dictionary<string,object>>(ArticleJson().Deserialize<string>(raw));
    }catch(Exception){article=null;}
    if(article==null||!(article["ok"] is bool)||!(bool)article["ok"]){Status("This page doesn't look like an article, so there's nothing to save for reading.");return;}
   }
   string url=article.ContainsKey("url")&&article["url"] is string?(string)article["url"]:tab.Url;
   var list=LoadReadLater();var existing=list.FirstOrDefault(a=>a.Url==url);
   var entry=existing??new SavedArticle {Id=Guid.NewGuid().ToString("N")};
   entry.Url=url;entry.Title=article["title"] as string??tab.Title;entry.Site=article.ContainsKey("site")?article["site"] as string:SiteFor(url);
   entry.Words=article.ContainsKey("words")&&article["words"] is int?(int)article["words"]:0;entry.Saved=DateTime.UtcNow;
   try {
    Directory.CreateDirectory(ReadLaterFolder);File.WriteAllText(Path.Combine(ReadLaterFolder,entry.Id+".json"),ArticleJson().Serialize(article));
    if(existing==null)list.Insert(0,entry);SaveReadLater(list);
   }catch(Exception e){Status("Couldn't save the article: "+e.Message);return;}
   Status(existing==null?"Saved for later. Find it in Read later, even offline.":"Updated the saved copy.");
  }
  internal void OpenSavedArticle(string id) {
   var entry=LoadReadLater().FirstOrDefault(a=>a.Id==id);if(entry==null){Status("That article is no longer saved.");return;}
   var open=state.Tabs.FirstOrDefault(t=>t.Url=="mica://read/"+id);
   if(open==null){open=state.AddTab("mica://read/"+id);open.Title=entry.Title;}
   if(readLaterPopup!=null)readLaterPopup.IsOpen=false;CloseSpotlight();SelectTab(open.Id);Save();
  }
  // From Activate, for a mica://read tab.
  void ShowSavedArticle(PageEntry tab) {
   string id=tab.Url.Substring(12);var article=LoadArticle(id);
   if(article==null){Status("That article is no longer saved.");tab.Url="mica://home";tab.Title="New tab";SelectTab(tab.Id);return;}
   var entry=LoadReadLater().FirstOrDefault(a=>a.Id==id);if(entry!=null&&tab.Title!=entry.Title){tab.Title=entry.Title;RenderTabs();}
   if(!(ReaderOpen&&readerTab==tab.Id))OpenReader(tab.Id,null,article);
   Find<TextBlock>("AddressText").Text="Saved · "+(entry!=null?entry.Site:"article");
  }
  void OpenOriginal(PageEntry tab) {
   var entry=tab==null?null:LoadReadLater().FirstOrDefault(a=>"mica://read/"+a.Id==tab.Url);if(entry==null)return;
   CloseReader();Navigate(entry.Url,false);
  }
  internal void RemoveSaved(string id) {
   var list=LoadReadLater();list.RemoveAll(a=>a.Id==id);
   try {SaveReadLater(list);File.Delete(Path.Combine(ReadLaterFolder,id+".json"));}catch(Exception){}
   foreach(var b in Program.Browsers.Where(b=>!b.closing&&b.profile==profile).ToList())foreach(var tab in b.state.Tabs.Where(t=>t.Url=="mica://read/"+id).ToList())b.CloseTab(tab.Id);
  }

  Popup readLaterPopup;
  internal void ShowReadLater() {
   CloseMenus();if(readLaterPopup==null){readLaterPopup=CreatePopup("ReadLaterPopup",380);readLaterPopup.Placement=PlacementMode.Center;}
   RenderReadLater();readLaterPopup.PlacementTarget=Find<Grid>("Root");readLaterPopup.IsOpen=true;
  }
  void RenderReadLater() {
   var body=PopupBody(readLaterPopup);var list=LoadReadLater();
   var head=DialogText("Read later",15,"Ink");head.FontWeight=FontWeights.SemiBold;head.Margin=new Thickness(14,12,14,2);body.Children.Add(head);
   var about=DialogText(list.Count==0?"Nothing saved yet. In reading mode (F9), the bookmark button keeps an article here to read offline.":list.Count+(list.Count==1?" article":" articles")+", kept on this device.",11,"Muted");
   about.TextWrapping=TextWrapping.Wrap;about.Margin=new Thickness(14,0,14,8);body.Children.Add(about);
   foreach(var article in list) {
    var a=article;var row=new Grid {Margin=new Thickness(6,1,6,1),Tag="saved:"+a.Id};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
    var text=new StackPanel();var title=DialogText(a.Title,12,"Ink");title.TextTrimming=TextTrimming.CharacterEllipsis;title.FontWeight=FontWeights.SemiBold;text.Children.Add(title);
    int minutes=Math.Max(1,(int)Math.Round(a.Words/230.0));text.Children.Add(DialogText((a.Site??"")+" · "+minutes+" min read · saved "+a.Saved.ToLocalTime().ToString("d MMM"),10,"Muted"));
    var open=new Button {Content=text,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(8,6,8,6),Tag="saved-open:"+a.Id,ToolTip=a.Url};open.Click+=delegate {OpenSavedArticle(a.Id);};row.Children.Add(open);
    var remove=new Button {Content="",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=10,Width=28,Height=28,Padding=new Thickness(0),ToolTip="Remove from Read later",Tag="saved-remove:"+a.Id,VerticalAlignment=VerticalAlignment.Center};
    remove.Click+=delegate {RemoveSaved(a.Id);RenderReadLater();};Grid.SetColumn(remove,1);row.Children.Add(remove);body.Children.Add(row);
   }
   body.Children.Add(new Border {Height=8});
  }

  // ---- Read aloud: Windows' voices read the article, the paragraph being read tinted ----
  Button readAloudButton;SpeechSynthesizer speech;readonly Dictionary<Prompt,Block> spoken=new Dictionary<Prompt,Block>();Block speaking;
  internal void ReadAloud() {
   if(speech!=null){StopReadAloud();return;}
   if(readerHost==null){ToggleReader();Status("Open reading mode, then press Read aloud.");return;}
   var viewer=LogicalTreeHelper.GetChildren(readerHost).OfType<FlowDocumentScrollViewer>().FirstOrDefault();if(viewer==null||viewer.Document==null)return;
   try {speech=new SpeechSynthesizer();speech.SetOutputToDefaultAudioDevice();}catch(Exception e){speech=null;Status("Windows can't read aloud here: "+e.Message);return;}
   string lang=readerArticle!=null&&readerArticle.ContainsKey("lang")?readerArticle["lang"] as string:null;
   try {
    var voice=speech.GetInstalledVoices().Where(v=>v.Enabled).FirstOrDefault(v=>!string.IsNullOrEmpty(lang)&&v.VoiceInfo.Culture.TwoLetterISOLanguageName==lang);
    if(voice!=null)speech.SelectVoice(voice.VoiceInfo.Name);
    else if(!string.IsNullOrEmpty(lang)&&lang!=speech.Voice.Culture.TwoLetterISOLanguageName)Status("No "+LanguageName(lang)+" voice is installed, so the default voice reads it. Add one in Windows Settings › Time & language › Speech.");
   }catch(Exception){}
   speech.Rate=0;spoken.Clear();
   foreach(var block in ReadableBlocks(viewer.Document)) {
    string text=new TextRange(block.ContentStart,block.ContentEnd).Text.Trim();if(text.Length<2)continue;
    var prompt=new Prompt(text);spoken[prompt]=block;speech.SpeakAsync(prompt);
   }
   var synth=speech;
   synth.SpeakStarted+=delegate(object sender,SpeakStartedEventArgs e) {window.Dispatcher.BeginInvoke(new Action(delegate {Block block;if(speech!=synth||!spoken.TryGetValue(e.Prompt,out block))return;Tint(block,true);block.BringIntoView();}));};
   synth.SpeakCompleted+=delegate(object sender,SpeakCompletedEventArgs e) {window.Dispatcher.BeginInvoke(new Action(delegate {Block block;if(speech!=synth)return;if(spoken.TryGetValue(e.Prompt,out block)&&block==speaking)Tint(block,false);spoken.Remove(e.Prompt);if(spoken.Count==0)StopReadAloud();}));};
   if(readAloudButton!=null){readAloudButton.Content="";readAloudButton.ToolTip="Stop reading aloud";}
   Status("Reading aloud. Press the button again to stop.");
  }
  static IEnumerable<Block> ReadableBlocks(FlowDocument document) {
   foreach(var block in document.Blocks) {
    var list=block as List;
    if(list!=null){foreach(var item in list.ListItems)foreach(var inner in item.Blocks)yield return inner;}
    else if(block is Paragraph)yield return block;
   }
  }
  void Tint(Block block,bool on) {
   if(speaking!=null&&speaking!=block)speaking.ClearValue(TextElement.BackgroundProperty);
   if(on){block.SetResourceReference(TextElement.BackgroundProperty,"Hover");speaking=block;}else {block.ClearValue(TextElement.BackgroundProperty);speaking=null;}
  }
  void StopReadAloud() {
   var synth=speech;speech=null;if(synth!=null){try {synth.SpeakAsyncCancelAll();}catch(Exception){}var done=Task.Delay(300).ContinueWith(t=>{try {synth.Dispose();}catch(Exception){}});}
   spoken.Clear();if(speaking!=null){speaking.ClearValue(TextElement.BackgroundProperty);speaking=null;}
   if(readAloudButton!=null){readAloudButton.Content="";readAloudButton.ToolTip="Read aloud";}
  }
 }
}
