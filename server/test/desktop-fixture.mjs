// Local-only transport fixture for the native desktop regression; never deployed.
import {createApp} from '../app.mjs';
import {writeFileSync} from 'node:fs';
const app=createApp({dbPath:process.argv[2],clientId:'desktop-test',clientSecret:'fixture-only',pollIntervalMs:0,
  fetchImpl:async url=>new Response(JSON.stringify(String(url).endsWith('/oauth2/token')?{access_token:'fixture-token',token_type:'Bearer'}:{id:'123456789012345678',username:'Desktop fixture'}),{status:200,headers:{'content-type':'application/json'}})});
app.listen(0,'127.0.0.1',()=>writeFileSync(process.argv[3],'http://127.0.0.1:'+app.address().port));
