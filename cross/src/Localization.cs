using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace Mica {
 // Mica's own words in another language, as on Windows (src/Localization.cs) and from the same table
 // (src/Strings.vi.tsv). The English stays in the code; every text, button label and tooltip is translated as it is
 // set, so a card built anywhere comes out in the chosen language with no change where it's built. Each control keeps
 // its English, so switching language redraws what's open without a restart. Typed text and page titles are the
 // user's and never translated (a TextBox's text isn't a TextBlock's; tab titles carry the "user" class).
 static class Localizer {
  public static string Language {get; private set;}
  static readonly Dictionary<string,string> exact=new Dictionary<string,string>(StringComparer.Ordinal);
  static List<KeyValuePair<string,string>> prefixes=new List<KeyValuePair<string,string>>(),suffixes=new List<KeyValuePair<string,string>>();
  static readonly List<Tuple<Regex,string>> patterns=new List<Tuple<Regex,string>>();
  static bool hooked,applying;
  static Localizer() {Language="en";}
  public static bool Vietnamese {get {return Language=="vi";}}
  // "en", "vi", or null for the language the system uses.
  public static string Resolve(string chosen) {
   string forced=Environment.GetEnvironmentVariable("MICA_LANGUAGE");if(forced=="vi"||forced=="en")return forced;
   if(chosen=="vi"||chosen=="en")return chosen;
   return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="vi"?"vi":"en";
  }
  public static void Use(string language) {
   Language=language=="vi"?"vi":"en";
   if(Vietnamese&&exact.Count==0)Load("Mica.Strings.vi.tsv");
   Hook();
  }
  // "Sunday, October 25" or "Chủ Nhật, 25 tháng 10".
  public static string LongDate(DateTime when) {
   if(!Vietnamese)return when.ToString("dddd, MMMM d",System.Globalization.CultureInfo.InvariantCulture);
   return when.ToString("dddd",new System.Globalization.CultureInfo("vi-VN"))+", "+when.Day+" tháng "+when.Month;
  }
  // The table as JSON, for Mica's own web pages (the new tab page, History, Downloads), which translate themselves.
  public static string PageTable(string html) {
   if(!Vietnamese)return "null";
   var used=new Dictionary<string,string>();
   foreach(var pair in exact)if(pair.Key.Length>1&&html.IndexOf(pair.Key.Trim(),StringComparison.Ordinal)>=0)used[pair.Key.Trim()]=pair.Value.Trim();
   return new System.Web.Script.Serialization.JavaScriptSerializer {MaxJsonLength=int.MaxValue}.Serialize(used);
  }

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
      if(key.EndsWith(" ")&&!value.EndsWith(" "))value+=" ";if(key.StartsWith(" ")&&!value.StartsWith(" "))value=" "+value;
      exact[key]=value;
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
   foreach(var pattern in patterns){var match=pattern.Item1.Match(text);if(match.Success)return Regex.Replace(pattern.Item2,@"\$(t?)(\d)",m=>{var g=match.Groups[int.Parse(m.Groups[2].Value)].Value;return m.Groups[1].Value=="t"?Translate(g):g;});}
   foreach(var prefix in prefixes)if(text.StartsWith(prefix.Key,StringComparison.Ordinal))return prefix.Value+text.Substring(prefix.Key.Length);
   foreach(var suffix in suffixes)if(text.EndsWith(suffix.Key,StringComparison.Ordinal))return text.Substring(0,text.Length-suffix.Key.Length)+suffix.Value;
   if(text.IndexOf('\n')>=0){var lines=text.Split('\n');var done=lines.Select(Translate).ToArray();if(!done.SequenceEqual(lines))return string.Join("\n",done);}
   // Section headings are the same words in capitals ("SITES" for "Sites").
   if(text.Length>2&&text==text.ToUpperInvariant()&&text.Any(char.IsLetter)) {
    string word=text.Substring(0,1)+text.Substring(1).ToLowerInvariant();
    if(exact.TryGetValue(word,out found))return found.ToUpper(new System.Globalization.CultureInfo("vi-VN"));
   }
   return text;
  }

  // ---- Interface ----
  // The English each control was given, per property: [English, last shown].
  static readonly ConditionalWeakTable<AvaloniaObject,Dictionary<AvaloniaProperty,string[]>> originals=new ConditionalWeakTable<AvaloniaObject,Dictionary<AvaloniaProperty,string[]>>();
  static void Hook() {
   if(hooked||!Vietnamese)return;hooked=true;
   TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>(delegate(TextBlock t,AvaloniaPropertyChangedEventArgs e) {if(!Skip(t))Set(t,TextBlock.TextProperty,e.NewValue as string);});
   ContentControl.ContentProperty.Changed.AddClassHandler<ContentControl>(delegate(ContentControl c,AvaloniaPropertyChangedEventArgs e) {if(e.NewValue is string&&!(c is Window))Set(c,ContentControl.ContentProperty,(string)e.NewValue);});
   HeaderedSelectingItemsControl.HeaderProperty.Changed.AddClassHandler<MenuItem>(delegate(MenuItem m,AvaloniaPropertyChangedEventArgs e) {if(e.NewValue is string)Set(m,MenuItem.HeaderProperty,(string)e.NewValue);});
   ToolTip.TipProperty.Changed.AddClassHandler<Control>(delegate(Control c,AvaloniaPropertyChangedEventArgs e) {if(e.NewValue is string)Set(c,ToolTip.TipProperty,(string)e.NewValue);});
   TextBox.WatermarkProperty.Changed.AddClassHandler<TextBox>(delegate(TextBox t,AvaloniaPropertyChangedEventArgs e) {if(e.NewValue is string)Set(t,TextBox.WatermarkProperty,(string)e.NewValue);});
  }
  static bool Skip(TextBlock text) {
   if(text.Classes.Contains("user"))return true;
   for(var node=text.Parent;node!=null;node=node.Parent)if(node is TextBox||(node is StyledElement&&((StyledElement)node).Classes.Contains("user")))return true;
   return false;
  }
  static void Set(AvaloniaObject target,AvaloniaProperty property,string current) {
   if(applying||current==null)return;
   var known=originals.GetOrCreateValue(target);string[] entry;
   if(!known.TryGetValue(property,out entry)||entry[1]!=current)known[property]=entry=new[]{current,current};
   string wanted=Vietnamese?Translate(entry[0]):entry[0];entry[1]=wanted;
   if(wanted==current)return;
   applying=true;try {target.SetValue(property,wanted);}finally {applying=false;}
  }
  // Switching language: everything on screen is said again in the new one.
  public static void Redraw(Visual root) {
   if(!hooked&&Vietnamese)Hook();
   foreach(var v in new[]{root}.Concat(root.GetVisualDescendants())) {
    var t=v as TextBlock;if(t!=null&&!Skip(t))Set(t,TextBlock.TextProperty,t.Text);
    var c=v as ContentControl;if(c!=null&&c.Content is string&&!(c is Window))Set(c,ContentControl.ContentProperty,(string)c.Content);
    var control=v as Control;if(control!=null&&ToolTip.GetTip(control) is string)Set(control,ToolTip.TipProperty,(string)ToolTip.GetTip(control));
   }
  }
 }
}
