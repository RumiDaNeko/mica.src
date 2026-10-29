using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

// The AI extensions Mica hosts in its AI panel, and the side-panel shim it adds to them. No WPF or engine code here:
// the Windows build and the cross-platform shell (cross\) both compile this file.
namespace Mica {
 // An AI's own browser extension, which the AI panel shows in place of its chat site when it's installed, so the
 // AI can read and act on the page. It belongs to the panel, so the extensions page and toolbar leave it out.
 sealed class AiExtension {
  public string Provider,Id,Name,Panel;public string[] Pages;
  // Claude's panel works for one tab (sidepanel.html?tabId=N) and is pointed at the tab in front.
  public bool FollowsTab;
  // ChatGPT's worker picks the panel's page with sidePanel.setOptions, which Chrome applies to its side panel.
  public bool FollowsPanelPath;
  public string PanelUrl {get {return "chrome-extension://"+Id+"/"+Panel;}}
  public string Store {get {return "https://chromewebstore.google.com/detail/"+Id;}}
  public bool IsPanel(string url) {return Pages.Any(p=>(url??"").StartsWith("chrome-extension://"+Id+"/"+p,StringComparison.OrdinalIgnoreCase));}
 }

 partial class BrowserApp {
  internal const string ClaudeExtensionId="fcoeoabgfenejglbffodgkkbkcdhcgfn",ChatGptExtensionId="hehggadaopoacecdllhhajmbjkdcmajg";
  internal static readonly AiExtension[] AiExtensions={
   new AiExtension {Provider="claude",Id=ClaudeExtensionId,Name="Claude for Chrome",Panel="sidepanel.html",Pages=new[]{"sidepanel.html"},FollowsTab=true},
   new AiExtension {Provider="chatgpt",Id=ChatGptExtensionId,Name="ChatGPT for Chrome",Panel="side-panel-loading.html",Pages=new[]{"side-panel-loading.html","waiting-room.html","codex-work-sidepanel.html"},FollowsPanelPath=true}};
  internal static AiExtension AiExtensionFor(string provider) {return AiExtensions.FirstOrDefault(e=>e.Provider==provider);}
  internal static AiExtension AiExtensionById(string id) {return AiExtensions.FirstOrDefault(e=>e.Id==id);}
 }

