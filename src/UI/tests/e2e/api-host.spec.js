const { test, expect } = require('@playwright/test');

const KEY = '22222222-2222-2222-2222-222222222222';

// Loads the page via 127.0.0.1 (standing in for a LAN address) and records which
// hosts the API calls go to.
async function loadVia127(page, calls) {
  await page.addInitScript(() => localStorage.setItem('bingoSound', 'off'));
  await page.route(/:5001\//, r => {
    const url = new URL(r.request().url());
    calls.push(url.host + url.pathname);
    if (url.pathname === '/GetGameKey') return r.fulfill({ json: KEY });
    if (url.pathname === '/Reset') return r.fulfill({ status: 200, body: '' });
    return r.fulfill({ json: { numberOfDrew: 1, currentNumber: 'B7', history: { 0: 'B7' } } });
  });
  await page.goto('http://127.0.0.1:8080/');
}

test('draws call the API on the host the page was loaded from', async ({ page }) => {
  const calls = [];
  await loadVia127(page, calls);
  await page.locator('#currentNumber').click();
  await expect(page.locator('#currentNumber')).toHaveText('B7');
  expect(calls).toEqual(['127.0.0.1:5001/GetGameKey', '127.0.0.1:5001/Get']);
});

test('reset calls the API on the host the page was loaded from', async ({ page }) => {
  const calls = [];
  await loadVia127(page, calls);
  await page.locator('#currentNumber').click();
  await expect(page.locator('#currentNumber')).toHaveText('B7');
  page.once('dialog', d => d.accept());
  await page.getByRole('button', { name: /Reset game/ }).click();
  await expect(page.locator('#currentNumber')).toHaveText('Push');
  expect(calls).toContain('127.0.0.1:5001/Reset');
  expect(calls.every(c => c.startsWith('127.0.0.1:5001/'))).toBe(true);
});
