// Reading mode: finds the article on the page and returns it as plain blocks for Mica to draw.
// The container holding the most paragraph text (with links and boilerplate weighed down) wins.
(() => {
  const bad = /comment|footer|footnote|sidebar|related|share|social|promo|newsletter|subscribe|advert|sponsor|cookie|banner|nav|menu|breadcrumb|popup|modal|widget|outbrain|taboola/i;
  const text = e => (e.innerText || e.textContent || '').replace(/\s+/g, ' ').trim();
  const skip = e => {
    for (let n = e; n && n !== document.body; n = n.parentElement) {
      const id = (n.id || '') + ' ' + (typeof n.className === 'string' ? n.className : '');
      if (bad.test(id) && !/article|content|post|story|entry|main/i.test(id)) return true;
      const tag = n.tagName;
      if (tag === 'NAV' || tag === 'FOOTER' || tag === 'ASIDE' || tag === 'FORM') return true;
    }
    return false;
  };
  const scores = new Map();
  for (const p of document.querySelectorAll('p, pre, blockquote')) {
    const t = text(p); if (t.length < 60 || skip(p)) continue;
    const linkText = [...p.querySelectorAll('a')].reduce((n, a) => n + text(a).length, 0);
    const score = (t.length - linkText * 1.5) / 100 + (t.split(',').length - 1) * 0.2;
    if (score <= 0) continue;
    let parent = p.parentElement, weight = 1;
    for (let i = 0; parent && i < 3; i++, parent = parent.parentElement, weight /= 2) scores.set(parent, (scores.get(parent) || 0) + score * weight);
  }
  let best = null, top = 0;
  for (const [e, s] of scores) { const tag = e.tagName; const bonus = tag === 'ARTICLE' || tag === 'MAIN' ? 1.3 : 1; if (s * bonus > top) { top = s * bonus; best = e; } }
  const article = document.querySelector('article');
  if (article && (!best || article.contains(best) && text(article).length < text(best).length * 3)) best = best && article.contains(best) ? best : article;
  if (!best || top < 3) return JSON.stringify({ ok: false });

  const blocks = [], seen = new Set();
  const add = (t, value) => { if (!value) return; const key = t + value; if (seen.has(key)) return; seen.add(key); blocks.push(Object.assign({ t }, typeof value === 'string' ? { text: value } : value)); };
  const walk = n => {
    if (n.nodeType !== 1 || skip(n)) return;
    const tag = n.tagName, style = getComputedStyle(n);
    if (style.display === 'none' || style.visibility === 'hidden') return;
    if (/^H[1-6]$/.test(tag)) { const t = text(n); if (t && t.length < 200) add(tag <= 'H2' ? 'h2' : 'h3', t); return; }
    if (tag === 'P') { const t = text(n); if (t.length > 1) add('p', t); for (const img of n.querySelectorAll('img')) walk(img); return; }
    if (tag === 'PRE') { add('pre', (n.innerText || '').replace(/\s+$/, '')); return; }
    if (tag === 'BLOCKQUOTE') { add('quote', text(n)); return; }
    if (tag === 'LI') { const t = text(n); if (t) add('li', t); return; }
    if (tag === 'IMG') {
      // The largest picture the page offers (up to 1600 wide), not the thumbnail it happened to load.
      let src = n.currentSrc || n.src || n.dataset.src || '', w = n.naturalWidth || n.width, best = 0;
      for (const part of (n.srcset || '').split(',')) {
        const [url, size] = part.trim().split(/\s+/); if (!url) continue;
        const width = /w$/.test(size || '') ? parseInt(size) : /x$/.test(size || '') ? parseFloat(size) * w : 0;
        if (width > best && width <= 1600) { best = width; try { src = new URL(url, location.href).href; } catch (e) {} }
      }
      if (/^https?:/.test(src) && w >= 180) add('img', { src, text: n.alt || '' });
      return;
    }
    if (tag === 'FIGCAPTION') { add('caption', text(n)); return; }
    if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'SVG' || tag === 'BUTTON' || tag === 'IFRAME') return;
    for (const child of n.children) walk(child);
    // Loose text right inside a container (some sites skip <p>).
    if (!n.querySelector('p,li,h1,h2,h3,h4,pre,blockquote') && n.children.length === 0) { const t = text(n); if (t.length > 80) add('p', t); }
  };
  walk(best);
  const meta = name => { const m = document.querySelector('meta[property="' + name + '"],meta[name="' + name + '"]'); return m ? m.content.trim() : ''; };
  const heading = document.querySelector('h1');
  const title = meta('og:title') || (heading ? text(heading) : '') || document.title;
  const author = meta('author') || meta('article:author');
  const words = blocks.reduce((n, b) => n + (b.text ? b.text.split(' ').length : 0), 0);
  if (words < 120) return JSON.stringify({ ok: false });
  // The article's own title would repeat the heading Mica draws.
  if (blocks.length && blocks[0].t === 'h2' && blocks[0].text === title) blocks.shift();
  const lang = (document.documentElement.lang || '').slice(0, 2).toLowerCase();
  return JSON.stringify({ ok: true, lang, url: location.href, title, author: /^https?:/.test(author) ? '' : author, site: meta('og:site_name') || location.hostname.replace(/^www\./, ''), words, blocks: blocks.slice(0, 600) });
})()
