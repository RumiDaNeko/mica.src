import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash,randomBytes} from 'node:crypto';
import {readFileSync} from 'node:fs';
import {Miniflare,convertV4MiniflareOptions} from 'miniflare';
import {createWorker} from '../index.mjs';
const envelope={format:1,kdf:'PBKDF2-SHA256',iterations:600000,salt:Buffer.alloc(16,1).toString('base64'),iv:Buffer.alloc(16,2).toString('base64'),ciphertext:Buffer.alloc(32,3).toString('base64'),mac:Buffer.alloc(32,4).toString('base64')};
async function fixture(t,options={}) {
 const mf=new Miniflare(convertV4MiniflareOptions({workers:[{name:"api",modules:true,script:"export default {fetch(){return new Response('ok')}}",compatibilityDate:'2026-10-19',d1Databases:['DB']}]}));
 t.after(()=>mf.dispose());const DB=await mf.getD1Database('DB','api');
 for (const m of ['0001_initial.sql','0002_user_avatar.sql','0003_settings.sql','0004_send_tabs.sql','0005_signin_code.sql','0006_user_banner.sql','0007_account_delete.sql']) await DB.exec(readFileSync(new URL('../migrations/'+m,import.meta.url),'utf8'));
 let time=Date.now(),userId='100';
 const settings={now:()=>time,fetchImpl:async(url,init)=>{
  if(url.endsWith('/oauth2/token')){assert.equal(init.body.get('client_secret'),'test-secret');return Response.json({access_token:'discord-token',token_type:'Bearer'});}
  return Response.json({id:userId,username:'test'+userId});
 },...options};
 const worker=createWorker(settings),env={DB,PUBLIC_URL:'https://the-api.lessnoise.online',DISCORD_CLIENT_ID:'test-client',DISCORD_CLIENT_SECRET:'test-secret'};
 const request=async(path,method='GET',data,token)=>{
  const req=new Request(env.PUBLIC_URL+path,{method,headers:{...(data!==undefined?{'Content-Type':'application/json'}:{}),...(token?{Authorization:'Bearer '+token}:{})},body:data!==undefined?JSON.stringify(data):undefined});
  const r=await worker.fetch(req,env);const text=await r.text();return {status:r.status,body:r.headers.get('content-type').includes('json')?JSON.parse(text):text,headers:r.headers};
 };
 const start=async()=>{const verifier=randomBytes(32).toString('base64url');const challenge=createHash('sha256').update(verifier).digest('base64url');const r=await request('/v1/auth/discord/start','POST',{challenge});assert.equal(r.status,200);return {...r.body,verifier,state:new URL(r.body.authorizationUrl).searchParams.get('state')};};
 const callback=login=>request('/v1/auth/discord/callback?state='+login.state+'&code=test');
 const poll=login=>request('/v1/auth/discord/poll','POST',{requestId:login.requestId,verifier:login.verifier});
 const login=async(id='100')=>{userId=id;const pending=await start();assert.equal((await callback(pending)).status,200);const r=await poll(pending);assert.equal(r.status,200);return r.body.token;};
 return {worker,env,request,start,callback,poll,login,settings,advance:ms=>time+=ms,as:id=>{userId=id;}};
}
test('OAuth proof, single-use state, pending poll limits, token delivery and logout',async t=>{
  const f=await fixture(t), login=await f.start();
  assert.equal((await f.request('/v1/auth/discord/poll','POST',{requestId:login.requestId,verifier:randomBytes(32).toString('base64url')})).status,401);
  assert.deepEqual((await f.poll(login)).body,{status:'pending'});
  assert.equal((await f.poll(login)).status,429);
  const callback=await f.callback(login);assert.equal(callback.status,200);assert.ok(!callback.body.includes('discord-token'));
  assert.equal((await f.callback(login)).status,400);
  f.advance(2000);const ready=await f.poll(login);assert.equal(ready.body.status,'ready');assert.equal(ready.body.user.id,'100');
  assert.equal((await f.poll(login)).status,401);
  assert.equal((await f.request('/v1/account','GET',undefined,ready.body.token)).body.user.id,'100');
  assert.equal((await f.request('/v1/auth/logout','POST',undefined,ready.body.token)).status,200);
  assert.equal((await f.request('/v1/account','GET',undefined,ready.body.token)).status,401);
});
test('expired OAuth state/proof and session are rejected',async t=>{
  const f=await fixture(t,{sessionTtlMs:1000}), token=await f.login(), login=await f.start();f.advance(600001);
  assert.equal((await f.callback(login)).status,400);assert.equal((await f.poll(login)).status,401);
  assert.equal((await f.request('/v1/account','GET',undefined,token)).status,401);
});
test('vault isolation, CAS race, validation, authorization and limits',async t=>{
  const f=await fixture(t),a=await f.login('100'),b=await f.login('200');
  assert.equal((await f.request('/v1/vault')).status,401);
  assert.deepEqual((await f.request('/v1/vault','GET',undefined,a)).body,{version:0,envelope:null});
  const writes=await Promise.all([f.request('/v1/vault','PUT',{version:0,envelope},a),f.request('/v1/vault','PUT',{version:0,envelope},a)]);
  assert.deepEqual(writes.map(r=>r.status).sort(),[200,409]);
  assert.deepEqual((await f.request('/v1/vault','GET',undefined,a)).body,{version:1,envelope});
  assert.deepEqual((await f.request('/v1/vault','GET',undefined,b)).body,{version:0,envelope:null});
  assert.equal((await f.request('/v1/vault','PUT',{version:1,envelope:{...envelope,mac:'bad'}},a)).status,400);
  assert.equal((await f.request('/v1/vault','PUT',{version:1,envelope:{...envelope,ciphertext:'A'.repeat(2*1024*1024)}},a)).status,413);
  assert.equal((await f.request('/v1/auth/discord/start','POST',{challenge:'A'.repeat(9000)})).status,413);
  assert.equal((await f.request('/v1/auth/discord/start','POST',{challenge:['A'.repeat(43)]})).status,400);
  const origin=await f.worker.fetch(new Request('https://the-api.lessnoise.online/v1/account',{headers:{Origin:'https://evil.invalid',Authorization:'Bearer '+a}}),f.env);assert.equal(origin.status,403);assert.equal(origin.headers.get('access-control-allow-origin'),null);
});

