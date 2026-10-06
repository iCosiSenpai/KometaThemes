// The ♪ button the plugin adds to Jellyfin's item pages.
const { test, expect } = require('@playwright/test');

const KAGUYA = 'a0000000-0000-0000-0000-000000000001';

test.beforeEach(async ({ page }) => {
  await page.request.get('/__reset');
});

test('administrators get the button on a managed anime, and a panel with its songs', async ({ page }) => {
  await page.goto(`/web/item.html?id=${KAGUYA}`);
  const button = page.getByRole('button', { name: 'Theme songs' });
  await expect(button).toBeVisible();

  // Placed before "More", like Jellyfin's own buttons.
  const order = await page.locator('.mainDetailButtons button').evaluateAll((nodes) => nodes.map((n) => n.title));
  expect(order).toEqual(['Play', 'Theme songs', 'More']);

  await button.click();
  const panel = page.getByRole('dialog', { name: 'Theme songs' });
  await expect(panel).toContainText('Love Dramatic feat. Rikka Ihara');
  await expect(panel.getByRole('link', { name: 'Manage themes' })).toHaveAttribute('href', `#/configurationpage?name=KometaThemes&item=${KAGUYA}`);

  await page.keyboard.press('Escape');
  await expect(panel).toHaveCount(0);
  await expect(button).toBeFocused();
});

test('no button for anime outside the managed libraries', async ({ page }) => {
  await page.route('**/Summary', (route) => route.fulfill({ json: { managed: false } }));
  await page.goto(`/web/item.html?id=${KAGUYA}`);
  await page.waitForTimeout(400);
  await expect(page.getByRole('button', { name: 'Theme songs' })).toHaveCount(0);
});

test('no button for users who are not administrators', async ({ page }) => {
  await page.goto(`/web/item.html?id=${KAGUYA}&admin=0`);
  await page.waitForTimeout(400);
  await expect(page.getByRole('button', { name: 'Theme songs' })).toHaveCount(0);
});
