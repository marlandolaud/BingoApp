const { test, expect } = require('@playwright/test');

const KEY = '11111111-1111-1111-1111-111111111111';
const JOIN = '33333333-3333-3333-3333-333333333333';
const API = 'http://localhost:5001';
const T0 = Date.parse('2026-11-27T12:00:00.000Z');

function snapshot(overrides = {}) {
  return {
    round: 1, numberOfDrew: 0, currentNumber: null, history: {},
    autoPilot: { on: false, nextDrawAt: null },
    serverTime: new Date(T0).toISOString(), viewerCount: 2, joinCode: JOIN,
    ...overrides,
  };
}

// Mocks every API endpoint the dealer page uses and records the requests made.
// `state.snap` is what /State and /Events return; tests may replace it.
async function mockApi(page, state) {
  const requests = [];
  await page.route(`${API}/**`, r => {
    const url = new URL(r.request().url());
    requests.push({ method: r.request().method(), path: url.pathname, search: url.search });
    switch (url.pathname) {
      case '/GetGameKey': return r.fulfill({ json: KEY });
      case '/Get': {
        const drew = { numberOfDrew: 1, currentNumber: 'B7', history: { 0: 'B7' } };
        state.snap = { ...state.snap, ...drew };
        return r.fulfill({ json: drew });
      }
      case '/State': return r.fulfill({ json: state.snap });
      case '/AutoPilot': return r.fulfill({ status: 204, body: '' });
      case '/Reset': return r.fulfill({ status: 200, body: '' });
      case '/Events': return r.fulfill({
        status: 200,
        headers: { 'content-type': 'text/event-stream' },
        body: 'event: state\ndata: ' + JSON.stringify(state.snap) + '\n\n',
      });
      default: return r.fulfill({ status: 404, body: '' });
    }
  });
  return requests;
}

const hits = (requests, path) => requests.filter(r => r.path === path);

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('bingoSound', 'off'));
});

test('first draw puts the game key in the URL', async ({ page }) => {
  await mockApi(page, { snap: snapshot() });
  await page.goto('/');
  await page.locator('#currentNumber').click();
  await expect(page.locator('#currentNumber')).toHaveText('B7');
  await expect(page).toHaveURL(new RegExp('\\?game=' + KEY + '$'));
});

test('refresh restores the board without drawing', async ({ page }) => {
  const requests = await mockApi(page, {
    snap: snapshot({ numberOfDrew: 3, currentNumber: 'G50', history: { 0: 'B7', 1: 'N31', 2: 'G50' } }),
  });
  await page.goto('/?game=' + KEY);
  await expect(page.locator('td.cell.called')).toHaveCount(3);
  await expect(page.locator('#currentNumber')).toHaveText('G50');
  expect(hits(requests, '/State')[0].search).toBe('?GameKey=' + KEY);
  expect(hits(requests, '/Get')).toHaveLength(0);
});

test('refresh opens the live event stream for the game', async ({ page }) => {
  const requests = await mockApi(page, { snap: snapshot() });
  await page.goto('/?game=' + KEY);
  await expect.poll(() => hits(requests, '/Events').length).toBeGreaterThan(0);
  expect(hits(requests, '/Events')[0].search).toBe('?GameKey=' + KEY);
});

test('malformed game param starts fresh', async ({ page }) => {
  const requests = await mockApi(page, { snap: snapshot() });
  let dialogs = 0;
  page.on('dialog', d => { dialogs++; d.dismiss(); });
  await page.goto('/?game=abc');
  await expect(page.locator('#currentNumber')).toHaveText('Push');
  await page.waitForTimeout(300);
  expect(hits(requests, '/State')).toHaveLength(0);
  expect(hits(requests, '/Events')).toHaveLength(0);
  expect(dialogs).toBe(0);
});

test('shows QR with the join link and viewer count', async ({ page }) => {
  await mockApi(page, { snap: snapshot() });
  await page.goto('/?game=' + KEY);
  const joinUrl = 'http://localhost:8080/watch.html?join=' + JOIN;
  await expect(page.locator('#qr')).toHaveAttribute('data-url', joinUrl);
  await expect(page.locator('#qr svg')).toBeVisible();
  await expect(page.locator('#joinLink')).toHaveAttribute('href', joinUrl);
  await expect(page.locator('#viewerCount')).toHaveText('👁 2 watching');
});

test('local hint shows on localhost', async ({ page }) => {
  await mockApi(page, { snap: snapshot() });
  await page.goto('/?game=' + KEY);
  await expect(page.locator('#localHint')).toBeVisible();
});

test('local hint is hidden on a LAN address', async ({ page }) => {
  // serve the page from a non-local hostname, proxied to the local static server
  await page.route('http://bingo.test:8080/**', async r => {
    const url = r.request().url().replace('http://bingo.test:8080', 'http://localhost:8080');
    return r.fulfill({ response: await r.fetch({ url }) });
  });
  await page.route('http://bingo.test:5001/**', r => {
    const path = new URL(r.request().url()).pathname;
    if (path === '/State') return r.fulfill({ json: snapshot() });
    return r.fulfill({ status: 404, body: '' });
  });
  await page.goto('http://bingo.test:8080/?game=' + KEY);
  await expect(page.locator('#viewerCount')).toHaveText('👁 2 watching');
  await expect(page.locator('#localHint')).toBeHidden();
});

