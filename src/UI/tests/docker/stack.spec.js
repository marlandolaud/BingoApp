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
