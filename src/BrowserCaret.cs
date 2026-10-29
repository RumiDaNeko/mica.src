using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // F7 caret browsing, without the engine's Edge-styled "Turn on caret browsing?" box.
 // The first F7 asks once (from the island); the answer is kept: either F7 toggles a text
 // cursor quietly from then on, or it never does anything again.
 partial class BrowserApp {
  static bool caretOn;
  string CaretChoicePath {get {return Path.Combine(profile,"caret-browsing.txt");}}
  string CaretChoice() {try {return File.Exists(CaretChoicePath)?File.ReadAllText(CaretChoicePath).Trim():"";}catch(Exception){return "";}}
  void SaveCaretChoice(string choice) {try {File.WriteAllText(CaretChoicePath,choice);}catch(Exception){}}

  void CaretKey() {
   string choice=CaretChoice();
   if(choice=="never")return;
   if(choice=="on"){SetCaretBrowsing(!caretOn);return;}
   ShowChoiceBubble(Find<Button>("AddressButton"),"","Turn on caret browsing?","Caret browsing",
    "A text cursor you move with the arrow keys; Shift selects, Enter opens a link. F7 turns it on and off.",null,
    new[]{new[]{"Never","never","","Don't ask again; F7 does nothing in Mica"},new[]{"Not now","later",""},new[]{"Turn on","on","primary"}},()=>!closing,
    delegate(string picked) {
     if(picked=="never"){SaveCaretChoice("never");Status("F7 won't do anything in Mica.");}
     else if(picked=="on"){SaveCaretChoice("on");SetCaretBrowsing(true);}
    });
  }
  void SetCaretBrowsing(bool on) {
   caretOn=on;
   foreach(var browser in Program.Browsers.Where(b=>!b.closing))foreach(var view in browser.views.Values)ApplyCaret(view);
   Status(on?"Caret browsing on. F7 turns it off.":"Caret browsing off.");
  }
  async void ApplyCaret(WebView2 view) {
   var core=LiveCore(view);if(core==null)return;
   try {await core.ExecuteScriptAsync(CaretScript+";window.__micaCaret&&window.__micaCaret("+(caretOn?"true":"false")+")");}catch(Exception){}
  }

  // A blinking bar at the selection's focus; arrow keys move it with Selection.modify.
  const string CaretScript=@"(()=>{if(window.__micaCaret)return;let on=false,bar=null;
const editing=e=>e&&(e.isContentEditable||/^(INPUT|TEXTAREA|SELECT)$/.test(e.tagName));
const place=()=>{const s=getSelection();if(s.rangeCount)return;const r=document.caretRangeFromPoint?document.caretRangeFromPoint(Math.min(innerWidth/2,200),Math.min(innerHeight/3,160)):null;
 if(r)s.collapse(r.startContainer,r.startOffset);else if(document.body)s.collapse(document.body,0);};
const draw=()=>{if(!on||!bar)return;const s=getSelection();if(!s.rangeCount||!s.focusNode){bar.style.display='none';return;}
 const r=document.createRange();try{r.setStart(s.focusNode,s.focusOffset);r.collapse(true);}catch(e){return;}
 let box=r.getClientRects()[0]||r.getBoundingClientRect();if(!box||(!box.height&&s.focusNode.nodeType===1))box=(s.focusNode.nodeType===1?s.focusNode:s.focusNode.parentElement).getBoundingClientRect();
 bar.style.display='block';bar.style.left=box.left+'px';bar.style.top=box.top+'px';bar.style.height=Math.max(14,box.height||16)+'px';
 if(box.top<0||box.bottom>innerHeight)(s.focusNode.nodeType===1?s.focusNode:s.focusNode.parentElement).scrollIntoView({block:'nearest'});};
const key=e=>{if(!on||editing(document.activeElement)||e.altKey||e.metaKey)return;const s=getSelection();
 const moves={ArrowLeft:['backward','character'],ArrowRight:['forward','character'],ArrowUp:['backward','line'],ArrowDown:['forward','line'],Home:['backward','lineboundary'],End:['forward','lineboundary']};
 const m=moves[e.key];if(m){place();s.modify(e.shiftKey?'extend':'move',m[0],e.ctrlKey&&m[1]==='character'?'word':m[1]);e.preventDefault();e.stopPropagation();requestAnimationFrame(draw);return;}
 if(e.key==='Enter'&&s.focusNode){const a=(s.focusNode.nodeType===1?s.focusNode:s.focusNode.parentElement).closest('a[href]');if(a){a.click();e.preventDefault();}}};
addEventListener('keydown',key,true);addEventListener('mouseup',()=>requestAnimationFrame(draw),true);addEventListener('scroll',()=>requestAnimationFrame(draw),true);addEventListener('resize',()=>requestAnimationFrame(draw));
window.__micaCaret=v=>{on=!!v;if(on){if(!bar){bar=document.createElement('div');bar.setAttribute('aria-hidden','true');
 bar.style.cssText='position:fixed;width:2px;background:#7b5cc4;z-index:2147483647;pointer-events:none;animation:__micaBlink 1s steps(1) infinite';
 const st=document.createElement('style');st.textContent='@keyframes __micaBlink{50%{opacity:0}}';(document.head||document.documentElement).appendChild(st);}
 (document.body||document.documentElement).appendChild(bar);place();draw();}else if(bar){bar.remove();}};})()";
 }
}
