// Display settings (high contrast, larger text) applied before the first paint. A classic script loaded in <head>;
// js/chrome.js changes them later. Storage may be blocked (private windows): then the defaults stay.
(function () {
  try {
    var d = JSON.parse(localStorage.getItem('display') || 'null') || {};
    if (d.contrast) document.documentElement.setAttribute('data-contrast', 'high');
    if (d.large) document.documentElement.setAttribute('data-text', 'large');
  } catch (e) { /* defaults */ }
})();
