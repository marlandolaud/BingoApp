// Works out the API's address from where the page was loaded, so the UI works
// from other devices on the network (not only on localhost).
(function (root) {
  const API_PORT = 5001;

  function apiBase(location) {
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
