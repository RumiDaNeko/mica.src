using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Mica {
 // Saves and fills passwords on web pages, like other browsers. A small script notices
 // login forms and submitted credentials; Mica asks before saving (with the details
 // editable) and fills saved logins. Like Chrome, filling doesn't wait for the vault's
 // lock: after the vault is opened once on this device, its key is kept protected by the
 // Windows account (DPAPI). The lock guards viewing passwords in the Passwords window.
 partial class BrowserApp {
  static VaultPayload sessionVault;static string sessionMaster;
  internal List<string> fillLog;
  string PassphrasePendingPath {get {return Path.Combine(profile,"vault-passphrase-pending");}}
  Action vaultAdoptSession;
  readonly HashSet<string> unlockDeclined=new HashSet<string>(StringComparer.Ordinal);
  string VaultFile {get {return Path.Combine(profile,"vault.enc.json");}}
  string NeverSavePath {get {return Path.Combine(profile,"passwords-never.json");}}


  const string LoginScript=@"(()=>{if(window.top!==window||!window.chrome||!chrome.webview)return;
const post=m=>{try{chrome.webview.postMessage(m)}catch(e){}};
const shown=e=>e&&!e.disabled&&(e.offsetWidth>0||e.offsetHeight>0||e.getClientRects().length>0);
const searchy=e=>e.type==='search'||!!(e.closest&&e.closest('[role=search],[role=combobox],[role=searchbox]'))||/search|find|query|filter|tìm/i.test([e.name,e.id,e.placeholder,e.getAttribute('aria-label'),e.getAttribute('role')].join(' '));
// A box for a one-time code (2FA, SMS, email check) is never the username, even right above the password.
const codey=e=>/one-time-code/.test(e.autocomplete||'')||/otp|2fa|mfa|one.?time|verif|auth.?code|security.?code|\bcode\b|\bpin\b|mã/i.test([e.name,e.id,e.placeholder,e.getAttribute('aria-label')].join(' '))||(e.maxLength>=4&&e.maxLength<=8&&/numeric|decimal|\[0-9\]|\\d/.test((e.inputMode||'')+' '+(e.getAttribute('pattern')||'')));
const userField=p=>{const root=(p&&p.form)||document;const all=[...root.querySelectorAll('input')].filter(i=>i.type!=='hidden'&&shown(i)&&!searchy(i)&&!codey(i));
 const hint=all.find(i=>/username|email/.test(i.autocomplete||''));if(hint)return hint;
 if(!p)return null;const at=all.indexOf(p);for(let i=at-1;i>=0;i--){if(['text','email','tel',''].includes(all[i].type))return all[i];}return null;};
const remember=v=>{try{sessionStorage.setItem('__micaUser',v)}catch(e){}};
const recall=()=>{try{return sessionStorage.getItem('__micaUser')||''}catch(e){return ''}};
// Payment pages mask card codes, PINs and one-time codes as passwords; those are never logins.
const paymenty=e=>/^(cc-|transaction|one-time-code)/.test(e.autocomplete||'')||/card|cvv|cvc|csc|security.?code|expir|\bpin\b|otp|one.?time|verif|thẻ/i.test([e.name,e.id,e.placeholder,e.getAttribute('aria-label')].join(' '))||(e.type==='password'&&e.maxLength>0&&e.maxLength<=6&&/numeric|decimal/.test(e.inputMode||e.getAttribute('pattern')||''));
const cardForm=f=>!!(f&&f.querySelector('input[autocomplete^=cc-],input[name*=card i],input[id*=card i],input[name*=cvv i],input[name*=cvc i]'));
const visiblePasswords=()=>[...document.querySelectorAll('input[type=password]')].filter(p=>shown(p)&&!paymenty(p));
// Keep what is typed: sites like Google swap the fields out before anything is 'submitted'.
let pending=null,armed=0,sent='',timer=0;
const note=()=>{const ps=visiblePasswords().filter(p=>p.value);
 if(!ps.length){const u=userField(null);if(u&&u.value&&u.value.length<200)remember(u.value);return;}
 const p=ps.find(x=>x.autocomplete==='new-password')||ps[0];const u=userField(p);if(u&&u.value)remember(u.value);
 pending={username:((u&&u.value)||recall()).slice(0,4096),password:p.value.slice(0,65536)};};
const send=()=>{if(!pending)return;const key=pending.username+'\n'+pending.password;pending=null;armed=0;clearInterval(timer);if(key===sent)return;sent=key;const i=key.indexOf('\n');post({mica:'credential',username:key.slice(0,i),password:key.slice(i+1)});};
// After a click or Enter, the login went through once the password field is gone.
const arm=()=>{note();if(!pending)return;armed=Date.now();clearInterval(timer);timer=setInterval(()=>{if(!pending||Date.now()-armed>8000){clearInterval(timer);return;}if(!visiblePasswords().length)send();},250);};
addEventListener('input',e=>{if(e.target&&e.target.tagName==='INPUT')note();},true);
addEventListener('change',e=>{if(e.target&&e.target.tagName==='INPUT')note();},true);
addEventListener('submit',()=>{note();send();},true);
addEventListener('click',e=>{const b=e.target&&e.target.closest&&e.target.closest('button,input[type=submit],input[type=button],input[type=image],[role=button],a');if(b)arm();},true);
addEventListener('keydown',e=>{if(e.key==='Enter'&&e.target&&e.target.tagName==='INPUT')arm();},true);
addEventListener('pagehide',()=>{if(pending&&armed)send();},true);
// Offered only for a sign-in form you can see: not a hidden one, a sign-up (new password) or a card form.
let told=false;const scan=()=>{if(told)return;
 const p=visiblePasswords().find(x=>x.autocomplete!=='new-password'&&!cardForm(x.form));
 const u=p||[...document.querySelectorAll('input[autocomplete~=username]')].find(x=>shown(x)&&!cardForm(x.form));
 if(p||u){told=true;post({mica:'loginForm'});}};
// Forms that start hidden (a 'Sign in' tab or dialog) are checked again when they appear or get focus.
const watch=()=>{scan();if(told)return;let queued=0;const again=()=>{if(!queued)queued=setTimeout(()=>{queued=0;scan();},200);};
 const o=new MutationObserver(()=>{again();if(told)o.disconnect();});o.observe(document.documentElement,{childList:true,subtree:true,attributes:true,attributeFilter:['class','style','hidden']});
 addEventListener('focusin',again,true);};
// The field Mica's dropdown sits under. The rectangle is in page pixels.
const fieldKind=e=>{if(!e||e.tagName!=='INPUT'||!shown(e))return null;if(e.type==='password')return 'password';
 if(!['text','email','tel',''].includes(e.type)||searchy(e)||paymenty(e)||codey(e)||cardForm(e.form))return null;
 // A checkout's contact email isn't a sign-in field.
 const checkout=!visiblePasswords().length&&!!document.querySelector('input[autocomplete^=cc-],input[name*=cvv i],input[name*=cvc i]');
 if(/username|webauthn/.test(e.autocomplete||'')||(!checkout&&/email/.test(e.autocomplete||'')))return 'username';if(checkout)return null;
 const p=visiblePasswords()[0];if(p&&userField(p)===e)return 'username';return /user|email|login|identifier|account/i.test((e.name||'')+' '+(e.id||''))?'username':null;};
let focused=null,frame=0;
const tell=(why)=>{frame=0;why=why||'move';if(!focused||!focused.isConnected||!shown(focused)){if(focused){focused=null;post({mica:'fieldBlur'});}return;}
 const r=focused.getBoundingClientRect(),k=fieldKind(focused);post({mica:'field',why,kind:k,x:r.left,y:r.top,w:r.width,h:r.height,typed:focused.value.length>0,prefix:k==='username'?focused.value.slice(0,200):'',passkey:!!pendingPasskey});};
const soon=()=>{if(focused&&!frame)frame=requestAnimationFrame(tell);};
addEventListener('focusin',e=>{if(fieldKind(e.target)){focused=e.target;tell('focus');}else if(focused){focused=null;post({mica:'fieldBlur'});}},true);
addEventListener('focusout',e=>{if(e.target===focused)setTimeout(()=>{if(focused&&document.activeElement!==focused){focused=null;post({mica:'fieldBlur'});}},120);},true);
addEventListener('mousedown',e=>{if(e.target===focused)setTimeout(()=>tell('click'),0);},true);
addEventListener('input',e=>{if(e.target===focused){if(frame)cancelAnimationFrame(frame);frame=requestAnimationFrame(()=>tell('input'));}},true);
addEventListener('scroll',soon,true);addEventListener('resize',soon);
addEventListener('pagehide',()=>{if(focused){focused=null;post({mica:'fieldBlur'});}},true);
addEventListener('keydown',e=>{if(!focused||e.target!==focused||!window.__micaMenuOpen)return;const k=e.key;
 if(k==='ArrowDown'||k==='ArrowUp'||(k==='Enter'&&window.__micaMenuPick)){e.preventDefault();e.stopImmediatePropagation();}
 if(['ArrowDown','ArrowUp','Enter','Escape','Tab'].includes(k))post({mica:'menuKey',key:k});},true);
Object.defineProperty(window,'__micaRetell',{value:()=>{focused=document.activeElement;tell('focus');}});
// Passkeys: a page's 'conditional' request waits for the dropdown, like Chrome's autofill.
let pendingPasskey=null;
if(navigator.credentials&&navigator.credentials.get&&window.PublicKeyCredential){
 const original=navigator.credentials.get.bind(navigator.credentials);
 navigator.credentials.get=function(o){
  if(!o||o.mediation!=='conditional'||!o.publicKey)return original(o);
  return new Promise((resolve,reject)=>{const entry={o,resolve,reject};pendingPasskey=entry;
   if(o.signal)o.signal.addEventListener('abort',()=>{if(pendingPasskey===entry)pendingPasskey=null;reject(o.signal.reason||new DOMException('The operation was aborted.','AbortError'));});soon();});
 };
 PublicKeyCredential.isConditionalMediationAvailable=()=>Promise.resolve(true);
 Object.defineProperty(window,'__micaUsePasskey',{value:()=>{const p=pendingPasskey;if(!p)return false;pendingPasskey=null;const o=Object.assign({},p.o);delete o.mediation;original(o).then(p.resolve,p.reject);return true;}});
}
if(document.readyState==='loading')addEventListener('DOMContentLoaded',watch);else watch();})();";

  static string FillScript(string origin,string username,string password,bool force=false) {
   var j=VaultCrypto.Json;
   return "(()=>{if(window.top!==window||location.origin!=="+j.Serialize(origin)+")return 'origin';"+
    "const shown=e=>e&&!e.disabled&&(e.offsetWidth>0||e.offsetHeight>0||e.getClientRects().length>0);"+
    "const set=(e,v)=>{Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(e,v);e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));};"+
    "const paymenty=e=>/^(cc-|transaction|one-time-code)/.test(e.autocomplete||'')||/card|cvv|cvc|csc|security.?code|expir|\\bpin\\b|otp|one.?time|verif|thẻ/i.test([e.name,e.id,e.placeholder,e.getAttribute('aria-label')].join(' '));"+
    "const p=[...document.querySelectorAll('input[type=password]')].find(e=>shown(e)&&!paymenty(e));const root=(p&&p.form)||document;"+
    "const searchy=e=>e.type==='search'||!!(e.closest&&e.closest('[role=search],[role=combobox],[role=searchbox]'))||/search|find|query|filter|t\u00ecm/i.test([e.name,e.id,e.placeholder,e.getAttribute('aria-label'),e.getAttribute('role')].join(' '));"+
    "const codey=e=>/one-time-code/.test(e.autocomplete||'')||/otp|2fa|mfa|one.?time|verif|auth.?code|security.?code|\\bcode\\b|\\bpin\\b|mã/i.test([e.name,e.id,e.placeholder,e.getAttribute('aria-label')].join(' '))||(e.maxLength>=4&&e.maxLength<=8&&/numeric|decimal|\\[0-9\\]|\\\\d/.test((e.inputMode||'')+' '+(e.getAttribute('pattern')||'')));"+
    "const all=[...root.querySelectorAll('input')].filter(i=>i.type!=='hidden'&&shown(i)&&!searchy(i)&&!codey(i));let u=all.find(i=>/username|email/.test(i.autocomplete||''));"+
    // Without a password field only a field that says it is a username or email is filled
    // (the page may have moved on, e.g. signed in, before the vault opened).
    "if(!u&&p){const at=all.indexOf(p);for(let i=at-1;i>=0;i--)if(['text','email','tel',''].includes(all[i].type)){u=all[i];break;}}"+
    "if(!u&&!p)u=all.find(i=>['text','email',''].includes(i.type)&&/^(identifier|email|username|login|user)$/i.test(i.name||i.id||''))||null;"+
    "if(u&&!p&&u.value&&!"+(force?"true":"false")+")return 'typed';"+
    "if(!p&&!u)return 'none';if(p&&p.value&&!"+(force?"true":"false")+")return 'typed';if(u&&"+j.Serialize(username)+")set(u,"+j.Serialize(username)+");if(p)set(p,"+j.Serialize(password)+");return 'filled';})()";
  }

  // A sign-in pop-up offers to save once it closes, so the card isn't hidden behind it.
  // Scripts that must be on a tab's very first page (login watching, fullscreen shim): their
  // registration is asynchronous, so the first navigation waits for it.
  static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CoreWebView2,List<Task>> pageScripts=new System.Runtime.CompilerServices.ConditionalWeakTable<CoreWebView2,List<Task>>();
  static void AddPageScript(CoreWebView2 core,string script) {pageScripts.GetOrCreateValue(core).Add(core.AddScriptToExecuteOnDocumentCreatedAsync(script));}
  static async Task PageScriptsReady(CoreWebView2 core) {List<Task> list;if(!pageScripts.TryGetValue(core,out list))return;try {await Task.WhenAll(list.ToArray());}catch(Exception){}}
  void WatchLogins(Func<bool> live,WebView2 view,CoreWebView2 core,Window popup=null) {
   if(privateMode)return;
   core.Settings.IsWebMessageEnabled=true;
   AddPageScript(core,LoginScript);AddPageScript(core,TotpScript);
   core.WebMessageReceived+=delegate(object sender,CoreWebView2WebMessageReceivedEventArgs e) {
    if(closing||!live())return;
    string origin;try {origin=VaultCrypto.Origin(e.Source);}catch(ArgumentException){return;} // HTTPS pages only.
    Dictionary<string,object> message;try {message=VaultCrypto.Json.Deserialize<Dictionary<string,object>>(e.WebMessageAsJson);}catch(Exception){return;}
    object kind;if(message==null||!message.TryGetValue("mica",out kind))return;
    if((kind as string)=="loginForm")OfferFill(live,view,origin,popup!=null);
    else if((kind as string)=="field")ShowLoginMenu(live,view,origin,message);
    else if((kind as string)=="fieldBlur")HideLoginMenu(view);
    else if((kind as string)=="otpField")FillCode(live,view,origin);
    else if((kind as string)=="totpKey"){object u,s,h;message.TryGetValue("uri",out u);message.TryGetValue("secret",out s);message.TryGetValue("user",out h);var uri=u as string??"";var secret=s as string??"";if(uri.Length<=1024&&secret.Length<=200)OfferSaveTotp(live,view,origin,uri,secret,h as string);}
    else if((kind as string)=="menuKey"){object key;message.TryGetValue("key",out key);LoginMenuKey(view,key as string);}
    else if((kind as string)=="credential"){
     object u,p;message.TryGetValue("username",out u);message.TryGetValue("password",out p);
     var username=u as string??"";var password=p as string;
     if(string.IsNullOrEmpty(password)||password.Length>65536||username.Length>4096)return;NoteRecentUser(origin,username);
     if(popup!=null&&popup.IsVisible){EventHandler later=null;later=delegate {popup.Closed-=later;window.Dispatcher.BeginInvoke(new Action(delegate {if(!closing)OfferSavePassword(origin,username,password,null);}));};popup.Closed+=later;}
     else OfferSavePassword(origin,username,password,null);
    }
   };
  }

  // Saved logins: the unlocked vault plus logins kept on this device (Windows-protected) while the
  // vault is locked. Saving and filling never ask for the master password.
  List<VaultEntry> SavedLogins(string origin) {
   var all=new List<VaultEntry>();if(sessionVault!=null)all.AddRange(sessionVault.Entries);
   foreach(var e in DeviceLogins.Load(profile))if(!all.Any(a=>VaultCrypto.Matches(a.Origin,e.Origin)&&a.Username==e.Username))all.Add(e);
   // A login kept only for its 2FA key has no password to fill.
   return all.Where(e=>VaultCrypto.Matches(e.Origin,origin)&&!string.IsNullOrEmpty(e.Password)).ToList();
  }
  List<string> NeverSave() {try {return File.Exists(NeverSavePath)?VaultCrypto.Json.Deserialize<List<string>>(File.ReadAllText(NeverSavePath))??new List<string>():new List<string>();}catch(Exception){return new List<string>();}}

  async void FillLogin(WebView2 view,string origin,VaultEntry entry,bool force=false) {
   var core=LiveCore(view);if(core==null||entry==null)return;NoteRecentUser(origin,entry.Username);
   try {string result=await core.ExecuteScriptAsync(FillScript(origin,entry.Username,entry.Password,force));if(fillLog!=null)fillLog.Add(DateTime.Now.ToString("ss.fff")+" fill result "+result);if(result=="\"filled\"")Status("Filled your saved password for "+new Uri(origin).Host+".");}catch(Exception){}
  }

  async void OfferFill(Func<bool> live,WebView2 view,string origin,bool quiet) {
   if(fillLog!=null)fillLog.Add(DateTime.Now.ToString("ss.fff")+" offer fill "+origin+" quiet="+quiet);
   Func<bool> alive=()=>!closing&&live()&&(quiet||view.IsVisible);
   if(sessionVault==null)await OpenAutofillVault();
   var saved=SavedLogins(origin);if(fillLog!=null)fillLog.Add(DateTime.Now.ToString("ss.fff")+" opened="+(sessionVault!=null)+" saved="+saved.Count+" alive="+alive());if(saved.Count==0)return;
   // Never filled straight from the page's message: its script could send one and read the password.
   // You pick the login in Mica's own bubble, which a page can't click.
   var choices=saved.Take(3).Select((e,i)=>new[]{string.IsNullOrEmpty(e.Username)?"(no username)":e.Username,i.ToString(),i==0?"primary":""}).ToArray();
   ShowChoiceBubble(Find<Button>("AddressButton"),"",saved.Count==1?"Sign in with your saved login":"Sign in with a saved login",new Uri(origin).Host,saved.Count>3?"Showing 3 of "+saved.Count+". Open Passwords for the rest.":null,null,choices,alive,
    delegate(string choice) {int index;if(choice!=null&&int.TryParse(choice,out index))FillLogin(view,origin,saved[index]);});
  }

  // Unlocks for page use: Windows Hello when it is set up, otherwise the master password.
  async Task<bool> UnlockSessionVault(Func<bool> alive,string master) {
   if(sessionVault!=null)return true;if(!File.Exists(VaultFile))return false;
   if(master==null&&await OpenAutofillVault())return true;
   try {
    if(master==null&&File.Exists(HelloUnlockStore.PathFor(profile))) {
     if(await helloVerifier.Verify(new System.Windows.Interop.WindowInteropHelper(window).Handle,()=>!closing))master=HelloUnlockStore.Load(profile);
     else {Status("Windows Hello didn't confirm. Your passwords stay locked.");return false;}
    }
    if(master==null) {
     var asked=new TaskCompletionSource<string>();var box=new PasswordBox {Margin=new Thickness(0,10,0,0),Padding=new Thickness(8,6,8,6)};
     box.Loaded+=delegate {box.Focus();Keyboard.Focus(box);};
     ShowChoiceBubble(Find<Button>("AddressButton"),"","Unlock your vault","Master password",null,null,new[]{new[]{"Cancel","cancel",""},new[]{"Unlock","unlock","primary"}},alive,
      delegate(string choice) {asked.TrySetResult(choice=="unlock"?box.Password:null);},box);
     master=await asked.Task;box.Clear();if(master==null)return false;
    }
    var envelope=VaultCrypto.Json.Deserialize<VaultEnvelope>(File.ReadAllText(VaultFile));string key=master;
    var opened=await Task.Run(()=>VaultCrypto.Decrypt(envelope,key));
    if(closing)return false;
    master=await Task.Run(()=>VaultCrypto.KeyFor(envelope,key));if(closing)return false;sessionVault=opened;sessionMaster=master;AutofillKeyStore.Save(profile,master);AdoptSessionEverywhere();MergeDeviceLogins();Status("Password filling is on for this device.");return true;
   }catch(System.Security.Cryptography.CryptographicException){Status("Wrong master password. Your passwords stay locked.");return false;}
   catch(Exception e){Status("Couldn't unlock the vault: "+e.Message);return false;}
  }
  void AdoptSessionEverywhere() {foreach(var browser in Program.Browsers.Where(b=>!b.closing&&!b.privateMode&&b.profile==profile))if(browser.vaultAdoptSession!=null)browser.vaultAdoptSession();}
  // Silent: uses the device-protected key, never asks. One open at a time; callers share it.
  static Task<bool> openingVault;
  Task<bool> OpenAutofillVault() {
   if(sessionVault!=null)return Task.FromResult(true);if(privateMode||!File.Exists(VaultFile)||!AutofillKeyStore.Exists(profile))return Task.FromResult(false);
   if(openingVault==null||openingVault.IsCompleted)openingVault=OpenAutofillVaultOnce();
   return openingVault;
  }
  async Task<bool> OpenAutofillVaultOnce() {
   try {
    string master=await Task.Run(()=>AutofillKeyStore.Load(profile));var envelope=VaultCrypto.Json.Deserialize<VaultEnvelope>(File.ReadAllText(VaultFile));
    var opened=await Task.Run(()=>VaultCrypto.Decrypt(envelope,master));if(closing)return false;
    if(sessionVault==null){sessionVault=opened;sessionMaster=master;}MergeDeviceLogins();return true;
   }catch(Exception){AutofillKeyStore.Forget(profile);return false;} // The master password changed elsewhere.
  }
  // Called by the Passwords window whenever it has the vault open.
  void ShareVault(VaultPayload payload,string master) {
   if(payload==null||string.IsNullOrEmpty(master))return;
   sessionVault=payload;sessionMaster=master;try {AutofillKeyStore.Save(profile,master);}catch(Exception){}MergeDeviceLogins();
  }

  // The save card: username and password stay editable, and nothing is kept unless Save is chosen.
  // No master password: a locked (or not yet created) vault saves to this device instead.
  async void OfferSavePassword(string origin,string username,string password,string problem) {
   if(NeverSave().Contains(origin))return;
   if(sessionVault==null)await OpenAutofillVault();if(closing)return;
   var same=SavedLogins(origin).FirstOrDefault(e=>e.Username==username);if(same==null&&sessionVault!=null)same=sessionVault.Entries.FirstOrDefault(e=>VaultCrypto.Matches(e.Origin,origin)&&e.Username==username);
   if(same!=null&&same.Password==password)return;
   bool update=same!=null;
   var form=new StackPanel {Margin=new Thickness(0,10,0,0)};
   Func<string,TextBlock> caption=text=>{var t=DialogText(text,11,"Muted");t.Margin=new Thickness(0,6,0,4);return t;};
   var user=new TextBox {Text=username,Tag="password-username"};
   var secret=new PasswordBox {Password=password,Tag="password-secret"};
   var plain=new TextBox {Text=password,Visibility=Visibility.Collapsed};
   var reveal=new CheckBox {Content="Show password",Margin=new Thickness(0,10,0,0),FontSize=11};
   reveal.Checked+=delegate {plain.Text=secret.Password;plain.Visibility=Visibility.Visible;secret.Visibility=Visibility.Collapsed;};
   reveal.Unchecked+=delegate {secret.Password=plain.Text;secret.Visibility=Visibility.Visible;plain.Visibility=Visibility.Collapsed;};
   form.Children.Add(caption("Username"));form.Children.Add(user);form.Children.Add(caption("Password"));form.Children.Add(secret);form.Children.Add(plain);form.Children.Add(reveal);
   ShowChoiceBubble(Find<Button>("AddressButton"),"",update?"Update saved password?":"Save password?",new Uri(origin).Host,problem,null,
    new[]{new[]{"Never","never","","Never save passwords for this site"},new[]{"Don't save","skip",""},new[]{update?"Update":"Save","save","primary"}},()=>!closing,
    async delegate(string choice) {
     string name=user.Text,value=reveal.IsChecked==true?plain.Text:secret.Password;
     secret.Clear();plain.Clear();
     if(choice=="never"){var list=NeverSave();if(!list.Contains(origin)){list.Add(origin);try {File.WriteAllText(NeverSavePath,VaultCrypto.Json.Serialize(list));}catch(Exception){}}Status("Mica won't offer to save passwords for "+new Uri(origin).Host+".");return;}
     if(choice!="save")return;
     if(string.IsNullOrEmpty(value)){OfferSavePassword(origin,name,value,"Enter a password to save.");return;}
     await StoreLogin(origin,name,value);
    },form);
  }

  async Task StoreLogin(string origin,string username,string password) {
   if(privateMode)return;
   string host=new Uri(origin).Host;
   if(sessionVault==null&&!File.Exists(VaultFile)) {
    // First save ever: make the vault right away with a key kept on this device. The master
    // password (a passphrase for viewing) is chosen later, in Passwords.
    var deviceKey=VaultCrypto.RandomVerifier()+VaultCrypto.RandomVerifier();
    try {AutofillKeyStore.Save(profile,deviceKey=VaultCrypto.NewDeviceKey());File.WriteAllText(PassphrasePendingPath,"");}catch(Exception e){Status("Couldn't save the password: "+e.Message);return;}
    sessionVault=new VaultPayload();sessionMaster=deviceKey;
   }
   if(sessionVault==null) {
    var login=new VaultEntry {Id=Guid.NewGuid().ToString("N"),Origin=VaultCrypto.Origin(origin),Username=username,Password=password};
    try {
     // Locked vault: seal the login into it; only the master password (or this device's key) opens it.
     if(await Task.Run(()=>VaultCrypto.AddToInbox(VaultFile,login))){Status("Password saved to your vault for "+host+".");return;}
     DeviceLogins.Upsert(profile,origin,username,password);Status("Password saved for "+host+". It joins your vault when you next open Passwords.");
    }catch(Exception e){Status("Couldn't save the password: "+e.Message);}
    return;
   }
   var entry=sessionVault.Entries.FirstOrDefault(e=>VaultCrypto.Matches(e.Origin,origin)&&e.Username==username);
   if(entry==null){entry=new VaultEntry {Id=Guid.NewGuid().ToString("N"),Origin=origin,Username=username};sessionVault.Entries.Add(entry);}
   entry.Password=password;VaultCrypto.Touch(sessionVault,entry);
   if(await SaveSessionVault())Status("Password saved to your vault for "+host+".");
  }
  async Task<bool> SaveSessionVault(bool noteChange=true) {
   try {
    var data=VaultCrypto.Json.Deserialize<VaultPayload>(VaultCrypto.Json.Serialize(sessionVault));string key=sessionMaster;
    var envelope=await Task.Run(()=>VaultCrypto.Encrypt(data,key));if(sessionVault!=null&&string.IsNullOrEmpty(sessionVault.InboxPrivate))sessionVault.InboxPrivate=data.InboxPrivate;
    if(!privateMode){VaultCrypto.Save(VaultFile,envelope);if(noteChange)NoteVaultChange();}
    AdoptSessionEverywhere();return true;
   }catch(Exception e){Status("Couldn't save the password: "+e.Message);return false;}
  }
  // Once the vault is open, logins saved on this device while it was locked move into it.
  async void MergeDeviceLogins() {
   if(sessionVault==null||privateMode)return;SyncVaultOnceOpened();var waiting=DeviceLogins.Load(profile);
   bool inbox=false;try {if(File.Exists(VaultFile)){var stored=VaultCrypto.Json.Deserialize<VaultEnvelope>(File.ReadAllText(VaultFile));inbox=VaultCrypto.HasInbox(stored)||string.IsNullOrEmpty(stored.inboxKey);}}catch(Exception){}
   if(waiting.Count==0){if(inbox)await SaveSessionVault();return;}
   foreach(var login in waiting) {
    var entry=sessionVault.Entries.FirstOrDefault(e=>VaultCrypto.Matches(e.Origin,login.Origin)&&e.Username==login.Username);
    if(entry==null){entry=new VaultEntry {Id=Guid.NewGuid().ToString("N"),Origin=login.Origin,Username=login.Username};sessionVault.Entries.Add(entry);}
    entry.Password=login.Password;VaultCrypto.Touch(sessionVault,entry);
   }
   if(await SaveSessionVault())DeviceLogins.Clear(profile);
  }
 }
 // Logins saved while the vault is locked or not created yet, protected by the Windows account
 // (the same protection as the autofill key). They move into the vault when it is next opened.
 static class DeviceLogins {
  static byte[] Entropy(string profile) {using(var sha=System.Security.Cryptography.SHA256.Create())return sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("Mica device logins v1|"+Path.GetFullPath(profile).ToUpperInvariant()));}
  internal static string PathFor(string profile) {return Path.Combine(profile,"logins-device.bin");}
  internal static List<VaultEntry> Load(string profile) {
   try {
    if(!File.Exists(PathFor(profile)))return new List<VaultEntry>();
    byte[] plain=System.Security.Cryptography.ProtectedData.Unprotect(File.ReadAllBytes(PathFor(profile)),Entropy(profile),System.Security.Cryptography.DataProtectionScope.CurrentUser);
    try {return VaultCrypto.Json.Deserialize<List<VaultEntry>>(System.Text.Encoding.UTF8.GetString(plain))??new List<VaultEntry>();}finally {Array.Clear(plain,0,plain.Length);}
   }catch(Exception){return new List<VaultEntry>();}
  }
  internal static void Upsert(string profile,string origin,string username,string password) {UpsertAll(profile,new[]{new VaultEntry {Origin=origin,Username=username,Password=password}});}
  // Many at once (an imported passwords file): one read, one write.
  internal static void UpsertAll(string profile,IEnumerable<VaultEntry> logins) {
   var all=Load(profile);
   foreach(var login in logins) {
    var entry=all.FirstOrDefault(e=>VaultCrypto.Matches(e.Origin,login.Origin)&&e.Username==login.Username);
    if(entry==null){entry=new VaultEntry {Id=Guid.NewGuid().ToString("N"),Origin=login.Origin,Username=login.Username};all.Add(entry);}
    entry.Password=login.Password;entry.Saved=VaultCrypto.Now();
   }
   byte[] plain=System.Text.Encoding.UTF8.GetBytes(VaultCrypto.Json.Serialize(all));
   try {var data=System.Security.Cryptography.ProtectedData.Protect(plain,Entropy(profile),System.Security.Cryptography.DataProtectionScope.CurrentUser);string path=PathFor(profile),temp=path+".tmp";File.WriteAllBytes(temp,data);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
   finally {Array.Clear(plain,0,plain.Length);}
  }
  internal static void Clear(string profile) {try {File.Delete(PathFor(profile));}catch(Exception){}}
 }
 // This device's vault key (VaultCrypto.IsKey: salt and derived key, never the master password), protected by
 // the Windows account, so autofill works without unlocking. Locking removes the file until the next unlock.
 static class AutofillKeyStore {
  static byte[] Entropy(string profile,int version) {using(var sha=System.Security.Cryptography.SHA256.Create())return sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("Mica autofill key v"+version+"|"+Path.GetFullPath(profile).ToUpperInvariant()));}
  static string PathFor(string profile) {return Path.Combine(profile,"autofill-vault-key.bin");}
  static string LegacyPath(string profile) {return Path.Combine(profile,"autofill-key.bin");}
  internal static bool Exists(string profile) {return File.Exists(PathFor(profile))||File.Exists(LegacyPath(profile));}
  internal static void Save(string profile,string key) {
   if(!VaultCrypto.IsKey(key))throw new ArgumentException("Only a vault key is kept on this device.");
   byte[] plain=System.Text.Encoding.UTF8.GetBytes(key);
   try {var data=System.Security.Cryptography.ProtectedData.Protect(plain,Entropy(profile,2),System.Security.Cryptography.DataProtectionScope.CurrentUser);string path=PathFor(profile),temp=path+".tmp";File.WriteAllBytes(temp,data);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
   finally {Array.Clear(plain,0,plain.Length);}
   Erase(LegacyPath(profile));
  }
  internal static string Load(string profile) {
   if(!File.Exists(PathFor(profile))&&File.Exists(LegacyPath(profile)))Migrate(profile);
   byte[] plain=System.Security.Cryptography.ProtectedData.Unprotect(File.ReadAllBytes(PathFor(profile)),Entropy(profile,2),System.Security.Cryptography.DataProtectionScope.CurrentUser);
   try {var key=System.Text.Encoding.UTF8.GetString(plain);if(!VaultCrypto.IsKey(key))throw new System.Security.Cryptography.CryptographicException("Invalid device key.");return key;}finally {Array.Clear(plain,0,plain.Length);}
  }
  // Older versions kept the master password itself here: swap it for the vault key, then erase it either way.
  static void Migrate(string profile) {
   string legacy=null;var vault=Path.Combine(profile,"vault.enc.json");
   try {
    byte[] plain=System.Security.Cryptography.ProtectedData.Unprotect(File.ReadAllBytes(LegacyPath(profile)),Entropy(profile,1),System.Security.Cryptography.DataProtectionScope.CurrentUser);
    try {legacy=System.Text.Encoding.UTF8.GetString(plain);}finally {Array.Clear(plain,0,plain.Length);}
    if(File.Exists(vault))Save(profile,VaultCrypto.KeyFor(VaultCrypto.Json.Deserialize<VaultEnvelope>(File.ReadAllText(vault)),legacy));
   }finally {legacy=null;Erase(LegacyPath(profile));}
  }
  static void Erase(string path) {try {if(File.Exists(path)){File.WriteAllBytes(path,new byte[new FileInfo(path).Length]);File.Delete(path);}}catch(Exception){}}
  internal static void Forget(string profile) {Erase(PathFor(profile));Erase(LegacyPath(profile));}
 }
}