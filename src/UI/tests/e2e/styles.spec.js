const { test, expect } = require('@playwright/test');

// Both pages share one stylesheet and one self-hosted, high-legibility font.
// The API is stubbed out: these tests only look at how the pages are styled.
const PAGES = ['/', '/watch.html'];
const FONT = 'Atkinson Hyperlegible Next';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('bingoSound', 'off'));
  await page.route('http://localhost:5001/**', r => r.fulfill({ status: 404, body: '' }));
});

for (const path of PAGES) {
  test.describe(`${path}`, () => {
    test('links the shared stylesheet and has no inline <style> block', async ({ page }) => {
      await page.goto(path);
      const sheets = await page.evaluate(() =>
        [...document.querySelectorAll('link[rel="stylesheet"]')].map(l => new URL(l.href).pathname));
      expect(sheets).toEqual(['/styles.css']);
      await expect(page.locator('style')).toHaveCount(0);
    });

    test('renders text in the self-hosted readable font', async ({ page }) => {
      const fontRequests = [];
      page.on('requestfinished', r => { if (r.resourceType() === 'font') fontRequests.push(new URL(r.url()).pathname); });
      await page.goto(path);
      const family = await page.evaluate(() => getComputedStyle(document.body).fontFamily);
      expect(family.split(',')[0].replace(/["']/g, '').trim()).toBe(FONT);
      const loaded = await page.evaluate(async (f) => {
        await document.fonts.ready;
        return document.fonts.check(`16px "${f}"`) &&
          [...document.fonts].some(ff => ff.family.replace(/["']/g, '') === f && ff.status === 'loaded');
      }, FONT);
      expect(loaded).toBe(true);
      expect(fontRequests).toEqual(['/vendor/fonts/atkinson-hyperlegible-next.woff2']);
    });

    // The font's tabular figures are a thin slab-style set; its default digits read better.
    // Board numbers sit centred in fixed-width cells, so they don't need equal-width digits.
    test('shows board numbers in the font\'s default, most legible digits', async ({ page }) => {
      await page.goto(path);
      const numeric = await page.evaluate(() => {
        // watch.html renders no board until it has joined a game, so build a cell here
        const table = document.createElement('table');
        table.className = 'board';
        table.innerHTML = '<tr><th><span class="count">0</span></th><td class="cell">88</td></tr>';
        document.body.appendChild(table);
        const v = ['td.cell', 'th .count'].map(sel => getComputedStyle(table.querySelector(sel)).fontVariantNumeric);
        table.remove();
        return v;
      });
      expect(numeric).toEqual(['normal', 'normal']);
    });

    test('keeps the countdown on equal-width digits so it does not jitter', async ({ page }) => {
      await page.goto(path);
      const numeric = await page.evaluate(() => {
        const el = document.createElement('div');
        el.className = 'countdown';
        el.textContent = 'Next ball in 1s';
        document.body.appendChild(el);
        const v = getComputedStyle(el).fontVariantNumeric;
        el.remove();
        return v;
      });
      expect(numeric).toBe('tabular-nums');
    });
  });
}
