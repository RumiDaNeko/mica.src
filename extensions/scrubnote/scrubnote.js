/*
 * scrubnote: record the animations on a page, scrub to the frame that looks
 * wrong, comment on an element, and copy exact timings/values for an agent.
 *
 * Load it on any page: paste this file into the DevTools console, add
 * <script src="scrubnote.js"></script>, or use the bookmarklet in README.md.
 * Running it again toggles the toolbar.
 *
 * Captures everything in document.getAnimations() (CSS transitions, CSS
 * animations, WAAPI / Motion's WAAPI path), inline-style changes made by
 * JS/rAF animation loops, and text changes on leaf elements. Other DOM nodes
 * added or removed during the recording are not replayed.
 */
(() => {
  if (window.__scrubnote) {
    window.__scrubnote.toggle();
    return;
  }

  const FRAME = 1000 / 60;
  const SPEEDS = [1, 0.75, 0.5, 0.25, 0.1];

  const state = {
    phase: 'idle', // idle | recording | review
    frozen: false, // review of a single frozen frame (picker used without recording)
    t0: 0,
    duration: 0,
    T: 0,
    range: [0, 0], // trim in/out
    recs: [],
    groups: [],
    styleTracks: new Map(), // Element -> { base, frames: [{ t, v }] } for the style attribute
    textTracks: new Map(), // same shape, for textContent of leaf elements
    comments: [],
    commentMode: false,
    playing: false,
    speed: 1,
    pausedAll: false,
  };

  const origRate = new WeakMap(); // Animation -> playbackRate before we slowed it down
  const pausedByUs = new Set();

  // ---------- icons ----------

  const svg = (d, extra = '') =>
    `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" ${extra}>${d}</svg>`;
  const ICON = {
    record: svg('<circle cx="12" cy="12" r="8"/>'),
    pause: svg('<path d="M9 5v14M15 5v14"/>'),
    resume: svg('<path d="M7 5l12 7-12 7z"/>'),
    speed: svg('<circle cx="12" cy="12" r="8"/><path d="M12 12l3-3"/>'),
    pick: svg('<path d="M4 9V5a1 1 0 0 1 1-1h4M15 4h4a1 1 0 0 1 1 1v4M4 15v4a1 1 0 0 0 1 1h4"/><path d="M12 12l8 3-3.5 1.5L15 20z"/>'),
    stop: '<svg width="14" height="14" viewBox="0 0 14 14"><rect width="14" height="14" rx="3" fill="#ff4d4d"/></svg>',
    back: svg('<path d="M9 14L4 9l5-5"/><path d="M4 9h10a6 6 0 0 1 0 12h-3"/>'),
    play: svg('<path d="M7 5l12 7-12 7z"/>'),
    playing: svg('<path d="M9 5v14M15 5v14"/>'),
    comment: svg('<rect x="3" y="4" width="18" height="13" rx="3"/><path d="M8 21l4-4"/>'),
    list: svg('<path d="M5 7h14M5 12h14M5 17h14"/>'),
    copy: svg('<rect x="9" y="9" width="11" height="11" rx="2"/><path d="M5 15V6a2 2 0 0 1 2-2h8"/>'),
  };

  // ---------- UI shell (shadow DOM keeps page CSS out) ----------

  const host = document.createElement('div');
  host.id = 'scrubnote-host';
  host.style.cssText = 'all:initial;position:fixed;inset:0;pointer-events:none;z-index:2147483647;';
  const shadow = host.attachShadow({ mode: 'open' });
  shadow.innerHTML = `
<style>
  :host { all: initial; }
  * { box-sizing: border-box; font-family: ui-sans-serif, system-ui, -apple-system, "Segoe UI", sans-serif; }
  .bar {
    position: fixed; left: 50%; bottom: 20px; transform: translateX(-50%);
    display: flex; align-items: center; gap: 2px; padding: 5px; height: 46px;
    background: #161618; color: #ededf0; border-radius: 999px;
    box-shadow: 0 8px 24px rgba(0,0,0,.28), 0 0 0 1px rgba(255,255,255,.06) inset; pointer-events: auto;
  }
  .bar.recording { padding: 5px; }
  .bar.review { width: min(560px, calc(100vw - 32px)); }
  button {
    all: unset; cursor: pointer; width: 36px; height: 36px; border-radius: 999px; flex: none;
    display: inline-flex; align-items: center; justify-content: center; color: #8e8e98; position: relative;
  }
  button:hover { background: #26262a; color: #f4f4f6; }
  button:focus-visible { outline: 2px solid #ec8686; outline-offset: 1px; }
  button.on { background: #ec8686; color: #fff; }
  button.lit { color: #f4f4f6; background: #26262a; }
  .speedtag { position: absolute; bottom: 3px; right: 1px; font-size: 9px; font-weight: 600; color: #f5c542; }
  .track {
    position: relative; flex: 1; height: 34px; margin: 0 4px; border-radius: 8px; background: #232327;
    cursor: ew-resize; touch-action: none; min-width: 120px;
  }
  .frozen { flex: 1; text-align: center; color: #8e8e98; font-size: 12px; }
  .trim { position: absolute; top: 0; bottom: 0; border: 2.5px solid #f4f4f6; border-radius: 8px; pointer-events: none; }
  .handle { position: absolute; top: 0; bottom: 0; width: 12px; margin-left: -6px; cursor: col-resize; z-index: 2; }
  .handle::after { content: ""; position: absolute; left: 4px; top: 9px; bottom: 9px; width: 3px; border-radius: 2px; background: #5d5d66; }
  .dia { position: absolute; top: 50%; width: 8px; height: 8px; margin: -4px 0 0 -4px; transform: rotate(45deg); background: #f5b82e; border-radius: 1px; cursor: pointer; }
  .dia.multi { background: #f97316; box-shadow: 0 0 0 2px rgba(249,115,22,.3); }
  .dia.edge { background: transparent; border: 1.2px solid #7a7a84; }
  .cdot { position: absolute; top: 50%; width: 7px; height: 7px; margin: -3.5px 0 0 -3.5px; border-radius: 50%; background: #ec8686; }
  .head { position: absolute; top: 4px; bottom: 4px; width: 3px; margin-left: -1.5px; background: #fff; border-radius: 2px; pointer-events: none; }
  .ms { position: absolute; right: 8px; bottom: 1px; font-size: 9.5px; color: #8e8e98; font-variant-numeric: tabular-nums; pointer-events: none; }
  .tip {
    position: fixed; padding: 4px 8px; border-radius: 6px; background: #161618; color: #e9e9ee; font-size: 12px;
    white-space: nowrap; display: none; pointer-events: none; box-shadow: 0 4px 12px rgba(0,0,0,.25);
  }
  .tip kbd { font: inherit; color: #8e8e98; margin-left: 6px; }
  .hl { position: fixed; pointer-events: none; border: 1.5px solid #ec8686; background: rgba(236,134,134,.10); border-radius: 4px; display: none; }
  .hl span {
    position: absolute; left: -1.5px; top: -21px; padding: 2px 6px; border-radius: 4px; background: #ec8686;
    color: #fff; font: 11px ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; white-space: nowrap;
  }
  .pin {
    position: fixed; width: 14px; height: 14px; margin: -7px 0 0 -7px; border-radius: 50%; background: #ec8686;
    border: 2px solid #fff; box-shadow: 0 2px 6px rgba(0,0,0,.3); pointer-events: auto; cursor: pointer;
  }
  .pop {
    position: fixed; width: 320px; padding: 8px; display: none; pointer-events: auto;
    background: #161618; border-radius: 16px; box-shadow: 0 14px 36px rgba(0,0,0,.35);
  }
  textarea {
    width: 100%; height: 72px; resize: none; padding: 9px 10px; border-radius: 10px; border: 1.5px solid #ec8686;
    background: #1d1d21; color: #f1f1f4; font-size: 13px; outline: none;
  }
  textarea::placeholder { color: #6a6a74; }
  .row { display: flex; align-items: center; gap: 4px; margin-top: 6px; }
  .sel { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: #7d7d88; font: 11px ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; padding-left: 4px; }
  .row button { width: auto; height: 30px; padding: 0 12px; font-size: 12.5px; color: #a6a6b0; }
  .row .add { background: #ec8686; color: #fff; }
  .row .add:hover { background: #e27272; }
  .list {
    position: fixed; left: 50%; bottom: 76px; transform: translateX(-50%); width: min(560px, calc(100vw - 32px));
    max-height: 40vh; overflow: auto; display: none; pointer-events: auto; padding: 6px;
    background: #161618; border-radius: 16px; color: #ededf0; font-size: 12.5px; box-shadow: 0 10px 30px rgba(0,0,0,.3);
  }
  .item { display: flex; gap: 10px; align-items: flex-start; padding: 7px 8px; border-radius: 10px; cursor: pointer; }
  .item:hover { background: #222226; }
  .item .t { color: #f2a7a7; font-variant-numeric: tabular-nums; min-width: 52px; }
  .item .body { flex: 1; min-width: 0; }
  .item .s { color: #7d7d88; font: 11px ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .item button { width: 24px; height: 24px; }
  .empty { padding: 10px; color: #7d7d88; text-align: center; }
  .toast {
    position: fixed; left: 50%; bottom: 76px; transform: translateX(-50%); padding: 7px 12px; border-radius: 10px;
    background: #ec8686; color: #fff; font-size: 12.5px; display: none;
  }
</style>
<div class="pins"></div>
<div class="hl"><span></span></div>
<div class="pop">
  <textarea placeholder="Describe the change…"></textarea>
  <div class="row"><span class="sel"></span><button class="cancel">Cancel</button><button class="add">Add</button></div>
</div>
<div class="list"></div>
<div class="toast"></div>
<div class="tip"></div>
<div class="bar"></div>`;

  const $ = (s) => shadow.querySelector(s);
  const ui = {
    bar: $('.bar'), pins: $('.pins'), hl: $('.hl'), hlLabel: $('.hl span'), pop: $('.pop'), text: $('textarea'),
    sel: $('.sel'), add: $('.add'), cancel: $('.cancel'), list: $('.list'), toast: $('.toast'), tip: $('.tip'),
  };

  const isOurs = (node) => !!node && (node === host || host.contains(node) || node.getRootNode?.() === shadow);
  const now = () => document.timeline?.currentTime ?? performance.now();

  // ---------- live controls: pause all / slow motion ----------

  function pageAnimations() {
    return document.getAnimations().filter((a) => !isOurs(a.effect?.target));
  }

  function governLive(a) {
    if (!origRate.has(a)) origRate.set(a, a.playbackRate);
    const want = origRate.get(a) * state.speed;
    if (a.playbackRate !== want) {
      try { a.updatePlaybackRate(want); } catch { a.playbackRate = want; }
    }
    if (state.pausedAll && a.playState === 'running') {
      a.pause();
      pausedByUs.add(a);
    }
  }

  function governAll() {
    if (state.phase === 'review') return;
    for (const a of pageAnimations()) governLive(a);
  }

  function setSpeed(next) {
    state.speed = next;
    governAll();
    render();
    toast(`${next}x`);
  }

  function setPausedAll(on) {
    state.pausedAll = on;
    if (on) governAll();
    else {
      for (const a of pausedByUs) {
        try { a.play(); } catch {}
      }
      pausedByUs.clear();
    }
    render();
  }

  // ---------- recording ----------

  function kindOf(a) {
    if (typeof CSSTransition !== 'undefined' && a instanceof CSSTransition) return 'css-transition';
    if (typeof CSSAnimation !== 'undefined' && a instanceof CSSAnimation) return 'css-animation';
    return 'waapi';
  }

  function describe(a) {
    const eff = a.effect;
    if (!eff || !eff.target || isOurs(eff.target)) return null;
    let keyframes = [];
    try { keyframes = eff.getKeyframes(); } catch {}
    const skip = new Set(['offset', 'computedOffset', 'easing', 'composite']);
    const props = [...new Set(keyframes.flatMap((k) => Object.keys(k).filter((p) => !skip.has(p))))];
    const timing = eff.getTiming();
    return {
      anim: a,
      kind: kindOf(a),
      name: a.transitionProperty || a.animationName || a.id || '',
      target: eff.target,
      pseudo: eff.pseudoElement || null,
      timing: {
        delay: timing.delay, duration: timing.duration, endDelay: timing.endDelay, easing: timing.easing,
        iterations: timing.iterations, direction: timing.direction, fill: timing.fill,
        playbackRate: origRate.has(a) ? origRate.get(a) : a.playbackRate, composite: eff.composite,
      },
      keyframes,
      props,
      relStart: null,
      rate: 1, // playback rate while recording (slow motion), set on stop
      wasRunning: false,
    };
  }

  // Several capture paths, so animations shorter than a frame or created while
  // rAF is throttled are still caught: Element.animate, CSS events, polling.
  let seen = new Set();
  let mo = null;
  let pollTimer = 0;
  let nativeAnimate = null;

  function collect(a) {
    if (seen.has(a)) return;
    seen.add(a);
    governLive(a);
    const rec = describe(a);
    if (rec) state.recs.push(rec);
  }

  function resolveStarts() {
    for (const r of state.recs) {
      if (r.relStart == null && r.anim.startTime != null) r.relStart = r.anim.startTime - state.t0;
    }
  }

  function poll() {
    if (state.phase === 'recording') {
      for (const a of document.getAnimations()) collect(a);
      resolveStarts();
      state.duration = now() - state.t0;
      renderTime();
    } else if (state.phase === 'idle' && (state.speed !== 1 || state.pausedAll)) {
      governAll();
    }
  }

  function onCssEvent(e) {
    if (state.phase !== 'recording' || isOurs(e.target)) return;
    try { for (const a of e.target.getAnimations()) collect(a); } catch {}
  }

  const push = (tracks, el, base, v, t) => {
    let track = tracks.get(el);
    if (!track) tracks.set(el, (track = { base, frames: [] }));
    const last = track.frames[track.frames.length - 1];
    if (last && last.t === t) last.v = v;
    else track.frames.push({ t, v });
  };

  function startRecording() {
    if (state.phase === 'review') exitReview();
    Object.assign(state, {
      recs: [], groups: [], styleTracks: new Map(), textTracks: new Map(), comments: [],
      T: 0, duration: 0, t0: now(), phase: 'recording', frozen: false,
    });
    seen = new Set();
    // Animations already running when recording starts are part of the clip.
    for (const a of document.getAnimations()) collect(a);

    nativeAnimate = Element.prototype.animate;
    Element.prototype.animate = function (...args) {
      const a = nativeAnimate.apply(this, args);
      if (state.phase === 'recording') collect(a);
      return a;
    };

    mo = new MutationObserver((muts) => {
      const t = performance.now() - state.t0;
      for (const m of muts) {
        if (m.type === 'attributes') {
          if (!isOurs(m.target)) push(state.styleTracks, m.target, m.oldValue, m.target.getAttribute('style'), t);
          continue;
        }
        // Text changes on leaf elements (counters, labels). Other DOM changes are not replayed.
        const el = m.type === 'characterData' ? m.target.parentElement : m.target;
        if (!el || isOurs(el) || el.children.length) continue;
        const onlyText = [...m.addedNodes, ...m.removedNodes].every((n) => n.nodeType === 3);
        if (m.type === 'childList' && !onlyText) continue;
        const base = m.type === 'characterData' ? m.oldValue : [...m.removedNodes].map((n) => n.data).join('');
        push(state.textTracks, el, base, el.textContent, t);
      }
    });
    mo.observe(document.documentElement, {
      attributes: true, attributeFilter: ['style'], attributeOldValue: true,
      characterData: true, characterDataOldValue: true, childList: true, subtree: true,
    });

    const tick = () => {
      if (state.phase !== 'recording') return;
      poll();
      requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
    render();
  }

  function stopRecording() {
    poll();
    mo?.disconnect();
    mo = null;
    if (nativeAnimate) Element.prototype.animate = nativeAnimate;
    nativeAnimate = null;
    const end = now();
    state.duration = Math.max(end - state.t0, FRAME);
    for (const r of state.recs) {
      const a = r.anim;
      if (r.relStart == null && a.currentTime != null) r.relStart = end - state.t0 - a.currentTime / (a.playbackRate || 1);
    }
    state.recs = state.recs.filter((r) => r.relStart != null);
    enterReview();
  }

  // Picker without a recording: freeze the current frame and comment on it.
  function freezeFrame() {
    Object.assign(state, {
      recs: [], groups: [], styleTracks: new Map(), textTracks: new Map(), comments: [],
      t0: now(), duration: 0, frozen: true,
    });
    seen = new Set();
    for (const a of document.getAnimations()) collect(a);
    for (const r of state.recs) {
      const a = r.anim;
      r.relStart = a.startTime != null ? a.startTime - state.t0 : -(a.currentTime || 0) / (a.playbackRate || 1);
    }
    enterReview();
    setCommentMode(true);
  }

  function enterReview() {
    for (const r of state.recs) {
      r.wasRunning = r.anim.playState === 'running' || pausedByUs.has(r.anim);
      r.rate = r.anim.playbackRate || 1;
      try { r.anim.persist(); } catch {}
    }
    buildGroups();
    state.phase = 'review';
    state.range = [0, state.duration];
    for (const r of state.recs) {
      try { r.anim.pause(); } catch {}
    }
    renderBar();
    setTime(state.duration);
  }

  // Animations on the same element + properties replace each other over time
  // (Idle -> Ring -> Timer). At time T only the latest one that has started is
  // applied; the rest are switched off so they don't leak their hold values.
  function buildGroups() {
    const ids = new Map();
    const map = new Map();
    for (const r of state.recs) {
      if (!ids.has(r.target)) ids.set(r.target, ids.size);
      const key = `${ids.get(r.target)}|${r.pseudo}|${[...r.props].sort().join(',')}`;
      if (!map.has(key)) map.set(key, []);
      map.get(key).push(r);
    }
    state.groups = [...map.values()].map((g) => g.sort((a, b) => a.relStart - b.relStart));
  }

  function applyTime(T) {
    for (const g of state.groups) {
      let active = g[0];
      for (const r of g) if (r.relStart <= T) active = r;
      for (const r of g) {
        const a = r.anim;
        try {
          if (a.playState !== 'paused') a.pause();
          if (r === active) {
            a.effect.updateTiming({ fill: 'both' });
            a.currentTime = Math.max(0, T - r.relStart) * r.rate;
          } else {
            a.effect.updateTiming({ fill: 'none' });
            a.currentTime = -1e7;
          }
        } catch {}
      }
    }
    for (const [el, track] of state.styleTracks) setStyle(el, valueAt(track, T));
    for (const [el, track] of state.textTracks) setText(el, valueAt(track, T));
  }

  function valueAt(track, T) {
    let v = track.base;
    for (const f of track.frames) {
      if (f.t <= T) v = f.v;
      else break;
    }
    return v;
  }

  const lastValue = (track) => (track.frames.length ? track.frames[track.frames.length - 1].v : track.base);

  function setStyle(el, v) {
    if (v == null) el.removeAttribute('style');
    else if (el.getAttribute('style') !== v) el.setAttribute('style', v);
  }

  function setText(el, v) {
    if (v != null && el.textContent !== v) el.textContent = v;
  }

  function setTime(T) {
    state.T = Math.min(Math.max(0, T), state.duration);
    applyTime(state.T);
    renderTimeline();
    renderPins();
  }

  function exitReview() {
    stopPlaying();
    for (const r of state.recs) {
      const a = r.anim;
      try {
        a.effect.updateTiming({ fill: r.timing.fill });
        if (r.wasRunning && !state.pausedAll) a.play();
        else if (!r.wasRunning && Number.isFinite(r.timing.iterations)) a.finish();
      } catch {}
    }
    for (const [el, track] of state.styleTracks) setStyle(el, lastValue(track));
    for (const [el, track] of state.textTracks) setText(el, lastValue(track));
    closeComment();
    ui.list.style.display = 'none';
    state.commentMode = false;
    state.phase = 'idle';
    state.frozen = false;
    render();
  }

  // ---------- playback ----------

  let raf = 0;
  function startPlaying() {
    if (state.phase !== 'review' || state.frozen) return;
    const [inT, outT] = state.range;
    if (state.T >= outT - 0.5 || state.T < inT) state.T = inT;
    state.playing = true;
    let last = performance.now();
    const loop = (ts) => {
      if (!state.playing) return;
      const next = state.T + (ts - last) * state.speed;
      last = ts;
      if (next >= state.range[1]) {
        setTime(state.range[1]);
        stopPlaying();
        return;
      }
      setTime(next);
      raf = requestAnimationFrame(loop);
    };
    raf = requestAnimationFrame(loop);
    renderBar();
  }

  function stopPlaying() {
    if (!state.playing) return;
    state.playing = false;
    cancelAnimationFrame(raf);
    renderBar();
  }

  // ---------- element info ----------

  function shortLabel(el) {
    const cls = [...el.classList].slice(0, 3).map((c) => '.' + c).join('');
    return el.tagName.toLowerCase() + (el.id ? '#' + el.id : '') + cls;
  }

  function selectorOf(el) {
    const parts = [];
    while (el && el.nodeType === 1 && el !== document.documentElement && parts.length < 6) {
      const testId = el.getAttribute('data-testid');
      if (testId) { parts.unshift(`[data-testid="${testId}"]`); break; }
      if (el.id) { parts.unshift('#' + CSS.escape(el.id)); break; }
      let s = el.tagName.toLowerCase() + [...el.classList].slice(0, 3).map((c) => '.' + CSS.escape(c)).join('');
      const parent = el.parentElement;
      if (parent) {
        const same = [...parent.children].filter((c) => c.tagName === el.tagName);
        if (same.length > 1) s += `:nth-of-type(${same.indexOf(el) + 1})`;
      }
      parts.unshift(s);
      if (el === document.body) break;
      el = parent;
    }
    return parts.join(' > ');
  }

  // Component names + source location from React/Vue dev builds, when present.
  function frameworkInfo(el) {
    const info = { components: [], source: null };
    for (let node = el; node && info.components.length === 0; node = node.parentElement) {
      const key = Object.keys(node).find((k) => k.startsWith('__reactFiber$') || k.startsWith('__reactInternalInstance$'));
      if (key) {
        for (let f = node[key]; f && info.components.length < 4; f = f.return) {
          const t = f.type;
          const n = t && typeof t !== 'string' && (t.displayName || t.name || t.render?.displayName || t.render?.name);
          if (n && !info.components.includes(n)) info.components.push(n);
          if (!info.source && f._debugSource) info.source = `${f._debugSource.fileName}:${f._debugSource.lineNumber}`;
        }
      }
      const vue = node.__vueParentComponent;
      if (vue) {
        for (let c = vue; c && info.components.length < 4; c = c.parent) {
          const n = c.type?.__name || c.type?.name;
          if (n) info.components.push(n);
          if (!info.source && c.type?.__file) info.source = c.type.__file;
        }
      }
    }
    return info;
  }

  const kebab = (p) => (p.startsWith('--') ? p : p.replace(/[A-Z]/g, (m) => '-' + m.toLowerCase()));
  const ms = (v) => (typeof v === 'number' ? `${+v.toFixed(1)}ms` : String(v));
  const sec = (v) => `${(v / 1000).toFixed(3)}s`;

  // Animations relevant to an element: its own, its descendants' and ancestors'.
  function animsFor(el) {
    const rank = (r) => (r.target === el ? 0 : el.contains(r.target) ? 1 : r.target.contains(el) ? 2 : -1);
    return state.recs
      .map((r) => ({ r, k: rank(r) }))
      .filter((x) => x.k >= 0)
      .sort((a, b) => a.k - b.k || a.r.relStart - b.r.relStart)
      .map((x) => ({ ...x.r, relation: ['this element', 'descendant', 'ancestor'][x.k] }));
  }

  function frameState(r, T) {
    // Effect-local time; differs from wall-clock when recorded in slow motion.
    const local = (T - r.relStart) * (r.rate / (r.timing.playbackRate || 1));
    let progress = null, iteration = null, phase;
    try {
      const ct = r.anim.effect.getComputedTiming();
      progress = ct.progress;
      iteration = ct.currentIteration;
    } catch {}
    const { delay, duration, iterations } = r.timing;
    const end = delay + (Number.isFinite(iterations) ? iterations * duration : Infinity);
    let linear = null;
    if (local < 0) phase = 'not started yet';
    else if (local < delay) phase = 'in delay';
    else if (local >= end) phase = 'finished';
    else {
      phase = 'running';
      if (duration > 0) linear = ((local - delay) % duration) / duration;
    }
    const values = {};
    try {
      const cs = getComputedStyle(r.target, r.pseudo);
      for (const p of r.props) values[kebab(p)] = cs.getPropertyValue(kebab(p)).trim();
    } catch {}
    return { local, phase, progress, linear, iteration, values, endAt: r.relStart + end / (r.rate || 1) };
  }

  // Keep what matters at this frame: the element's own animations, plus
  // ancestor/descendant animations running at T or within 300ms of it.
  function relevant(a, T) {
    if (a.relation === 'this element') return true;
    const f = a.frame;
    if (f.phase === 'running' || f.phase === 'in delay') return true;
    if (f.phase === 'finished') return T - f.endAt <= 300;
    return a.relStart - T <= 300;
  }

  // One CSS state change starts several transitions with identical timing
  // (width + height + 4 border radii). Report them as one entry.
  function mergeSiblings(anims) {
    const out = [];
    for (const a of anims) {
      const prev = out.find((b) =>
        b.kind === 'css-transition' && a.kind === 'css-transition' && b.target === a.target && b.pseudo === a.pseudo &&
        Math.abs(b.relStart - a.relStart) < 1 && JSON.stringify(b.timing) === JSON.stringify(a.timing));
      if (!prev) {
        out.push({ ...a, keyframes: a.keyframes.map((k) => ({ ...k })), frame: { ...a.frame, values: { ...a.frame.values } } });
        continue;
      }
      prev.name += ', ' + a.name;
      prev.props = [...prev.props, ...a.props];
      a.keyframes.forEach((k, i) => prev.keyframes[i] && Object.assign(prev.keyframes[i], k));
      Object.assign(prev.frame.values, a.frame.values);
    }
    return out;
  }

  function snapshot(el) {
    const T = state.T;
    const fw = frameworkInfo(el);
    const text = (el.innerText || '').trim().replace(/\s+/g, ' ').slice(0, 60);
    const anims = mergeSiblings(
      animsFor(el).map((r) => ({ ...r, frame: frameState(r, T) })).filter((a) => relevant(a, T))
    ).slice(0, 8);
    const scripted = [];
    for (const [tel, track] of state.styleTracks) {
      if (tel === el || el.contains(tel) || tel.contains(el)) {
        scripted.push({ label: shortLabel(tel), changes: track.frames.length, styleNow: tel.getAttribute('style') });
      }
    }
    return { label: shortLabel(el), selector: selectorOf(el), ...fw, text, anims, scripted: scripted.slice(0, 4) };
  }

  // ---------- comment mode ----------

  let hoverEl = null;
  let pendingEl = null;

  function setCommentMode(on) {
    state.commentMode = on && state.phase === 'review';
    if (!state.commentMode) {
      ui.hl.style.display = 'none';
      hoverEl = null;
    }
    renderBar();
  }

  function pickTarget(e) {
    const el = e.composedPath ? e.composedPath()[0] : e.target;
    if (!el || el.nodeType !== 1 || isOurs(el)) return null;
    return el;
  }

  function highlight(el) {
    if (!el) { ui.hl.style.display = 'none'; return; }
    const r = el.getBoundingClientRect();
    Object.assign(ui.hl.style, { display: 'block', left: r.left + 'px', top: r.top + 'px', width: r.width + 'px', height: r.height + 'px' });
    ui.hlLabel.textContent = shortLabel(el);
  }

  function onPointerMove(e) {
    if (!state.commentMode || ui.pop.style.display === 'block') return;
    const el = pickTarget(e);
    if (el !== hoverEl) { hoverEl = el; highlight(el); }
  }

  function onClick(e) {
    if (!state.commentMode) return;
    const el = pickTarget(e);
    if (!el) return;
    e.preventDefault();
    e.stopPropagation();
    e.stopImmediatePropagation();
    openComment(el);
  }

  function swallow(e) {
    if (state.commentMode && pickTarget(e)) { e.preventDefault(); e.stopPropagation(); }
  }

  function openComment(el) {
    pendingEl = el;
    highlight(el);
    ui.sel.textContent = shortLabel(el);
    ui.text.value = '';
    const r = el.getBoundingClientRect();
    const w = 320, h = 128;
    const left = Math.min(Math.max(8, r.left + r.width / 2 - w / 2), innerWidth - w - 8);
    let top = r.bottom + 10;
    if (top + h > innerHeight - 80) top = Math.max(8, r.top - h - 10);
    Object.assign(ui.pop.style, { display: 'block', left: left + 'px', top: top + 'px' });
    ui.text.focus();
  }

  function closeComment() {
    ui.pop.style.display = 'none';
    pendingEl = null;
    if (!state.commentMode) ui.hl.style.display = 'none';
  }

  function addComment() {
    const text = ui.text.value.trim();
    if (!text || !pendingEl) return;
    state.comments.push({ T: state.T, el: pendingEl, text, snap: snapshot(pendingEl) });
    state.comments.sort((a, b) => a.T - b.T);
    closeComment();
    setCommentMode(false);
    render();
  }

  // ---------- export ----------

  function fmtKeyframes(kfs) {
    return kfs.map((k) => {
      const off = k.computedOffset ?? k.offset;
      const decl = Object.entries(k)
        .filter(([p]) => !['offset', 'computedOffset', 'easing', 'composite'].includes(p))
        .map(([p, v]) => `${kebab(p)}: ${v}`)
        .join('; ');
      const ease = k.easing && k.easing !== 'linear' ? `  (easing to next: ${k.easing})` : '';
      return `      ${(off * 100).toFixed(0)}%: ${decl}${ease}`;
    }).join('\n');
  }

  function fmtAnim(a, i) {
    const t = a.timing;
    const name = a.name ? ` "${a.name}"` : '';
    const f = a.frame;
    const where = a.where || (a.relation === 'this element' ? 'this element' : `${a.relation} \`${shortLabel(a.target)}\``);
    const lines = [
      `  ${i + 1}. ${a.kind}${name} on ${where}${a.pseudo ? ' ' + a.pseudo : ''}`,
      `     timing: duration ${ms(t.duration)}, delay ${ms(t.delay)}, easing \`${t.easing}\`, iterations ${t.iterations}, direction ${t.direction}, fill ${t.fill}${t.playbackRate !== 1 ? `, playbackRate ${t.playbackRate}` : ''}`,
      `     started at ${sec(a.relStart)}; at this frame: local ${ms(f.local)}, ${f.phase}` +
        (f.linear != null ? `, time progress ${f.linear.toFixed(3)}` : '') +
        (f.phase === 'running' && f.progress != null ? `, eased progress ${f.progress.toFixed(3)}` : '') +
        (f.iteration != null && t.iterations !== 1 ? `, iteration ${f.iteration}` : ''),
      `     keyframes:\n${fmtKeyframes(a.keyframes)}`,
    ];
    const vals = Object.entries(f.values);
    if (vals.length) lines.push(`     computed now: ${vals.map(([p, v]) => `${p}: ${v}`).join('; ')}`);
    return lines.join('\n');
  }

  function buildExport() {
    const slow = state.recs.find((r) => r.rate !== r.timing.playbackRate);
    const out = [
      '# Animation feedback',
      '',
      `Page: ${location.href}`,
      state.frozen
        ? `Frozen frame, ${state.recs.length} animation(s) running at that moment.`
        : `Recording: ${sec(state.duration)}, ${state.recs.length} animation(s), ${state.styleTracks.size} element(s) with JS-driven inline styles. Times are from record start.`,
    ];
    if (slow) out.push('Recorded in slow motion: "started at" times are wall-clock; "local" times and timings are real (1x) values.');
    out.push('');
    if (!state.comments.length) {
      const [inT, outT] = state.range;
      const inRange = state.recs.filter((r) => r.relStart <= outT && frameState(r, inT).endAt >= inT);
      out.push(`No comments. Animations between ${sec(inT)} and ${sec(outT)}:`, '');
      inRange.forEach((r, i) => out.push(fmtAnim({ ...r, where: `\`${selectorOf(r.target)}\``, frame: frameState(r, state.T) }, i)));
      return out.join('\n');
    }
    state.comments.forEach((c, i) => {
      const s = c.snap;
      out.push(`## ${i + 1}. At ${sec(c.T)}: \`${s.label}\``, '');
      out.push(...c.text.split('\n').map((l) => '> ' + l), '');
      out.push(`- Selector: \`${s.selector}\``);
      if (s.components.length) out.push(`- Component: ${s.components.join(' < ')}${s.source ? ` (${s.source})` : ''}`);
      else if (s.source) out.push(`- Source: ${s.source}`);
      if (s.text) out.push(`- Text: "${s.text}"`);
      if (s.anims.length) {
        out.push('- Animations involved:');
        s.anims.forEach((a, j) => out.push(fmtAnim(a, j)));
      } else {
        out.push('- No CSS/WAAPI animation touches this element.');
      }
      for (const sc of s.scripted) {
        out.push(`- JS-driven inline style on \`${sc.label}\` (${sc.changes} changes during recording); at this frame: \`${sc.styleNow ?? ''}\``);
      }
      out.push('');
    });
    return out.join('\n');
  }

  async function copyExport() {
    if (state.phase !== 'review') return;
    const text = buildExport();
    api.lastExport = text;
    try {
      await Promise.race([
        navigator.clipboard.writeText(text),
        new Promise((_, reject) => setTimeout(() => reject(new Error('clipboard timeout')), 800)),
      ]);
    } catch {
      const ta = document.createElement('textarea');
      ta.value = text;
      ta.style.cssText = 'position:fixed;opacity:0;';
      shadow.appendChild(ta);
      ta.select();
      document.execCommand('copy');
      ta.remove();
    }
    toast(`Copied ${state.comments.length ? state.comments.length + ' comment(s)' : 'timings'} for your agent`);
  }

  // ---------- render ----------

  let toastTimer = 0;
  function toast(msg) {
    ui.toast.textContent = msg;
    ui.toast.style.display = 'block';
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => (ui.toast.style.display = 'none'), 1500);
  }

  const btn = (cls, icon, label, key) => `<button class="${cls}" data-tip="${label}" data-key="${key || ''}" aria-label="${label}">${icon}</button>`;

  function renderBar() {
    const { phase } = state;
    const bar = ui.bar;
    bar.className = 'bar ' + phase;
    if (phase === 'idle') {
      bar.innerHTML =
        btn('rec', ICON.record, 'Record', '⇧R') +
        btn(`pauseall${state.pausedAll ? ' lit' : ''}`, state.pausedAll ? ICON.resume : ICON.pause, state.pausedAll ? 'Resume animations' : 'Pause animations', '⇧P') +
        btn(`speed${state.speed !== 1 ? ' lit' : ''}`, ICON.speed + (state.speed !== 1 ? `<span class="speedtag">${state.speed}</span>` : ''), `${SPEEDS[(SPEEDS.indexOf(state.speed) + 1) % SPEEDS.length]}×`, '⇧S') +
        btn('pick', ICON.pick, 'Freeze & comment', '⇧F');
    } else if (phase === 'recording') {
      bar.innerHTML = btn('rec', ICON.stop, 'Stop', '⇧R');
    } else {
      bar.innerHTML =
        btn('back', ICON.back, 'Exit', 'Esc') +
        (state.frozen ? '' : btn('play', state.playing ? ICON.playing : ICON.play, state.playing ? 'Pause' : 'Play', 'Space')) +
        (state.frozen ? '<div class="frozen">Frozen frame</div>' : '<div class="track"></div>') +
        btn(`comment${state.commentMode ? ' on' : ''}`, ICON.comment, 'Comment', 'C') +
        btn('listbtn', ICON.list, `Comments (${state.comments.length})`, 'L') +
        btn('copy', ICON.copy, 'Copy', '⇧C');
      renderTimeline();
    }
  }

  function renderTime() {
    if (state.phase === 'recording') ui.bar.querySelector('.rec')?.setAttribute('data-tip', `Stop · ${sec(state.duration)}`);
  }

  function renderTimeline() {
    const tr = ui.bar.querySelector('.track');
    if (!tr) return;
    const D = state.duration || 1;
    const pct = (t) => `${Math.min(100, Math.max(0, (t / D) * 100))}%`;
    // One diamond per distinct start/end time; several animations at once -> orange.
    const times = [];
    for (const r of state.recs) {
      times.push(Math.max(0, r.relStart));
      const { delay, duration, iterations } = r.timing;
      const end = r.relStart + (delay + iterations * duration) / (r.rate || 1);
      if (Number.isFinite(end) && end > 0 && end < state.duration) times.push(end);
    }
    const starts = [];
    for (const t of times.sort((a, b) => a - b)) {
      const last = starts[starts.length - 1];
      if (last && t - last.t < 20) last.n++;
      else starts.push({ t, n: 1 });
    }
    const [inT, outT] = state.range;
    let html = `<div class="dia edge" style="left:${pct(0)};margin-left:2px"></div>`;
    for (const s of starts) html += `<div class="dia${s.n > 2 ? ' multi' : ''}" data-t="${s.t}" style="left:${pct(s.t)}"></div>`;
    for (const c of state.comments) html += `<div class="cdot" style="left:${pct(c.T)}"></div>`;
    html += `<div class="trim" style="left:${pct(inT)};right:calc(100% - ${pct(outT)})"></div>`;
    html += `<div class="handle" data-h="0" style="left:${pct(inT)}"></div><div class="handle" data-h="1" style="left:${pct(outT)}"></div>`;
    html += `<div class="head" style="left:${pct(state.T)}"></div><div class="ms">${Math.round(state.T)}ms</div>`;
    tr.innerHTML = html;
  }

  function renderPins() {
    ui.pins.innerHTML = '';
    if (state.phase !== 'review') return;
    state.comments.forEach((c) => {
      if (!c.el.isConnected) return;
      const r = c.el.getBoundingClientRect();
      const pin = document.createElement('div');
      pin.className = 'pin';
      pin.title = `${sec(c.T)} · ${c.text}`;
      pin.style.left = r.left + 'px';
      pin.style.top = r.top + 'px';
      pin.addEventListener('click', () => { stopPlaying(); setTime(c.T); });
      ui.pins.appendChild(pin);
    });
  }

  function renderList() {
    ui.list.innerHTML = '';
    if (!state.comments.length) {
      ui.list.innerHTML = '<div class="empty">No comments yet. Turn on comment mode and click an element.</div>';
      return;
    }
    state.comments.forEach((c, i) => {
      const item = document.createElement('div');
      item.className = 'item';
      item.innerHTML = `<span class="t"></span><div class="body"><div class="txt"></div><div class="s"></div></div><button aria-label="Delete">✕</button>`;
      item.querySelector('.t').textContent = `${Math.round(c.T)}ms`;
      item.querySelector('.txt').textContent = c.text;
      item.querySelector('.s').textContent = c.snap.label;
      item.addEventListener('click', () => { stopPlaying(); setTime(c.T); });
      item.querySelector('button').addEventListener('click', (e) => {
        e.stopPropagation();
        state.comments.splice(i, 1);
        renderList();
        render();
      });
      ui.list.appendChild(item);
    });
  }

  function render() {
    renderBar();
    renderPins();
    if (ui.list.style.display === 'block') renderList();
  }

  // ---------- events ----------

  function toggleRecord() {
    state.phase === 'recording' ? stopRecording() : startRecording();
  }

  function toggleList() {
    const open = ui.list.style.display !== 'block';
    if (open) renderList();
    ui.list.style.display = open ? 'block' : 'none';
  }

  const actions = {
    rec: toggleRecord,
    pauseall: () => setPausedAll(!state.pausedAll),
    speed: () => setSpeed(SPEEDS[(SPEEDS.indexOf(state.speed) + 1) % SPEEDS.length]),
    pick: freezeFrame,
    back: exitReview,
    play: () => (state.playing ? stopPlaying() : startPlaying()),
    comment: () => setCommentMode(!state.commentMode),
    listbtn: toggleList,
    copy: copyExport,
  };

  ui.bar.addEventListener('click', (e) => {
    const b = e.target.closest('button');
    if (!b) return;
    hideTip();
    actions[b.classList[0]]?.();
  });

  ui.bar.addEventListener('pointerover', (e) => {
    const b = e.target.closest('button');
    if (!b) return hideTip();
    const r = b.getBoundingClientRect();
    ui.tip.innerHTML = '';
    ui.tip.append(b.dataset.tip);
    if (b.dataset.key) {
      const k = document.createElement('kbd');
      k.textContent = b.dataset.key;
      ui.tip.append(k);
    }
    ui.tip.style.display = 'block';
    const tw = ui.tip.offsetWidth;
    ui.tip.style.left = Math.min(Math.max(8, r.left + r.width / 2 - tw / 2), innerWidth - tw - 8) + 'px';
    ui.tip.style.top = r.top - 32 + 'px';
  });
  ui.bar.addEventListener('pointerleave', hideTip);
  function hideTip() { ui.tip.style.display = 'none'; }

  ui.add.addEventListener('click', addComment);
  ui.cancel.addEventListener('click', closeComment);
  ui.text.addEventListener('keydown', (e) => {
    e.stopPropagation();
    if (e.key === 'Enter' && (e.metaKey || e.ctrlKey || !e.shiftKey)) { e.preventDefault(); addComment(); }
    if (e.key === 'Escape') closeComment();
  });

  // Timeline: drag to scrub, drag the bracket handles to trim, click a diamond to jump.
  let drag = null;
  ui.bar.addEventListener('pointerdown', (e) => {
    const tr = e.target.closest('.track');
    if (!tr || state.phase !== 'review') return;
    stopPlaying();
    const h = e.target.closest('.handle');
    const dia = e.target.closest('.dia[data-t]');
    drag = h ? { handle: +h.dataset.h } : { handle: null };
    tr.setPointerCapture(e.pointerId);
    if (dia) setTime(+dia.dataset.t);
    else seek(e, tr);
  });
  ui.bar.addEventListener('pointermove', (e) => {
    const tr = ui.bar.querySelector('.track');
    if (drag && tr) seek(e, tr);
  });
  const endDrag = () => (drag = null);
  ui.bar.addEventListener('pointerup', endDrag);
  ui.bar.addEventListener('pointercancel', endDrag);

  function seek(e, tr) {
    const r = tr.getBoundingClientRect();
    const t = Math.min(Math.max(0, ((e.clientX - r.left) / r.width) * state.duration), state.duration);
    if (drag?.handle === 0) state.range[0] = Math.min(t, state.range[1] - FRAME);
    else if (drag?.handle === 1) state.range[1] = Math.max(t, state.range[0] + FRAME);
    setTime(drag?.handle != null ? state.range[drag.handle] : t);
  }

  const isEditable = (el) => el && (el.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(el.tagName));
  function onKey(e) {
    if (!host.isConnected || isEditable(document.activeElement) || e.metaKey || e.ctrlKey || e.altKey) return;
    const k = e.key.toLowerCase();
    const review = state.phase === 'review';
    if (e.shiftKey && k === 'r') toggleRecord();
    else if (e.shiftKey && k === 's' && state.phase !== 'review') actions.speed();
    else if (e.shiftKey && k === 'p' && state.phase === 'idle') actions.pauseall();
    else if (e.shiftKey && k === 'f' && state.phase === 'idle') freezeFrame();
    else if (e.shiftKey && k === 'c' && review) copyExport();
    else if (!review) return;
    else if (k === ' ') actions.play();
    else if (k === 'arrowleft' || k === 'arrowright') {
      stopPlaying();
      setTime(state.T + (k === 'arrowright' ? 1 : -1) * FRAME * (e.shiftKey ? 10 : 1));
    } else if (k === 'c' && !e.shiftKey) actions.comment();
    else if (k === 'l') toggleList();
    else if (k === 'escape') {
      if (ui.pop.style.display === 'block') closeComment();
      else if (state.commentMode) setCommentMode(false);
      else exitReview();
    } else return;
    e.preventDefault();
    e.stopPropagation();
  }

  function listen(on) {
    const m = on ? 'addEventListener' : 'removeEventListener';
    document[m]('pointermove', onPointerMove, true);
    document[m]('click', onClick, true);
    document[m]('pointerdown', swallow, true);
    document[m]('mousedown', swallow, true);
    document[m]('keydown', onKey, true);
    document[m]('transitionrun', onCssEvent, true);
    document[m]('animationstart', onCssEvent, true);
    window[m]('scroll', renderPins, true);
    window[m]('resize', renderPins);
    if (on) pollTimer = setInterval(poll, 30);
    else clearInterval(pollTimer);
  }

  const api = {
    toggle() {
      if (host.isConnected) {
        if (state.phase === 'recording') stopRecording();
        if (state.phase === 'review') exitReview();
        if (state.pausedAll) setPausedAll(false);
        if (state.speed !== 1) setSpeed(1);
        listen(false);
        host.remove();
      } else {
        document.documentElement.appendChild(host);
        listen(true);
        render();
      }
    },
    get state() { return state; },
    seek: (t) => state.phase === 'review' && setTime(t),
    export: buildExport,
    lastExport: '',
  };
  window.__scrubnote = api;
  api.toggle();
})();
