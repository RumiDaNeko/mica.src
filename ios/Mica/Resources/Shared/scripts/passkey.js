(()=>{if(window.top!==window||location.protocol!=='https:'||typeof micaLogin==='undefined'||window.__micaPasskey)return;window.__micaPasskey=1;
// Passkeys go to Android's Credential Manager. Until Mica is on the providers' list of trusted browsers the
// call fails at once (a person cancelling takes longer), so Mica can say why instead of the page's bare error.
const c=navigator.credentials;if(!c||!c.get||!c.create||!window.PublicKeyCredential)return;
const watch=(name)=>{const original=c[name].bind(c);c[name]=function(o){const t=Date.now();const p=original(o);
 if(o&&o.publicKey&&o.mediation!=='conditional')p.catch(e=>{if(e&&e.name==='NotAllowedError'&&Date.now()-t<1500){try{micaLogin.postMessage(JSON.stringify({mica:'passkeyFailed'}))}catch(x){}}});
 return p;};};
watch('get');watch('create');})();
