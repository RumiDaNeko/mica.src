import http from 'node:http';
import { DatabaseSync } from 'node:sqlite';
import { createHash, randomBytes, timingSafeEqual } from 'node:crypto';

const hash = value => createHash('sha256').update(value).digest('base64url');
const random = () => randomBytes(32).toString('base64url');
const object = value => value && typeof value === 'object' && !Array.isArray(value);
const exact = (value, keys) => object(value) && Object.keys(value).length === keys.length && keys.every(k => Object.hasOwn(value, k));
const fail = (status, code) => Object.assign(new Error(code), {status, code});
const encoded = (value, length) => typeof value === 'string' && /^[A-Za-z0-9+/]+={0,2}$/.test(value) && Buffer.from(value, 'base64').toString('base64') === value && (length ? Buffer.from(value, 'base64').length === length : Buffer.from(value, 'base64').length > 0 && Buffer.from(value, 'base64').length % 16 === 0);
// Plain, non-secret browser preferences (theme, toggles). Secrets stay in the encrypted vault.
export function validSettings(s) {
  return s && typeof s==='object' && !Array.isArray(s) && Object.keys(s).length<=64 && Object.entries(s).every(([k,v])=>/^[A-Za-z][A-Za-z0-9]{0,40}$/.test(k) && (typeof v==='boolean' || (typeof v==='number'&&Number.isFinite(v)) || (typeof v==='string'&&v.length<=200)));
}
// Send tab to device: each Mica install names itself; a sent tab waits for that device to pick it up.
export const deviceId = v => typeof v==='string' && /^[A-Za-z0-9_-]{16,43}$/.test(v);
export const deviceName = v => typeof v==='string' && v.trim().length>0 && v.length<=64 && !/[\u0000-\u001f]/.test(v);
export function validSentTab(t) {
  if (!exact(t,['to','from','url','title']) || !deviceId(t.to) || !deviceName(t.from) || typeof t.title!=='string' || t.title.length>300 || typeof t.url!=='string' || t.url.length>4096) return false;
  try {const u=new URL(t.url);return (u.protocol==='https:'||u.protocol==='http:') && !u.username && !u.password;} catch {return false;}
}
export const DeviceSeenMs = 60*24*3600*1000, PendingTabLimit = 50;
export function validEnvelope(e) {
  return exact(e, ['format', 'kdf', 'iterations', 'salt', 'iv', 'ciphertext', 'mac']) && e.format === 1 && e.kdf === 'PBKDF2-SHA256' && e.iterations === 600000 && encoded(e.salt,16) && encoded(e.iv,16) && encoded(e.mac,32) && encoded(e.ciphertext);
}

