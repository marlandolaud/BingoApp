const { test, expect } = require('@playwright/test');

const V = '44444444-4444-4444-4444-444444444444';
const JOIN = '33333333-3333-3333-3333-333333333333';
const API = 'http://localhost:5001';
const T0 = Date.parse('2026-11-27T12:00:00.000Z');

function snapshot(overrides = {}) {
  return {
    round: 1, numberOfDrew: 0, currentNumber: null, history: {},
    autoPilot: { on: false, nextDrawAt: null },
    serverTime: new Date(T0).toISOString(), viewerCount: 2, joinCode: null,
    ...overrides,
  };
}

const sse = (snap) => ({
  status: 200,
  headers: { 'content-type': 'text/event-stream' },
  // long retry so a finished body does not reconnect during the test
  body: 'retry: 60000\nevent: state\ndata: ' + JSON.stringify(snap) + '\n\n',
});

// Mocks the viewer endpoints; `opts.eventsHandler` overrides the SSE response.
async function mockApi(page, snap, opts = {}) {
  const requests = [];
  await page.route(`${API}/**`, r => {
    const url = new URL(r.request().url());
    requests.push({ method: r.request().method(), path: url.pathname, search: url.search });
    if (url.pathname === '/viewers') {
      return opts.joinMissing
        ? r.fulfill({ status: 404, body: '' })
        : r.fulfill({ json: { viewerId: V } });
    }
    if (url.pathname === `/watch/${V}/state`) {
      return opts.viewerMissing ? r.fulfill({ status: 404, body: '' }) : r.fulfill({ json: snap });
    }
    if (url.pathname === `/watch/${V}/events`) {
      return opts.eventsHandler ? opts.eventsHandler(r) : r.fulfill(sse(snap));
    }
    return r.fulfill({ status: 404, body: '' });
  });
  return requests;
}

// A mocked SSE body ends right away, so the browser flips back to "reconnecting"
// immediately. Record every status the page showed instead of sampling it.
async function recordStatuses(page) {
  await page.addInitScript(() => {
    window.__statuses = [];
    document.addEventListener('DOMContentLoaded', () => {
      const el = document.getElementById('status');
      const note = () => window.__statuses.push(el.textContent);
      new MutationObserver(note).observe(el, { childList: true, characterData: true, subtree: true });
    });
  });
}
const sawStatus = (page, text) => expect.poll(() => page.evaluate(() => window.__statuses)).toContain(text);

test('join swaps to a personal viewer url', async ({ page }) => {
  await mockApi(page, snapshot());
  await page.goto('/watch.html?join=' + JOIN);
  await expect(page).toHaveURL(new RegExp('watch\\.html\\?v=' + V + '$'));
});

test('renders snapshot from the event stream', async ({ page }) => {
  const snap = snapshot({ numberOfDrew: 4, currentNumber: 'O70', history: { 0: 'B5', 1: 'I20', 2: 'N35', 3: 'O70' } });
  await mockApi(page, snap);
  await recordStatuses(page);
  await page.goto('/watch.html?v=' + V);
  await expect(page.locator('#currentNumber')).toHaveText('O70');
  await expect(page.locator('#boardBody td.called')).toHaveCount(4);
  await expect(page.locator('#track .dot')).toHaveCount(4);
  await sawStatus(page, 'Live');
});

test('unknown join shows game not found', async ({ page }) => {
  await mockApi(page, snapshot(), { joinMissing: true });
  await page.goto('/watch.html?join=' + JOIN);
  await expect(page.locator('#notFound')).toBeVisible();
  await expect(page.locator('#notFound')).toHaveText('Game not found');
});

test('unknown viewer shows game not found', async ({ page }) => {
  await mockApi(page, snapshot(), { viewerMissing: true });
  await page.goto('/watch.html?v=' + V);
  await expect(page.locator('#notFound')).toBeVisible();
  await expect(page.locator('#currentNumber')).toHaveText('--');
});

test('page without a join code or viewer id shows game not found', async ({ page }) => {
  await mockApi(page, snapshot());
  await page.goto('/watch.html');
  await expect(page.locator('#notFound')).toBeVisible();
});

test('shows countdown and waiting for dealer when overdue', async ({ page }) => {
  await page.clock.install({ time: T0 });
  const snap = snapshot({ autoPilot: { on: true, nextDrawAt: new Date(T0 + 3000).toISOString() } });
  await mockApi(page, snap);
  await page.goto('/watch.html?v=' + V);
  await expect(page.locator('#countdown')).toHaveText('Next ball in 3s');
  await page.clock.runFor(9000);
  await expect(page.locator('#countdown')).toHaveText('Waiting for dealer…');
  await expect(page.locator('#status')).toHaveText('Waiting for dealer…');
});

