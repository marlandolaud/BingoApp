// Works out the API's address from where the page was loaded, so the UI works
// from other devices on the network (not only on localhost).
(function (root) {
  const API_PORT = 5001;

  // `configured` (window.BINGO_API_BASE, set by config.js) wins when present, e.g.
  // '/api' when the UI and API sit behind the same reverse proxy (Docker / ngrok).
  function apiBase(location, configured) {
    if (typeof configured === 'string' && configured) return configured.replace(/\/$/, '');
    if (location.protocol === 'file:' || !location.hostname) {
      return 'http://localhost:' + API_PORT;
    }
    let host = location.hostname;
    if (host.includes(':') && !host.startsWith('[')) host = '[' + host + ']';
    return location.protocol + '//' + host + ':' + API_PORT;
  }

  const api = { apiBase };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.BingoApi = api;
})(this);