export function createApp({dbPath = ':memory:', publicUrl = 'https://the-api.lessnoise.online', clientId, clientSecret, fetchImpl = fetch, now = Date.now, authTtlMs = 300000, sessionTtlMs = 2592000000, pollIntervalMs = 2000} = {}) {
  if (!clientId || !clientSecret) throw new Error('DISCORD_CLIENT_ID and DISCORD_CLIENT_SECRET are required');
  const origin = new URL(publicUrl);
  if (origin.protocol !== 'https:' || origin.username || origin.password || origin.pathname !== '/' || origin.search || origin.hash) throw new Error('PUBLIC_URL must be an HTTPS origin');
  const callback = origin.origin + '/v1/auth/discord/callback';
  const db = new DatabaseSync(dbPath);
  db.exec(`PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;
    CREATE TABLE IF NOT EXISTS users(id TEXT PRIMARY KEY, username TEXT NOT NULL);
    CREATE TABLE IF NOT EXISTS requests(id TEXT PRIMARY KEY, state TEXT UNIQUE NOT NULL, challenge TEXT NOT NULL, expires INTEGER NOT NULL, status TEXT NOT NULL, user_id TEXT, last_poll INTEGER NOT NULL DEFAULT 0);
    CREATE TABLE IF NOT EXISTS sessions(hash TEXT PRIMARY KEY, user_id TEXT NOT NULL REFERENCES users(id), expires INTEGER NOT NULL);
    CREATE TABLE IF NOT EXISTS vaults(user_id TEXT PRIMARY KEY REFERENCES users(id), version INTEGER NOT NULL, envelope TEXT NOT NULL);
    CREATE TABLE IF NOT EXISTS settings(user_id TEXT PRIMARY KEY REFERENCES users(id), updated_at INTEGER NOT NULL, data TEXT NOT NULL);
    CREATE TABLE IF NOT EXISTS devices(user_id TEXT NOT NULL REFERENCES users(id), id TEXT NOT NULL, name TEXT NOT NULL, seen INTEGER NOT NULL, PRIMARY KEY(user_id,id));
    CREATE TABLE IF NOT EXISTS sent_tabs(id TEXT PRIMARY KEY, user_id TEXT NOT NULL REFERENCES users(id), to_device TEXT NOT NULL, from_name TEXT NOT NULL, url TEXT NOT NULL, title TEXT NOT NULL, created INTEGER NOT NULL);`);
  if (!db.prepare('PRAGMA table_info(users)').all().some(column=>column.name==='avatar')) db.exec('ALTER TABLE users ADD COLUMN avatar TEXT');
  const stmt = sql => db.prepare(sql);
  const rates = new Map();
  function rate(key, maximum, period) {
    const time = now(); let entry = rates.get(key);
    if (!entry || entry.until <= time) {
      if (!entry && rates.size >= 10000) {
        for (const [k,v] of rates) if (v.until <= time) rates.delete(k);
        if (rates.size >= 10000) throw fail(503, 'busy');
      }
      entry = {count:0, until:time+period}; rates.set(key, entry);
    }
    if (++entry.count > maximum) throw fail(429, 'rate_limited');
  }
  function cleanup() {
    stmt('DELETE FROM requests WHERE expires <= ?').run(now());
    stmt('DELETE FROM sessions WHERE expires <= ?').run(now());
    for (const [k,v] of rates) if (v.until <= now()) rates.delete(k);
  }
  const timer = setInterval(cleanup, 60000); timer.unref();
  async function body(req, max = 8192) {
    if ((req.headers['content-type'] || '').split(';')[0].trim().toLowerCase() !== 'application/json') throw fail(415, 'json_required');
    if (req.headers['content-encoding'] && req.headers['content-encoding'] !== 'identity') throw fail(415, 'encoding_not_supported');
    if (Number(req.headers['content-length']) > max) throw fail(413, 'payload_too_large');
    let size=0; const chunks=[];
    for await (const chunk of req) { size+=chunk.length; if (size>max) throw fail(413,'payload_too_large'); chunks.push(chunk); }
    try { return JSON.parse(Buffer.concat(chunks).toString('utf8')); } catch { throw fail(400,'invalid_json'); }
  }
  function authenticated(req) {
    const match = /^Bearer ([A-Za-z0-9_-]{43})$/.exec(req.headers.authorization || '');
    if (!match) throw fail(401, 'unauthorized');
    const tokenHash=hash(match[1]);
    const user=stmt('SELECT users.id,users.username,users.avatar FROM sessions JOIN users ON users.id=sessions.user_id WHERE sessions.hash=? AND sessions.expires>?').get(tokenHash,now());
    if (!user) throw fail(401,'unauthorized');
    rate('user:'+user.id,120,60000);
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
    return {id:user.id,username:user.username,avatar:typeof user.avatar==='string'&&/^(a_)?[0-9a-f]{32}$/.test(user.avatar)?user.avatar:null};
  }
  const server=http.createServer(async (req,res) => {
    const send=(status,payload) => {res.writeHead(status,{'Content-Type':'application/json; charset=utf-8'});res.end(JSON.stringify(payload));};
    res.setHeader('Cache-Control','no-store'); res.setHeader('X-Content-Type-Options','nosniff');
    res.setHeader('Referrer-Policy','no-referrer'); res.setHeader('Content-Security-Policy',"default-src 'none'; frame-ancestors 'none'");
    try {
      rate('ip:'+(req.socket.remoteAddress || 'unknown'),1000,60000);
      const url=new URL(req.url,'http://localhost'); const path=url.pathname;
      if (req.headers.origin && req.headers.origin !== origin.origin) throw fail(403,'origin_not_allowed');
      if (req.method==='GET' && path==='/healthz') return send(200,{status:'ok'});
      if (req.method==='POST' && path==='/v1/auth/discord/start') {
        rate('start:'+(req.socket.remoteAddress || 'unknown'),20,60000);
        const input=await body(req);
        if (!exact(input,['challenge']) || typeof input.challenge !== 'string' || !/^[A-Za-z0-9_-]{43}$/.test(input.challenge)) throw fail(400,'invalid_request');
        cleanup();
        const id=random(),state=random(),expires=now()+authTtlMs;
        stmt("INSERT INTO requests(id,state,challenge,expires,status) VALUES(?,?,?,?,'pending')").run(id,hash(state),input.challenge,expires);
        const auth=new URL('https://discord.com/oauth2/authorize');
        auth.search=new URLSearchParams({client_id:clientId,response_type:'code',redirect_uri:callback,scope:'identify',state,prompt:'consent'}).toString();
        return send(200,{requestId:id,authorizationUrl:auth.toString(),expiresAt:new Date(expires).toISOString()});
      }
      if (req.method==='GET' && path==='/v1/auth/discord/callback') {
        const state=url.searchParams.get('state'),code=url.searchParams.get('code');
        if (!state || !/^[A-Za-z0-9_-]{43}$/.test(state)) throw fail(400,'invalid_state');
        const request=stmt("UPDATE requests SET status='processing' WHERE state=? AND status='pending' AND expires>? RETURNING id").get(hash(state),now());
        if (!request) throw fail(400,'invalid_state');
        if (!code || code.length>2048 || url.searchParams.has('error')) {stmt("UPDATE requests SET status='failed' WHERE id=?").run(request.id);throw fail(400,'authorization_denied');}
        try {
          const user=await exchange(code);
          stmt('INSERT INTO users(id,username,avatar) VALUES(?,?,?) ON CONFLICT(id) DO UPDATE SET username=excluded.username,avatar=excluded.avatar').run(user.id,user.username,user.avatar);
          stmt("UPDATE requests SET status='ready',user_id=? WHERE id=? AND expires>?").run(user.id,request.id,now());
        } catch {stmt("UPDATE requests SET status='failed' WHERE id=?").run(request.id);throw fail(502,'discord_auth_failed');}
        res.writeHead(200,{'Content-Type':'text/html; charset=utf-8'});
        return res.end('<!doctype html><html lang="en"><meta charset="utf-8"><title>Mica sign-in</title><h1>Sign-in complete</h1><p>You can close this window and return to Mica.</p></html>');
      }
      if (req.method==='POST' && path==='/v1/auth/discord/poll') {
        const input=await body(req);
        if (!exact(input,['requestId','verifier']) || typeof input.requestId !== 'string' || !/^[A-Za-z0-9_-]{43}$/.test(input.requestId) || typeof input.verifier !== 'string' || !/^[A-Za-z0-9_-]{43,128}$/.test(input.verifier)) throw fail(400,'invalid_request');
        const request=stmt('SELECT * FROM requests WHERE id=? AND expires>?').get(input.requestId,now());
        if (!request || !timingSafeEqual(Buffer.from(hash(input.verifier)),Buffer.from(request.challenge))) throw fail(401,'invalid_proof');
        if (request.last_poll && now()-request.last_poll<pollIntervalMs) throw fail(429,'rate_limited');
        stmt('UPDATE requests SET last_poll=? WHERE id=?').run(now(),request.id);
        if (request.status==='failed') throw fail(400,'authorization_failed');
        if (request.status!=='ready') return send(200,{status:'pending'});
        const token=random(), user=stmt('SELECT id,username,avatar FROM users WHERE id=?').get(request.user_id);
        db.exec('BEGIN IMMEDIATE');
        try {stmt('INSERT INTO sessions(hash,user_id,expires) VALUES(?,?,?)').run(hash(token),user.id,now()+sessionTtlMs);stmt('DELETE FROM requests WHERE id=?').run(request.id);db.exec('COMMIT');} catch(e) {db.exec('ROLLBACK');throw e;}
        return send(200,{status:'ready',token,user});
      }
      if (path==='/v1/account' || path==='/v1/auth/logout' || path==='/v1/vault' || path==='/v1/settings') {
        const {user,tokenHash}=authenticated(req);
        if (req.method==='GET' && path==='/v1/account') return send(200,{user});
        if (req.method==='POST' && path==='/v1/auth/logout') {stmt('DELETE FROM sessions WHERE hash=?').run(tokenHash);return send(200,{status:'ok'});}
        if (req.method==='GET' && path==='/v1/vault') {const row=stmt('SELECT version,envelope FROM vaults WHERE user_id=?').get(user.id);return send(200,row?{version:row.version,envelope:JSON.parse(row.envelope)}:{version:0,envelope:null});}
        if (req.method==='GET' && path==='/v1/settings') {const row=stmt('SELECT updated_at,data FROM settings WHERE user_id=?').get(user.id);return send(200,row?{settings:JSON.parse(row.data),updatedAt:row.updated_at}:{settings:null,updatedAt:0});}
        if (req.method==='PUT' && path==='/v1/settings') {
          const input=await body(req,16*1024);
          if (!exact(input,['settings','updatedAt']) || !Number.isSafeInteger(input.updatedAt) || input.updatedAt<=0 || !validSettings(input.settings)) throw fail(400,'invalid_settings');
          stmt('INSERT INTO settings(user_id,updated_at,data) VALUES(?,?,?) ON CONFLICT(user_id) DO UPDATE SET updated_at=excluded.updated_at,data=excluded.data WHERE excluded.updated_at>=settings.updated_at').run(user.id,input.updatedAt,JSON.stringify(input.settings));
          const row=stmt('SELECT updated_at,data FROM settings WHERE user_id=?').get(user.id);
          return send(200,{settings:JSON.parse(row.data),updatedAt:row.updated_at});
        }
        if (req.method==='PUT' && path==='/v1/vault') {
          const input=await body(req,2*1024*1024);
          if (!exact(input,['version','envelope']) || !Number.isSafeInteger(input.version) || input.version<0 || input.version>=Number.MAX_SAFE_INTEGER || !validEnvelope(input.envelope)) throw fail(400,'invalid_vault');
          db.exec('BEGIN IMMEDIATE');
          try {
            const current=stmt('SELECT version FROM vaults WHERE user_id=?').get(user.id);
            if ((current?.version || 0)!==input.version) throw fail(409,'version_conflict');
            stmt('INSERT INTO vaults(user_id,version,envelope) VALUES(?,?,?) ON CONFLICT(user_id) DO UPDATE SET version=excluded.version,envelope=excluded.envelope').run(user.id,input.version+1,JSON.stringify(input.envelope));
            db.exec('COMMIT');
          } catch(e) {db.exec('ROLLBACK');throw e;}
          return send(200,{version:input.version+1});
        }
      }
      if (path==='/v1/devices' || path==='/v1/tabs' || /^\/v1\/tabs\/[A-Za-z0-9_-]{22}$/.test(path)) {
        const {user}=authenticated(req);
        const listDevices=()=>stmt('SELECT id,name,seen FROM devices WHERE user_id=? AND seen>? ORDER BY seen DESC').all(user.id,now()-DeviceSeenMs).map(d=>({id:d.id,name:d.name,seen:d.seen}));
        if (req.method==='GET' && path==='/v1/devices') return send(200,{devices:listDevices()});
        if (req.method==='POST' && path==='/v1/devices') {
          const input=await body(req,4096);
          if (!exact(input,['id','name']) || !deviceId(input.id) || !deviceName(input.name)) throw fail(400,'invalid_device');
          if (stmt('SELECT COUNT(*) AS n FROM devices WHERE user_id=? AND id<>?').get(user.id,input.id).n>=20) throw fail(409,'too_many_devices');
          stmt('INSERT INTO devices(user_id,id,name,seen) VALUES(?,?,?,?) ON CONFLICT(user_id,id) DO UPDATE SET name=excluded.name,seen=excluded.seen').run(user.id,input.id,input.name.trim(),now());
          return send(200,{devices:listDevices()});
        }
        if (req.method==='POST' && path==='/v1/tabs') {
          const input=await body(req,8192);
          if (!validSentTab(input)) throw fail(400,'invalid_tab');
          if (!stmt('SELECT 1 AS ok FROM devices WHERE user_id=? AND id=?').get(user.id,input.to)) throw fail(404,'unknown_device');
          if (stmt('SELECT COUNT(*) AS n FROM sent_tabs WHERE user_id=? AND to_device=?').get(user.id,input.to).n>=PendingTabLimit) throw fail(409,'too_many_tabs');
          const id=randomBytes(16).toString('base64url');
          stmt('INSERT INTO sent_tabs(id,user_id,to_device,from_name,url,title,created) VALUES(?,?,?,?,?,?,?)').run(id,user.id,input.to,input.from.trim(),input.url,input.title,now());
          return send(200,{id});
        }
        if (req.method==='GET' && path==='/v1/tabs') {
          const device=url.searchParams.get('device');if (!deviceId(device)) throw fail(400,'invalid_device');
          const tabs=stmt('SELECT id,from_name,url,title,created FROM sent_tabs WHERE user_id=? AND to_device=? ORDER BY created').all(user.id,device).map(t=>({id:t.id,from:t.from_name,url:t.url,title:t.title,created:t.created}));
          return send(200,{tabs});
        }
        if (req.method==='DELETE' && path.startsWith('/v1/tabs/')) {stmt('DELETE FROM sent_tabs WHERE user_id=? AND id=?').run(user.id,path.slice(9));return send(200,{status:'ok'});}
      }
      throw fail(404,'not_found');
    } catch(error) {
      if (res.headersSent || res.destroyed) return;
      if (error.status===429) res.setHeader('Retry-After','2');
      if (error.status===413) {res.setHeader('Connection','close');req.resume();}
      send(error.status || 500,{error:error.code && error.status ? error.code : 'internal_error'});
    }
  });
  server.requestTimeout=30000;server.headersTimeout=15000;server.maxHeadersCount=64;
  server.once('close',()=>{clearInterval(timer);db.close();});
  return server;
}
