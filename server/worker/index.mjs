import { createHash, randomBytes, timingSafeEqual } from 'node:crypto';

const hash = value => createHash('sha256').update(value).digest('base64url');
const random = () => randomBytes(32).toString('base64url');
const object = value => value && typeof value === 'object' && !Array.isArray(value);
const exact = (value, keys) => object(value) && Object.keys(value).length === keys.length && keys.every(k => Object.hasOwn(value, k));
const fail = (status, code) => Object.assign(new Error(code), {status, code});
const encoded = (value, length) => typeof value === 'string' && /^[A-Za-z0-9+/]+={0,2}$/.test(value) && Buffer.from(value, 'base64').toString('base64') === value && (length ? Buffer.from(value, 'base64').length === length : Buffer.from(value, 'base64').length > 0 && Buffer.from(value, 'base64').length % 16 === 0);
// Plain, non-secret browser preferences: only the toggles Mica syncs (BrowserSettingsSync.SettingsSnapshot), as booleans.
// Secrets stay in the encrypted vault.
const SettingKeys = ['dark', 'ambientYoutube', 'strictTracking'];
export function validSettings(s) {
  return object(s) && Object.entries(s).every(([k,v])=>SettingKeys.includes(k) && typeof v==='boolean');
}
// Send tab to device: each Mica install names itself; a sent tab waits for that device to pick it up.
export const deviceId = v => typeof v==='string' && /^[A-Za-z0-9_-]{16,43}$/.test(v);
export const deviceName = v => typeof v==='string' && v.trim().length>0 && v.length<=64 && !/[\u0000-\u001f]/.test(v);
export function validSentTab(t) {
  if (!exact(t,['to','from','url','title']) || !deviceId(t.to) || !deviceName(t.from) || typeof t.title!=='string' || t.title.length>300 || typeof t.url!=='string' || t.url.length>4096) return false;
  try {const u=new URL(t.url);return (u.protocol==='https:'||u.protocol==='http:') && !u.username && !u.password;} catch {return false;}
}
// Not exported: a module worker's named exports must all be handlers, and workerd refuses numbers.
const DeviceSeenMs = 60*24*3600*1000, PendingTabLimit = 50;
// Sign-in confirmation code: Mica shows it, and the browser page that finishes Discord sign-in asks for it,
// so a sign-in link someone else started can't be completed by a victim who only clicks through.
// 8 characters without look-alikes (0/O, 1/I/L), shown as XXXX-XXXX.
const CodeAlphabet = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789', CodeAttempts = 5;
export function signinCode() {
  let code = '';
  while (code.length < 8) for (const b of randomBytes(16)) if (b < 248 && code.length < 8) code += CodeAlphabet[b % 31];
  return code.slice(0, 4) + '-' + code.slice(4);
}
export const normalizeCode = v => typeof v === 'string' ? v.toUpperCase().replace(/[^A-Z0-9]/g, '') : '';
const escapeHtml = v => String(v).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const PageStyle = `:root{color-scheme:light dark;--bg:#e4ddea;--card:#f9f7f3;--ink:#2a2433;--muted:#6c6477;--accent:#6b4f8f;--accent-ink:#fff;--warn:#b0413a;--ok:#3f8f5a}
@media (prefers-color-scheme:dark){:root{--bg:#1d1824;--card:#262030;--ink:#efebf4;--muted:#a69db3;--accent:#c9b3ea;--accent-ink:#1d1824;--warn:#f0a090;--ok:#8fd3a2}}
*{box-sizing:border-box}
body{margin:0;min-height:100vh;display:grid;place-items:center;background:var(--bg);color:var(--ink);font:16px/1.5 "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif}
main{background:var(--card);border-radius:22px;padding:32px clamp(18px,6vw,34px) 28px;width:min(440px,calc(100% - 32px));margin:16px;box-shadow:0 30px 60px -30px rgba(0,0,0,.45);text-align:center;animation:rise .55s cubic-bezier(.2,.7,.2,1) both}
h1{font:600 26px/1.15 "Segoe UI Variable Display","Segoe UI",system-ui,sans-serif;letter-spacing:-.02em;margin:0 0 8px}
p{margin:8px 0;color:var(--muted)}.warn{color:var(--warn)}.note{font-size:13px;margin-top:18px}
form{margin:0}
.code{position:relative;margin:22px 0 8px}
.code input{font:600 24px ui-monospace,Consolas,monospace;letter-spacing:.12em;text-transform:uppercase;text-align:center;width:100%;padding:10px 12px;border-radius:12px;border:1px solid var(--muted);background:transparent;color:var(--ink)}
.cells{display:none}
.code.live input{position:absolute;inset:0;width:100%;height:100%;opacity:0;font-size:16px;color:transparent;caret-color:transparent;cursor:text;z-index:1}
.code.live .cells{display:flex;align-items:center;justify-content:center;gap:clamp(4px,1.4vw,7px)}
.cell{width:clamp(30px,8.4vw,42px);height:clamp(42px,11vw,54px);border-radius:12px;display:grid;place-items:center;font:600 clamp(20px,5.4vw,26px) ui-monospace,Consolas,monospace;background:color-mix(in srgb,var(--ink) 5%,transparent);box-shadow:inset 0 0 0 1.5px color-mix(in srgb,var(--ink) 14%,transparent);transition:box-shadow .2s,background .2s}
.cell.filled{background:color-mix(in srgb,var(--accent) 10%,transparent)}
.cell.active{box-shadow:inset 0 0 0 2px var(--accent),0 0 0 4px color-mix(in srgb,var(--accent) 22%,transparent)}
.cell.active::after{content:"";width:2px;height:46%;border-radius:1px;background:var(--accent);animation:blink 1.05s steps(1) infinite}
.cell.pop{animation:pop .24s cubic-bezier(.3,1.7,.5,1)}
.dash{color:var(--muted);font-weight:600;font-size:22px;width:10px}
.code.complete .cell{box-shadow:inset 0 0 0 2px var(--accent)}
.code.checking .cell{animation:wave .9s ease-in-out infinite;animation-delay:calc(var(--i) * 70ms)}
.code.shake{animation:shake .5s cubic-bezier(.36,.07,.19,.97)}
.code.shake .cell{box-shadow:inset 0 0 0 2px var(--warn);background:color-mix(in srgb,var(--warn) 10%,transparent)}
.row{display:flex;gap:10px;flex-wrap:wrap;justify-content:center;margin-top:20px}
button{font:inherit;font-weight:600;font-size:15px;padding:11px 22px;border-radius:980px;border:0;cursor:pointer;background:var(--accent);color:var(--accent-ink);transition:filter .2s,transform .2s}
button:hover{filter:brightness(1.08)}button:active{transform:scale(.98)}
button.danger{background:var(--warn);color:#fff}
button.plain{background:transparent;color:var(--muted);box-shadow:inset 0 0 0 1.5px color-mix(in srgb,var(--muted) 60%,transparent)}
.mark{width:68px;height:68px;margin:0 auto 14px}
.mark svg{width:100%;height:100%;fill:none;stroke-width:3.5;stroke-linecap:round;stroke-linejoin:round}
.mark circle{stroke-dasharray:151;stroke-dashoffset:151;animation:draw .6s .1s cubic-bezier(.65,0,.45,1) forwards}
.mark path{stroke-dasharray:40;stroke-dashoffset:40;animation:draw .35s .6s cubic-bezier(.65,0,.45,1) forwards}
.mark.ok{color:var(--ok)}.mark.stop{color:var(--muted)}.mark.bad{color:var(--warn)}
.mark svg *{stroke:currentColor}
@keyframes rise{from{opacity:0;transform:translateY(16px) scale(.98)}}
@keyframes pop{0%{transform:scale(.55);opacity:.2}100%{transform:none;opacity:1}}
@keyframes blink{50%{opacity:0}}
@keyframes wave{0%,100%{transform:none}50%{transform:translateY(-6px)}}
@keyframes shake{10%,90%{transform:translateX(-2px)}20%,80%{transform:translateX(4px)}30%,50%,70%{transform:translateX(-8px)}40%,60%{transform:translateX(8px)}}
@keyframes draw{to{stroke-dashoffset:0}}
@media (prefers-reduced-motion:reduce){*,*::after{animation-duration:.01ms!important;animation-iteration-count:1!important;transition:none!important}.mark circle,.mark path{stroke-dashoffset:0}}`;

