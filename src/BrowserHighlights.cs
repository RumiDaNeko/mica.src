using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // Highlights and notes: select text, right-click › Highlight. Highlights are kept per page on this device and
 // come back when you return; Highlights and notes lists them with a note each, and the other pages you marked.
 partial class BrowserApp {
  sealed class Highlight {public string Id {get;set;} public string Text {get;set;} public string Note {get;set;} public string Color {get;set;} public DateTime Created {get;set;} public string Title {get;set;}}
  static readonly string[] HighlightColors={"#FFE26A","#9EE6A8","#9CCBFF","#FFB3C7"};
  string HighlightsPath {get {return Path.Combine(profile,"highlights.json");}}
  Dictionary<string,List<Highlight>> highlights;
  Dictionary<string,List<Highlight>> Highlights() {
   if(highlights!=null)return highlights;
   try {if(!privateMode&&File.Exists(HighlightsPath))highlights=new JavaScriptSerializer {MaxJsonLength=int.MaxValue}.Deserialize<Dictionary<string,List<Highlight>>>(File.ReadAllText(HighlightsPath));}catch(Exception){}
   if(highlights==null)highlights=new Dictionary<string,List<Highlight>>();return highlights;
  }
  void SaveHighlights() {
   if(privateMode)return;
   try {foreach(var key in highlights.Keys.Where(k=>highlights[k]==null||highlights[k].Count==0).ToList())highlights.Remove(key);string temp=HighlightsPath+".tmp";File.WriteAllText(temp,new JavaScriptSerializer {MaxJsonLength=int.MaxValue}.Serialize(highlights));if(File.Exists(HighlightsPath))File.Replace(temp,HighlightsPath,null);else File.Move(temp,HighlightsPath);}catch(Exception e){Status("Couldn't save highlights: "+e.Message);}
  }
  // The page a highlight belongs to: its address without the #fragment.
  static string HighlightKey(string url) {if(url==null)return null;int hash=url.IndexOf('#');return hash>=0?url.Substring(0,hash):url;}
  List<Highlight> PageHighlights(string url,bool create) {
   var all=Highlights();string key=HighlightKey(url);List<Highlight> list;
   if(key==null)return new List<Highlight>();if(!all.TryGetValue(key,out list)){list=new List<Highlight>();if(create)all[key]=list;}return list;
  }

  const string HighlightLib=@"
const micaWrap=(range,id,color)=>{let root=range.commonAncestorContainer;if(root.nodeType===3)root=root.parentNode;const nodes=[];
 const w=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);let n;while(n=w.nextNode()){if(range.intersectsNode(n)&&n.data.trim())nodes.push(n);}
 const start=range.startContainer,so=range.startOffset,end=range.endContainer,eo=range.endOffset;let count=0;
 for(const t of nodes){const s=t===start?so:0,e=t===end?eo:t.data.length;if(s>=e)continue;let node=t;if(e<node.data.length)node.splitText(e);if(s>0)node=node.splitText(s);
  const m=document.createElement('mark');m.dataset.micaHl=id;m.style.cssText='background:'+color+';color:inherit;border-radius:2px;padding:0;box-shadow:0 0 0 1px '+color;node.parentNode.insertBefore(m,node);m.appendChild(node);count++;}
 return count>0;};
