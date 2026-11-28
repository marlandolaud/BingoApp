const { test } = require('node:test');
const assert = require('node:assert/strict');
const L = require('../../live.js');

const G = '123e4567-e89b-12d3-a456-426614174000';
const T0 = Date.parse('2026-11-27T10:00:00.000Z');

test('gameKeyFromUrl accepts a GUID', () => {
  assert.equal(L.gameKeyFromUrl('?game=' + G), G);
  assert.equal(L.gameKeyFromUrl('?x=1&game=' + G.toUpperCase()), G.toUpperCase());
});
test('gameKeyFromUrl rejects non-GUIDs and missing values', () => {
  assert.equal(L.gameKeyFromUrl('?game=abc'), null);
  assert.equal(L.gameKeyFromUrl(''), null);
  assert.equal(L.gameKeyFromUrl('?game=' + G + 'x'), null);
  assert.equal(L.gameKeyFromUrl('?game=x' + G), null);
  assert.equal(L.gameKeyFromUrl('?game=' + G.replace(/-/g, '')), null);
});
test('joinCodeFromUrl reads join', () => {
  assert.equal(L.joinCodeFromUrl('?join=' + G), G);
  assert.equal(L.joinCodeFromUrl('?game=' + G), null);
  assert.equal(L.joinCodeFromUrl('?join=nope'), null);
});
test('viewerIdFromUrl reads v', () => {
  assert.equal(L.viewerIdFromUrl('?v=' + G), G);
  assert.equal(L.viewerIdFromUrl('?join=' + G), null);
  assert.equal(L.viewerIdFromUrl('?v=nope'), null);
});

test('withParam adds a parameter and keeps the others', () => {
  const out = L.withParam('http://h/p.html?a=1', 'b', '2');
  assert.equal(out, 'http://h/p.html?a=1&b=2');
});
test('withParam replaces an existing parameter', () => {
  assert.equal(L.withParam('http://h/p.html?a=1&b=0', 'b', '2'), 'http://h/p.html?a=1&b=2');
});

test('joinUrl uses origin and root path', () => {
  assert.equal(L.joinUrl({ origin: 'https://x.ngrok.app', pathname: '/' }, G),
    'https://x.ngrok.app/watch.html?join=' + G);
});
test('joinUrl keeps the directory of the pathname', () => {
  const u = L.joinUrl({ origin: 'https://x', pathname: '/bingo/index.html' }, G);
  assert.equal(u, 'https://x/bingo/watch.html?join=' + G);
});

test('isLocalOnly', () => {
  for (const h of ['localhost', '127.0.0.1', '127.5.5.5', '::1', '[::1]']) assert.equal(L.isLocalOnly(h), true, h);
  for (const h of ['192.168.1.5', 'x.ngrok.app', '1127.0.0.1', 'notlocalhost', 'localhost.evil.com']) assert.equal(L.isLocalOnly(h), false, h);
});

test('clockOffset is server minus client in ms, handling microseconds and offsets', () => {
  assert.equal(L.clockOffset('2026-11-27T10:00:05.000000+00:00', T0), 5000);
  assert.equal(L.clockOffset('2026-11-27T11:00:00.123456+01:00', T0), 123);
  assert.equal(L.clockOffset('2026-11-27T09:59:58Z', T0), -2000);
});

const snap = (offsetSec) => ({ autoPilot: { on: true, nextDrawAt: new Date(T0 + offsetSec * 1000).toISOString() } });

test('countdownText is empty when auto pilot is off', () => {
  assert.equal(L.countdownText({ autoPilot: { on: false, nextDrawAt: null } }, T0, 0), '');
  assert.equal(L.countdownText({}, T0, 0), '');
});
test('countdownText counts down, rounding seconds up', () => {
  assert.equal(L.countdownText(snap(3.2), T0, 0), 'Next ball in 4s');
  assert.equal(L.countdownText(snap(3), T0, 0), 'Next ball in 3s');
  assert.equal(L.countdownText(snap(0.001), T0, 0), 'Next ball in 1s');
});
test('countdownText boundaries for overdue', () => {
  assert.equal(L.countdownText(snap(0), T0, 0), 'Drawing…');
  assert.equal(L.countdownText(snap(-1), T0, 0), 'Drawing…');
  assert.equal(L.countdownText(snap(-5), T0, 0), 'Drawing…');
  assert.equal(L.countdownText(snap(-5.001), T0, 0), 'Waiting for dealer…');
});
test('countdownText applies the clock offset', () => {
  assert.equal(L.countdownText(snap(10), T0, 4000), 'Next ball in 6s');
  assert.equal(L.countdownText(snap(10), T0, -4000), 'Next ball in 14s');
});

test('statusText', () => {
  assert.equal(L.statusText(1), 'Live');
  assert.equal(L.statusText(0), 'Reconnecting…');
  assert.equal(L.statusText(2), 'Reconnecting…');
});

test('viewState lists history in key order and falls back to placeholder', () => {
  const v = L.viewState({ currentNumber: 'N40', history: { 0: 'B5', 1: 'N40' } }, '--');
  assert.deepEqual(v, { currentNumber: 'N40', history: ['B5', 'N40'] });
  const e = L.viewState({ currentNumber: null, history: {} }, '--');
  assert.deepEqual(e, { currentNumber: '--', history: [] });
});
test('viewState tolerates a missing history', () => {
  assert.deepEqual(L.viewState({ currentNumber: null }, '--'), { currentNumber: '--', history: [] });
});
