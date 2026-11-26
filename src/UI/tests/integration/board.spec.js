const { test, expect } = require('@playwright/test');

// Runs against the real ASP.NET API (started by playwright.config.js when INTEGRATION=1).
test('board matches every number the real API draws', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('bingoSound', 'off'));
  await page.goto('/');
  const drawn = [];
  for (let k = 0; k < 10; k++) {
    const resp = page.waitForResponse(r => r.url().includes('/Get?'));
    await page.locator('#currentNumber').click();
    drawn.push((await (await resp).json()).currentNumber);
  }
  await expect(page.locator('#currentNumber')).toHaveText(drawn[9]);
  await expect(page.locator('td.cell.called')).toHaveCount(10);
  for (const ball of drawn) {
    const [letter, n] = [ball[0], Number(ball.slice(1))];
    const idx = (n - 1) % 15;
    await expect(page.locator(`tr[data-letter="${letter}"] td.cell`).nth(idx)).toHaveClass(/called/);
  }
  await expect(page.locator('td.cell.last')).toHaveText(String(Number(drawn[9].slice(1))));
  // balanced draws: 10 calls => exactly 2 per letter
  await expect(page.locator('tr[data-letter] .count')).toHaveText(['2', '2', '2', '2', '2']);
});
