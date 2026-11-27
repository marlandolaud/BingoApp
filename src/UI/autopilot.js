// Dealer-side auto pilot: schedules one draw at the server's nextDrawAt.
// Works in the browser (window.BingoAutoPilot) and in Node (module.exports) for tests.
(function (root) {
  function create({ draw, now, setTimer, clearTimer }) {
    let scheduled = false;
    let timer;
    let scheduledFor = null;
    let inFlight = false;

    function cancel() {
      if (scheduled) clearTimer(timer);
      scheduled = false;
      scheduledFor = null;
    }

    function fire() {
      if (inFlight) return;
      inFlight = true;
      const release = () => { inFlight = false; };
      new Promise(resolve => resolve(draw())).then(release, release);
    }

    function sync(snapshot, offsetMs) {
      const pilot = snapshot.autoPilot;
      if (!pilot || !pilot.on) { cancel(); return; }
      if (scheduled && scheduledFor === pilot.nextDrawAt) return;
      cancel();
      const delay = Math.max(0, Date.parse(pilot.nextDrawAt) - (now() + offsetMs));
      scheduledFor = pilot.nextDrawAt;
      scheduled = true;
      timer = setTimer(fire, delay);
    }

    return { sync, stop: cancel };
  }

  const api = { create };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.BingoAutoPilot = api;
})(this);
