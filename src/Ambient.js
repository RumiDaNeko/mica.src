(() => {
  if (window.top !== window || !/(^|\.)youtube\.com$/i.test(location.hostname)) return false;
  if (window.__micaAmbient) return true;
  const canvas = document.createElement('canvas');
  canvas.width = 64; canvas.height = 36;
  canvas.setAttribute('aria-hidden', 'true');
  canvas.dataset.micaAmbient = 'true';
  canvas.style.cssText = 'position:fixed;pointer-events:none;z-index:2000;filter:blur(60px);opacity:.25;transition:opacity .3s;border-radius:24px;mix-blend-mode:screen;';
  const ctx = canvas.getContext('2d', {alpha:false});
  let stopped = false, timer = null;
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  function draw() {
    if (stopped) return;
    const video = document.querySelector('video');
    if (!document.hidden && !reduced.matches && video && video.readyState >= 2 && location.pathname === '/watch' && !document.fullscreenElement) {
      if (!canvas.isConnected) document.documentElement.appendChild(canvas);
      const box = video.getBoundingClientRect();
      canvas.style.display = box.width && box.height ? 'block' : 'none';
      canvas.style.left = (box.left - 25) + 'px'; canvas.style.top = (box.top - 25) + 'px';
      canvas.style.width = (box.width + 50) + 'px'; canvas.style.height = (box.height + 50) + 'px';
      try { ctx.drawImage(video, 0, 0, 64, 36); } catch (_) { canvas.style.display = 'none'; }
    } else canvas.style.display = 'none';
    timer = setTimeout(draw, document.hidden ? 1000 : 125);
  }
  const api = {stop() {stopped=true;clearTimeout(timer);canvas.remove();window.removeEventListener('pagehide',api.stop);delete window.__micaAmbient;}};
  window.__micaAmbient = api;
  window.addEventListener('pagehide',api.stop,{once:true});
  draw(); return true;
})();
