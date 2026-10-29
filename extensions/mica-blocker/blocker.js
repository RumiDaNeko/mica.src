// Shared by the worker and control.html. The engine applies dynamic rules but not an extension's static
// rulesets, so the chosen lists are loaded as dynamic rules (Chromium allows 30,000 of them), with one
// rule that lets excluded sites load everything.
const LIMIT = 29900;
async function bundled(path) { return (await fetch(chrome.runtime.getURL(path))).json(); }
async function rulesFor(config) {
  const out = []; const skipped = []; let id = 2;
  for (const list of config.lists) {
    let rules; try { rules = await bundled('rules/' + list + '.json'); } catch (e) { continue; }
    // A list of ad hosts is about what those hosts serve on other sites: visiting one of them yourself
    // (AdMob's console on apps.admob.com) loads it with its own scripts, so each such rule gets a twin that
    // allows the host's requests on its own site.
    const twins = rules.filter(r => r.action.type === 'block' && Object.keys(r.condition).join() === 'requestDomains');
    if (out.length + rules.length + twins.length > LIMIT) { skipped.push(list); continue; }
    for (const rule of rules) { rule.id = id++; out.push(rule); }
    for (const rule of twins) out.push({ id: id++, priority: rule.priority, action: { type: 'allow' }, condition: { requestDomains: rule.condition.requestDomains, domainType: 'firstParty' } });
  }
  if (config.excluded.length) out.push({ id: 1, priority: 2000000000, action: { type: 'allowAllRequests' }, condition: { requestDomains: config.excluded, resourceTypes: ['main_frame', 'sub_frame'] } });
  return { rules: out, skipped };
}
// Mica calls this from control.html with { lists: [...], excluded: [...] }.
async function micaApply(config) {
  const { rules, skipped } = await rulesFor(config);
  const existing = await chrome.declarativeNetRequest.getDynamicRules();
  await chrome.declarativeNetRequest.updateDynamicRules({ removeRuleIds: existing.map(r => r.id), addRules: rules });
  await chrome.storage.local.set({ config, version: chrome.runtime.getManifest().version });
  return JSON.stringify({ rules: rules.length, skipped });
}
// On start: the first time, or when a newer bundled copy arrived, load the saved (or default) choice again.
async function restore() {
  const { config, version } = await chrome.storage.local.get(['config', 'version']);
  if (config && version === chrome.runtime.getManifest().version) return;
  await micaApply(config || await bundled('rules/default.json'));
}
