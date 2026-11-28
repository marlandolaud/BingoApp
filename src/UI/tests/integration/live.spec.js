const { test, expect } = require('@playwright/test');

const contexts = [];
test.afterEach(async () => {
  await Promise.all(contexts.splice(0).map(c => c.close().catch(() => {})));
});

// Dealer + viewer against the real API and a real Postgres (see scripts/integration-api.sh).
async function newPage(browser) {
  const ctx = await browser.newContext();
  contexts.push(ctx);
  const page = await ctx.newPage();
  await page.addInitScript(() => localStorage.setItem('bingoSound', 'off'));
  return { ctx, page };
}

async function draw(page, times) {
  for (let k = 0; k < times; k++) {
    const resp = page.waitForResponse(r => r.url().includes('/Get?'));
    await page.locator('#currentNumber').click();
    await resp;
  }
}

async function startDealer(browser) {
  const dealer = await newPage(browser);
  await dealer.page.goto('/');
  await draw(dealer.page, 1);
  await expect(dealer.page.locator('#qr')).toHaveAttribute('data-url', /watch\.html\?join=/);
  const joinUrl = await dealer.page.locator('#qr').getAttribute('data-url');
  return { ...dealer, joinUrl };
}

async function startViewer(browser, joinUrl) {
  const viewer = await newPage(browser);
  await viewer.page.goto(joinUrl);
  await expect(viewer.page).toHaveURL(/watch\.html\?v=/);
  return viewer;
}

test('viewer board follows dealer draws', async ({ browser }) => {
  const dealer = await startDealer(browser);
  const viewer = await startViewer(browser, dealer.joinUrl);
  await draw(dealer.page, 3);
  await expect(viewer.page.locator('td.cell.called')).toHaveCount(4, { timeout: 10000 });
  const current = await dealer.page.locator('#currentNumber').innerText();
  await expect(viewer.page.locator('#currentNumber')).toHaveText(current, { timeout: 10000 });
});

test('reset clears viewer board without reload', async ({ browser }) => {
  const dealer = await startDealer(browser);
  const viewer = await startViewer(browser, dealer.joinUrl);
  await draw(dealer.page, 2);
  await expect(viewer.page.locator('td.cell.called')).toHaveCount(3, { timeout: 10000 });
  const url = viewer.page.url();
  dealer.page.once('dialog', d => d.accept());
  await dealer.page.locator('#resetBtn').click();
  await expect(viewer.page.locator('td.cell.called')).toHaveCount(0, { timeout: 10000 });
  expect(viewer.page.url()).toBe(url);
});

test('dealer refresh keeps the game', async ({ browser }) => {
  const dealer = await newPage(browser);
  await dealer.page.goto('/');
  await draw(dealer.page, 3);
  await expect(dealer.page).toHaveURL(/\?game=/);
  const url = dealer.page.url();
  await dealer.page.reload();
  await expect(dealer.page.locator('td.cell.called')).toHaveCount(3);
  expect(dealer.page.url()).toBe(url);
});

test('viewer count goes up when a viewer joins', async ({ browser }) => {
  const dealer = await startDealer(browser);
  const viewer = await startViewer(browser, dealer.joinUrl);
  await expect(dealer.page.locator('#viewerCount')).toHaveText('👁 1 watching', { timeout: 10000 });
  await viewer.ctx.close();
  await expect(dealer.page.locator('#viewerCount')).toHaveText('👁 0 watching', { timeout: 10000 });
});

test('auto pilot draws on its own and both screens count down', async ({ browser }) => {
  const dealer = await startDealer(browser);
  const viewer = await startViewer(browser, dealer.joinUrl);
  const called = viewer.page.locator('td.cell.called');
  await expect(called).toHaveCount(1, { timeout: 10000 });
  await dealer.page.locator('#autoPilotBtn').click();
  await expect(dealer.page.locator('#countdown')).toHaveText(/Next ball in [12]s/, { timeout: 10000 });
  await expect(viewer.page.locator('#countdown')).toHaveText(/Next ball in [12]s/, { timeout: 10000 });
  await expect.poll(() => called.count(), { timeout: 7000 }).toBeGreaterThanOrEqual(3);
  await dealer.page.locator('#autoPilotBtn').click();
  await expect(dealer.page.locator('#autoPilotBtn')).toContainText('Auto pilot');
  await dealer.page.waitForTimeout(1000);
  const n = await called.count();
  await viewer.page.waitForTimeout(5000);
  expect(await called.count()).toBe(n);
});

test('two dealer tabs on auto pilot draw each tick once', async ({ browser }) => {
  const a = await startDealer(browser);
  const gameUrl = a.page.url();
  const b = await newPage(browser);
  await b.page.goto(gameUrl);
  await expect(b.page.locator('td.cell.called')).toHaveCount(1, { timeout: 10000 });

  // due -> numberOfDrew reported by each tab's auto draw
  const byDue = new Map();
  for (const tab of [a, b]) {
    tab.page.on('response', async (r) => {
      const due = new URL(r.url()).searchParams.get('due');
      if (!r.url().includes('/Get?') || !due) return;
      const body = await r.json().catch(() => null);
      if (!body || typeof body !== 'object') return;
      byDue.set(due, [...(byDue.get(due) || []), body.numberOfDrew]);
    });
  }
  await a.page.locator('#autoPilotBtn').click();
  await expect.poll(() => [...byDue.values()].filter(v => v.length === 2).length, { timeout: 15000 })
    .toBeGreaterThanOrEqual(2);
  await a.page.locator('#autoPilotBtn').click();
  await expect(a.page.locator('#autoPilotBtn')).toContainText('Auto pilot');
  await a.page.waitForTimeout(1000);

  // both tabs fired at the same tick, yet the server drew one ball for it
  for (const [, counts] of byDue) if (counts.length === 2) expect(counts[0]).toBe(counts[1]);
  const balls = await a.page.locator('td.cell.called').count();
  expect(balls).toBeLessThanOrEqual(1 + byDue.size);
});
