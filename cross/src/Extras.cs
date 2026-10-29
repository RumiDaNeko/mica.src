using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Xilium.CefGlue;

namespace Mica {
 // The rest of the Windows toolbar and menu: reading mode, the extensions card, the site card, private windows,
 // Personalize (the frame colour) and the task manager.
 sealed partial class BrowserWindow {
  Button readerButton,extensionsButton;
  readonly Border privateBadge=new Border {IsVisible=false,CornerRadius=new CornerRadius(4),Padding=new Thickness(6,2),Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center};

  void BuildExtras(StackPanel right) {
   readerButton=IconButton(Glyphs.Reader,"Reading mode (F9)",15);readerButton.Click+=delegate {ToggleReader();};
   extensionsButton=IconButton(Glyphs.Puzzle,"Extensions",15);extensionsButton.Click+=delegate {ShowExtensions();};
   int at=right.Children.IndexOf(bookmark);right.Children.Insert(at,extensionsButton);right.Children.Insert(at,readerButton);
   Res(privateBadge,Border.BackgroundProperty,"Hover");privateBadge.Child=Res(new TextBlock {Text="PRIVATE",FontSize=9},TextBlock.ForegroundProperty,"Accent");
   privateBadge.IsVisible=Program.Private;
   siteGlyph.Cursor=new Cursor(StandardCursorType.Hand);
   siteGlyph.PointerPressed+=delegate(object s,PointerPressedEventArgs e) {e.Handled=true;ShowSiteCard();};
   ApplyFrame();
  }