test('settings sync: per-user, newest write wins, validated',async t=>{
  const f=await fixture(t),a=await f.login('100'),b=await f.login('200');
  assert.equal((await f.request('/v1/settings')).status,401);
  assert.deepEqual((await f.request('/v1/settings','GET',undefined,a)).body,{settings:null,updatedAt:0});
  const first=await f.request('/v1/settings','PUT',{settings:{dark:true,ambientYoutube:false},updatedAt:2000},a);
  assert.deepEqual(first.body,{settings:{dark:true,ambientYoutube:false},updatedAt:2000});
  const stale=await f.request('/v1/settings','PUT',{settings:{dark:false},updatedAt:1000},a);
  assert.equal(stale.status,200);assert.deepEqual(stale.body,{settings:{dark:true,ambientYoutube:false},updatedAt:2000});
  assert.deepEqual((await f.request('/v1/settings','PUT',{settings:{dark:false},updatedAt:3000},a)).body,{settings:{dark:false},updatedAt:3000});
  assert.deepEqual((await f.request('/v1/settings','GET',undefined,b)).body,{settings:null,updatedAt:0});
  for (const bad of [{settings:{nested:{x:1}},updatedAt:4000},{settings:{'bad key':true},updatedAt:4000},{settings:{dark:true},updatedAt:-1},{settings:{dark:true}},{settings:{s:'x'.repeat(201)},updatedAt:4000},
    {settings:{inbox:true},updatedAt:4000},{settings:{dark:1},updatedAt:4000},{settings:{strictTracking:'false'},updatedAt:4000},{settings:[true],updatedAt:4000}])
    assert.equal((await f.request('/v1/settings','PUT',bad,a)).status,400);
  assert.equal((await f.request('/v1/settings','PUT',{settings:{dark:true,ambientYoutube:true,strictTracking:false},updatedAt:5000},a)).status,200);
});
test('hardening: security headers, extra envelope fields, per-IP poll and per-account write limits',async t=>{
  const f=await fixture(t),a=await f.login('100'),b=await f.login('200');
  const r=await f.request('/v1/account','GET',undefined,a);
  assert.equal(r.headers.get('strict-transport-security'),'max-age=31536000; includeSubDomains');
  assert.equal(r.headers.get('x-frame-options'),'DENY');assert.match(r.headers.get('content-security-policy'),/frame-ancestors 'none'/);
  assert.equal(r.headers.get('access-control-allow-origin'),null);
  const page=await f.callback(await f.start());assert.equal(page.headers.get('x-frame-options'),'DENY');assert.ok(page.headers.get('strict-transport-security'));
  for (const extra of [{inbox:'AAAA'},{inboxKey:'AAAA'}]) assert.equal((await f.request('/v1/vault','PUT',{version:0,envelope:{...envelope,...extra}},a)).status,400);
  assert.equal((await f.request('/v1/vault','PUT',{version:0,envelope,inbox:'AAAA'},a)).status,400);
  // The three rejected writes above count toward the 30/min vault limit too.
  for (let v=0;v<27;v++) assert.equal((await f.request('/v1/vault','PUT',{version:v,envelope},a)).status,200);
  const limited=await f.request('/v1/vault','PUT',{version:27,envelope},a);assert.equal(limited.status,429);assert.equal(limited.headers.get('retry-after'),'2');
  assert.equal((await f.request('/v1/vault','PUT',{version:0,envelope},b)).status,200);
  for (let i=1;i<=60;i++) assert.equal((await f.request('/v1/settings','PUT',{settings:{dark:i%2===0},updatedAt:i},b)).status,200);
  assert.equal((await f.request('/v1/settings','PUT',{settings:{dark:true},updatedAt:61},b)).status,429);
  // The two logins above already polled once each from this address; the limit is 90/min.
  for (let i=0;i<88;i++) assert.equal((await f.request('/v1/auth/discord/poll','POST',{requestId:randomBytes(32).toString('base64url'),verifier:randomBytes(32).toString('base64url')})).status,401);
  assert.equal((await f.request('/v1/auth/discord/poll','POST',{requestId:randomBytes(32).toString('base64url'),verifier:randomBytes(32).toString('base64url')})).status,429);
  f.advance(60001);assert.equal((await f.request('/v1/vault','PUT',{version:27,envelope},a)).status,200);
});
test('sign-in confirmation code: browser page must match the code Mica shows',async t=>{
  const f=await fixture(t);
  const begin=async()=>{const verifier=randomBytes(32).toString('base64url');const challenge=createHash('sha256').update(verifier).digest('base64url');const r=await f.request('/v1/auth/discord/start','POST',{challenge,confirm:true});assert.equal(r.status,200);return {...r.body,verifier,state:new URL(r.body.authorizationUrl).searchParams.get('state')};};
  const formOf=page=>/name="form" value="([A-Za-z0-9_-]{43})"/.exec(page.body)[1];
  const submit=(fields,origin='https://the-api.lessnoise.online')=>f.worker.fetch(new Request(f.env.PUBLIC_URL+'/v1/auth/discord/confirm',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded',...(origin?{Origin:origin}:{})},body:new URLSearchParams(fields)}),f.env).then(async r=>({status:r.status,text:await r.text(),headers:r.headers}));
  // Happy path: code shown to Mica, typed on the page in any case and without the dash.
  const a=await begin();
  assert.match(a.confirmationCode,/^[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}$/);
  const page=await f.callback(a);assert.equal(page.status,200);assert.match(page.body,/Confirm Mica sign-in/);assert.match(page.body,/test100/);
  assert.match(page.headers.get('content-security-policy'),/form-action 'self'/);assert.match(page.headers.get('content-security-policy'),/style-src 'nonce-/);
  assert.equal(page.headers.get('referrer-policy'),'same-origin');assert.ok(!page.body.includes(a.confirmationCode));
  // The animated code field: its script runs only with this response's nonce, and the plain input stays in the form.
  const nonce=/script-src 'nonce-([^']+)'/.exec(page.headers.get('content-security-policy'))[1];
  assert.ok(page.body.includes('<script nonce="'+nonce+'">'));assert.equal((page.body.match(/class="cell"/g)||[]).length,8);assert.match(page.body,/name="code"[^>]*maxlength="24"/);
  assert.deepEqual((await f.poll(a)).body,{status:'pending'});
  const form=formOf(page);
  assert.equal((await submit({form,code:'AAAA-AAAA',action:'confirm'},'https://evil.invalid')).status,403);
  const wrong=await submit({form,code:'AAAA-AAAA',action:'confirm'});assert.equal(wrong.status,400);assert.match(wrong.text,/4 tries left/);
  f.advance(2000);assert.deepEqual((await f.poll(a)).body,{status:'pending'});
  // Pasted with spaces and lower case, and submitted by the page itself (no button value): still accepted.
  const ok=await submit({form,code:' '+a.confirmationCode.toLowerCase().replace('-',' ')+' '});assert.equal(ok.status,200);assert.match(ok.text,/Sign-in complete/);assert.match(ok.text,/class="mark ok"/);
  f.advance(2000);const ready=await f.poll(a);assert.equal(ready.body.status,'ready');assert.equal(ready.body.user.id,'100');
  assert.equal((await submit({form,code:a.confirmationCode,action:'confirm'})).status,400);
  // "I didn't start this" cancels; Mica's poll then fails.
  const b=await begin();const denied=await submit({form:formOf(await f.callback(b)),action:'deny'});assert.equal(denied.status,200);assert.match(denied.text,/Sign-in cancelled/);
  assert.equal((await f.poll(b)).body.error,'authorization_failed');
  // Five wrong codes cancel the sign-in, even if the right one comes after.
  const c=await begin(),cForm=formOf(await f.callback(c));
  for (let i=0;i<4;i++) assert.equal((await submit({form:cForm,code:'BBBB-BBBB',action:'confirm'})).status,400);
  assert.match((await submit({form:cForm,code:'BBBB-BBBB',action:'confirm'})).text,/Too many wrong codes/);
  assert.equal((await submit({form:cForm,code:c.confirmationCode,action:'confirm'})).status,400);
  assert.equal((await f.poll(c)).body.error,'authorization_failed');
  // Unknown form tokens and malformed submissions get nowhere.
  assert.equal((await submit({form:randomBytes(32).toString('base64url'),code:'AAAA-AAAA'})).status,400);
  assert.equal((await f.request('/v1/auth/discord/confirm','POST',{form:'x'})).status,415);
  // Old clients without the code still work until SIGNIN_CODE_REQUIRED is turned on.
  const legacy=await f.start();assert.equal(legacy.confirmationCode,undefined);
  const strict={...f.env,SIGNIN_CODE_REQUIRED:'true'},post=body=>f.worker.fetch(new Request(f.env.PUBLIC_URL+'/v1/auth/discord/start',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)}),strict);
  const refused=await post({challenge:'a'.repeat(43)});assert.equal(refused.status,400);assert.deepEqual(await refused.json(),{error:'confirmation_required'});
  assert.equal((await post({challenge:'a'.repeat(43),confirm:true})).status,200);
  assert.equal((await post({challenge:'a'.repeat(43),confirm:false})).status,400);
});
test('Discord banner and accent colour: stored at sign-in, validated, refreshed, returned with the user',async t=>{
  let profile={};
  const f=await fixture(t,{fetchImpl:async url=>url.endsWith('/oauth2/token')?Response.json({access_token:'discord-token',token_type:'Bearer'}):Response.json({id:'300',username:'banner',...profile})});
  const signIn=async()=>{const pending=await f.start();assert.equal((await f.callback(pending)).status,200);f.advance(2000);const r=await f.poll(pending);assert.equal(r.body.status,'ready');return r.body;};
  profile={banner:'a_'+'0123456789abcdef'.repeat(2),accent_color:0x5865F2};
  let ready=await signIn();
  assert.equal(ready.user.banner,'a_0123456789abcdef0123456789abcdef');assert.equal(ready.user.accent_color,0x5865F2);
  assert.deepEqual((await f.request('/v1/account','GET',undefined,ready.token)).body.user,{id:'300',username:'banner',avatar:null,banner:'a_0123456789abcdef0123456789abcdef',accent_color:0x5865F2});
  profile={banner:'../evil.png',accent_color:0x1000000};
  ready=await signIn();
  assert.deepEqual((await f.request('/v1/account','GET',undefined,ready.token)).body.user,{id:'300',username:'banner',avatar:null,banner:null,accent_color:null});
  profile={banner:'f'.repeat(32),accent_color:0};
  ready=await signIn();assert.equal(ready.user.banner,'f'.repeat(32));assert.equal(ready.user.accent_color,0);
});
test('account deletion: browser flow after Discord sign-in, app endpoint, email reply, other accounts untouched',async t=>{
  const replies=[];
  const f=await fixture(t,{loadEmailMessage:async()=>class {constructor(from,to,raw){Object.assign(this,{from,to,raw});}}}),a=await f.login('100'),b=await f.login('200');
  const count=async(table,col='user_id',id='100')=>(await f.env.DB.prepare('SELECT COUNT(*) AS n FROM '+table+' WHERE '+col+'=?').bind(id).first()).n;
  for (const token of [a,b]) assert.equal((await f.request('/v1/vault','PUT',{version:0,envelope},token)).status,200);
  await f.request('/v1/settings','PUT',{settings:{dark:true},updatedAt:1},a);
  await f.request('/v1/devices','POST',{id:'d'.repeat(22),name:'Phone'},a);
  assert.equal((await f.request('/v1/tabs','POST',{to:'d'.repeat(22),from:'PC',url:'https://example.com/',title:'x'},a)).status,200);
  const submit=fields=>f.worker.fetch(new Request(f.env.PUBLIC_URL+'/v1/account/delete/confirm',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded',Origin:f.env.PUBLIC_URL},body:new URLSearchParams(fields)}),f.env).then(async r=>({status:r.status,text:await r.text()}));
  const begin=async id=>{f.as(id);const r=await f.worker.fetch(new Request(f.env.PUBLIC_URL+'/v1/account/delete'),f.env);assert.equal(r.status,302);
    const auth=new URL(r.headers.get('location'));assert.equal(auth.origin+auth.pathname,'https://discord.com/oauth2/authorize');assert.equal(auth.searchParams.get('scope'),'identify');
    return f.callback({state:auth.searchParams.get('state')});};
  const formOf=page=>/name="form" value="([A-Za-z0-9_-]{43})"/.exec(page.body)[1];
  // A Discord user without a Mica account: nothing to delete, and no account is created.
  const none=await begin('999');assert.match(none.body,/No Mica account/);assert.equal(await count('users','id','999'),0);
  // Cancel keeps everything; the page can't be reused.
  const kept=formOf(await begin('100'));
  assert.match((await submit({form:kept,action:'cancel'})).text,/Nothing was deleted/);assert.equal(await count('vaults'),1);
  assert.equal((await submit({form:kept,action:'delete'})).status,400);
  // The delete request is not a sign-in: the sign-in confirm form and app polling get nothing from it.
  const page=await begin('100');assert.match(page.body,/Delete your Mica account\?/);assert.match(page.body,/test100/);
  const form=formOf(page);
  assert.equal((await f.worker.fetch(new Request(f.env.PUBLIC_URL+'/v1/auth/discord/confirm',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:new URLSearchParams({form,code:'AAAA-AAAA'})}),f.env)).status,400);
  assert.equal((await submit({form,action:'delete'},)).status,200);
  for (const table of ['sent_tabs','devices','settings','vaults','sessions','requests']) assert.equal(await count(table),0,table);
  assert.equal(await count('users','id'),0);
  assert.equal((await f.request('/v1/account','GET',undefined,a)).status,401);
  assert.equal((await submit({form,action:'delete'})).status,400);
  // The other account is untouched, and the app endpoint deletes it with its own token.
  assert.deepEqual((await f.request('/v1/vault','GET',undefined,b)).body,{version:1,envelope});
  assert.equal((await f.request('/v1/account','DELETE')).status,401);
  assert.deepEqual((await f.request('/v1/account','DELETE',undefined,b)).body,{status:'deleted'});
  assert.equal(await count('vaults','user_id','200'),0);assert.equal((await f.request('/v1/account','GET',undefined,b)).status,401);
  // Mail to the deletion address gets one reply a day with the link; automatic mail gets none.
  const mail=(from,extra={})=>({from,to:'accounts@lessnoise.online',headers:new Headers({subject:'Delete my Mica account','message-id':'<m1@example.com>',...extra}),reply:async m=>replies.push(m),forward:async to=>replies.push({forwarded:to})});
  await f.worker.email(mail('user@example.com'),{...f.env,SUPPORT_EMAIL:'help@example.com'},{});
  assert.equal(replies.length,1);assert.equal(replies[0].to,'user@example.com');assert.equal(replies[0].from,'accounts@lessnoise.online');
  assert.match(replies[0].raw,/^In-Reply-To: <m1@example.com>$/m);assert.match(replies[0].raw,/^Auto-Submitted: auto-replied$/m);assert.match(replies[0].raw,/^Reply-To: help@example.com$/m);
  const boundary=/boundary="([^"]+)"/.exec(replies[0].raw)[1];
  const parts=replies[0].raw.split('--'+boundary).slice(1,-1).map(p=>({type:/Content-Type: ([^;\r]+)/.exec(p)[1],body:Buffer.from(p.split('\r\n\r\n')[1].replace(/\r\n/g,''),'base64').toString()}));
  assert.deepEqual(parts.map(p=>p.type),['text/plain','text/html']);assert.match(replies[0].raw.slice(-(boundary.length+6)),new RegExp('--'+boundary+'--\\r\\n$'));
  for (const p of parts) assert.match(p.body,/help@example\.com/);
  for (const p of parts) assert.match(p.body,/https:\/\/the-api\.lessnoise\.online\/v1\/account\/delete/);
  assert.match(parts[1].body,/<a href="https:\/\/the-api\.lessnoise\.online\/v1\/account\/delete"[^>]*>Delete my account<\/a>/);
  await f.worker.email(mail('User@Example.com'),f.env,{});
  await f.worker.email(mail('bot@example.com',{'auto-submitted':'auto-replied'}),f.env,{});
  await f.worker.email(mail('MAILER-DAEMON@example.com'),f.env,{});
  assert.equal(replies.length,1);
  f.advance(86400001);await f.worker.email(mail('user@example.com'),{...f.env,DELETE_FORWARD_TO:'me@example.com'},{});
  assert.deepEqual(replies.slice(1).map(r=>r.forwarded||r.to),['me@example.com','user@example.com']);
});
test('concurrent poll delivers one token; fresh handler shares D1; scheduled cleanup',async t=>{
 const f=await fixture(t),pending=await f.start();await f.callback(pending);
 const results=await Promise.all([f.poll(pending),f.poll(pending),f.poll(pending)]);
 const ready=results.filter(r=>r.status===200&&r.body.status==='ready');assert.equal(ready.length,1);
 const token=ready[0].body.token;
 assert.equal((await f.request('/v1/vault','PUT',{version:0,envelope},token)).status,200);
 const other=createWorker(f.settings);
 const r=await other.fetch(new Request(f.env.PUBLIC_URL+'/v1/vault',{headers:{Authorization:'Bearer '+token}}),f.env);
 assert.deepEqual(await r.json(),{version:1,envelope});
 f.advance(2592000001);await f.worker.scheduled({},f.env,{});
 assert.equal((await f.env.DB.prepare('SELECT COUNT(*) AS n FROM sessions').first()).n,0);
 assert.equal((await f.env.DB.prepare('SELECT COUNT(*) AS n FROM requests').first()).n,0);
 assert.equal((await f.env.DB.prepare('SELECT COUNT(*) AS n FROM vaults').first()).n,1);
});
test('denied callbacks, configuration, rate limits and atomic updates',async t=>{
 const f=await fixture(t),pending=await f.start();
 assert.equal((await f.request('/v1/auth/discord/callback?state='+pending.state+'&error=access_denied')).status,400);
 assert.equal((await f.callback(pending)).status,400);
 assert.equal((await f.poll(pending)).body.error,'authorization_failed');
 const missing=await f.worker.fetch(new Request(f.env.PUBLIC_URL+'/healthz'),{});assert.equal(missing.status,503);
 const token=await f.login();await f.request('/v1/vault','PUT',{version:0,envelope},token);
 const race=await Promise.all([f.request('/v1/vault','PUT',{version:1,envelope},token),f.request('/v1/vault','PUT',{version:1,envelope},token)]);
 assert.deepEqual(race.map(r=>r.status).sort(),[200,409]);
 for(let i=0;i<18;i++)assert.equal((await f.request('/v1/auth/discord/start','POST',{challenge:'a'.repeat(43)})).status,200);
 assert.equal((await f.request('/v1/auth/discord/start','POST',{challenge:'a'.repeat(43)})).status,429);
});

