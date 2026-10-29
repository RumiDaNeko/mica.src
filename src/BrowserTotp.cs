using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // Two-step sign-in codes, kept with your logins. When a site shows its 2FA setup key (the text under
 // "Can't scan the QR code?", or an otpauth link), Mica offers to save it; after that, when the site asks
 // for the 6-digit code, Mica fills the current one in by itself. The key lives in the encrypted vault
 // and never goes back to a page: pages only ever get a code, which is good for 30 seconds.
 partial class BrowserApp {
  const string TotpScript=@"(()=>{if(window.top!==window||!window.chrome||!chrome.webview)return;
const post=m=>{try{chrome.webview.postMessage(m)}catch(e){}};
const shown=e=>e&&!e.disabled&&(e.offsetWidth>0||e.offsetHeight>0||e.getClientRects().length>0);
const words=e=>[e.name,e.id,e.placeholder,e.getAttribute('aria-label'),e.autocomplete,e.className].join(' ');
const notCode=/cvv|cvc|csc|card|thẻ|zip|postal|coupon|promo|discount|gift|referral|captcha|invite|phone|search|tìm/i;
// One field for the whole code, or a row of one-digit boxes.
// Attributes first; whether it's on screen (which makes the page measure itself) only for a likely match.
const single=e=>{if(!e||e.tagName!=='INPUT'||e.readOnly||e.value||e.disabled)return false;if(!['text','tel','number','password',''].includes(e.type))return false;
 const w=words(e);if(notCode.test(w))return false;
 const m=e.maxLength;const likely=/one-time-code/.test(e.autocomplete||'')||((m===-1||(m>=6&&m<=8))&&/otp|totp|2fa|two.?factor|mfa|authenticat|verif|one.?time|app.?code|security.?code|\bcode\b|pin.?code|mã|xác/i.test(w));
 return likely&&shown(e);};
const boxes=()=>{for(const e of document.querySelectorAll('input[maxlength=""1""]')){if(e.value||notCode.test(words(e))||!shown(e))continue;
 const row=[...e.parentElement.parentElement.querySelectorAll('input')].filter(i=>shown(i)&&i.maxLength===1);if(row.length>=6&&row.length<=8&&row[0]===e)return row;}return null;};
const codeTarget=()=>{const one=[...document.querySelectorAll('input')].find(single);if(one)return [one];return boxes();};
Object.defineProperty(window,'__micaCodeTarget',{value:codeTarget});
let asked=new WeakSet(),queued=0;
// Cheap on busy pages: nothing runs while the tab is hidden, and the page's text is only read for a setup key
// once a box for a code is there (every setup page has one), a few times at most.
let scans=0;
const look=()=>{queued=0;if(document.hidden)return;const t=codeTarget();if(!t)return;if(!asked.has(t[0])){asked.add(t[0]);post({mica:'otpField'});}if(scans++<6)keys();};
// A setup page: a key shown to type into an authenticator app, next to a box for the first code.
const told=new Set();
const report=(uri,secret)=>{const id=uri||secret;if(told.has(id))return;told.add(id);let user='';try{user=sessionStorage.getItem('__micaUser')||''}catch(e){}post({mica:'totpKey',uri:uri||'',secret:secret||'',user:user.slice(0,200)});};
const setupWords=/secret|setup.?key|key|manual|enter this|can.?t scan|authenticat|khóa|bí mật|thủ công|nhập mã/i;
const keyText=/(?:^|[^A-Za-z0-9])((?:[A-Za-z2-7]{4}[ -]?){4,16}[A-Za-z2-7]{0,4})(?=$|[^A-Za-z0-9])/;
const keys=()=>{
 for(const a of document.querySelectorAll('a[href^=""otpauth:""],[data-otpauth],input[value^=""otpauth:""]')){const v=a.getAttribute('href')||a.getAttribute('data-otpauth')||a.value;if(/^otpauth:\/\/totp\//i.test(v))report(v,'');}
 const body=document.body;if(!body)return;const text=body.innerText||'';
 const link=text.match(/otpauth:\/\/totp\/[^\s""'<>]+/i);if(link)report(link[0],'');
 if(!/authenticat|two.?factor|2fa|2-step|two.?step|xác (minh|thực)|bảo mật 2/i.test(text.slice(0,20000)+' '+document.title))return;
 const walker=document.createTreeWalker(body,NodeFilter.SHOW_TEXT);let node,seen=0;
 while((node=walker.nextNode())&&seen++<4000){const s=node.nodeValue;if(!s||s.length<16||s.length>200)continue;const m=s.match(keyText);if(!m)continue;
  const key=m[1].replace(/[ -]/g,'');if(key.length<16||!/[2-7]/.test(key)||!/[A-Za-z]/.test(key))continue;
  const el=node.parentElement;if(!el||!shown(el)||el.closest('script,style,textarea,[contenteditable]'))continue;
  let around='',up=el;for(let i=0;i<4&&up;i++,up=up.parentElement)around=(up.innerText||'').slice(0,800);
  if(setupWords.test(around))report('',key);}
 for(const i of document.querySelectorAll('input[readonly],input[type=text]')){const v=(i.value||'').trim();if(v.length<16||v.length>80||!shown(i))continue;const m=v.match(keyText);if(m&&m[1].replace(/[ -]/g,'').length>=16&&/[2-7]/.test(v)){let up=i.parentElement,around='';for(let k=0;k<4&&up;k++,up=up.parentElement)around=(up.innerText||'').slice(0,800);if(setupWords.test(around))report('',m[1].replace(/[ -]/g,''));}}};
const soon=()=>{if(!queued)queued=setTimeout(look,600);};
const start=()=>{look();new MutationObserver(soon).observe(document.documentElement,{childList:true,subtree:true});addEventListener('focusin',soon,true);document.addEventListener('visibilitychange',soon);};
if(document.readyState==='loading')addEventListener('DOMContentLoaded',start);else start();})();";

  static string CodeFillScript(string origin,string code) {
   return "(()=>{if(window.top!==window||location.origin!=="+VaultCrypto.Json.Serialize(origin)+"||!window.__micaCodeTarget)return 'origin';const t=window.__micaCodeTarget();if(!t)return 'none';"+
    "const code="+VaultCrypto.Json.Serialize(code)+";const set=(e,v)=>{e.focus();Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(e,v);e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));};"+
    "if(t.length===1)set(t[0],code);else t.forEach((e,i)=>set(e,code[i]||''));return 'filled';})()";
  }

  // The login last used on each site, so a site with several saved 2FA keys gets the right code.
  readonly Dictionary<string,string> recentUser=new Dictionary<string,string>(StringComparer.Ordinal);
  void NoteRecentUser(string origin,string username) {if(!string.IsNullOrEmpty(username))recentUser[origin]=username;}

  List<VaultEntry> SavedTotps(string origin) {
   return sessionVault==null?new List<VaultEntry>():sessionVault.Entries.Where(e=>!string.IsNullOrEmpty(e.Totp)&&VaultCrypto.Matches(e.Origin,origin)).ToList();
  }

  // The site asks for a code. Only a code goes to the page, and only one this site's own key makes.
  async void FillCode(Func<bool> live,WebView2 view,string origin) {
   if(sessionVault==null)await OpenAutofillVault();if(closing||!live())return;
   var keys=SavedTotps(origin);if(keys.Count==0)return;
   string user;recentUser.TryGetValue(origin,out user);
   var entry=keys.FirstOrDefault(e=>e.Username==user)??keys[0];
   string code=Totp.Code(entry.Totp);if(code==null)return;
   // Within the last few seconds the code is about to change; the next one is a moment away, so wait for it.
   if(Totp.SecondsLeft(entry.Totp)<=2){await Task.Delay(Totp.SecondsLeft(entry.Totp)*1000+300);if(closing||!live())return;code=Totp.Code(entry.Totp);}
   var core=LiveCore(view);if(core==null)return;
   try {
    string result=await core.ExecuteScriptAsync(CodeFillScript(origin,code));
    if(fillLog!=null)fillLog.Add(DateTime.Now.ToString("ss.fff")+" code fill "+result);
    if(result=="\"filled\"")Status("Filled your 2FA code for "+new Uri(origin).Host+(keys.Count>1&&!string.IsNullOrEmpty(entry.Username)?" ("+entry.Username+")":"")+".");
   }catch(Exception){}
  }

  // A setup page showed its key: offer to keep it, then fill the first code the page asks for.
  readonly HashSet<string> offeredKeys=new HashSet<string>(StringComparer.Ordinal);
  void OfferSaveTotp(Func<bool> live,WebView2 view,string origin,string uri,string secret,string userHint) {
   Totp.Key key;if(!Totp.TryParse(string.IsNullOrEmpty(uri)?secret:uri,out key))return;
   string normalized=Totp.Normalize(key);if(!offeredKeys.Add(origin+" "+normalized))return;
   if(SavedTotps(origin).Any(e=>{Totp.Key other;return Totp.TryParse(e.Totp,out other)&&other.Secret.SequenceEqual(key.Secret);}))return;
   var form=new StackPanel {Margin=new Thickness(0,10,0,0)};
   var label=DialogText("Account",11,"Muted");label.Margin=new Thickness(0,0,0,4);form.Children.Add(label);
   string guess=!string.IsNullOrEmpty(userHint)?userHint:key.Account??"";
   if(string.IsNullOrEmpty(guess)){string recent;if(recentUser.TryGetValue(origin,out recent))guess=recent;}
   var account=new TextBox {Text=guess,Tag="totp-account"};form.Children.Add(account);
   ShowChoiceBubble(Find<Button>("AddressButton"),"\uE8D7","Save 2FA key?",new Uri(origin).Host,"Mica will fill this site's 6-digit codes for you. The key stays in your encrypted vault.",null,
    new[]{new[]{"Not now","skip",""},new[]{"Save","save","primary"}},()=>!closing&&live(),
    async delegate(string choice) {
     if(choice!="save")return;
     if(await StoreTotp(live,origin,account.Text.Trim(),normalized))FillCode(live,view,origin);
    },form);
  }

  async Task<bool> StoreTotp(Func<bool> live,string origin,string username,string normalized) {
   if(privateMode)return false;string host=new Uri(origin).Host;
   if(sessionVault==null&&!File.Exists(VaultFile)) {
    // No vault yet: make one with a key kept on this device, as the first saved password does.
    string deviceKey;
    try {AutofillKeyStore.Save(profile,deviceKey=VaultCrypto.NewDeviceKey());File.WriteAllText(PassphrasePendingPath,"");}catch(Exception e){Status("Couldn't save the 2FA key: "+e.Message);return false;}
    sessionVault=new VaultPayload();sessionMaster=deviceKey;
   }
   if(sessionVault==null&&!await UnlockSessionVault(()=>!closing&&live(),null)){Status("Unlock your vault to save the 2FA key.");return false;}
   var logins=sessionVault.Entries.Where(e=>VaultCrypto.Matches(e.Origin,origin)).ToList();
   var entry=logins.FirstOrDefault(e=>e.Username==username)??(string.IsNullOrEmpty(username)&&logins.Count==1?logins[0]:null);
   if(entry==null){entry=new VaultEntry {Id=Guid.NewGuid().ToString("N"),Origin=VaultCrypto.Origin(origin),Username=username,Password=""};sessionVault.Entries.Add(entry);}
   entry.Totp=normalized;VaultCrypto.Touch(sessionVault,entry);NoteRecentUser(origin,entry.Username);
   if(!await SaveSessionVault())return false;
   Status("2FA key saved. Mica fills the codes for "+host+" from now on.");return true;
  }
 }
}
