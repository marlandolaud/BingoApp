const { test } = require('node:test');
const assert = require('node:assert/strict');
const AP = require('../../autopilot.js');

const T0 = 1_000_000;
function setup(drawImpl) {
  const timers = [];
  const cleared = [];
  const draws = { n: 0 };
  const ap = AP.create({
    draw: () => { draws.n++; return drawImpl ? drawImpl() : Promise.resolve(); },
    now: () => T0,
    setTimer: (fn, ms) => { const t = { fn, ms, id: timers.length }; timers.push(t); return t.id; },
    clearTimer: (id) => cleared.push(id),
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
