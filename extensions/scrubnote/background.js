// Clicking the icon (or Alt+Shift+S) toggles scrubnote on the current tab.
// It runs in the page's main world so it can patch Element.animate and read
// React/Vue dev info; extension-injected files aren't blocked by page CSP.

const ACCENT = '#ec8686';

async function setBadge(tabId, text, title) {
  await chrome.action.setBadgeBackgroundColor({ tabId, color: ACCENT });
  await chrome.action.setBadgeText({ tabId, text });
  await chrome.action.setTitle({ tabId, title });
}

chrome.action.onClicked.addListener(async (tab) => {
  const target = { tabId: tab.id };
  try {
    await chrome.scripting.executeScript({ target, world: 'MAIN', files: ['scrubnote.js'] });
    const [{ result: on }] = await chrome.scripting.executeScript({
      target,
      world: 'MAIN',
      func: () => !!document.getElementById('scrubnote-host')?.isConnected,
    });
    await setBadge(tab.id, on ? 'ON' : '', on ? 'scrubnote is on (click to close)' : 'scrubnote: toggle on this tab (Alt+Shift+S)');
  } catch {
    // chrome://, the Web Store and similar pages don't allow scripts.
    await setBadge(tab.id, '!', 'scrubnote cannot run on this page');
  }
});

// A reload or navigation removes the toolbar, so clear the badge with it.
chrome.tabs.onUpdated.addListener((tabId, info) => {
  if (info.status === 'loading') {
    chrome.action.setBadgeText({ tabId, text: '' });
    chrome.action.setTitle({ tabId, title: 'scrubnote: toggle on this tab (Alt+Shift+S)' });
  }
});
