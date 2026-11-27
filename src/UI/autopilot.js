// Dealer-side auto pilot: schedules one draw at the server's nextDrawAt.
// Works in the browser (window.BingoAutoPilot) and in Node (module.exports) for tests.
(function (root) {
  function create({ draw, now, setTimer, clearTimer, onError }) {
    const report = onError || ((err) => console.warn('Auto pilot draw failed', err));
    let scheduled = false;
    let timer;
    let scheduledFor = null;
    let inFlight = false;
    let handledFor = null; // nextDrawAt of the draw in flight or last successfully drawn

    function cancel() {
      if (scheduled) clearTimer(timer);
      scheduled = false;
      scheduledFor = null;
    }

    function fire() {
      // The timer is spent; the next sync must schedule afresh.
      const target = scheduledFor;
      scheduled = false;
      scheduledFor = null;
      if (inFlight) return;
      inFlight = true;
      handledFor = target;
      const release = () => { inFlight = false; };
      new Promise(resolve => resolve(draw())).then(release, (err) => {
        release();
        handledFor = null; // a failed draw may be retried by the next sync
        try { report(err); } catch (_) { /* a failing reporter must not escape */ }
      });
    }

    function sync(snapshot, offsetMs) {
      const pilot = snapshot.autoPilot;
      if (!pilot || !pilot.on || Number.isNaN(Date.parse(pilot.nextDrawAt))) { cancel(); return; }
      if (pilot.nextDrawAt === handledFor) return; // stale snapshot of a draw already made
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