// Mirrors the real (invisible) input into the code cells: typing or pasting "abcd-2345", "ABCD 2345" or
// "abcd2345" all fill the cells; a full code submits itself. Without this script the plain input still works.
const PageScript = `(function(){var box=document.querySelector("[data-code]");if(!box)return;box.classList.add("live");
var input=box.querySelector("input"),cells=[].slice.call(box.querySelectorAll(".cell")),form=box.closest("form"),sent=false;
cells.forEach(function(c,i){c.style.setProperty("--i",i);});
function sync(){var v=input.value.toUpperCase().replace(/[^A-Z0-9]/g,"").slice(0,8);if(input.value!==v)input.value=v;
var focused=document.activeElement===input;
cells.forEach(function(c,i){var ch=v[i]||"";if(c.textContent!==ch){c.textContent=ch;if(ch){c.classList.remove("pop");void c.offsetWidth;c.classList.add("pop");}}
c.classList.toggle("filled",!!ch);c.classList.toggle("active",focused&&v.length<8&&i===v.length);});
box.classList.toggle("complete",v.length===8);
if(v.length===8&&!sent){sent=true;setTimeout(function(){box.classList.add("checking");var b=form.querySelector("button[value=confirm]");if(form.requestSubmit)form.requestSubmit(b);else b.click();},380);}
if(v.length<8)sent=false;}
input.addEventListener("input",function(){box.classList.remove("shake");sync();});
input.addEventListener("focus",sync);input.addEventListener("blur",sync);
form.addEventListener("submit",function(e){if(e.submitter&&e.submitter.value==="confirm")box.classList.add("checking");});
input.focus();sync();})();`;

