(()=>{if(window.top!==window||location.protocol!=='https:'||typeof micaLogin==='undefined')return;if(window.__micaLogin)return;window.__micaLogin=1;
const post=m=>{try{micaLogin.postMessage(JSON.stringify(m))}catch(e){}};
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
if(document.readyState==='loading')addEventListener('DOMContentLoaded',watch);else watch();})();