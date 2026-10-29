// Mica's site card asks how much was blocked on this page with a 'mica-blocked-ask' event on the document;
// the answer lands in the data-mica-blocked attribute, then a 'mica-blocked-told' event. Nothing runs until asked.
document.addEventListener('mica-blocked-ask', () => {
  chrome.runtime.sendMessage({ what: 'blockedHere', since: Math.floor(performance.timeOrigin) }).then(n => {
    document.documentElement.setAttribute('data-mica-blocked', String(n));
    document.dispatchEvent(new Event('mica-blocked-told'));
  }, () => {});
});