function page(title, content, nonce) {
  return '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>'+escapeHtml(title)+'</title>'
    +'<style nonce="'+nonce+'">'+PageStyle+'</style></head><body><main>'+content+'</main><script nonce="'+nonce+'">'+PageScript+'</script></body></html>';
}
// A finished state: an animated mark (tick, cross or stop) above a heading and a line of text.
function statusPage(kind, heading, text) {
  const shape = kind==='ok' ? '<path d="M16 27l7 7 14-15"/>' : kind==='bad' ? '<path d="M19 19l14 14M33 19L19 33"/>' : '<path d="M18 26h16"/>';
  return '<div class="mark '+kind+'" aria-hidden="true"><svg viewBox="0 0 52 52"><circle cx="26" cy="26" r="24"/>'+shape+'</svg></div><h1>'+escapeHtml(heading)+'</h1><p>'+escapeHtml(text)+'</p>';
}
function confirmForm(form, username, error) {
  const cells = i => '<span class="cell"></span>'.repeat(i);
  return '<h1>Confirm Mica sign-in</h1><p>Signing in as <strong>'+escapeHtml(username)+'</strong>. Type or paste the code Mica is showing on your computer.</p>'
    +(error?'<p class="warn" role="alert">'+escapeHtml(error)+'</p>':'')
    +'<form method="post" action="/v1/auth/discord/confirm"><input type="hidden" name="form" value="'+escapeHtml(form)+'">'
    +'<div class="code'+(error?' shake':'')+'" data-code><input id="code" name="code" aria-label="Code from Mica" autocomplete="one-time-code" autocapitalize="characters" spellcheck="false" maxlength="24" placeholder="XXXX-XXXX" required>'
    +'<div class="cells" aria-hidden="true">'+cells(4)+'<span class="dash">-</span>'+cells(4)+'</div></div>'
    +'<div class="row"><button type="submit" name="action" value="confirm">Confirm</button><button type="submit" name="action" value="deny" class="plain" formnovalidate>I didn\'t start this</button></div></form>'
    +'<p class="warn note">Only enter a code shown in your own Mica window. If someone sent you this link or a code, choose “I didn\'t start this”.</p>';
}
function deleteForm(form, username) {
  return '<h1>Delete your Mica account?</h1><p>Signed in as <strong>'+escapeHtml(username)+'</strong>.</p>'
    +'<p>This permanently deletes the account and everything stored with it: the encrypted password vault, synced settings, devices, sent tabs and the sign-in on every device. It can\'t be undone.</p>'
    +'<form method="post" action="/v1/account/delete/confirm"><input type="hidden" name="form" value="'+escapeHtml(form)+'">'
    +'<div class="row"><button type="submit" name="action" value="delete" class="danger">Delete account</button><button type="submit" name="action" value="cancel" class="plain">Cancel</button></div></form>'
    +'<p class="note">Browsing data and passwords saved on your devices stay there. Remove them in Mica or by uninstalling it.</p>';
}
// Auto-reply to mail sent to the deletion address. Mail can't prove which Discord account is yours, so it only
// points to the signed-in deletion page; nothing is deleted from an email.
function deletionReply(message, link, help = message.to) {
  const text = 'Hi,\r\n\r\nWe received a request to delete a Mica account from this address.\r\n\r\n'
    +'An email can\'t prove which Discord account is yours, so deletion is done by signing in with it:\r\n\r\n  '+link+'\r\n\r\n'
    +'Open the link, sign in with the Discord account you use in Mica, and choose Delete account. The account and everything stored with it are deleted straight away.\r\n\r\n'
    +'If you can no longer sign in to that Discord account, email '+help+' with your Discord username and we will help.\r\n\r\n'
    +'Xin chào, để xoá tài khoản Mica, hãy mở liên kết trên, đăng nhập bằng tài khoản Discord bạn dùng trong Mica và chọn Delete account.\r\n\r\n- Mica · LessNoise by cloudcode\r\n';
  const subject = (message.headers.get('subject') || 'Delete my Mica account').replace(/[\r\n]/g, ' ').slice(0, 200);
  const id = message.headers.get('message-id'), boundary = 'mica-'+random();
  const b64 = s => Buffer.from(s).toString('base64').replace(/.{76}/g, '$&\r\n');
  return ['From: Mica <'+message.to+'>', 'To: '+message.from, 'Subject: =?UTF-8?B?'+Buffer.from((/^re:/i.test(subject)?'':'Re: ')+subject).toString('base64')+'?=',
    'Date: '+new Date().toUTCString(), 'Message-ID: <'+random()+'@'+message.to.split('@')[1]+'>',
    ...(id ? ['In-Reply-To: '+id, 'References: '+id] : []), 'Reply-To: '+help, 'Auto-Submitted: auto-replied', 'MIME-Version: 1.0',
    'Content-Type: multipart/alternative; boundary="'+boundary+'"', '',
    '--'+boundary, 'Content-Type: text/plain; charset=utf-8', 'Content-Transfer-Encoding: base64', '', b64(text),
    '--'+boundary, 'Content-Type: text/html; charset=utf-8', 'Content-Transfer-Encoding: base64', '', b64(deletionReplyHtml(link, message.to, help)),
    '--'+boundary+'--', ''].join('\r\n');
}
// The HTML side of that reply: tables and inline styles so it holds up in Gmail and Outlook, with a dark
// palette for clients that honour prefers-color-scheme. Only our own link and address go into it.
function deletionReplyHtml(link, address, help) {
  const href = escapeHtml(link), mail = escapeHtml(address), helpMail = '<a class="link" href="mailto:'+escapeHtml(help)+'" style="color:#6b4f8f">'+escapeHtml(help)+'</a>', font = "'Segoe UI',-apple-system,BlinkMacSystemFont,Roboto,Helvetica,Arial,sans-serif";
  const step = (n, title, text) => '<tr><td width="40" valign="top" style="padding:0 0 18px"><div class="num" style="width:28px;height:28px;border-radius:14px;background:#efe8f7;color:#6b4f8f;font:600 14px/28px '+font+';text-align:center">'+n+'</div></td>'
    +'<td valign="top" style="padding:3px 0 18px;font:15px/1.5 '+font+';color:#2a2433" class="ink"><strong>'+title+'</strong><br><span class="muted" style="color:#6c6477">'+text+'</span></td></tr>';
  return '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">'
    +'<meta name="color-scheme" content="light dark"><meta name="supported-color-schemes" content="light dark"><title>Delete your Mica account</title>'
    +'<style>@media (prefers-color-scheme:dark){.bg{background:#16121c!important}.card{background:#231d2b!important}.ink{color:#efebf4!important}.muted{color:#a69db3!important}'
    +'.num{background:#3a2f48!important;color:#d9c8f2!important}.btn{background:#c9b3ea!important}.btn a{color:#1d1824!important}.box{background:#2c2536!important}.line{border-color:#3a3245!important}.link{color:#c9b3ea!important}}'
    +'@media (max-width:520px){.pad{padding:28px 22px!important}h1{font-size:24px!important}}</style></head>'
    +'<body class="bg" style="margin:0;padding:0;background:#f2eef6;-webkit-text-size-adjust:100%">'
    +'<div style="display:none;max-height:0;overflow:hidden;opacity:0">Sign in with Discord to delete your Mica account. Nothing is deleted from an email.</div>'
    +'<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" class="bg" style="background:#f2eef6"><tr><td align="center" style="padding:36px 14px">'
    +'<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:520px">'
    // Brand
    +'<tr><td align="center" style="padding:0 0 22px"><img src="https://lessnoise.online/assets/Mica.png" width="44" height="44" alt="Mica" style="display:block;border:0;border-radius:11px"></td></tr>'
    // Card
    +'<tr><td class="card pad" style="background:#ffffff;border-radius:22px;padding:38px 36px 32px">'
    +'<p class="muted" style="margin:0 0 6px;font:600 13px/1.4 '+font+';letter-spacing:.04em;text-transform:uppercase;color:#6b4f8f">Mica account</p>'
    +'<h1 class="ink" style="margin:0 0 14px;font:600 28px/1.2 '+font+';letter-spacing:-.02em;color:#2a2433">Delete your account.</h1>'
    +'<p class="muted" style="margin:0 0 26px;font:16px/1.55 '+font+';color:#6c6477">We got a request to delete a Mica account from this address. An email can\'t prove which Discord account is yours, so you delete it by signing in with Discord.</p>'
    +'<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">'
    +step(1, 'Open the deletion page', 'Use the button below, in any browser.')
    +step(2, 'Sign in with Discord', 'The same Discord account you use in Mica.')
    +step(3, 'Choose Delete account', 'Your account and everything stored with it are deleted straight away, and every device is signed out.')
    +'</table>'
    // Button
    +'<table role="presentation" cellpadding="0" cellspacing="0" border="0" style="margin:8px 0 14px"><tr><td class="btn" align="center" style="background:#6b4f8f;border-radius:980px">'
    +'<a href="'+href+'" style="display:inline-block;padding:13px 28px;font:600 16px/1.2 '+font+';color:#ffffff;text-decoration:none;border-radius:980px">Delete my account</a></td></tr></table>'
    +'<p class="muted" style="margin:0 0 26px;font:13px/1.5 '+font+';color:#6c6477">Or open <a class="link" href="'+href+'" style="color:#6b4f8f;word-break:break-all">'+href+'</a></p>'
    // Notes
    +'<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr><td class="box" style="background:#f7f4fa;border-radius:14px;padding:16px 18px;font:14px/1.5 '+font+';color:#6c6477">'
    +'<span class="muted"><strong class="ink" style="color:#2a2433">No Mica account?</strong> Nothing happens. After signing in you\'ll see that there\'s nothing to delete.</span><br><br>'
    +'<span class="muted"><strong class="ink" style="color:#2a2433">Can\'t sign in to Discord any more?</strong> Email '+helpMail+' with your Discord username and we\'ll help.</span></td></tr></table>'
    // Vietnamese
    +'<p class="muted line" style="margin:28px 0 0;padding:22px 0 0;border-top:1px solid #ece6f2;font:14px/1.55 '+font+';color:#6c6477" lang="vi">'
    +'<strong class="ink" style="color:#2a2433">Tiếng Việt.</strong> Để xoá tài khoản Mica, bấm <a class="link" href="'+href+'" style="color:#6b4f8f">Delete my account</a>, đăng nhập bằng tài khoản Discord bạn dùng trong Mica rồi chọn Delete account. Tài khoản và dữ liệu bị xoá ngay. Không đăng nhập được Discord nữa thì gửi email tới '+helpMail+' kèm tên Discord.</p>'
    +'</td></tr>'
    // Footer
    +'<tr><td align="center" class="muted" style="padding:22px 10px 0;font:12px/1.6 '+font+';color:#8a8295">'
    +'Mica · LessNoise by cloudcode · <a class="link" href="https://lessnoise.online/" style="color:#8a8295">lessnoise.online</a> · <a class="link" href="https://lessnoise.online/privacy/" style="color:#8a8295">Privacy</a><br>'
    +'This is an automatic reply to your email to '+mail+'.</td></tr>'
    +'</table></td></tr></table></body></html>';
}
export function validEnvelope(e) {
  return exact(e, ['format', 'kdf', 'iterations', 'salt', 'iv', 'ciphertext', 'mac']) && e.format === 1 && e.kdf === 'PBKDF2-SHA256' && e.iterations === 600000 && encoded(e.salt,16) && encoded(e.iv,16) && encoded(e.mac,32) && encoded(e.ciphertext);
}