  // ── Reading mode (F9): Reader.js (shared with Windows) finds the article; Mica lays it over the page in a quiet
  // column of its own, inside the page so nothing has to sit over Chromium's view. Esc or F9 goes back.
  static string readerScript;
  static string ReaderScript() {
   if(readerScript==null)using(var reader=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Mica.Reader.js")))readerScript=reader.ReadToEnd();
   return readerScript;
  }
  async void ToggleReader() {
   var tab=Active;if(tab==null||tab.View==null||!Navigation.IsWeb(tab.Entry.Url)){Status("Open an article to read it here.");return;}
   string open=await EvalAsync(tab.View,"Promise.resolve(String(!!document.getElementById('mica-reader')))",5);
   if(open=="true"){tab.View.ExecuteJavaScript("document.getElementById('mica-reader').remove();document.documentElement.style.overflow=''","",1);readerButton.Classes.Set("on",false);return;}
   string raw;
   try {raw=await tab.View.EvaluateJavaScript<string>("return (\n"+ReaderScript().Trim().TrimEnd(';')+"\n)",(string)null,"about:blank",1,TimeSpan.FromSeconds(8));} catch(Exception) {raw=null;}
   bool ok=false;
   try {if(raw!=null)using(var doc=System.Text.Json.JsonDocument.Parse(raw)){System.Text.Json.JsonElement value;ok=doc.RootElement.TryGetProperty("ok",out value)&&value.ValueKind==System.Text.Json.JsonValueKind.True;}} catch(System.Text.Json.JsonException) {}
   if(!ok){Status(raw==null?"This page can't be read in reading mode.":"This page doesn't look like an article.");return;}
   if(Active!=tab)return;
   int size=state.ReaderSize>=13&&state.ReaderSize<=28?state.ReaderSize:18;
   tab.View.ExecuteJavaScript("("+ReaderView+")("+raw+","+(state.Dark?"true":"false")+","+size+","+(state.ReaderSerif?"true":"false")+")","",1);
   readerButton.Classes.Set("on",true);
   Status("Reading mode. Press Esc or F9 to go back to the page.");
  }
  // Built with the page's own DOM in a shadow root, text only through textContent.
  const string ReaderView=@"function(a,dark,size,serif){
 const old=document.getElementById('mica-reader');if(old)old.remove();
 const host=document.createElement('div');host.id='mica-reader';host.style.cssText='position:fixed;inset:0;z-index:2147483647';
 const root=host.attachShadow({mode:'closed'});
 const c=dark?{ink:'#F0EBF5',muted:'#B0A7BC',bg:'#24212A',panel:'#302B38',accent:'#C8B4E0',line:'#403948'}:{ink:'#302D38',muted:'#716B7A',bg:'#FAF9F6',panel:'#FFFFFF',accent:'#71608C',line:'#E9E5E9'};
 const style=document.createElement('style');
 style.textContent=`:host{all:initial}.scroll{position:absolute;inset:0;overflow:auto;background:${c.bg};color:${c.ink}}
 article{max-width:${size*36}px;margin:0 auto;padding:64px 36px 96px;font:${size}px/1.65 ${serif?'Georgia,Cambria,""Noto Serif"",serif':'""Segoe UI"",system-ui,-apple-system,Ubuntu,sans-serif'}}
 .site{font:600 11px system-ui,sans-serif;letter-spacing:.04em;color:${c.accent};text-transform:uppercase}
 h1{font:400 ${Math.round(size*2)}px/1.2 Georgia,Cambria,serif;margin:14px 0 10px}.by{color:${c.muted};font:13px system-ui,sans-serif;margin-bottom:34px}
 h2{font-size:1.3em;margin:1.2em 0 .5em}h3{font-size:1.12em;margin:1em 0 .4em}p{margin:0 0 .9em}li{margin:0 0 .35em}
 blockquote{font-style:italic;border-left:3px solid ${c.accent};padding:2px 0 2px 1em;margin:0 0 .9em}
 pre{font:.8em/1.45 ""Cascadia Mono"",Consolas,Menlo,monospace;background:${c.panel};padding:10px 14px;border-radius:8px;white-space:pre-wrap}
 img{max-width:100%;border-radius:8px;margin:.4em 0 .3em}.cap{color:${c.muted};font-size:13px;margin-bottom:1.2em}
 .bar{position:fixed;top:12px;right:22px;display:flex;gap:4px}.bar button{font:14px system-ui,sans-serif;color:${c.ink};background:${c.panel};border:1px solid ${c.line};border-radius:9px;width:36px;height:34px;cursor:pointer}
 .bar button:hover{border-color:${c.accent}}`;
 const scroll=document.createElement('div');scroll.className='scroll';
 const art=document.createElement('article');
 const el=(t,cls,text)=>{const e=document.createElement(t);if(cls)e.className=cls;if(text!=null)e.textContent=text;return e;};
 art.append(el('div','site',a.site||''),el('h1',null,a.title||''),el('div','by',[a.author,Math.max(1,Math.round((a.words||0)/230))+' min read'].filter(Boolean).join(' · ')));
 let list=null;
 for(const b of a.blocks||[]){
  if(b.t!=='li')list=null;
  if(b.t==='li'){if(!list){list=el('ul');art.append(list);}list.append(el('li',null,b.text));}
  else if(b.t==='img'){if(/^https?:/.test(b.src||'')){const i=el('img');i.src=b.src;i.alt=b.text||'';art.append(i);}}
  else if(b.t==='quote')art.append(el('blockquote',null,b.text));
  else if(b.t==='caption')art.append(el('div','cap',b.text));
  else art.append(el(b.t==='h2'||b.t==='h3'||b.t==='pre'?b.t:'p',null,b.text));
 }
 scroll.append(art);
 const bar=el('div','bar');const btn=(label,tip,run)=>{const x=el('button',null,label);x.title=tip;x.onclick=run;bar.append(x);};
 const resize=d=>{size=Math.min(28,Math.max(13,size+d));art.style.fontSize=size+'px';art.style.maxWidth=(size*36)+'px';};
 btn('A−','Smaller text',()=>resize(-1));btn('A+','Larger text',()=>resize(1));
 btn('Aa','Serif or sans-serif',()=>{serif=!serif;art.style.fontFamily=serif?'Georgia,Cambria,serif':'system-ui,-apple-system,sans-serif';});
 const close=()=>{host.remove();document.documentElement.style.overflow='';removeEventListener('keydown',key,true);};
 const key=e=>{if(e.key==='Escape'){e.preventDefault();close();}};addEventListener('keydown',key,true);
 btn('✕','Leave reading mode (Esc)',close);
 root.append(style,scroll,bar);document.documentElement.append(host);document.documentElement.style.overflow='hidden';scroll.tabIndex=-1;scroll.focus();
}";

