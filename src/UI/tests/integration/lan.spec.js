const { test, expect } = require('@playwright/test');
const os = require('node:os');

// First private IPv4 address of this machine, i.e. what another device on the LAN would use.
function lanAddress() {
  const all = Object.values(os.networkInterfaces()).flat();
  const ip = all.find(a => a.family === 'IPv4' && !a.internal && /^(10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.)/.test(a.address) && !a.address.startsWith('172.17.'));
  return ip && ip.address;
}

test('UI loaded via the LAN address draws numbers from the real API', async ({ page }) => {
  const host = lanAddress();
  test.skip(!host, 'no private LAN address on this machine');
  test.skip(!process.env.LAN, 'set LAN=1 with the API listening on 0.0.0.0:5001 (npm run api)');

  await page.addInitScript(() => localStorage.setItem('bingoSound', 'off'));
  await page.goto(`http://${host}:8080/`);
  const resp = page.waitForResponse(r => r.url().startsWith(`http://${host}:5001/Get?`));
  await page.locator('#currentNumber').click();
  const { currentNumber } = await (await resp).json();
  await expect(page.locator('#currentNumber')).toHaveText(currentNumber);
  await expect(page.locator('td.cell.called')).toHaveCount(1);
});
