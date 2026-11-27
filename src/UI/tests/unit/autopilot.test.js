const { test } = require('node:test');
const assert = require('node:assert/strict');
const AP = require('../../autopilot.js');

const T0 = 1_000_000;
function setup(drawImpl, onError) {
  const timers = [];
  const cleared = [];
  const draws = { n: 0 };
  const ap = AP.create({
    draw: () => { draws.n++; return drawImpl ? drawImpl() : Promise.resolve(); },
    now: () => T0,
    setTimer: (fn, ms) => { const t = { fn, ms, id: timers.length }; timers.push(t); return t.id; },
    clearTimer: (id) => cleared.push(id),
    ...(onError ? { onError } : {}),
  });
  return { ap, timers, cleared, draws };
}
const on = (ms) => ({ autoPilot: { on: true, nextDrawAt: new Date(T0 + ms).toISOString() } });
const off = { autoPilot: { on: false, nextDrawAt: null } };

test('schedules one draw at nextDrawAt', () => {
  const { ap, timers, draws } = setup();
  ap.sync(on(3000), 0);
  assert.equal(timers.length, 1);
  assert.equal(timers[0].ms, 3000);
  assert.equal(draws.n, 0);
  timers[0].fn();
  assert.equal(draws.n, 1);
});
test('applies the clock offset to the delay', () => {
  const { ap, timers } = setup();
  ap.sync(on(3000), 1000);
  assert.equal(timers[0].ms, 2000);
});
test('same snapshot twice schedules once', () => {
  const { ap, timers, cleared, draws } = setup();
  ap.sync(on(3000), 0);
  ap.sync(on(3000), 0);
  assert.equal(timers.length, 1);
  assert.deepEqual(cleared, []);
  timers[0].fn();
  assert.equal(draws.n, 1);
});
test('new nextDrawAt reschedules and clears the old timer', () => {
  const { ap, timers, cleared } = setup();
  ap.sync(on(3000), 0);
  ap.sync(on(6000), 0);
  assert.equal(timers.length, 2);
  assert.deepEqual(cleared, [0]);
  assert.equal(timers[1].ms, 6000);
});
test('off clears timer', () => {
  const { ap, timers, cleared } = setup();
  ap.sync(on(3000), 0);
  ap.sync(off, 0);
  assert.deepEqual(cleared, [0]);
  assert.equal(timers.length, 1);
});
test('off with nothing scheduled does not clear', () => {
  const { ap, cleared } = setup();
  ap.sync(off, 0);
  assert.deepEqual(cleared, []);
});
test('turning back on after off schedules again', () => {
  const { ap, timers } = setup();
  ap.sync(on(3000), 0);
  ap.sync(off, 0);
  ap.sync(on(3000), 0);
  assert.equal(timers.length, 2);
});
test('overdue fires immediately', () => {
  const { ap, timers } = setup();
  ap.sync(on(-4000), 0);
  assert.equal(timers[0].ms, 0);
});
test('does not draw twice while a draw is in flight', async () => {
  let resolve;
  const { ap, timers, draws } = setup(() => new Promise(r => { resolve = r; }));
  ap.sync(on(0), 0);
  timers[0].fn();
  timers[0].fn();
  assert.equal(draws.n, 1);
  resolve();
  await new Promise(r => setImmediate(r));
  timers[0].fn();
  assert.equal(draws.n, 2);
});
test('a rejected draw still releases the in-flight guard', async () => {
  const { ap, timers, draws } = setup(() => Promise.reject(new Error('boom')));
  ap.sync(on(0), 0);
  timers[0].fn();
  await new Promise(r => setImmediate(r));
  timers[0].fn();
  assert.equal(draws.n, 2);
});
test('a synchronous non-promise draw result is tolerated', async () => {
  const { ap, timers, draws } = setup(() => undefined);
  ap.sync(on(0), 0);
  timers[0].fn();
  await new Promise(r => setImmediate(r));
  timers[0].fn();
  assert.equal(draws.n, 2);
});
test('a draw that throws synchronously releases the guard', async () => {
  const { ap, timers, draws } = setup(() => { throw new Error('boom'); });
  ap.sync(on(0), 0);
  timers[0].fn();
  await new Promise(r => setImmediate(r));
  timers[0].fn();
  assert.equal(draws.n, 2);
});
test('stop clears timer', () => {
  const { ap, timers, cleared } = setup();
  ap.sync(on(3000), 0);
  ap.stop();
  assert.deepEqual(cleared, [0]);
  ap.sync(on(3000), 0);
  assert.equal(timers.length, 2);
});
test('stop with nothing scheduled does not clear', () => {
  const { ap, cleared } = setup();
  ap.stop();
  assert.deepEqual(cleared, []);
});
test('repeated off does not clear an already cleared timer', () => {
  const { ap, cleared } = setup();
  ap.sync(on(3000), 0);
  ap.sync(off, 0);
  ap.sync(off, 0);
  ap.stop();
  assert.deepEqual(cleared, [0]);
});

