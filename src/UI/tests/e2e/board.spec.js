const { test, expect } = require('@playwright/test');

const KEY = '11111111-1111-1111-1111-111111111111';

// Mocks the API: each /Get returns the next draw from `draws`.
async function mockApi(page, draws) {
  let i = 0;
  await page.route('http://localhost:5001/GetGameKey', r => r.fulfill({ json: KEY }));
  await page.route('http://localhost:5001/Get?*', r => {
    i++;
    const history = Object.fromEntries(draws.slice(0, i).map((d, k) => [k, d]));
    r.fulfill({ json: { numberOfDrew: i, currentNumber: draws[i - 1], history } });
  });
  await page.route('http://localhost:5001/Reset?*', r => r.fulfill({ status: 200, body: '' }));
}

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('bingoSound', 'off'));
});

test('shows a 5x15 board with every number uncalled at start', async ({ page }) => {
  await mockApi(page, []);
  await page.goto('/');
  const board = page.getByRole('table', { name: 'Called numbers by letter' });
  await expect(board.locator('th .letter')).toHaveText(['B', 'I', 'N', 'G', 'O']);
  await expect(board.locator('td.cell')).toHaveCount(75);
  await expect(board.locator('td.cell.called')).toHaveCount(0);
  await expect(board.getByRole('row', { name: /^B/ }).locator('td.cell').first()).toHaveText('1');
  await expect(board.getByRole('row', { name: /^O/ }).locator('td.cell').last()).toHaveText('75');
});

test('drawn numbers light up in their letter row', async ({ page }) => {
  await mockApi(page, ['B7', 'O75']);
  await page.goto('/');
  await page.locator('#currentNumber').click();
  await expect(page.locator('#currentNumber')).toHaveText('B7');
  await page.locator('#currentNumber').click();
  await expect(page.locator('#currentNumber')).toHaveText('O75');

  const called = page.locator('td.cell.called');
  await expect(called).toHaveText(['7', '75']);
  await expect(page.locator('tr[data-letter="B"] td.cell.called')).toHaveText(['7']);
  await expect(page.locator('tr[data-letter="O"] td.cell.called')).toHaveText(['75']);
});

test('the latest call is highlighted as last', async ({ page }) => {
  await mockApi(page, ['N31', 'G50']);
  await page.goto('/');
  await page.locator('#currentNumber').click();
  await page.locator('#currentNumber').click();
  await expect(page.locator('td.cell.last')).toHaveText(['50']);
});

test('each row shows how many numbers have been called for its letter', async ({ page }) => {
  await mockApi(page, ['I16', 'I30', 'G50']);
  await page.goto('/');
  for (let k = 0; k < 3; k++) await page.locator('#currentNumber').click();
  await expect(page.locator('#currentNumber')).toHaveText('G50');
  await expect(page.locator('tr[data-letter] .count')).toHaveText(['0', '2', '0', '1', '0']);
});

test('called cells are announced as called to assistive tech', async ({ page }) => {
  await mockApi(page, ['B7']);
  await page.goto('/');
  await page.locator('#currentNumber').click();
  await expect(page.locator('tr[data-letter="B"] td.cell').nth(6)).toHaveAttribute('aria-label', 'B7 called');
  await expect(page.locator('tr[data-letter="B"] td.cell').nth(5)).toHaveAttribute('aria-label', 'B6');
});

test('reset clears the board', async ({ page }) => {
  await mockApi(page, ['B7']);
  await page.goto('/');
  await page.locator('#currentNumber').click();
  await expect(page.locator('td.cell.called')).toHaveCount(1);
  page.once('dialog', d => d.accept());
  await page.getByRole('button', { name: /Reset game/ }).click();
  await expect(page.locator('td.cell.called')).toHaveCount(0);
  await expect(page.locator('tr[data-letter] .count')).toHaveText(['0', '0', '0', '0', '0']);
});
