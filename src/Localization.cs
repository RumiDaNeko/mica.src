using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Mica {
 // Mica's own words in another language. The English stays in the code; a built-in table (Strings.vi.tsv)
 // maps each phrase to its translation, and every piece of interface is translated as it appears on screen,
 // so a window, menu or card built anywhere comes out in the chosen language with no change where it's built.
 // Each element keeps its English, so switching language redraws what's open without a restart.
 static class Localizer {
  public static string Language {get; private set;}
  static Dictionary<string,string> exact=new Dictionary<string,string>(StringComparer.Ordinal);
  static List<KeyValuePair<string,string>> prefixes=new List<KeyValuePair<string,string>>(),suffixes=new List<KeyValuePair<string,string>>();
  static List<Tuple<Regex,string>> patterns=new List<Tuple<Regex,string>>();
  static bool hooked,everTranslated;
  static readonly SortedSet<string> misses=new SortedSet<string>(StringComparer.Ordinal);
  static string missLog;

  static Localizer() {Language="en";}
  public static bool Vietnamese {get {return Language=="vi";}}
  // "en", "vi", or null for the language Windows is set to.
  public static string Resolve(string chosen) {
   string forced=Environment.GetEnvironmentVariable("MICA_LANGUAGE");if(forced=="vi"||forced=="en")return forced;
   if(chosen=="vi"||chosen=="en")return chosen;
   return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="vi"?"vi":"en";
  }
  public static void Use(string language) {
   Language=language=="vi"?"vi":"en";
   if(Vietnamese&&exact.Count==0)Load("Mica.Strings.vi.tsv");
   if(Vietnamese)everTranslated=true;
   missLog=Environment.GetEnvironmentVariable("MICA_STRING_MISSES");
   var app=Application.Current;if(app!=null)app.Resources["SerifFont"]=new FontFamily(Vietnamese?"Cambria, Georgia":"Georgia, Cambria");
   Hook();
   if(app!=null)foreach(Window window in app.Windows)Walk(window);
  }
  // A serif face that has every Vietnamese letter for text that may be Vietnamese (page titles, articles):
  // Georgia lacks most of them, and mixing two faces inside one word looks broken.
  public static FontFamily SerifFor(string text) {return new FontFamily(Vietnamese||HasVietnamese(text)?"Cambria, Georgia":"Georgia, Cambria");}
  public static FontFamily Serif {get {return SerifFor(null);}}
  // "Sunday, October 25" or "Chủ Nhật, 25 tháng 10".
  public static string LongDate(DateTime when) {
   if(!Vietnamese)return when.ToString("dddd, MMMM d",System.Globalization.CultureInfo.InvariantCulture);
   return when.ToString("dddd",new System.Globalization.CultureInfo("vi-VN"))+", "+when.Day+" tháng "+when.Month;
  }
  static readonly Regex vietnameseLetters=new Regex("[ăđơưẠ-ỹĂĐƠƯ]",RegexOptions.Compiled);
  public static bool HasVietnamese(string text) {return text!=null&&vietnameseLetters.IsMatch(text);}

  static void Load(string resource) {
   using(var stream=typeof(Localizer).Assembly.GetManifestResourceStream(resource)) {
    if(stream==null)return;
    using(var reader=new StreamReader(stream,Encoding.UTF8)) {
     string line;
     while((line=reader.ReadLine())!=null) {
      if(line.Length==0||line.StartsWith("#"))continue;int tab=line.IndexOf('\t');if(tab<=0)continue;
      string key=line.Substring(0,tab),value=Unescape(line.Substring(tab+1));
      if(key.StartsWith("re:")){try {patterns.Add(Tuple.Create(new Regex("^"+key.Substring(3)+"$",RegexOptions.Compiled|RegexOptions.Singleline),value));}catch(ArgumentException){}continue;}
      key=Unescape(key);if(value.Length==0||key==value)continue;
      // A fragment's edge spaces join it to the rest; editors tend to drop them from the translation.
      if(key.EndsWith(" ")&&!value.EndsWith(" "))value+=" ";if(key.StartsWith(" ")&&!value.StartsWith(" "))value=" "+value;
      exact[key]=value;
      // A fragment that other text is joined to: "Couldn't save this session: " + the reason.
      if(key.Length>=6&&(key.EndsWith(" ")||key.EndsWith(": ")||key.EndsWith("\n")))prefixes.Add(new KeyValuePair<string,string>(key,value));
      else if(key.Length>=8&&key.StartsWith(" "))suffixes.Add(new KeyValuePair<string,string>(key,value));
     }
    }
   }
   prefixes=prefixes.OrderByDescending(p=>p.Key.Length).ToList();suffixes=suffixes.OrderByDescending(p=>p.Key.Length).ToList();
  }
  static string Unescape(string text) {
   if(text.IndexOf('\\')<0)return text;var output=new StringBuilder();
   for(int i=0;i<text.Length;i++) {
    char c=text[i];if(c!='\\'||i+1>=text.Length){output.Append(c);continue;}
    char n=text[++i];
    if(n=='n')output.Append('\n');else if(n=='t')output.Append('\t');else if(n=='r')output.Append('\r');
    else if(n=='u'&&i+4<text.Length){int code;if(int.TryParse(text.Substring(i+1,4),System.Globalization.NumberStyles.HexNumber,null,out code)){output.Append((char)code);i+=4;}else output.Append("\\u");}
    else output.Append(n);
   }
   return output.ToString();
  }

  public static string Translate(string text) {
   if(!Vietnamese||string.IsNullOrEmpty(text))return text;
   string found;if(exact.TryGetValue(text,out found))return found;
   string trimmed=text.Trim();if(trimmed.Length!=text.Length&&exact.TryGetValue(trimmed,out found))return text.Replace(trimmed,found);
   foreach(var pattern in patterns){var match=pattern.Item1.Match(text);if(match.Success)return TranslateGroups(match,pattern.Item2);}
   foreach(var prefix in prefixes)if(text.StartsWith(prefix.Key,StringComparison.Ordinal))return prefix.Value+text.Substring(prefix.Key.Length);
   foreach(var suffix in suffixes)if(text.EndsWith(suffix.Key,StringComparison.Ordinal))return text.Substring(0,text.Length-suffix.Key.Length)+suffix.Value;
   // Lines joined with newlines (tooltips, notes) translate line by line.
   if(text.IndexOf('\n')>=0){var lines=text.Split('\n');var done=lines.Select(Translate).ToArray();if(!done.SequenceEqual(lines))return string.Join("\n",done);}
   Missed(text);return text;
  }
  // "$1" in a pattern's translation is the matched text as is; "$t1" is that text translated too.
  static string TranslateGroups(Match match,string template) {
   return Regex.Replace(template,@"\$(t?)(\d)",m=>{var g=match.Groups[int.Parse(m.Groups[2].Value)].Value;return m.Groups[1].Value=="t"?Translate(g):g;});
  }
  static void Missed(string text) {
   if(missLog==null||HasVietnamese(text)||!Regex.IsMatch(text,"[A-Za-z]{3}"))return;
   lock(misses){if(misses.Add(text))try {File.WriteAllLines(missLog,misses,new UTF8Encoding(false));}catch(IOException){}}
  }

  // ---- Interface ----
  // The English each element was given, per property, so it can be translated again or put back.
  static readonly ConditionalWeakTable<DependencyObject,Dictionary<DependencyProperty,string[]>> originals=new ConditionalWeakTable<DependencyObject,Dictionary<DependencyProperty,string[]>>();
  static readonly DependencyPropertyDescriptor textChanges=DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock));
  static readonly DependencyPropertyDescriptor contentChanges=DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty,typeof(ContentControl));
  static readonly DependencyPropertyDescriptor headerChanges=DependencyPropertyDescriptor.FromProperty(HeaderedItemsControl.HeaderProperty,typeof(HeaderedItemsControl));
  static readonly DependencyPropertyDescriptor tipChanges=DependencyPropertyDescriptor.FromProperty(FrameworkElement.ToolTipProperty,typeof(FrameworkElement));
  static bool applying;

  // WPF only tells elements that asked about being loaded, so the first sign of an element is its first
  // size. From then on it is watched while it's on screen (popups and menus come and go).
  static readonly ConditionalWeakTable<FrameworkElement,object> seen=new ConditionalWeakTable<FrameworkElement,object>();
  static void Hook() {
   if(hooked||!everTranslated)return;hooked=true;
   EventManager.RegisterClassHandler(typeof(FrameworkElement),FrameworkElement.SizeChangedEvent,new SizeChangedEventHandler(delegate(object sender,SizeChangedEventArgs e) {First(sender as FrameworkElement);}),true);
   EventManager.RegisterClassHandler(typeof(FrameworkElement),FrameworkElement.ToolTipOpeningEvent,new ToolTipEventHandler(delegate(object sender,ToolTipEventArgs e) {var element=sender as FrameworkElement;if(element!=null&&element.ToolTip is string&&!Skip(element))Set(element,FrameworkElement.ToolTipProperty,(string)element.ToolTip);}),true);
  }
  static void First(FrameworkElement element) {
   object known;if(element==null||seen.TryGetValue(element,out known))return;seen.Add(element,true);
   element.Loaded+=delegate {Apply(element,true);};element.Unloaded+=delegate {Unwatch(element);};
   Apply(element,true);
  }
  static void Walk(DependencyObject root) {
   if(root==null)return;var element=root as FrameworkElement;if(element!=null)Apply(element,element.IsLoaded);
   int count=0;try {count=VisualTreeHelper.GetChildrenCount(root);}catch(InvalidOperationException){}
   for(int i=0;i<count;i++)Walk(VisualTreeHelper.GetChild(root,i));
   foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().Where(c=>!(c is Visual)))Walk(child);
  }
  static bool Skip(FrameworkElement element) {
   // Typed text and page content are the user's, never translated.
   for(DependencyObject node=element;node!=null;node=BrowserApp.TreeParent(node)) {
    if(node is TextBox||node is PasswordBox||node is RichTextBox)return true;
    var tagged=node as FrameworkElement;if(tagged!=null&&tagged.Tag is string&&((string)tagged.Tag).StartsWith("tab:"))return true;
    if(node is Window)break;
   }
   return false;
  }
  static void Apply(FrameworkElement element,bool watch) {
   if(element==null||applying||!everTranslated)return;
   if(Skip(element))return;
   var text=element as TextBlock;
   if(text!=null&&(text.Inlines.Count<=1)){Set(text,TextBlock.TextProperty,text.Text);if(watch)Watch(text,textChanges);}
   var content=element as ContentControl;
   if(content!=null&&!(element is Window)&&content.Content is string){Set(content,ContentControl.ContentProperty,(string)content.Content);if(watch)Watch(content,contentChanges);}
   var header=element as HeaderedItemsControl;
   if(header!=null&&header.Header is string){Set(header,HeaderedItemsControl.HeaderProperty,(string)header.Header);if(watch)Watch(header,headerChanges);}
   var window=element as Window;
   if(window!=null&&window!=Application.Current.MainWindow&&!(window.Title??"").Contains(" — Mica"))Set(window,Window.TitleProperty,window.Title);
   if(element.ToolTip is string){Set(element,FrameworkElement.ToolTipProperty,(string)element.ToolTip);if(watch)Watch(element,tipChanges);}
   string name=AutomationProperties.GetName(element);if(!string.IsNullOrEmpty(name))Set(element,AutomationProperties.NameProperty,name);
  }
  static void Set(DependencyObject target,DependencyProperty property,string current) {
   if(current==null)return;
   // Per property: the English, and what was last shown. A value other than the last shown is new English from the code.
   var known=originals.GetOrCreateValue(target);string[] entry;
   if(!known.TryGetValue(property,out entry)||entry[1]!=current)known[property]=entry=new[]{current,current};
   string wanted=Vietnamese?Translate(entry[0]):entry[0];entry[1]=wanted;
   if(wanted==current)return;
   applying=true;try {target.SetCurrentValue(property,wanted);}finally {applying=false;}
  }
  // Text set later (a status line, a counter, a button that changes its label) is translated as it changes.
  static readonly ConditionalWeakTable<DependencyObject,List<DependencyPropertyDescriptor>> watched=new ConditionalWeakTable<DependencyObject,List<DependencyPropertyDescriptor>>();
  static void Watch(FrameworkElement element,DependencyPropertyDescriptor descriptor) {
   var list=watched.GetOrCreateValue(element);if(list.Contains(descriptor))return;list.Add(descriptor);
   descriptor.AddValueChanged(element,Changed);
  }
  static void Unwatch(FrameworkElement element) {
   List<DependencyPropertyDescriptor> list;if(element==null||!watched.TryGetValue(element,out list))return;
   foreach(var descriptor in list)descriptor.RemoveValueChanged(element,Changed);list.Clear();
  }
  static void Changed(object sender,EventArgs e) {if(!applying)Apply(sender as FrameworkElement,false);}
 }

 static partial class Program {
  // Kept beside the main profile ("en" or "vi"); without it Mica follows the language Windows uses.
  internal static string LanguageFolder;
  internal static string ReadLanguage() {try {return File.ReadAllText(Path.Combine(LanguageFolder,"language")).Trim();}catch(Exception){return null;}}
  internal static void WriteLanguage(string language) {try {Directory.CreateDirectory(LanguageFolder);File.WriteAllText(Path.Combine(LanguageFolder,"language"),language);}catch(IOException){}catch(UnauthorizedAccessException){}}
 }
 partial class BrowserApp {
  // Its own name, so it can be found in either language.
  static string LanguageHint() {return Localizer.Vietnamese?"Tiếng Việt":"English";}
  internal void SwitchLanguage(string language) {
   Program.WriteLanguage(language);Localizer.Use(language);
   foreach(var browser in Program.Browsers){browser.menuKey=null;browser.RenderTabs();browser.UpdateNavigation();}
   Status(Localizer.Vietnamese?"Mica giờ dùng tiếng Việt.":"Mica is in English now.");
  }
 }
}