test('reconnecting then live', async ({ page }) => {
  let calls = 0;
  const snap = snapshot();
  await mockApi(page, snap, {
    eventsHandler: r => (++calls === 1 ? r.abort() : r.fulfill(sse(snap))),
  });
  await recordStatuses(page);
  await page.goto('/watch.html?v=' + V);
  await sawStatus(page, 'Reconnecting…');
  await expect.poll(() => page.evaluate(() => window.__statuses), { timeout: 10000 }).toContain('Live');
  const seen = await page.evaluate(() => window.__statuses);
  expect(seen.indexOf('Reconnecting…')).toBeLessThan(seen.indexOf('Live'));
});

test('a closed stream is reopened after a bad response', async ({ page }) => {
  let calls = 0;
  const snap = snapshot({ currentNumber: 'B5', history: { 0: 'B5' } });
  await mockApi(page, snap, {
    eventsHandler: r => (++calls === 1 ? r.fulfill({ status: 502, body: '' }) : r.fulfill(sse(snap))),
  });
  await recordStatuses(page);
  await page.goto('/watch.html?v=' + V);
  await sawStatus(page, 'Reconnecting…');
  await expect.poll(() => page.evaluate(() => window.__statuses), { timeout: 10000 }).toContain('Live');
  expect(calls).toBe(2);
  await expect(page.locator('#currentNumber')).toHaveText('B5');
  await expect(page.locator('#boardBody td.called')).toHaveCount(1);
});

test('a viewer that disappears mid-session shows game not found', async ({ page }) => {
  let stateCalls = 0;
  const snap = snapshot();
  await page.route(`${API}/**`, r => {
    const path = new URL(r.request().url()).pathname;
    if (path === `/watch/${V}/state`) {
      return ++stateCalls === 1 ? r.fulfill({ json: snap }) : r.fulfill({ status: 404, body: '' });
    }
    return r.fulfill({ status: 404, body: '' });
  });
  await page.goto('/watch.html?v=' + V);
  await expect(page.locator('#notFound')).toBeVisible({ timeout: 10000 });
  expect(stateCalls).toBe(2);
});

test('viewer only calls viewer endpoints', async ({ page }) => {
  const requests = await mockApi(page, snapshot({ currentNumber: 'B5', history: { 0: 'B5' } }));
  await page.goto('/watch.html?join=' + JOIN);
  await expect(page.locator('#currentNumber')).toHaveText('B5');
  await page.locator('#currentNumber').click();
  await page.keyboard.press('Enter');
  await page.keyboard.press('Escape');
  expect(requests.length).toBeGreaterThan(0);
  const allowed = /^\/(viewers|watch\/[0-9a-f-]{36}\/(state|events))$/;
  expect(requests.filter(r => !allowed.test(r.path))).toEqual([]);
});

test('a failed join shows reconnecting and retries instead of game not found', async ({ page }) => {
  let joins = 0;
  const snap = snapshot({ currentNumber: 'B5', history: { 0: 'B5' } });
  await mockApi(page, snap);
  await page.route(`${API}/viewers?*`, r => (++joins === 1
    ? r.fulfill({ status: 503, body: '' })
    : r.fulfill({ json: { viewerId: V } })));
  await recordStatuses(page);
  await page.goto('/watch.html?join=' + JOIN);
  await sawStatus(page, 'Reconnecting…');
  await expect(page.locator('#notFound')).toBeHidden();
  await expect(page).toHaveURL(new RegExp('watch\\.html\\?v=' + V + '$'), { timeout: 10000 });
  expect(joins).toBe(2);
  await expect(page.locator('#currentNumber')).toHaveText('B5');
});

test('a join that cannot reach the server retries', async ({ page }) => {
  let joins = 0;
  await mockApi(page, snapshot());
  await page.route(`${API}/viewers?*`, r => (++joins === 1 ? r.abort() : r.fulfill({ json: { viewerId: V } })));
  await recordStatuses(page);
  await page.goto('/watch.html?join=' + JOIN);
  await sawStatus(page, 'Reconnecting…');
  await expect(page).toHaveURL(new RegExp('watch\\.html\\?v=' + V + '$'), { timeout: 10000 });
  expect(joins).toBe(2);
});

test('the viewer page does not request a favicon', async ({ page }) => {
  const favicons = [];
  page.on('request', r => { if (/favicon/.test(r.url())) favicons.push(r.url()); });
  await mockApi(page, snapshot());
  await page.goto('/watch.html?v=' + V);
  await expect(page.locator('#currentNumber')).toHaveText('--');
  await page.waitForLoadState('networkidle');
  expect(favicons).toEqual([]);
  await expect(page.locator('link[rel="icon"]')).toHaveAttribute('href', 'data:,');
});