const micaFind=(q)=>{const walker=document.createTreeWalker(document.body,NodeFilter.SHOW_TEXT,{acceptNode:n=>/^(SCRIPT|STYLE|NOSCRIPT|TEXTAREA)$/.test(n.parentNode.nodeName)?2:1});
 let text='',map=[],n,space=true;while(n=walker.nextNode()){const d=n.data;for(let i=0;i<d.length;i++){const c=d[i];if(/\s/.test(c)){if(space)continue;space=true;text+=' ';map.push([n,i]);}else{space=false;text+=c;map.push([n,i]);}}}
 const at=text.indexOf(q);if(at<0)return null;const a=map[at],b=map[at+q.length-1];const r=document.createRange();r.setStart(a[0],a[1]);r.setEnd(b[0],b[1]+1);return r;};";
  static string MarkScript(string id,string color) {
   var j=new JavaScriptSerializer();
   return "(()=>{"+HighlightLib+"const s=getSelection();if(!s||s.isCollapsed||!s.rangeCount)return null;const r=s.getRangeAt(0);const text=s.toString().replace(/\\s+/g,' ').trim();if(!text||text.length>5000)return null;if(!micaWrap(r,"+j.Serialize(id)+","+j.Serialize(color)+"))return null;s.removeAllRanges();return text;})()";
  }
  static string RestoreScript(List<Highlight> list) {
   var j=new JavaScriptSerializer();var items=list.Select(h=>new Dictionary<string,object>{{"id",h.Id},{"text",h.Text},{"color",h.Color??HighlightColors[0]}}).ToList();
   return "(()=>{"+HighlightLib+"let done=0;for(const h of "+j.Serialize(items)+"){if(document.querySelector('[data-mica-hl=\"'+h.id+'\"]')){done++;continue;}const q=h.text.replace(/\\s+/g,' ').trim();const r=q&&micaFind(q);if(r&&micaWrap(r,h.id,h.color+'99'))done++;}return done;})()";
  }

  internal async void HighlightSelection(PageEntry tab,WebView2 view) {
   var core=LiveCore(view);if(core==null||tab==null||!Navigation.IsWeb(tab.Url))return;
   string id=Guid.NewGuid().ToString("N").Substring(0,12),color=HighlightColors[0];string raw;
   try {raw=await core.ExecuteScriptAsync(MarkScript(id,color+"99"));}catch(Exception){Status("Couldn't highlight that.");return;}
   string text=raw=="null"?null:new JavaScriptSerializer().Deserialize<string>(raw);
   if(string.IsNullOrEmpty(text)){Status("Select some text first, then choose Highlight.");return;}
   PageHighlights(core.Source,true).Add(new Highlight {Id=id,Text=text,Color=color,Created=DateTime.UtcNow,Title=tab.Title});SaveHighlights();
   Status(privateMode?"Highlighted (a private window forgets it when it closes).":"Highlighted. It'll be here when you come back.");
  }
  // After each page load: put back what you marked there.
  async void RestoreHighlights(WebView2 view) {
   var core=LiveCore(view);if(core==null)return;var list=PageHighlights(core.Source,false);if(list.Count==0)return;
   try {await core.ExecuteScriptAsync(RestoreScript(list));}catch(Exception){}
  }

  Popup highlightsPopup;
  internal void ShowHighlights() {
   CloseMenus();if(highlightsPopup==null){highlightsPopup=CreatePopup("HighlightsPopup",380);highlightsPopup.Placement=PlacementMode.Center;}
   RenderHighlights();highlightsPopup.PlacementTarget=Find<Grid>("Root");highlightsPopup.IsOpen=true;
  }
  void RenderHighlights() {
   var body=PopupBody(highlightsPopup);var tab=Active;var view=CurrentView();var core=view==null?null:LiveCore(view);
   string url=core!=null&&tab!=null&&Navigation.IsWeb(tab.Url)?core.Source:null;
   var head=DialogText("Highlights and notes",15,"Ink");head.FontWeight=FontWeights.SemiBold;head.Margin=new Thickness(14,12,14,2);body.Children.Add(head);
   var page=url==null?new List<Highlight>():PageHighlights(url,false);
   var about=DialogText(page.Count==0?"Nothing marked on this page. Select text, right-click and choose Highlight.":page.Count+(page.Count==1?" highlight":" highlights")+" on this page.",11,"Muted");
   about.TextWrapping=TextWrapping.Wrap;about.Margin=new Thickness(14,0,14,8);body.Children.Add(about);
   foreach(var item in page.ToList()) {
    var h=item;var card=new Border {Margin=new Thickness(12,3,12,3),Padding=new Thickness(10,8,8,8),CornerRadius=new CornerRadius(6),BorderThickness=new Thickness(3,0,0,0),BorderBrush=new SolidColorBrush(ParseColor(h.Color??HighlightColors[0])),Tag="highlight:"+h.Id};
    card.SetResourceReference(Border.BackgroundProperty,"Hover");var stack=new StackPanel();card.Child=stack;
    var quote=new Button {Content=new TextBlock {Text=h.Text.Length>280?h.Text.Substring(0,280)+"…":h.Text,TextWrapping=TextWrapping.Wrap,FontSize=12},HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(0),Background=Brushes.Transparent,BorderThickness=new Thickness(0),ToolTip="Show it on the page",Tag="highlight-go:"+h.Id};
    quote.Click+=async delegate {try {await core.ExecuteScriptAsync("(()=>{const m=document.querySelector('[data-mica-hl=\""+h.Id+"\"]');if(m)m.scrollIntoView({block:'center',behavior:'smooth'});})()");}catch(Exception){}};
    stack.Children.Add(quote);
    var tools=new Grid {Margin=new Thickness(0,6,0,0)};tools.ColumnDefinitions.Add(new ColumnDefinition());tools.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
    var note=new TextBox {Text=h.Note??"",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxHeight=90,Padding=new Thickness(6,3,6,3),FontSize=11,Tag="highlight-note:"+h.Id,ToolTip="Your note"};
    note.LostKeyboardFocus+=delegate {string v=note.Text.Trim();if(v!=(h.Note??"")){h.Note=v.Length==0?null:v;SaveHighlights();}};tools.Children.Add(note);
    var remove=new Button {Content="",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=11,Width=28,Height=26,Padding=new Thickness(0),Margin=new Thickness(6,0,0,0),ToolTip="Remove the highlight",Tag="highlight-remove:"+h.Id,VerticalAlignment=VerticalAlignment.Top};
    remove.Click+=async delegate {
     page.Remove(h);SaveHighlights();
     try {await core.ExecuteScriptAsync("(()=>{for(const m of document.querySelectorAll('[data-mica-hl=\""+h.Id+"\"]')){const p=m.parentNode;while(m.firstChild)p.insertBefore(m.firstChild,m);p.removeChild(m);p.normalize();}})()");}catch(Exception){}
     RenderHighlights();
    };
    Grid.SetColumn(remove,1);tools.Children.Add(remove);stack.Children.Add(tools);body.Children.Add(card);
   }
   // Other pages you marked, newest first.
   var others=Highlights().Where(p=>p.Key!=HighlightKey(url)&&p.Value.Count>0).OrderByDescending(p=>p.Value.Max(h=>h.Created)).Take(12).ToList();
   if(others.Count>0) {
    MenuDivider(body);MenuHeading(body,"OTHER PAGES");
    foreach(var pair in others) {
     var target=pair.Key;var first=pair.Value[0];
     MenuCommand(body,highlightsPopup,"",string.IsNullOrWhiteSpace(first.Title)?Navigation.AddressLabel(target):first.Title,pair.Value.Count.ToString(),delegate {Navigate(target,true);});
    }
   }
   body.Children.Add(new Border {Height=8});
  }
 }
}