 // The engine reports every view as a tab, so the extension's own panel page, hosted in Mica's AI panel, reaches
 // its worker looking like a tab, and the worker answers side-panel-only requests (claude.ai's panel page asking
 // for the account) with nothing. A small script loaded before the worker shows it what Chrome would: senders
 // inside that panel page carry no tab, and the page counts as a side panel. Every other sender is left alone.
 static class ClaudeSidePanelPatch {
  const string FileName="mica-sidepanel.js";
  // The script for one extension: which of its pages are the side panel, then the shims.
  static string Script(string[] pages) {return "// Added by Mica: its AI panel hosts this extension's side panel page.\nself.__micaPanelPages = "+new JavaScriptSerializer().Serialize(pages)+";\n"+SenderShim+TabsShim;}
  const string SenderShim=@"
(() => {
  const pages = (self.__micaPanelPages || ['sidepanel.html']).map(p => chrome.runtime.getURL(p)), isPanelUrl = u => typeof u === 'string' && pages.some(p => u.startsWith(p));
  const inPanel = s => !!(s && s.tab && isPanelUrl(s.tab.url));
  const asPanel = s => { if (!inPanel(s)) return s; const c = Object.assign({}, s); delete c.tab; return c; };
  const wrap = (ev, adapt) => {
    if (!ev || typeof ev.addListener !== 'function') return;
    const add = ev.addListener.bind(ev), remove = ev.removeListener.bind(ev), has = typeof ev.hasListener === 'function' ? ev.hasListener.bind(ev) : null, map = new Map();
    ev.addListener = (fn, ...rest) => { const w = adapt(fn); map.set(fn, w); return add(w, ...rest); };
    ev.removeListener = fn => remove(map.get(fn) || fn);
    if (has) ev.hasListener = fn => has(map.get(fn) || fn);
  };
  const forMessages = fn => (message, sender, reply) => fn(message, asPanel(sender), reply);
  const forPorts = fn => port => { if (port && inPanel(port.sender)) { try { Object.defineProperty(port, 'sender', { value: asPanel(port.sender), configurable: true }); } catch (e) {} } return fn(port); };
  wrap(chrome.runtime.onMessage, forMessages); wrap(chrome.runtime.onMessageExternal, forMessages);
  wrap(chrome.runtime.onConnect, forPorts); wrap(chrome.runtime.onConnectExternal, forPorts);
  if (typeof chrome.runtime.getContexts === 'function') {
    const get = chrome.runtime.getContexts.bind(chrome.runtime);
    chrome.runtime.getContexts = async filter => {
      const f = Object.assign({}, filter || {}), types = f.contextTypes, widen = !!(types && types.includes('SIDE_PANEL') && !types.includes('TAB'));
      if (widen) f.contextTypes = types.concat('TAB');
      const found = (await get(f)).map(c => c.contextType === 'TAB' && isPanelUrl(c.documentUrl || '')
        ? Object.assign({}, c, { contextType: 'SIDE_PANEL', tabId: Number(new URL(c.documentUrl).searchParams.get('tabId')) || -1 }) : c);
      return widen ? found.filter(c => c.contextType !== 'TAB') : found;
    };
  }
})();
";
  // The engine shows every Mica tab as a one-tab popup window and has no tab strip, so tab groups can't be made
  // there; Claude's tools put the tabs they work in into a group first. This keeps groups in the extension's own
  // session storage, reports windows as normal ones, keeps the side panel page out of the tab list, and follows a
  // tab the extension opens to the Mica tab it is reopened in. Used by the worker and the panel page alike.
  internal const string TabsShim=@"(() => {
  if (!self.chrome || !chrome.tabs || !chrome.windows || self.__micaTabs) return; self.__micaTabs = true;
  const NONE = -1, KEY = 'micaTabGroups', BLANK = 'https://www.google.com/';
  const pages = (self.__micaPanelPages || ['sidepanel.html']).map(p => chrome.runtime.getURL(p));
  const T = chrome.tabs, W = chrome.windows, G = chrome.tabGroups || (chrome.tabGroups = {}), area = chrome.storage && chrome.storage.session;
  const o = { get: T.get.bind(T), query: T.query.bind(T), create: T.create.bind(T), update: T.update.bind(T), wGet: W.get.bind(W), wAll: W.getAll.bind(W), wLast: W.getLastFocused.bind(W) };
  if (G.TAB_GROUP_ID_NONE === undefined) G.TAB_GROUP_ID_NONE = NONE;
  if (!G.Color) G.Color = { GREY: 'grey', BLUE: 'blue', RED: 'red', YELLOW: 'yellow', GREEN: 'green', PINK: 'pink', PURPLE: 'purple', CYAN: 'cyan', ORANGE: 'orange' };
  let state = { next: 1001, groups: {}, members: {} }, ready = null;
  const load = () => ready || (ready = (async () => { try { const v = area && (await area.get(KEY))[KEY]; if (v && v.groups) state = v; } catch (e) {} return state; })());
  if (chrome.storage && chrome.storage.onChanged) chrome.storage.onChanged.addListener((c, a) => { if (a === 'session' && c[KEY] && c[KEY].newValue) state = c[KEY].newValue; });
  const save = async () => { try { if (area) await area.set({ [KEY]: state }); } catch (e) {} };
  const isPanel = t => !!(t && typeof t.url === 'string' && pages.some(p => t.url.startsWith(p)));
  const fix = t => { if (t && typeof t.id === 'number') { const g = state.members[t.id]; t.groupId = g === undefined ? NONE : g; } return t; };
  const fixWin = w => { if (w) { w.type = 'normal'; if (w.tabs) w.tabs = w.tabs.filter(t => !isPanel(t)).map(fix); } return w; };
  // Callers using callbacks read failures from chrome.runtime.lastError, as Chrome sets it.
  const failWith = (e, cb) => { const had = Object.getOwnPropertyDescriptor(chrome.runtime, 'lastError'); try { Object.defineProperty(chrome.runtime, 'lastError', { value: { message: String(e && e.message || e) }, configurable: true }); cb(undefined); } finally { if (had) Object.defineProperty(chrome.runtime, 'lastError', had); else delete chrome.runtime.lastError; } };
  const api = fn => function (...args) { const cb = typeof args[args.length - 1] === 'function' ? args.pop() : null; const p = Promise.resolve().then(() => fn(...args)); if (!cb) return p; p.then(v => cb(v), e => failWith(e, cb)); };
  const event = () => { const set = new Set(); return { set, addListener: f => set.add(f), removeListener: f => set.delete(f), hasListener: f => set.has(f), hasListeners: () => set.size > 0 }; };
  const fire = (ev, ...args) => { for (const f of [...ev.set]) { try { f(...args); } catch (e) { console.error(e); } } };
  const groupEvents = { onCreated: event(), onUpdated: event(), onRemoved: event(), onMoved: event() };
  Object.assign(G, groupEvents);
  const tabUpdated = event(), realAdd = T.onUpdated.addListener.bind(T.onUpdated), realRemove = T.onUpdated.removeListener.bind(T.onUpdated), wrapped = new Map();
  T.onUpdated.addListener = f => { const w = (id, info, tab) => f(id, info, fix(tab)); wrapped.set(f, w); tabUpdated.set.add(f); realAdd(w); };
  T.onUpdated.removeListener = f => { tabUpdated.set.delete(f); realRemove(wrapped.get(f) || f); wrapped.delete(f); };
  // The side panel page isn't a tab; when it has focus, the tab it is bound to (or the last page window that had
  // focus) stands for the focused window.
  let lastPage = null;
  if (W.onFocusChanged) W.onFocusChanged.addListener(async id => { if (id < 0) return; try { const w = await o.wGet(id, { populate: true }); if (w.tabs && w.tabs.length && !w.tabs.every(isPanel)) lastPage = id; } catch (e) {} });
  const panelTabId = async () => { for (const t of await o.query({})) if (isPanel(t)) { const id = Number(new URL(t.url).searchParams.get('tabId')); if (id) return id; } return null; };
  const lastFocused = async populate => {
    let w = await o.wLast({ populate: true });
    if (w && w.tabs && w.tabs.length && w.tabs.every(isPanel)) { const id = await panelTabId(); try { if (id) w = await o.wGet((await o.get(id)).windowId, { populate: true }); else if (lastPage !== null) w = await o.wGet(lastPage, { populate: true }); } catch (e) {} }
    w = fixWin(w); if (!populate && w) delete w.tabs; return w;
  };
  const drop = async ids => {
    const gone = new Set();
    for (const id of ids) { const g = state.members[id]; if (g !== undefined) { delete state.members[id]; gone.add(g); } }
    for (const g of gone) if (!Object.values(state.members).includes(g) && state.groups[g]) { const group = state.groups[g]; delete state.groups[g]; fire(groupEvents.onRemoved, group); }
  };
  T.get = api(async id => fix(await o.get(id)));
  T.query = api(async (info = {}) => {
    await load(); const q = Object.assign({}, info), group = q.groupId; delete q.groupId; delete q.windowType;
    if (q.currentWindow || q.lastFocusedWindow) { const w = await lastFocused(false); delete q.currentWindow; delete q.lastFocusedWindow; if (w) q.windowId = w.id; }
    return (await o.query(q)).filter(t => !isPanel(t)).map(fix).filter(t => group === undefined || t.groupId === group);
  });
  // Bringing a tab forward happens in Mica's tab strip, which the engine knows nothing of: ask Mica for it by the
  // page's DevTools target, then give the page a moment to draw (a page out of sight can't be captured or clicked).
  const front = async tabId => {
    try {
      const target = (await chrome.debugger.getTargets()).find(x => x.tabId === tabId); if (!target) return;
      let asked = { id: -1 }; try { asked = (await o.create({ url: 'https://open.mica.invalid/?from=claude&focus=' + encodeURIComponent(target.id), active: false })) || asked; } catch (e) {}
      for (let i = 0; i < 16; i++) { await new Promise(r => setTimeout(r, 150)); try { await o.get(asked.id); } catch (e) { break; } }
      await new Promise(r => setTimeout(r, 350));
    } catch (e) {}
  };
  if (chrome.debugger && chrome.debugger.sendCommand) {
    const D = chrome.debugger, send = D.sendCommand.bind(D), sight = /^(Page\.captureScreenshot|Input\.)/;
    const seen = async tabId => { try { const r = await send({ tabId }, 'Runtime.evaluate', { expression: 'document.visibilityState', returnByValue: true }); if (r && r.result && r.result.value === 'hidden') await front(tabId); } catch (e) {} };
    D.sendCommand = function (target, method, params, cb) {
      if (typeof params === 'function') { cb = params; params = undefined; }
      const go = () => cb ? send(target, method, params, cb) : send(target, method, params);
      if (!(target && typeof target.tabId === 'number' && sight.test(method || ''))) return go();
      const p = seen(target.tabId).then(go); return cb ? undefined : p;
    };
  }
  T.update = api(async (...a) => {
    const id = typeof a[0] === 'number' ? a[0] : null, props = typeof a[0] === 'number' ? a[1] : a[0];
    const t = fix(await o.update(...a)); if (props && (props.active || props.highlighted)) await front(id === null ? t.id : id); return t;
  });
  T.group = api(async ({ tabIds, groupId, createProperties } = {}) => {
    await load(); const ids = [].concat(tabIds); if (!ids.length) throw new Error('No tabs to group.');
    const tabs = []; for (const id of ids) tabs.push(await o.get(id));
    let g = groupId, created = false;
    if (g === undefined) { g = state.next++; created = true; state.groups[g] = { id: g, title: '', color: G.Color.GREY, collapsed: false, windowId: (createProperties && createProperties.windowId) || tabs[0].windowId }; }
    else if (!state.groups[g]) throw new Error('No group with id: ' + g + '.');
    const before = ids.map(id => state.members[id]);
    for (const id of ids) state.members[id] = g;
    for (const old of new Set(before)) if (old !== undefined && old !== g && !Object.values(state.members).includes(old) && state.groups[old]) { const group = state.groups[old]; delete state.groups[old]; fire(groupEvents.onRemoved, group); }
    await save();
    if (created) fire(groupEvents.onCreated, Object.assign({}, state.groups[g]));
    tabs.forEach(t => fire(tabUpdated, t.id, { groupId: g }, fix(t)));
    return g;
  });
  T.ungroup = api(async tabIds => {
    await load(); const ids = [].concat(tabIds), had = ids.filter(id => state.members[id] !== undefined);
    await drop(ids); await save();
    for (const id of had) { try { fire(tabUpdated, id, { groupId: NONE }, fix(await o.get(id))); } catch (e) {} }
  });
  G.get = api(async id => { await load(); const g = state.groups[id]; if (!g) throw new Error('No group with id: ' + id + '.'); return Object.assign({}, g); });
  G.query = api(async (info = {}) => { await load(); return Object.values(state.groups).filter(g => Object.keys(info).every(k => g[k] === info[k])).map(g => Object.assign({}, g)); });
  G.update = api(async (id, props = {}) => {
    await load(); const g = state.groups[id]; if (!g) throw new Error('No group with id: ' + id + '.');
    for (const k of ['title', 'color', 'collapsed']) if (props[k] !== undefined) g[k] = props[k];
    await save(); fire(groupEvents.onUpdated, Object.assign({}, g)); return Object.assign({}, g);
  });
  G.move = api(async id => { await load(); const g = state.groups[id]; if (!g) throw new Error('No group with id: ' + id + '.'); return Object.assign({}, g); });
  T.onRemoved.addListener(async id => { await load(); if (state.members[id] !== undefined) { await drop([id]); await save(); } });
  // Mica closes a page the engine opens on its own and reopens it as a Mica tab; answer with that tab.
  const open = async props => {
    const p = Object.assign({}, props); if (!p.url || /^(chrome|edge):\/\/newtab\/?$/i.test(p.url) || p.url === 'about:blank') p.url = BLANK;
    const ask = 'https://open.mica.invalid/?active=' + (p.active === false ? 0 : 1) + '&from=claude#' + encodeURIComponent(p.url);
    const known = new Set((await o.query({})).map(t => t.id));
    let first = { id: -1 }; try { first = (await o.create({ url: ask, active: p.active !== false })) || first; } catch (e) {}
    const same = t => { const u = t.pendingUrl || t.url || ''; try { const a = new URL(u), b = new URL(p.url); return a.host === b.host; } catch (e) { return u === p.url; } };
    for (let i = 0; i < 48; i++) {
      await new Promise(r => setTimeout(r, 250));
      let alive = true; try { await o.get(first.id); } catch (e) { alive = false; }
      if (alive && i < 47) continue; if (alive) return first;
      const fresh = (await o.query({})).filter(t => !known.has(t.id) && t.id !== first.id && !isPanel(t));
      const hit = fresh.find(same) || (fresh.length === 1 ? fresh[0] : null); if (hit) return hit;
    }
    return first;
  };
  T.create = api(async (props = {}) => fix(await open(props)));
  W.create = api(async (props = {}) => {
    const url = Array.isArray(props.url) ? props.url[0] : props.url; let tab;
    if (typeof props.tabId === 'number') tab = await o.get(props.tabId); else tab = await open({ url, active: true });
    const w = fixWin(await o.wGet(tab.windowId, { populate: true })); w.tabs = [fix(tab)]; return w;
  });
  const anyType = info => { const q = Object.assign({}, info); delete q.windowTypes; return q; };
  W.get = api(async (id, info) => fixWin(await o.wGet(id, anyType(info))));
  W.getAll = api(async info => (await o.wAll(anyType(info))).map(fixWin).filter(w => !w.tabs || w.tabs.length));
  const wUpdate = W.update.bind(W);
  W.update = api(async (id, props = {}) => {
    const w = await wUpdate(id, props);
    if (props.focused || props.drawAttention) { try { const tabs = (await o.query({ windowId: id })).filter(t => !isPanel(t)); if (tabs.length) await front(tabs[0].id); } catch (e) {} }
    return fixWin(w);
  });
  W.getLastFocused = api(async info => lastFocused(!!(info && info.populate)));
  W.getCurrent = api(async info => lastFocused(!!(info && info.populate)));
  load().then(async () => { try { const live = new Set((await o.query({})).map(t => t.id)), stale = Object.keys(state.members).map(Number).filter(id => !live.has(id)); if (stale.length) { await drop(stale); await save(); } } catch (e) {} });
})();
";
  // True when something changed. The shim goes first in the worker: an import in a module worker, importScripts
  // in a classic one.
  internal static bool Apply(string folder,string[] pages) {
   var manifest=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(folder,"manifest.json"))) as System.Collections.Generic.Dictionary<string,object>;object value;
   var background=manifest!=null&&manifest.TryGetValue("background",out value)?value as System.Collections.Generic.Dictionary<string,object>:null;
   if(background==null||!background.TryGetValue("service_worker",out value)||!(value is string))return false;
   object type;bool module=background.TryGetValue("type",out type)&&(type as string)=="module";
   string root=Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,worker=Path.GetFullPath(Path.Combine(root,(string)value));
   if(!worker.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!File.Exists(worker))return false;
   bool changed=false;var utf8=new UTF8Encoding(false);string shim=Path.Combine(root,FileName);
   string script=Script(pages);if(!File.Exists(shim)||File.ReadAllText(shim,Encoding.UTF8)!=script){File.WriteAllText(shim,script,utf8);changed=true;}
   string import=module?"import '/"+FileName+"';":"importScripts('/"+FileName+"');",text=File.ReadAllText(worker,Encoding.UTF8);
   if(!text.StartsWith(import,StringComparison.Ordinal)){File.WriteAllText(worker,import+"\n"+text,utf8);changed=true;}
   return changed;
  }
 }
}