test('failing live calls only warn, never alert', async ({ page }) => {
  await page.route(`${API}/**`, r => r.fulfill({ status: 500, body: '' }));
  let dialogs = 0;
  page.on('dialog', d => { dialogs++; d.dismiss(); });
  const warnings = [];
  page.on('console', m => { if (m.type() === 'warning') warnings.push(m.text()); });
  await page.goto('/?game=' + KEY);
  await expect.poll(() => warnings.some(w => /restore/i.test(w))).toBe(true);
  await page.locator('#autoPilotBtn').click();
  await expect.poll(() => warnings.some(w => /auto pilot/i.test(w))).toBe(true);
  expect(dialogs).toBe(0);
});

test('auto pilot toggles and counts down', async ({ page }) => {
  await page.clock.install({ time: T0 });
  const state = { snap: snapshot() };
  const requests = await mockApi(page, state);
  await page.goto('/?game=' + KEY);
  const btn = page.locator('#autoPilotBtn');
  await expect(btn).toHaveText('▶ Auto pilot');
  await expect(btn).toHaveAttribute('aria-pressed', 'false');
  await expect(page.locator('#viewerCount')).toHaveText('👁 2 watching');

  state.snap = snapshot({ autoPilot: { on: true, nextDrawAt: new Date(T0 + 10_000).toISOString() } });
  await btn.click();
  await expect(btn).toHaveText('■ Stop');
  await expect(btn).toHaveAttribute('aria-pressed', 'true');
  const on = hits(requests, '/AutoPilot');
  expect(on).toHaveLength(1);
  expect(on[0].method).toBe('POST');
  expect(on[0].search).toBe('?GameKey=' + KEY + '&on=true');
  await expect(page.locator('#countdown')).toHaveText('Next ball in 10s');

  expect(hits(requests, '/Get')).toHaveLength(0);
  await page.clock.runFor(10_000);
  await expect.poll(() => hits(requests, '/Get').length).toBe(1);
  await expect(page.locator('#currentNumber')).toHaveText('B7');

  state.snap = snapshot({ autoPilot: { on: false, nextDrawAt: null } });
  await btn.click();
  await expect(btn).toHaveText('▶ Auto pilot');
  await expect(btn).toHaveAttribute('aria-pressed', 'false');
  expect(hits(requests, '/AutoPilot')[1].search).toBe('?GameKey=' + KEY + '&on=false');
  await expect(page.locator('#countdown')).toHaveText('');
  expect(hits(requests, '/Get')).toHaveLength(1);
});

test('reset stops auto pilot even when no live update arrives', async ({ page }) => {
  await page.clock.install({ time: T0 });
  const state = { snap: snapshot({ autoPilot: { on: true, nextDrawAt: new Date(T0 + 10_000).toISOString() } }) };
  const requests = await mockApi(page, state);
  // the event stream delivers the initial frame once, then stays silent
  const firstFrame = 'event: state\ndata: ' + JSON.stringify(state.snap) + '\n\n';
  let served = false;
  await page.route(`${API}/Events?*`, r => {
    if (served) return r.abort();
    served = true;
    return r.fulfill({ status: 200, headers: { 'content-type': 'text/event-stream' }, body: firstFrame });
  });
  await page.goto('/?game=' + KEY);
  const btn = page.locator('#autoPilotBtn');
  await expect(btn).toHaveText('■ Stop');
  await expect(page.locator('#countdown')).toHaveText('Next ball in 10s');

  state.snap = snapshot();
  page.once('dialog', d => d.accept());
  await page.getByRole('button', { name: /Reset game/ }).click();
  await expect(btn).toHaveText('▶ Auto pilot');
  await expect(page.locator('#countdown')).toHaveText('');
  await page.clock.runFor(15_000);
  expect(hits(requests, '/Reset')).toHaveLength(1);
  expect(hits(requests, '/Get')).toHaveLength(0);
});

test('two quick first draws share one game key', async ({ page }) => {
  const requests = await mockApi(page, { snap: snapshot() });
  const keys = [KEY, '44444444-4444-4444-4444-444444444444'];
  let n = 0;
  await page.route(`${API}/GetGameKey`, async r => {
    requests.push({ method: 'GET', path: '/GetGameKey', search: '' });
    const key = keys[n++];
    await new Promise(res => setTimeout(res, 300));
    return r.fulfill({ json: key });
  });
  await page.goto('/');
  await page.locator('#currentNumber').click();
  await page.locator('#currentNumber').click();
  await expect.poll(() => hits(requests, '/Get').length).toBe(2);
  expect(hits(requests, '/GetGameKey')).toHaveLength(1);
  await expect(page).toHaveURL(new RegExp('\\?game=' + KEY + '$'));
  await expect.poll(() => hits(requests, '/Events').length).toBeGreaterThan(0);
  expect(hits(requests, '/Events').every(r => r.search === '?GameKey=' + KEY)).toBe(true);
  expect(hits(requests, '/Get').every(r => r.search === '?GameKey=' + KEY)).toBe(true);
});

test('drawing at game over keeps the board', async ({ page }) => {
  await mockApi(page, {
    snap: snapshot({ numberOfDrew: 3, currentNumber: 'G50', history: { 0: 'B7', 1: 'N31', 2: 'G50' } }),
  });
  await page.route(`${API}/Get?*`, r => r.fulfill({ json: 'Game Over!' }));
  await page.goto('/?game=' + KEY);
  await expect(page.locator('td.cell.called')).toHaveCount(3);
  const dialog = page.waitForEvent('dialog', { timeout: 5000 });
  await page.locator('#currentNumber').click();
  const d = await dialog;
  expect(d.message()).toBe('Game Over!');
  await d.accept();
  await expect(page.locator('#currentNumber')).toHaveText('G50');
  await expect(page.locator('td.cell.called')).toHaveCount(3);
});
