const { test, expect } = require('@playwright/test');

// Run against `docker compose up` (see README): nginx serves the UI on :8080 and
// proxies /api to the API container; the API itself is not published.
test.skip(!process.env.DOCKER, 'set DOCKER=1 with the compose stack running');
const UI = `http://localhost:${process.env.UI_PORT || 8080}`;

test('the containerised UI draws numbers from the API through the /api proxy', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('bingoSound', 'off'));
  await page.goto(UI + '/');
  const resp = page.waitForResponse(r => new URL(r.url()).pathname === '/api/Get');
  await page.locator('#currentNumber').click();
  const r = await resp;
  expect(r.status()).toBe(200);
  const { currentNumber } = await r.json();
  await expect(page.locator('#currentNumber')).toHaveText(currentNumber);
  await expect(page.locator('td.cell.called')).toHaveCount(1);
});

test('the proxy hands out game keys from the API', async ({ request }) => {
  const r = await request.get(UI + '/api/GetGameKey');
  expect(r.status()).toBe(200);
  expect(await r.json()).toMatch(/^[0-9a-f-]{36}$/);
});

test('the image points the UI at the /api proxy', async ({ request }) => {
  const r = await request.get(UI + '/config.js');
  expect(await r.text()).toContain("window.BINGO_API_BASE = '/api'");
});

// The ngrok agent's local inspector (:4040) reports the public URL. Needs NGROK_AUTHTOKEN in .env.
async function publicUrl(request) {
  const r = await request.get('http://localhost:4040/api/tunnels').catch(() => null);
  if (!r || !r.ok()) return null;
  const t = (await r.json()).tunnels.find(x => x.public_url.startsWith('https://'));
  return t && t.public_url;
}

test.describe('ngrok tunnel', () => {
  test('rejects requests without basic auth', async ({ request }) => {
    const url = await publicUrl(request);
    test.skip(!url, 'ngrok tunnel not running (needs NGROK_AUTHTOKEN)');
    const r = await request.get(url + '/', { headers: { 'ngrok-skip-browser-warning': '1' } });
    expect(r.status()).toBe(401);
  });

  test('serves the UI and API with the right credentials', async ({ request }) => {
    const url = await publicUrl(request);
    test.skip(!url, 'ngrok tunnel not running (needs NGROK_AUTHTOKEN)');
    const auth = 'Basic ' + Buffer.from(`${process.env.BASIC_AUTH_USER}:${process.env.BASIC_AUTH_PASS}`).toString('base64');
    const headers = { Authorization: auth, 'ngrok-skip-browser-warning': '1' };
    expect((await request.get(url + '/', { headers })).status()).toBe(200);
    expect((await request.get(url + '/api/GetGameKey', { headers })).status()).toBe(200);
  });
});

const { execSync } = require('node:child_process');

async function newGame(request) {
  const key = await (await request.get(UI + '/api/GetGameKey')).json();
  return key;
}

test('game survives an api restart', async ({ request }) => {
  test.setTimeout(90_000);
  const key = await newGame(request);
  for (let i = 0; i < 2; i++) {
    expect((await request.get(`${UI}/api/Get?GameKey=${key}`)).status()).toBe(200);
  }
  execSync('docker compose restart api', { cwd: '..', stdio: 'ignore' });
  await expect.poll(async () => {
    const r = await request.get(`${UI}/api/State?GameKey=${key}`).catch(() => null);
    return r ? r.status() : 0;
  }, { timeout: 60_000 }).toBe(200);
  const state = await (await request.get(`${UI}/api/State?GameKey=${key}`)).json();
  expect(state.numberOfDrew).toBe(2);
});

test('watch page streams through nginx', async ({ page, request }) => {
  const key = await newGame(request);
  const { joinCode } = await (await request.get(`${UI}/api/State?GameKey=${key}`)).json();
  const joined = await request.post(`${UI}/api/viewers?join=${joinCode}`);
  expect(joined.status()).toBe(200);
  const { viewerId } = await joined.json();
  await page.goto(`${UI}/watch.html?v=${viewerId}`);
  await expect(page.locator('#status')).not.toHaveText('Connecting…');
  const drawn = await (await request.get(`${UI}/api/Get?GameKey=${key}`)).json();
  await expect(page.locator('#currentNumber')).toHaveText(drawn.currentNumber, { timeout: 3000 });
});

test.describe('ngrok viewer exemption', () => {
  test('viewer paths need no auth but dealer page does', async ({ request }) => {
    const url = await publicUrl(request);
    test.skip(!url, 'ngrok tunnel not running (needs NGROK_AUTHTOKEN)');
    const headers = { 'ngrok-skip-browser-warning': '1' };
    expect((await request.get(url + '/watch.html', { headers })).status()).toBe(200);
    expect((await request.get(url + '/', { headers })).status()).toBe(401);
    expect((await request.get(url + '/api/GetGameKey', { headers })).status()).toBe(401);
    const traversal = await request.get(url + '/api/watch/..%2fGetGameKey', { headers });
    expect(traversal.status()).toBe(401);
  });
});

test('nginx serves the shared stylesheet and the self-hosted font', async ({ request }) => {
  const css = await request.get(UI + '/styles.css');
  expect(css.status()).toBe(200);
  expect(css.headers()['content-type']).toContain('text/css');
  const font = await request.get(UI + '/vendor/fonts/atkinson-hyperlegible-next.woff2');
  expect(font.status()).toBe(200);
  expect(font.headers()['content-type']).toBe('font/woff2');
});
