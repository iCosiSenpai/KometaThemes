// Automated WCAG 2.1 A/AA checks with axe on every view, in Jellyfin's Dark and Light themes.
const { test, expect } = require('@playwright/test');
const AxeBuilder = require('@axe-core/playwright').default;

const KAGUYA = 'a0000000-0000-0000-0000-000000000001';

async function open(page, query, reset) {
  await page.request.get('/__reset' + (reset ? '?' + reset : ''));
  await page.goto('/web/host.html' + (query ? '?' + query : ''));
  await page.waitForFunction(() => window.__ktReady === true);
  await expect(page.locator('#ktRoot')).not.toHaveAttribute('aria-busy', 'true');
}

async function audit(page) {
  const results = await new AxeBuilder({ page })
    .include('#KometaThemesPage')
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze();
  const summary = results.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).slice(0, 3).join(' | ')}`);
  expect(summary).toEqual([]);
}

for (const theme of ['dark', 'light']) {
  test.describe(`${theme} theme`, () => {
    const prefix = theme === 'light' ? 'theme=light&' : '';

    test('library', async ({ page }) => {
      await open(page, prefix);
      await expect(page.locator('.kt-row')).toHaveCount(24);
      await audit(page);
    });

    test('anime page', async ({ page }) => {
      await open(page, `${prefix}item=${KAGUYA}`);
      await expect(page.locator('.kt-track')).toHaveCount(3);
      await page.locator('details.kt-fold').first().locator('summary').click();
      await audit(page);
    });

    test('match dialog', async ({ page }) => {
      await open(page, `${prefix}item=${KAGUYA}`);
      await page.getByRole('button', { name: 'Change match' }).click();
      await expect(page.locator('.kt-result')).not.toHaveCount(0);
      await audit(page);
    });

    test('settings', async ({ page }) => {
      await open(page, prefix);
      await page.getByRole('tab', { name: 'Settings' }).click();
      await page.locator('details.kt-fold summary').click();
      await audit(page);
    });

    test('setup', async ({ page }) => {
      await open(page, prefix, 'setup=1');
      await expect(page.getByRole('heading', { name: 'Set up KometaThemes' })).toBeVisible();
      await audit(page);
    });
  });
}