  // ── Extensions: what Chromium loaded this run, and adding one from the Chrome Web Store (it loads after a restart).
  void ShowExtensions() {
   var input=new TextBox {Watermark="Chrome Web Store link or extension ID",Margin=new Thickness(12,4,12,8)};
   input.Classes.Add("field");
   var note=Muted(new TextBlock {FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12,0,12,8)});
   var restart=new Button {Content="Restart Mica",Margin=new Thickness(12,0,12,6),Padding=new Thickness(14,7),IsVisible=false};restart.Classes.Add("flat");restart.Classes.Add("on");restart.Click+=delegate {Restart();};
   var card=MenuCard(extensionsButton,330,PlacementMode.Bottom,delegate(StackPanel body,Popup popup) {
    MenuHeading(body,"EXTENSIONS");
    var installed=CrossExtensions.Installed(profile);
    foreach(var folder in CrossExtensions.Folders(profile)) {
     string id=installed.Where(p=>p.Value==folder).Select(p=>p.Key).FirstOrDefault();
     var row=new Grid {ColumnDefinitions=new ColumnDefinitions("28,*,Auto"),Margin=new Thickness(12,7,6,7)};
     var icon=Glyph(Glyphs.Puzzle,14,"accent");icon.HorizontalAlignment=HorizontalAlignment.Left;row.Children.Add(icon);
     var text=new StackPanel {VerticalAlignment=VerticalAlignment.Center};
     string name,version;ManifestName(folder,out name,out version);
     text.Children.Add(new TextBlock {Text=name,FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis});
     text.Children.Add(Muted(new TextBlock {Text=(id==null?"Built in":"Chrome Web Store")+(version.Length>0?" · "+version:""),FontSize=10,Margin=new Thickness(0,3,0,0)}));
     Grid.SetColumn(text,1);row.Children.Add(text);
     if(id!=null) {
      var remove=new Button {Content="Remove",FontSize=11,Padding=new Thickness(9,5)};remove.Classes.Add("flat");remove.Classes.Add("muted");string drop=id;
      remove.Click+=delegate {CrossExtensions.Remove(profile,drop);remove.IsEnabled=false;remove.Content="Removed";note.Text="Removed. It stops after Mica restarts.";restart.IsVisible=true;};
      Grid.SetColumn(remove,2);row.Children.Add(remove);
     }
     body.Children.Add(row);
    }
    MenuDivider(body);
    MenuHeading(body,"ADD FROM THE CHROME WEB STORE");
    body.Children.Add(input);body.Children.Add(note);
    var add=new Button {Content="Add extension",Margin=new Thickness(12,0,12,6),Padding=new Thickness(14,7)};add.Classes.Add("flat");add.Classes.Add("on");
    add.Click+=async delegate {
     string what=(input.Text??"").Trim();if(what.Length==0){note.Text="Paste the extension's Web Store address.";return;}
     add.IsEnabled=false;note.Text="Downloading and checking its signature…";
     try {await CrossExtensions.Install(profile,what,CefRuntime.ChromeVersion);note.Text="Added. It starts when Mica restarts.";restart.IsVisible=true;}
     catch(Exception e) {note.Text="Couldn't add it: "+e.Message;}
     add.IsEnabled=true;
    };
    body.Children.Add(add);body.Children.Add(restart);
   });
   if(card!=null){TakeKeyboard();}
  }
  // The name an extension shows, resolving __MSG_name__ from its English (or default) messages.
  static void ManifestName(string folder,out string name,out string version) {
   name=Path.GetFileName(folder);version="";
   try {
    var json=new JavaScriptSerializer();var manifest=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(folder,"manifest.json")));
    object value;if(manifest.TryGetValue("version",out value))version=value as string??"";
    if(!manifest.TryGetValue("name",out value)||!(value is string))return;name=(string)value;
    if(!name.StartsWith("__MSG_"))return;
    string key=name.Substring(6).TrimEnd('_');object locale;string lang=manifest.TryGetValue("default_locale",out locale)?locale as string:"en";
    foreach(var l in new[]{"en",lang}) {
     string file=Path.Combine(folder,"_locales",l??"en","messages.json");if(!File.Exists(file))continue;
     var messages=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file));
     foreach(var pair in messages)if(string.Equals(pair.Key,key,StringComparison.OrdinalIgnoreCase)){var m=pair.Value as Dictionary<string,object>;if(m!=null&&m.ContainsKey("message")){name=m["message"] as string??name;return;}}
    }
   } catch(Exception) {}
  }

  // ── The site card (click the lock): the connection, zoom, how the tab rests, and forgetting the site's cookies.
  void ShowSiteCard() {
   var tab=Active;if(tab==null||!Navigation.IsWeb(tab.Entry.Url))return;
   string site=SiteOf(tab.Entry.Url),url=tab.Entry.Url;bool secure=url.StartsWith("https://");
   MenuCard(addressButton,330,PlacementMode.Bottom,delegate(StackPanel body,Popup popup) {
    var head=new StackPanel {Margin=new Thickness(12,8,12,10)};
    head.Children.Add(new TextBlock {Text=site,FontSize=15,FontWeight=FontWeight.SemiBold});
    head.Children.Add(Muted(new TextBlock {Text=secure?"Connection is secure. What you send here stays between you and this site.":"Not secure. Don't type passwords or card numbers here.",FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)}));
    body.Children.Add(head);
    ZoomRow(body);
    MenuDivider(body);
    MenuHeading(body,"MEMORY SAVER");
    string mode;state.SiteMemory.TryGetValue(site,out mode);
    foreach(var choice in new[]{new[]{"","Usual","Rests after 15 minutes in the background"},new[]{"never","Keep awake","For music, calls and dashboards"},new[]{"quick","Rest quickly","After 2 minutes in the background"}}) {
     string value=choice[0];
     var item=MenuCommand(body,popup,value=="never"?Glyphs.Sun:value=="quick"?Glyphs.Rest:Glyphs.Tabs,choice[1],(mode??"")==value?"✓":"",delegate {if(value.Length==0)state.SiteMemory.Remove(site);else state.SiteMemory[site]=value;Save();Status(choice[1]+" for "+site+".");});
     ToolTip.SetTip(item,choice[2]);
    }
    MenuDivider(body);
    MenuCommand(body,popup,Glyphs.Trash,"Clear cookies for "+site,"",delegate {ClearSiteCookies(url,site);});
    MenuCommand(body,popup,Glyphs.Copy,"Copy page address","",delegate {CopyAddress(tab);});
   });
  }
  void ClearSiteCookies(string url,string site) {
   var jar=CefCookieManager.GetGlobal(null);if(jar==null)return;
   foreach(var origin in new[]{"https://"+site+"/","https://www."+site+"/","http://"+site+"/"})jar.DeleteCookies(origin,"",null);
   jar.FlushStore(null);Status("Cookies for "+site+" cleared. Reload to sign in again.");
  }

  // ── Private window (Ctrl+Shift+N): a second Mica with a throwaway profile, deleted when it closes.
  void NewPrivateWindow() {
   try {
    var start=new ProcessStartInfo(Environment.ProcessPath) {UseShellExecute=false};start.ArgumentList.Add("--private");
    Process.Start(start);
   } catch(Exception e) {Status("Couldn't open a private window: "+e.Message);}
  }

  static Color Mix(Color a,Color b,double t) {return Color.FromRgb((byte)(a.R+(b.R-a.R)*t),(byte)(a.G+(b.G-a.G)*t),(byte)(a.B+(b.B-a.B)*t));}
  static double Luminance(Color c) {Func<double,double> f=v=>{v/=255;return v<=0.03928?v/12.92:Math.Pow((v+0.055)/1.055,2.4);};return 0.2126*f(c.R)+0.7152*f(c.G)+0.0722*f(c.B);}
  Color FrameColor() {
   if(string.IsNullOrEmpty(state.FrameColor))return Color.Parse(state.Dark?"#332C40":"#DDD7E5");
   var chosen=Color.Parse(state.FrameColor);
   if(!state.Dark&&chosen.R<0x30&&chosen.G<0x30&&chosen.B<0x30)return Color.Parse("#F1F1F2");
   return state.Dark&&Luminance(chosen)>0.08?Mix(chosen,Color.FromRgb(0x1A,0x18,0x20),0.55):chosen;
  }
  // The frame and the sidebar on it: a custom colour gets tints of itself, with light text on dark colours.
  void ApplyFrame() {
   var res=sidebar.Resources;foreach(var key in new[]{"Ink","Muted","Panel","SideCard","Hover","Line","Accent","Divider"})res.Remove(key);
   if(string.IsNullOrEmpty(state.FrameColor)){ClearValue(BackgroundProperty);Res(this,BackgroundProperty,"Backdrop");return;}
   var frame=FrameColor();Background=new SolidColorBrush(frame);
   bool light=Luminance(frame)<0.22;Color white=Colors.White,black=Colors.Black;
   res["Ink"]=new SolidColorBrush(light?Color.Parse("#F7F4FA"):Mix(frame,black,0.82));
   res["Muted"]=new SolidColorBrush(light?Mix(frame,white,0.68):Mix(frame,black,0.6));
   res["SideCard"]=new SolidColorBrush(light?Mix(frame,white,0.11):Mix(frame,white,0.5));
   res["Panel"]=new SolidColorBrush(light?Mix(frame,white,0.19):Mix(frame,white,0.78));
   res["Hover"]=new SolidColorBrush(light?Mix(frame,white,0.15):Mix(frame,white,0.6));
   res["Line"]=new SolidColorBrush(light?Mix(frame,white,0.2):Mix(frame,black,0.12));
   res["Divider"]=res["Line"];
   res["Accent"]=new SolidColorBrush(light?Mix(frame,white,0.72):Mix(frame,black,0.5));
  }

  // ── Task manager (Shift+Esc): each tab awake or resting, what Mica and its engine use, and a way to rest tabs.
  void ShowTaskManager() {
   var total=Muted(new TextBlock {Text="Measuring…",FontSize=11,Margin=new Thickness(12,0,12,8)});
   MenuCard(addressButton,380,PlacementMode.Bottom,delegate(StackPanel body,Popup popup) {
    MenuHeading(body,"TASK MANAGER");body.Children.Add(total);
    foreach(var tab in tabs.Values.Where(t=>t.Entry.Url!="mica://home").OrderByDescending(t=>t.View!=null)) {
     var t=tab;var row=new Grid {ColumnDefinitions=new ColumnDefinitions("26,*,Auto"),Margin=new Thickness(12,5,6,5)};
     var icon=TabIcon(t,15);icon.HorizontalAlignment=HorizontalAlignment.Left;icon.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(icon);
     var text=new StackPanel {VerticalAlignment=VerticalAlignment.Center};
     text.Children.Add(new TextBlock {Text=TabTitle(t.Entry),FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis});
     text.Children.Add(Muted(new TextBlock {Text=(t.Entry.Id==state.ActiveId?"In front":t.View!=null?"Awake":"Resting")+" · "+t.Entry.Space,FontSize=10,Margin=new Thickness(0,3,0,0)}));
     Grid.SetColumn(text,1);row.Children.Add(text);
     if(t.View!=null&&t.Entry.Id!=state.ActiveId) {
      var rest=new Button {Content="Rest",FontSize=11,Padding=new Thickness(9,5)};rest.Classes.Add("flat");rest.Classes.Add("muted");
      rest.Click+=delegate {RestTab(t);rest.IsEnabled=false;rest.Content="Resting";};
      Grid.SetColumn(rest,2);row.Children.Add(rest);
     }
     body.Children.Add(row);
    }
    MenuDivider(body);
    MenuCommand(body,popup,Glyphs.Rest,"Rest all background tabs","",delegate {RestIdleTabs(true);});
   });
   Task.Run(()=>MemorySummary()).ContinueWith(t=>Dispatcher.UIThread.Post(delegate {total.Text=t.Result;}));
  }
  void RestTab(CrossTab tab) {
   if(tab.View==null||tab.Entry.Id==state.ActiveId)return;
   pages.Children.Remove(tab.View);tab.View.Dispose();tab.View=null;tab.Loading=false;RefreshTab(tab);
  }
  // Mica plus Chromium's helper processes from this copy of Mica.
  static string MemorySummary() {
   long bytes=0;int count=0;string folder=AppContext.BaseDirectory;
   foreach(var p in Process.GetProcesses()) {
    try {
     if(p.Id==Environment.ProcessId||((p.ProcessName=="Xilium.CefGlue.BrowserProcess"||p.ProcessName=="Mica")&&p.MainModule!=null&&p.MainModule.FileName.StartsWith(folder,StringComparison.OrdinalIgnoreCase))){bytes+=p.WorkingSet64;count++;}
    } catch(Exception) {}
    finally {p.Dispose();}
   }
   return (bytes/1048576.0).ToString("0")+" MB in use · "+count+" processes (Mica and Chromium)";
  }
 }
}
