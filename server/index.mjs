import { mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { createApp } from './app.mjs';
process.umask(0o077);
const dbPath=resolve(process.env.DB_PATH || './data/mica.sqlite');
mkdirSync(dirname(dbPath),{recursive:true,mode:0o700});
try {
  const server=createApp({dbPath,publicUrl:process.env.PUBLIC_URL || 'https://the-api.lessnoise.online',clientId:process.env.DISCORD_CLIENT_ID,clientSecret:process.env.DISCORD_CLIENT_SECRET});
  const port=Number(process.env.PORT || 3000);
  if (!Number.isInteger(port) || port<1 || port>65535) throw new Error('Invalid PORT');
  server.listen(port,process.env.HOST || '127.0.0.1',()=>console.log('Mica sync server ready'));
  for (const signal of ['SIGTERM','SIGINT']) process.on(signal,()=>{server.close(()=>process.exit(0));setTimeout(()=>process.exit(1),10000).unref();});
} catch(error) {console.error('Startup failed: '+error.message);process.exit(1);}