test('full module runs inside workerd with D1 and large encrypted envelopes',async t=>{
 const mf=new Miniflare(convertV4MiniflareOptions({workers:[{name:'api',modules:true,script:readFileSync(new URL('../index.mjs',import.meta.url),'utf8'),compatibilityDate:'2026-10-19',compatibilityFlags:['nodejs_compat'],d1Databases:['DB'],bindings:{PUBLIC_URL:'https://the-api.lessnoise.online',DISCORD_CLIENT_ID:'fixture',DISCORD_CLIENT_SECRET:'fixture-secret'}}]}));
 t.after(()=>mf.dispose());const db=await mf.getD1Database('DB','api');for (const m of ['0001_initial.sql','0002_user_avatar.sql','0003_settings.sql','0004_send_tabs.sql','0005_signin_code.sql','0006_user_banner.sql','0007_account_delete.sql']) await db.exec(readFileSync(new URL('../migrations/'+m,import.meta.url),'utf8'));
 let r=await mf.dispatchFetch('https://the-api.lessnoise.online/healthz');assert.equal(r.status,200);assert.deepEqual(await r.json(),{status:'ok'});
 r=await mf.dispatchFetch('https://the-api.lessnoise.online/v1/auth/discord/start',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({challenge:'a'.repeat(43)})});assert.equal(r.status,200);assert.ok((await r.json()).authorizationUrl.startsWith('https://discord.com/oauth2/authorize?'));
 const token=randomBytes(32).toString('base64url');await db.prepare('INSERT INTO users(id,username) VALUES(?,?)').bind('900','runtime').run();await db.prepare('INSERT INTO sessions VALUES(?,?,?)').bind(createHash('sha256').update(token).digest('base64url'),'900',Date.now()+60000).run();
 const big={...envelope,ciphertext:Buffer.alloc(1400000,7).toString('base64')};
 r=await mf.dispatchFetch('https://the-api.lessnoise.online/v1/vault',{method:'PUT',headers:{Authorization:'Bearer '+token,'Content-Type':'application/json'},body:JSON.stringify({version:0,envelope:big})});assert.equal(r.status,200,await r.text());
 r=await mf.dispatchFetch('https://the-api.lessnoise.online/v1/vault',{headers:{Authorization:'Bearer '+token}});assert.deepEqual(await r.json(),{version:1,envelope:big});
});
