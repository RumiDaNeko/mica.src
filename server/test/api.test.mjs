import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash,randomBytes} from 'node:crypto';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {createApp} from '../app.mjs';

const envelope={format:1,kdf:'PBKDF2-SHA256',iterations:600000,salt:Buffer.alloc(16,1).toString('base64'),iv:Buffer.alloc(16,2).toString('base64'),ciphertext:Buffer.alloc(32,3).toString('base64'),mac:Buffer.alloc(32,4).toString('base64')};
async function fixture(t, options={}) {
  let time=Date.now(),userId='100';
  const app=createApp({clientId:'test-client',clientSecret:'test-secret',now:()=>time,fetchImpl:async(url,init)=>{
    if(url.endsWith('/oauth2/token')) {assert.equal(init.body.get('client_secret'),'test-secret');return Response.json({access_token:'discord-token',token_type:'Bearer'});}
    assert.equal(init.headers.Authorization,'Bearer discord-token');return Response.json({id:userId,username:'test'+userId});
  },...options});
  await new Promise(resolve=>app.listen(0,'127.0.0.1',resolve));
  t.after(()=>app.listening?new Promise(resolve=>{app.closeAllConnections();app.close(resolve);}):undefined);
  const base='http://127.0.0.1:'+app.address().port;
  const request=async(path,method='GET',data,token)=>{
    const r=await fetch(base+path,{method,headers:{...(data!==undefined?{'Content-Type':'application/json'}:{}),...(token?{Authorization:'Bearer '+token}:{})},body:data!==undefined?JSON.stringify(data):undefined});
    const text=await r.text();return {status:r.status,body:r.headers.get('content-type').includes('json')?JSON.parse(text):text,headers:r.headers};
  };
  const start=async()=>{const verifier=randomBytes(32).toString('base64url');const challenge=createHash('sha256').update(verifier).digest('base64url');const r=await request('/v1/auth/discord/start','POST',{challenge});assert.equal(r.status,200);return {...r.body,verifier,state:new URL(r.body.authorizationUrl).searchParams.get('state')};};
  const callback=login=>request('/v1/auth/discord/callback?state='+login.state+'&code=test');
  const poll=login=>request('/v1/auth/discord/poll','POST',{requestId:login.requestId,verifier:login.verifier});
  const login=async(id='100')=>{userId=id;const pending=await start();assert.equal((await callback(pending)).status,200);const r=await poll(pending);assert.equal(r.status,200);return r.body.token;};
  return {app,base,request,start,callback,poll,login,advance:ms=>{time+=ms;}};
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
  const f=await fixture(t,{sessionTtlMs:1000}), token=await f.login(), login=await f.start();f.advance(300001);
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
  const origin=await fetch(f.base+'/v1/account',{headers:{Origin:'https://evil.invalid',Authorization:'Bearer '+a}});assert.equal(origin.status,403);assert.equal(origin.headers.get('access-control-allow-origin'),null);
});
test('SQLite persists sessions and vault across server restart',async t=>{
  const directory=mkdtempSync(join(tmpdir(),'mica-test-'));
  const dbPath=join(directory,'test.sqlite');const f=await fixture(t,{dbPath});const token=await f.login();
  assert.equal((await f.request('/v1/vault','PUT',{version:0,envelope},token)).status,200);
  await new Promise(resolve=>f.app.close(resolve));
  const second=await fixture(t,{dbPath});assert.deepEqual((await second.request('/v1/vault','GET',undefined,token)).body,{version:1,envelope});
  t.after(()=>rmSync(directory,{recursive:true,force:true}));
});
test('bad callback consumes state and startup requires OAuth secrets',async t=>{
  assert.throws(()=>createApp(),/required/);const f=await fixture(t),login=await f.start();
  assert.equal((await f.request('/v1/auth/discord/callback?state='+login.state+'&error=access_denied')).status,400);
  assert.equal((await f.callback(login)).status,400);assert.equal((await f.poll(login)).body.error,'authorization_failed');
});
test('send tab: devices register, tabs wait per device, other accounts see nothing',async t=>{
  const f=await fixture(t),a=await f.login('100'),b=await f.login('200');
  const laptop='laptop-device-0001',phone='phone-device-00002';
  assert.equal((await f.request('/v1/devices','POST',{id:'short',name:'x'},a)).status,400);
  assert.equal((await f.request('/v1/devices','POST',{id:laptop,name:'Laptop'},a)).status,200);
  const listed=await f.request('/v1/devices','POST',{id:phone,name:'Desk PC'},a);
  assert.deepEqual(listed.body.devices.map(d=>d.name).sort(),['Desk PC','Laptop']);
  assert.equal((await f.request('/v1/tabs','POST',{to:'nobody-device-00001',from:'Laptop',url:'https://example.com/',title:'Example'},a)).status,404);
  assert.equal((await f.request('/v1/tabs','POST',{to:phone,from:'Laptop',url:'javascript:alert(1)',title:'x'},a)).status,400);
  assert.equal((await f.request('/v1/tabs','POST',{to:phone,from:'Laptop',url:'https://u:p@example.com/',title:'x'},a)).status,400);
  const sent=await f.request('/v1/tabs','POST',{to:phone,from:'Laptop',url:'https://example.com/a',title:'Example A'},a);
  assert.equal(sent.status,200);assert.match(sent.body.id,/^[A-Za-z0-9_-]{22}$/);
  assert.equal((await f.request('/v1/tabs','POST',{to:phone,from:'Laptop',url:'https://example.com/a',title:'Example A'},b)).status,404);
  const waiting=await f.request('/v1/tabs?device='+phone,'GET',undefined,a);
  assert.deepEqual(waiting.body.tabs.map(x=>[x.url,x.from,x.title]),[['https://example.com/a','Laptop','Example A']]);
  assert.deepEqual((await f.request('/v1/tabs?device='+phone,'GET',undefined,b)).body.tabs,[]);
  assert.deepEqual((await f.request('/v1/tabs?device='+laptop,'GET',undefined,a)).body.tabs,[]);
  assert.equal((await f.request('/v1/tabs/'+sent.body.id,'DELETE',undefined,b)).status,200);
  assert.equal((await f.request('/v1/tabs?device='+phone,'GET',undefined,a)).body.tabs.length,1);
  assert.equal((await f.request('/v1/tabs/'+sent.body.id,'DELETE',undefined,a)).status,200);
  assert.deepEqual((await f.request('/v1/tabs?device='+phone,'GET',undefined,a)).body.tabs,[]);
  assert.equal((await f.request('/v1/tabs?device='+phone)).status,401);
});
