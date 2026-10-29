(() => {
  if (window.top !== window || location.protocol !== 'https:' || location.hostname !== 'chromewebstore.google.com') return;
  if (window.__micaStoreBridge) return;
  window.__micaStoreBridge = true;
  const token='__MICA_INSTALL_TOKEN__';
  const currentId = () => { const m=location.pathname.match(/^\/detail\/(?:[^/]+\/)?([a-p]{32})\/?$/);return m&&m[1]; };
  function install(e) {
    if (!e.isTrusted || !currentId()) return;
    e.preventDefault(); e.stopImmediatePropagation();
    location.href='mica-install://'+currentId()+'?token='+token;
  }
  document.addEventListener('click',e=>{
    const button=e.target.closest('button,[role="button"]');
    if (button && /^(add to (chrome|edge)|thêm vào (chrome|edge))$/i.test(button.textContent.trim())) install(e);
  },true);
  const button=document.createElement('button');button.id='mica-install-extension';
  button.textContent='＋ Add to Mica';button.setAttribute('aria-label','Add extension to Mica');
  button.style.cssText='position:fixed;right:24px;bottom:24px;z-index:2147483647;border:1px solid #91809e;border-radius:16px;padding:14px 22px;background:#393043;color:#faf7ff;font:600 14px system-ui;box-shadow:0 8px 32px #0003;cursor:pointer';
  button.addEventListener('click',install);document.documentElement.appendChild(button);
  const timer=setInterval(()=>{button.hidden=!currentId();},800);
  window.addEventListener('pagehide',()=>clearInterval(timer),{once:true});
})();