const tick = () => new Promise(r => setImmediate(r));

test('after a rejected draw the next sync of the same snapshot schedules a new timer', async () => {
  const { ap, timers } = setup(() => Promise.reject(new Error('boom')), () => {});
  ap.sync(on(0), 0);
  timers[0].fn();
  await tick();
  assert.equal(timers.length, 1);
  ap.sync(on(0), 0);
  assert.equal(timers.length, 2);
});
test('a fired timer is consumed so it is not cleared later', () => {
  const { ap, timers, cleared } = setup();
  ap.sync(on(0), 0);
  timers[0].fn();
  ap.stop();
  ap.sync(off, 0);
  assert.deepEqual(cleared, []);
});
test('timer firing while a draw is in flight is consumed and the next sync reschedules', async () => {
  let resolve;
  const { ap, timers, draws } = setup(() => new Promise(r => { resolve = r; }));
  ap.sync(on(0), 0);
  timers[0].fn();
  ap.sync(on(1000), 0);
  timers[1].fn();
  assert.equal(draws.n, 1);
  ap.sync(on(1000), 0);
  assert.equal(timers.length, 3);
  resolve();
  await tick();
  timers[2].fn();
  assert.equal(draws.n, 2);
});
test('draw rejection is reported to onError', async () => {
  const err = new Error('boom');
  const seen = [];
  const { ap, timers } = setup(() => Promise.reject(err), (e) => seen.push(e));
  ap.sync(on(0), 0);
  timers[0].fn();
  await tick();
  assert.deepEqual(seen, [err]);
});
test('a successful draw does not call onError', async () => {
  const seen = [];
  const { ap, timers } = setup(() => Promise.resolve(), (e) => seen.push(e));
  ap.sync(on(0), 0);
  timers[0].fn();
  await tick();
  assert.deepEqual(seen, []);
});
test('onError defaults to console.warn', async () => {
  const orig = console.warn;
  const seen = [];
  console.warn = (...a) => seen.push(a);
  try {
    const err = new Error('boom');
    const { ap, timers } = setup(() => Promise.reject(err));
    ap.sync(on(0), 0);
    timers[0].fn();
    await tick();
    assert.equal(seen.length, 1);
    assert.equal(seen[0][1], err);
  } finally { console.warn = orig; }
});
test('on with null or unparseable nextDrawAt is treated as off', () => {
  const { ap, timers, cleared } = setup();
  ap.sync(on(3000), 0);
  ap.sync({ autoPilot: { on: true, nextDrawAt: null } }, 0);
  assert.deepEqual(cleared, [0]);
  ap.sync({ autoPilot: { on: true, nextDrawAt: 'garbage' } }, 0);
  assert.equal(timers.length, 1);
  assert.deepEqual(cleared, [0]);
});
test('a snapshot without autoPilot is treated as off', () => {
  const { ap, timers, cleared } = setup();
  ap.sync(on(3000), 0);
  ap.sync({}, 0);
  assert.deepEqual(cleared, [0]);
  assert.equal(timers.length, 1);
});

test('stale snapshot after a successful draw does not draw again', async () => {
  const { ap, timers, draws } = setup();
  ap.sync(on(0), 0);
  timers[0].fn();
  await tick();
  ap.sync(on(0), 0);
  assert.equal(timers.length, 1);
  assert.equal(draws.n, 1);
});
test('stale snapshot while the draw is in flight does not schedule', () => {
  const { ap, timers } = setup(() => new Promise(() => {}));
  ap.sync(on(0), 0);
  timers[0].fn();
  ap.sync(on(0), 0);
  assert.equal(timers.length, 1);
});
test('new nextDrawAt after a successful draw schedules', async () => {
  const { ap, timers } = setup();
  ap.sync(on(0), 0);
  timers[0].fn();
  await tick();
  ap.sync(on(5000), 0);
  assert.equal(timers.length, 2);
  assert.equal(timers[1].ms, 5000);
});
test('an onError that throws does not cause an unhandled rejection', async () => {
  const unhandled = [];
  const h = (e) => unhandled.push(e);
  process.on('unhandledRejection', h);
  try {
    const { ap, timers } = setup(() => Promise.reject(new Error('a')), () => { throw new Error('b'); });
    ap.sync(on(0), 0);
    timers[0].fn();
    await tick(); await tick();
    assert.deepEqual(unhandled, []);
  } finally { process.off('unhandledRejection', h); }
});
