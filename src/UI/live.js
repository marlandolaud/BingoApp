// Live-view helpers shared by the dealer page and the watch page.
// Works in the browser (window.BingoLive) and in Node (module.exports) for tests.
(function (root) {
  const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  const OVERDUE_GRACE_MS = 5000;
  const EVENT_SOURCE_OPEN = 1;

  function guidParam(search, name) {
    const value = new URLSearchParams(search).get(name);
    return value !== null && GUID.test(value) ? value : null;
  }

  const gameKeyFromUrl = (search) => guidParam(search, 'game');
  const joinCodeFromUrl = (search) => guidParam(search, 'join');
  const viewerIdFromUrl = (search) => guidParam(search, 'v');

  function withParam(href, name, value) {
    const url = new URL(href);
    url.searchParams.set(name, value);
    return url.toString();
  }

  function joinUrl(location, joinCode) {
    const dir = location.pathname.replace(/[^/]*$/, '');
    return location.origin + dir + 'watch.html?join=' + joinCode;
  }

  function isLocalOnly(hostname) {
    return hostname === 'localhost' || hostname === '::1' || hostname === '[::1]' || /^127\./.test(hostname);
  }

  function clockOffset(serverTimeIso, nowMs) {
    return Date.parse(serverTimeIso) - nowMs;
  }

  function countdownText(snapshot, nowMs, offsetMs) {
    const pilot = snapshot.autoPilot;
    if (!pilot || !pilot.on) return '';
    const remaining = Date.parse(pilot.nextDrawAt) - (nowMs + offsetMs);
    if (remaining > 0) return 'Next ball in ' + Math.ceil(remaining / 1000) + 's';
    if (remaining >= -OVERDUE_GRACE_MS) return 'Drawing…';
    return 'Waiting for dealer…';
  }

  function statusText(readyState) {
    return readyState === EVENT_SOURCE_OPEN ? 'Live' : 'Reconnecting…';
  }

  function viewState(snapshot, placeholder) {
    return {
      currentNumber: snapshot.currentNumber ?? placeholder,
      history: Object.values(snapshot.history || {}),
    };
  }

  const api = {
    gameKeyFromUrl, joinCodeFromUrl, viewerIdFromUrl, withParam, joinUrl,
    isLocalOnly, clockOffset, countdownText, statusText, viewState,
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.BingoLive = api;
})(this);
