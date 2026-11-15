// Local-only fixture for the native Discord sync regression (Mica.exe --discord-sync-test); never deployed.
// Serves the real Worker over loopback HTTP with an in-memory D1 and a fake Discord token exchange, so the
// desktop drives the same sign-in (confirmation code included) and /v1/settings code that production runs.
// Usage: node desktop-fixture.mjs <endpoint-file>
import {createServer} from 'node:http';
import {readFileSync,readdirSync,writeFileSync} from 'node:fs';
import {Miniflare,convertV4MiniflareOptions} from 'miniflare';
import {createWorker} from '../index.mjs';

const mf=new Miniflare(convertV4MiniflareOptions({workers:[{name:'api',modules:true,script:"export default {fetch(){return new Response('ok')}}",compatibilityDate:'2026-10-19',d1Databases:['DB']}]}));
const DB=await mf.getD1Database('DB','api');
const migrations=new URL('../migrations/',import.meta.url);
for (const m of readdirSync(migrations).filter(f=>f.endsWith('.sql')).sort()) await DB.exec(readFileSync(new URL(m,migrations),'utf8').replace(/--[^\n]*/g,'').replace(/\s*\n\s*/g,' ').trim());

// Discord itself is mocked: any authorization code becomes the same test user, so two "machines" share one account.
const worker=createWorker({pollIntervalMs:0,fetchImpl:async url=>String(url).endsWith('/oauth2/token')
  ?Response.json({access_token:'fixture-token',token_type:'Bearer'})
  :Response.json({id:'223456789012345678',username:'Sync fixture'})});
const env={DB,PUBLIC_URL:'https://the-api.lessnoise.online',DISCORD_CLIENT_ID:'desktop-test',DISCORD_CLIENT_SECRET:'fixture-only',SIGNIN_CODE_REQUIRED:'true'};

// POST /fixture/legacy-settings makes /v1/settings behave like the Worker before look-and-feel settings
// (only dark, ambientYoutube and strictTracking), so the test can check Mica still syncs against it.
let legacySettings=false;
const legacyKeys=['dark','ambientYoutube','strictTracking'];
const server=createServer(async (req,res)=>{
  try {
    const chunks=[];for await (const chunk of req) chunks.push(chunk);
    if (req.method==='POST'&&req.url==='/fixture/legacy-settings') {legacySettings=true;res.writeHead(204);return res.end();}
    if (legacySettings&&req.method==='PUT'&&req.url==='/v1/settings') {
      let input;try {input=JSON.parse(Buffer.concat(chunks).toString('utf8'));} catch {}
      if (!input||!input.settings||Object.keys(input.settings).some(k=>!legacyKeys.includes(k))) {res.writeHead(400,{'content-type':'application/json'});return res.end('{"error":"invalid_settings"}');}
    }
    const headers=new Headers();for (const [k,v] of Object.entries(req.headers)) if (typeof v==='string') headers.set(k,v);
    const body=chunks.length&&req.method!=='GET'&&req.method!=='HEAD'?Buffer.concat(chunks):undefined;
    const response=await worker.fetch(new Request(env.PUBLIC_URL+req.url.replace(/^\/+/,'/'),{method:req.method,headers,body}),env);
    res.writeHead(response.status,Object.fromEntries(response.headers));res.end(Buffer.from(await response.arrayBuffer()));
  } catch (e) {res.writeHead(500,{'content-type':'text/plain'});res.end(String(e));}
});
server.listen(0,'127.0.0.1',()=>writeFileSync(process.argv[2],'http://127.0.0.1:'+server.address().port));
