(()=>{if(window.top!==window||location.protocol!=='https:'||typeof micaLogin==='undefined')return;if(window.__micaTotp)return;window.__micaTotp=1;
const post=m=>{try{micaLogin.postMessage(JSON.stringify(m))}catch(e){}};
const shown=e=>e&&!e.disabled&&(e.offsetWidth>0||e.offsetHeight>0||e.getClientRects().length>0);
const words=e=>[e.name,e.id,e.placeholder,e.getAttribute('aria-label'),e.autocomplete,e.className].join(' ');
const notCode=/cvv|cvc|csc|card|thẻ|zip|postal|coupon|promo|discount|gift|referral|captcha|invite|phone|search|tìm/i;
// One field for the whole code, or a row of one-digit boxes.
// Attributes first; whether it's on screen (which makes the page measure itself) only for a likely match.
const single=e=>{if(!e||e.tagName!=='INPUT'||e.readOnly||e.value||e.disabled)return false;if(!['text','tel','number','password',''].includes(e.type))return false;
 const w=words(e);if(notCode.test(w))return false;
 const m=e.maxLength;const likely=/one-time-code/.test(e.autocomplete||'')||((m===-1||(m>=6&&m<=8))&&/otp|totp|2fa|two.?factor|mfa|authenticat|verif|one.?time|app.?code|security.?code|\bcode\b|pin.?code|mã|xác/i.test(w));
 return likely&&shown(e);};
const boxes=()=>{for(const e of document.querySelectorAll('input[maxlength="1"]')){if(e.value||notCode.test(words(e))||!shown(e))continue;
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
 for(const a of document.querySelectorAll('a[href^="otpauth:"],[data-otpauth],input[value^="otpauth:"]')){const v=a.getAttribute('href')||a.getAttribute('data-otpauth')||a.value;if(/^otpauth:\/\/totp\//i.test(v))report(v,'');}
 const body=document.body;if(!body)return;const text=body.innerText||'';
 const link=text.match(/otpauth:\/\/totp\/[^\s"'<>]+/i);if(link)report(link[0],'');
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
if(document.readyState==='loading')addEventListener('DOMContentLoaded',start);else start();})();