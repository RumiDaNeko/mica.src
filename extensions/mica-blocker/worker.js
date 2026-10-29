importScripts('blocker.js');
chrome.runtime.onInstalled.addListener(restore);
chrome.runtime.onStartup.addListener(restore);
// The site card's count: requests this tab's page had blocked or redirected since it opened. Allow rules
// (excluded sites, list exceptions) don't count. Rule actions are looked up once per rule change.
let blocking = null;
chrome.storage.onChanged.addListener(() => { blocking = null; });
async function blockedIn(tabId, since) {
  if (!blocking) blocking = new Set((await chrome.declarativeNetRequest.getDynamicRules()).filter(r => r.action.type === 'block' || r.action.type === 'redirect').map(r => r.id));
  const { rulesMatchedInfo } = await chrome.declarativeNetRequest.getMatchedRules({ tabId, minTimeStamp: since || 0 });
  return rulesMatchedInfo.filter(m => blocking.has(m.rule.ruleId)).length;
}
chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if (!message || message.what !== 'blockedHere' || !sender.tab) return;
  blockedIn(sender.tab.id, message.since).then(reply, () => reply(-1));
  return true;
});