// Everything the server keeps for one account, in one transaction. Rate counters hold no account data and expire alone.
const deleteAccount = (db, userId) => db.batch(['sent_tabs', 'devices', 'settings', 'vaults', 'sessions', 'requests'].map(t => db.prepare('DELETE FROM '+t+' WHERE user_id=?').bind(userId))
  .concat(db.prepare('DELETE FROM users WHERE id=?').bind(userId)));
export function createWorker({now=Date.now,fetchImpl=fetch,loadEmailMessage=async()=>(await import('cloudflare:email')).EmailMessage,authTtlMs=600000,sessionTtlMs=2592000000,pollIntervalMs=2000}={}) {
 return {async fetch(req,env) {
  const headers={'Cache-Control':'no-store','X-Content-Type-Options':'nosniff','Referrer-Policy':'no-referrer','Content-Security-Policy':"default-src 'none'; frame-ancestors 'none'",'X-Frame-Options':'DENY','Strict-Transport-Security':'max-age=31536000; includeSubDomains','Cross-Origin-Resource-Policy':'same-origin'};
  const send=(status,payload)=>Response.json(payload,{status,headers:{...headers,...(status===429?{'Retry-After':'2'}:{})}});
  const html=(status,title,content)=>{const nonce=randomBytes(16).toString('base64');return new Response(page(title,content,nonce),{status,headers:{...headers,'Content-Type':'text/html; charset=utf-8',
    // no-referrer would make the browser send "Origin: null" on the confirm form post, which the origin check refuses.
    'Referrer-Policy':'same-origin','Content-Security-Policy':`default-src 'none'; style-src 'nonce-${nonce}'; script-src 'nonce-${nonce}'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'`}});};
  try {
  const {DB:db,DISCORD_CLIENT_ID:clientId,DISCORD_CLIENT_SECRET:clientSecret}=env;
  if (!db||!clientId||!clientSecret) throw fail(503,'configuration_required');
  const origin=new URL(env.PUBLIC_URL);
  if (origin.protocol!=='https:'||origin.username||origin.password||origin.pathname!=='/'||origin.search||origin.hash) throw fail(503,'configuration_required');
  const callback=origin.origin+'/v1/auth/discord/callback';
  const stmt=sql=>({get:(...args)=>db.prepare(sql).bind(...args).first(),run:(...args)=>db.prepare(sql).bind(...args).run(),all:async(...args)=>(await db.prepare(sql).bind(...args).all()).results});
  async function rate(key,maximum,period) {
   const time=now();
   const row=await stmt('INSERT INTO rate_limits(key,count,expires) VALUES(?,1,?) ON CONFLICT(key) DO UPDATE SET count=CASE WHEN expires<=? THEN 1 ELSE count+1 END,expires=CASE WHEN expires<=? THEN excluded.expires ELSE expires END RETURNING count').get(key,time+period,time,time);
   if(row.count>maximum)throw fail(429,'rate_limited');
  }
  async function body(req, max = 8192) {
    if ((req.headers.get('content-type') || '').split(';')[0].trim().toLowerCase() !== 'application/json') throw fail(415, 'json_required');
    if (req.headers.get('content-encoding') && req.headers.get('content-encoding') !== 'identity') throw fail(415, 'encoding_not_supported');
    if (Number(req.headers.get('content-length')) > max) throw fail(413, 'payload_too_large');
    let size=0; const chunks=[];
    for await (const chunk of (req.body || [])) { size+=chunk.length; if (size>max) throw fail(413,'payload_too_large'); chunks.push(chunk); }
    try { return JSON.parse(Buffer.concat(chunks).toString('utf8')); } catch { throw fail(400,'invalid_json'); }
  }
  async function formBody(req, max = 2048) {
    if ((req.headers.get('content-type') || '').split(';')[0].trim().toLowerCase() !== 'application/x-www-form-urlencoded') throw fail(415, 'form_required');
    if (Number(req.headers.get('content-length')) > max) throw fail(413, 'payload_too_large');
    let size=0; const chunks=[];
    for await (const chunk of (req.body || [])) { size+=chunk.length; if (size>max) throw fail(413,'payload_too_large'); chunks.push(chunk); }
    return new URLSearchParams(Buffer.concat(chunks).toString('utf8'));
  }
  async function authenticated(req) {
    const match = /^Bearer ([A-Za-z0-9_-]{43})$/.exec(req.headers.get('authorization') || '');
    if (!match) throw fail(401, 'unauthorized');
    const tokenHash=hash(match[1]);
    const user=await stmt('SELECT users.id,users.username,users.avatar,users.banner,users.accent_color FROM sessions JOIN users ON users.id=sessions.user_id WHERE sessions.hash=? AND sessions.expires>?').get(tokenHash,now());
    if (!user) throw fail(401,'unauthorized');
    await rate('user:'+user.id,120,60000);
    return {user,tokenHash};
  }
  async function exchange(code) {
    const result=await fetchImpl('https://discord.com/api/v10/oauth2/token',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:new URLSearchParams({client_id:clientId,client_secret:clientSecret,grant_type:'authorization_code',code,redirect_uri:callback}),signal:AbortSignal.timeout(15000)});
    if (!result.ok) throw fail(502,'discord_auth_failed');
    const token=await result.json();
    if (typeof token.access_token !== 'string' || token.token_type?.toLowerCase() !== 'bearer') throw fail(502,'discord_auth_failed');
    const response=await fetchImpl('https://discord.com/api/v10/users/@me',{headers:{Authorization:'Bearer '+token.access_token},signal:AbortSignal.timeout(15000)});
    if (!response.ok) throw fail(502,'discord_auth_failed');
    const user=await response.json();
    if (!/^[0-9]{1,32}$/.test(user.id) || typeof user.username !== 'string' || user.username.length>128 || !user.username) throw fail(502,'discord_auth_failed');
    const hashOrNull=v=>typeof v==='string'&&/^(a_)?[0-9a-f]{32}$/.test(v)?v:null;
    return {id:user.id,username:user.username,avatar:hashOrNull(user.avatar),banner:hashOrNull(user.banner),
      accent_color:Number.isInteger(user.accent_color)&&user.accent_color>=0&&user.accent_color<=0xFFFFFF?user.accent_color:null};
  }
      await rate('ip:'+hash(req.headers.get('CF-Connecting-IP') || 'local'),1000,60000);
      const url=new URL(req.url); const path=url.pathname;
      if (req.headers.get('origin') && req.headers.get('origin') !== origin.origin) throw fail(403,'origin_not_allowed');
      if (req.method==='GET' && path==='/healthz') return send(200,{status:'ok'});
      if (req.method==='POST' && path==='/v1/auth/discord/start') {
        await rate('start:'+hash(req.headers.get('CF-Connecting-IP') || 'local'),20,60000);
        const input=await body(req);
        // {"confirm":true} opts in to the confirmation code. SIGNIN_CODE_REQUIRED=true turns away clients that don't
        // show one, which is what actually stops phishing; leave it off until Mica versions that show the code are out.
        const confirm=exact(input,['challenge','confirm'])&&input.confirm===true;
        if (!(confirm||exact(input,['challenge'])) || typeof input.challenge !== 'string' || !/^[A-Za-z0-9_-]{43}$/.test(input.challenge)) throw fail(400,'invalid_request');
        if (!confirm && env.SIGNIN_CODE_REQUIRED==='true') throw fail(400,'confirmation_required');
        const id=random(),state=random(),expires=now()+authTtlMs,code=confirm?signinCode():null;
        await stmt("INSERT INTO requests(id,state,challenge,expires,status,code_hash) VALUES(?,?,?,?,'pending',?)").run(id,hash(state),input.challenge,expires,code&&hash(normalizeCode(code)));
        const auth=new URL('https://discord.com/oauth2/authorize');
        auth.search=new URLSearchParams({client_id:clientId,response_type:'code',redirect_uri:callback,scope:'identify',state,prompt:'consent'}).toString();
        return send(200,{requestId:id,authorizationUrl:auth.toString(),expiresAt:new Date(expires).toISOString(),...(code?{confirmationCode:code}:{})});
      }
      if (req.method==='GET' && path==='/v1/auth/discord/callback') {
        const state=url.searchParams.get('state'),code=url.searchParams.get('code');
        if (!state || !/^[A-Za-z0-9_-]{43}$/.test(state)) throw fail(400,'invalid_state');
        const request=await stmt("UPDATE requests SET status='processing' WHERE state=? AND status='pending' AND expires>? RETURNING id,code_hash,purpose").get(hash(state),now());
        if (!request) throw fail(400,'invalid_state');
        if (!code || code.length>2048 || url.searchParams.has('error')) {await stmt("UPDATE requests SET status='failed' WHERE id=?").run(request.id);throw fail(400,'authorization_denied');}
        if (request.purpose==='delete') {
          // Deleting: only an existing account, and no profile refresh; the delete waits on the page's button.
          let user,form;
          try {user=await exchange(code);} catch {await stmt("UPDATE requests SET status='failed' WHERE id=?").run(request.id);throw fail(502,'discord_auth_failed');}
          if (await stmt('SELECT 1 AS ok FROM users WHERE id=?').get(user.id)) {form=random();await stmt("UPDATE requests SET status='delete',user_id=?,form_hash=? WHERE id=? AND expires>?").run(user.id,hash(form),request.id,now());}
          else await stmt("UPDATE requests SET status='failed' WHERE id=?").run(request.id);
          if (!form) return html(200,'Delete Mica account',statusPage('stop','No Mica account','There is no Mica account for '+user.username+' on Discord, so there is nothing to delete.'));
          return html(200,'Delete Mica account',deleteForm(form,user.username));
        }
        let user,form;
        try {
          user=await exchange(code);
          await stmt('INSERT INTO users(id,username,avatar,banner,accent_color) VALUES(?,?,?,?,?) ON CONFLICT(id) DO UPDATE SET username=excluded.username,avatar=excluded.avatar,banner=excluded.banner,accent_color=excluded.accent_color').run(user.id,user.username,user.avatar,user.banner,user.accent_color);
          // With a confirmation code, the session waits on this page's form instead of becoming ready straight away.
          if (request.code_hash) {form=random();await stmt("UPDATE requests SET status='confirm',user_id=?,form_hash=? WHERE id=? AND expires>?").run(user.id,hash(form),request.id,now());}
          else await stmt("UPDATE requests SET status='ready',user_id=? WHERE id=? AND expires>?").run(user.id,request.id,now());
        } catch {await stmt("UPDATE requests SET status='failed' WHERE id=?").run(request.id);throw fail(502,'discord_auth_failed');}
        if (form) return html(200,'Confirm Mica sign-in',confirmForm(form,user.username,''));
        return html(200,'Mica sign-in',statusPage('ok','Sign-in complete','You can close this window and return to Mica.'));
      }
      if (req.method==='POST' && path==='/v1/auth/discord/confirm') {
        await rate('confirm:'+hash(req.headers.get('CF-Connecting-IP') || 'local'),30,60000);
        const fields=await formBody(req);
        const form=fields.get('form')||'';
        const request=/^[A-Za-z0-9_-]{43}$/.test(form)?await stmt("SELECT requests.id,requests.code_hash,users.username FROM requests JOIN users ON users.id=requests.user_id WHERE requests.form_hash=? AND requests.status='confirm' AND requests.expires>?").get(hash(form),now()):null;
        if (!request) return html(400,'Mica sign-in',statusPage('stop','This sign-in has ended','It expired, was already finished, or was cancelled. Start again from Mica if you need to.'));
        if (fields.get('action')==='deny') {
          await stmt("UPDATE requests SET status='failed' WHERE id=? AND status='confirm'").run(request.id);
          return html(200,'Mica sign-in cancelled',statusPage('stop','Sign-in cancelled','No one was signed in to your account. If someone sent you that link, don\'t trust them.'));
        }
        const entered=normalizeCode(fields.get('code')||'');
        if (entered.length===8 && timingSafeEqual(Buffer.from(hash(entered)),Buffer.from(request.code_hash))) {
          const done=await stmt("UPDATE requests SET status='ready' WHERE id=? AND status='confirm' AND expires>? RETURNING id").get(request.id,now());
          if (done) return html(200,'Mica sign-in',statusPage('ok','Sign-in complete','You can close this window and return to Mica.'));
          return html(400,'Mica sign-in',statusPage('stop','This sign-in has ended','Start again from Mica if you need to.'));
        }
        const missed=await stmt("UPDATE requests SET attempts=attempts+1,status=CASE WHEN attempts+1>=? THEN 'failed' ELSE status END WHERE id=? AND status='confirm' RETURNING attempts").get(CodeAttempts,request.id);
        if (!missed || missed.attempts>=CodeAttempts) return html(400,'Mica sign-in',statusPage('bad','Too many wrong codes','This sign-in was cancelled. Start again from Mica.'));
        return html(400,'Confirm Mica sign-in',confirmForm(form,request.username,'That code doesn\'t match. '+(CodeAttempts-missed.attempts)+' tries left.'));
      }
      // Account deletion in a browser, without the app: sign in with Discord, then confirm on our page.
      if (req.method==='GET' && path==='/v1/account/delete') {
        await rate('start:'+hash(req.headers.get('CF-Connecting-IP') || 'local'),20,60000);
        const state=random();
        // No app polls this request: the challenge is a random value nobody holds.
        await stmt("INSERT INTO requests(id,state,challenge,expires,status,purpose) VALUES(?,?,?,?,'pending','delete')").run(random(),hash(state),hash(random()),now()+authTtlMs);
        const auth=new URL('https://discord.com/oauth2/authorize');
        auth.search=new URLSearchParams({client_id:clientId,response_type:'code',redirect_uri:callback,scope:'identify',state,prompt:'consent'}).toString();
        return new Response(null,{status:302,headers:{...headers,Location:auth.toString()}});
      }
      if (req.method==='POST' && path==='/v1/account/delete/confirm') {
        await rate('confirm:'+hash(req.headers.get('CF-Connecting-IP') || 'local'),30,60000);
        const fields=await formBody(req);
        const form=fields.get('form')||'';
        const ended=()=>html(400,'Delete Mica account',statusPage('stop','This request has ended','It expired or was already used. Start again from lessnoise.online/delete-account.'));
        if (!/^[A-Za-z0-9_-]{43}$/.test(form)) return ended();
        // Claimed once: a second submit of the same page finds nothing.
        const request=await stmt("UPDATE requests SET status='failed' WHERE form_hash=? AND status='delete' AND expires>? RETURNING user_id").get(hash(form),now());
        if (!request) return ended();
        if (fields.get('action')!=='delete') return html(200,'Delete Mica account',statusPage('stop','Nothing was deleted','Your Mica account is unchanged.'));
        await deleteAccount(db,request.user_id);
        return html(200,'Mica account deleted',statusPage('ok','Account deleted','Your Mica account and the data stored with it are gone. Every device has been signed out.'));
      }
      if (req.method==='POST' && path==='/v1/auth/discord/poll') {
        // Mica polls every 2 s (30/min); this leaves room for a few sign-ins behind one address.
        await rate('poll:'+hash(req.headers.get('CF-Connecting-IP') || 'local'),90,60000);
        const input=await body(req);
        if (!exact(input,['requestId','verifier']) || typeof input.requestId !== 'string' || !/^[A-Za-z0-9_-]{43}$/.test(input.requestId) || typeof input.verifier !== 'string' || !/^[A-Za-z0-9_-]{43,128}$/.test(input.verifier)) throw fail(400,'invalid_request');
        const proof=hash(input.verifier), time=now();
        const request=await stmt('SELECT * FROM requests WHERE id=? AND expires>?').get(input.requestId,time);
        if (!request || !timingSafeEqual(Buffer.from(proof),Buffer.from(request.challenge))) throw fail(401,'invalid_proof');
        const claimed=await stmt('UPDATE requests SET last_poll=? WHERE id=? AND expires>? AND (last_poll=0 OR last_poll<=?) RETURNING *').get(time,request.id,time,time-pollIntervalMs);
        if (!claimed) throw fail(429,'rate_limited');
        if (claimed.status==='failed') throw fail(400,'authorization_failed');
        if (claimed.status!=='ready') return send(200,{status:'pending'});
        const token=random();
        // One transaction, conditional insert and delete: concurrent isolates cannot deliver twice.
        const delivered=await db.batch([
          db.prepare("INSERT INTO sessions(hash,user_id,expires) SELECT ?,user_id,? FROM requests WHERE id=? AND challenge=? AND status='ready' AND expires>? RETURNING user_id").bind(hash(token),time+sessionTtlMs,request.id,proof,time),
          db.prepare("DELETE FROM requests WHERE id=? AND challenge=? AND status='ready'").bind(request.id,proof)
        ]);
        const userId=delivered[0].results[0]?.user_id;
        if (!userId) throw fail(401,'invalid_proof');
        const user=await stmt('SELECT id,username,avatar,banner,accent_color FROM users WHERE id=?').get(userId);
        return send(200,{status:'ready',token,user});
      }
      if (path==='/v1/account' || path==='/v1/auth/logout' || path==='/v1/vault' || path==='/v1/settings') {
        const {user,tokenHash}=await authenticated(req);
        if (req.method==='GET' && path==='/v1/account') return send(200,{user});
        if (req.method==='DELETE' && path==='/v1/account') {await deleteAccount(db,user.id);return send(200,{status:'deleted'});}
        if (req.method==='POST' && path==='/v1/auth/logout') {await stmt('DELETE FROM sessions WHERE hash=?').run(tokenHash);return send(200,{status:'ok'});}
        if (req.method==='GET' && path==='/v1/vault') {const row=await stmt('SELECT version,envelope FROM vaults WHERE user_id=?').get(user.id);return send(200,row?{version:row.version,envelope:JSON.parse(row.envelope)}:{version:0,envelope:null});}
        if (req.method==='GET' && path==='/v1/settings') {const row=await stmt('SELECT updated_at,data FROM settings WHERE user_id=?').get(user.id);return send(200,row?{settings:JSON.parse(row.data),updatedAt:row.updated_at}:{settings:null,updatedAt:0});}
        if (req.method==='PUT' && path==='/v1/settings') {
          await rate('settings:'+user.id,60,60000);
          const input=await body(req,16*1024);
          if (!exact(input,['settings','updatedAt']) || !Number.isSafeInteger(input.updatedAt) || input.updatedAt<=0 || !validSettings(input.settings)) throw fail(400,'invalid_settings');
          // Last writer wins by the client's change time; an older write is ignored and the newer copy returned.
          await stmt('INSERT INTO settings(user_id,updated_at,data) VALUES(?,?,?) ON CONFLICT(user_id) DO UPDATE SET updated_at=excluded.updated_at,data=excluded.data WHERE excluded.updated_at>=settings.updated_at').run(user.id,input.updatedAt,JSON.stringify(input.settings));
          const row=await stmt('SELECT updated_at,data FROM settings WHERE user_id=?').get(user.id);
          return send(200,{settings:JSON.parse(row.data),updatedAt:row.updated_at});
        }
        if (req.method==='PUT' && path==='/v1/vault') {
          await rate('vault:'+user.id,30,60000);
          const input=await body(req,2*1024*1024);
          if (!exact(input,['version','envelope']) || !Number.isSafeInteger(input.version) || input.version<0 || input.version>=Number.MAX_SAFE_INTEGER || !validEnvelope(input.envelope)) throw fail(400,'invalid_vault');
          const serialized=JSON.stringify(input.envelope);
          const changed=input.version===0
            ? await stmt('INSERT INTO vaults(user_id,version,envelope) VALUES(?,1,?) ON CONFLICT(user_id) DO NOTHING RETURNING version').get(user.id,serialized)
            : await stmt('UPDATE vaults SET version=version+1,envelope=? WHERE user_id=? AND version=? RETURNING version').get(serialized,user.id,input.version);
          if (!changed) throw fail(409,'version_conflict');
          return send(200,{version:input.version+1});
        }
      }
      if (path==='/v1/devices' || path==='/v1/tabs' || /^\/v1\/tabs\/[A-Za-z0-9_-]{22}$/.test(path)) {
        const {user}=await authenticated(req);
        const listDevices=async()=>(await stmt('SELECT id,name,seen FROM devices WHERE user_id=? AND seen>? ORDER BY seen DESC').all(user.id,now()-DeviceSeenMs)).map(d=>({id:d.id,name:d.name,seen:d.seen}));
        if (req.method==='GET' && path==='/v1/devices') return send(200,{devices:await listDevices()});
        if (req.method==='POST' && path==='/v1/devices') {
          const input=await body(req,4096);
          if (!exact(input,['id','name']) || !deviceId(input.id) || !deviceName(input.name)) throw fail(400,'invalid_device');
          if ((await stmt('SELECT COUNT(*) AS n FROM devices WHERE user_id=? AND id<>?').get(user.id,input.id)).n>=20) throw fail(409,'too_many_devices');
          await stmt('INSERT INTO devices(user_id,id,name,seen) VALUES(?,?,?,?) ON CONFLICT(user_id,id) DO UPDATE SET name=excluded.name,seen=excluded.seen').run(user.id,input.id,input.name.trim(),now());
          return send(200,{devices:await listDevices()});
        }
        if (req.method==='POST' && path==='/v1/tabs') {
          const input=await body(req,8192);
          if (!validSentTab(input)) throw fail(400,'invalid_tab');
          if (!await stmt('SELECT 1 AS ok FROM devices WHERE user_id=? AND id=?').get(user.id,input.to)) throw fail(404,'unknown_device');
          if ((await stmt('SELECT COUNT(*) AS n FROM sent_tabs WHERE user_id=? AND to_device=?').get(user.id,input.to)).n>=PendingTabLimit) throw fail(409,'too_many_tabs');
          const id=randomBytes(16).toString('base64url');
          await stmt('INSERT INTO sent_tabs(id,user_id,to_device,from_name,url,title,created) VALUES(?,?,?,?,?,?,?)').run(id,user.id,input.to,input.from.trim(),input.url,input.title,now());
          return send(200,{id});
        }
        if (req.method==='GET' && path==='/v1/tabs') {
          const device=url.searchParams.get('device');if (!deviceId(device)) throw fail(400,'invalid_device');
          const tabs=(await stmt('SELECT id,from_name,url,title,created FROM sent_tabs WHERE user_id=? AND to_device=? ORDER BY created').all(user.id,device)).map(t=>({id:t.id,from:t.from_name,url:t.url,title:t.title,created:t.created}));
          return send(200,{tabs});
        }
        if (req.method==='DELETE' && path.startsWith('/v1/tabs/')) {await stmt('DELETE FROM sent_tabs WHERE user_id=? AND id=?').run(user.id,path.slice(9));return send(200,{status:'ok'});}
      }
      throw fail(404,'not_found');
  } catch(error) {return send(error.status||500,{error:error.status&&error.code?error.code:'internal_error'});}
 },async email(message,env,ctx) {
  // Cloudflare Email Routing sends accounts@ here; people who can't sign in are pointed (and Reply-To'd) to SUPPORT_EMAIL. A human copy goes to DELETE_FORWARD_TO when it's set (a verified destination).
  if (env.DELETE_FORWARD_TO) await message.forward(env.DELETE_FORWARD_TO);
  const auto=(message.headers.get('auto-submitted')||'no').toLowerCase()!=='no' || /^(bulk|list|junk)$/i.test(message.headers.get('precedence')||'')
    || /^(mailer-daemon|postmaster|no-?reply)@/i.test(message.from) || !/^[^\s@<>]+@[^\s@<>]+$/.test(message.from);
  if (auto || !env.PUBLIC_URL) return;
  // One reply per sender a day, so a reply to our reply (or a loop) gets no second one.
  const time=now(),key='mail:'+hash(message.from.toLowerCase());
  const row=await env.DB.prepare('INSERT INTO rate_limits(key,count,expires) VALUES(?,1,?) ON CONFLICT(key) DO UPDATE SET count=CASE WHEN expires<=? THEN 1 ELSE count+1 END,expires=CASE WHEN expires<=? THEN excluded.expires ELSE expires END RETURNING count').bind(key,time+86400000,time,time).first();
  if (row.count>1) return;
  const EmailMessage=await loadEmailMessage();
  await message.reply(new EmailMessage(message.to,message.from,deletionReply(message,new URL(env.PUBLIC_URL).origin+'/v1/account/delete',env.SUPPORT_EMAIL||message.to)));
 },async scheduled(event,env,ctx) {
  const time=now();
  await env.DB.batch([
   env.DB.prepare('DELETE FROM requests WHERE expires<=?').bind(time),
   env.DB.prepare('DELETE FROM sessions WHERE expires<=?').bind(time),
   env.DB.prepare('DELETE FROM rate_limits WHERE expires<=?').bind(time)
  ]);
 }};
}
export default createWorker();
