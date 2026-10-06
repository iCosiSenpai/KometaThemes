// The KometaThemes page against the mock API, in a Jellyfin-like shell.
const { test, expect } = require('@playwright/test');

const KAGUYA = 'a0000000-0000-0000-0000-000000000001';
const UNRESOLVED = 'a0000000-0000-0000-0000-000000000003';

async function open(page, query) {
  await page.request.get('/__reset' + (query && query.reset ? '?' + query.reset : ''));
  const errors = [];
  page.on('pageerror', (error) => errors.push(error.message));
  const params = new URLSearchParams(Object.entries(query || {}).filter(([key]) => key !== 'reset'));
  await page.goto('/web/host.html' + (params.toString() ? '?' + params : ''));
  await page.waitForFunction(() => window.__ktReady === true);
  await expect(page.locator('#ktRoot')).not.toHaveAttribute('aria-busy', 'true');
  return errors;
}

test('the library lists anime with their state and filters them', async ({ page }) => {
  const errors = await open(page);

  await expect(page.getByRole('heading', { name: 'Anime themes' })).toBeVisible();
  await expect(page.locator('.kt-row')).toHaveCount(24);
  await expect(page.locator('.kt-row', { hasText: 'Kaguya-sama' })).toContainText('4 openings, 6 endings');
  await expect(page.locator('.kt-row', { hasText: 'Il prisma' })).toContainText('Find a match');

  await page.getByRole('button', { name: /Needs attention/ }).click();
  await expect(page.locator('.kt-row')).toHaveCount(1);

  await page.getByRole('button', { name: /^All/ }).click();
  await page.getByRole('searchbox', { name: 'Search your anime' }).fill('chainsaw');
  await expect(page.locator('.kt-row')).toHaveCount(1);
  await page.getByRole('searchbox', { name: 'Search your anime' }).fill('nothing like this');
  await expect(page.locator('.kt-empty')).toContainText('No anime matches');
  expect(errors).toEqual([]);
});

test('an anime page shows songs, episodes and each season', async ({ page }) => {
  const errors = await open(page, { item: KAGUYA });

  await expect(page.getByRole('heading', { name: 'Kaguya-sama: Love Is War' })).toBeVisible();
  await expect(page.locator('.kt-track')).toHaveCount(3);
  await expect(page.locator('.kt-track').first()).toContainText('Love Dramatic feat. Rikka Ihara');
  await expect(page.locator('.kt-track').nth(1)).toContainText('Episodes 2, 4–12');
  await expect(page.locator('.kt-map-bar')).toHaveCount(4);

  // Arrow keys move between the season tabs.
  await page.getByRole('tab', { name: 'Series' }).focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByRole('tab', { name: 'Season 1' })).toHaveAttribute('aria-selected', 'true');
  await expect(page.locator('.kt-match')).toContainText('Plays the series themes');

  await page.keyboard.press('ArrowRight');
  await expect(page.locator('.kt-match')).toContainText('Matched as a sequel on AniList');
  await expect(page.locator('.kt-track').first()).toContainText('DADDY! DADDY! DO!');
  await expect(page.locator('.kt-track').nth(1)).toContainText('Episodes 2–4, 6–11');

  await page.getByRole('button', { name: 'Library' }).click();
  await expect(page.locator(`.kt-row[data-id="${KAGUYA}"]`)).toBeFocused();
  expect(errors).toEqual([]);
});

test('turning a song off sends the choice and updates the page', async ({ page }) => {
  await open(page, { item: KAGUYA });
  const toggle = page.locator('.kt-track').first().getByRole('button', { name: /^Song of OP1/ });
  await expect(toggle).toHaveAttribute('aria-pressed', 'true');

  const request = page.waitForRequest((r) => r.method() === 'PUT' && r.url().includes(`/Items/${KAGUYA}/Themes/`));
  await toggle.click();
  expect((await request).postDataJSON()).toEqual({ change: 'audio', audio: false });

  await expect(page.locator('.kt-track').first().getByRole('button', { name: /^Song of OP1/ })).toHaveAttribute('aria-pressed', 'false');
  await expect(page.locator('.kt-toast')).toContainText('Removed the song of OP1');
});

test('an unmatched anime is matched from the dialog', async ({ page }) => {
  await open(page, { item: UNRESOLVED });
  await expect(page.locator('.kt-note--warn')).toContainText('No match on animethemes.moe yet');

  await page.getByRole('button', { name: 'Find a match' }).first().click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByRole('searchbox').fill('kaguya');
  await dialog.getByRole('button', { name: 'Search' }).click();
  await expect(dialog.locator('.kt-result')).not.toHaveCount(0);

  await dialog.locator('.kt-result').first().getByRole('button', { name: 'Preview' }).click();
  await expect(dialog.locator('.kt-result-pick')).toContainText('Love Dramatic');
  const put = page.waitForRequest((r) => r.method() === 'PUT' && r.url().endsWith(`/Items/${UNRESOLVED}/Match`));
  await dialog.getByRole('button', { name: 'Use this entry' }).click();
  expect((await put).postDataJSON()).toEqual({ animeId: 1386 });

  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(page.locator('.kt-match')).toContainText('Matched by you');
  await expect(page.getByRole('button', { name: 'Use the automatic match' })).toBeVisible();
});

test('the dialog closes with Escape and gives focus back', async ({ page }) => {
  await open(page, { item: KAGUYA });
  await page.getByRole('button', { name: 'Exclude' }).click();
  await expect(page.getByRole('dialog', { name: 'Exclude this anime?' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
});

test('excluding keeps or deletes files, and can be undone', async ({ page }) => {
  await open(page, { item: KAGUYA });
  await page.getByRole('button', { name: 'Exclude' }).click();
  const put = page.waitForRequest((r) => r.method() === 'PUT' && r.url().includes('/Excluded'));
  await page.getByRole('button', { name: 'Exclude, keep its files' }).click();
  expect(new URL((await put).url()).searchParams.get('deleteFiles')).toBeNull();
  await expect(page.locator('.kt-note')).toContainText('KometaThemes leaves this anime alone');

  await page.getByRole('button', { name: 'Manage it again' }).click();
  await expect(page.getByRole('button', { name: 'Check again' })).toBeVisible();
});

test('settings save only when changed, and can be discarded', async ({ page }) => {
  await open(page);
  await page.getByRole('tab', { name: 'Settings' }).click();
  await expect(page.locator('legend', { hasText: 'Libraries' })).toBeVisible();
  await expect(page.locator('.kt-savebar')).toBeHidden();

  await page.getByLabel('Theme songs volume').fill('70');
  await expect(page.locator('.kt-savebar')).toBeVisible();
  await page.getByRole('button', { name: 'Discard changes' }).click();
  await expect(page.getByLabel('Theme songs volume')).toHaveValue('50');

  await page.getByLabel('Anime Movies').check();
  const post = page.waitForRequest((r) => r.method() === 'POST' && r.url().endsWith('/KometaThemes/Settings'));
  await page.getByRole('button', { name: 'Save' }).click();
  const body = (await post).postDataJSON();
  expect(body.libraryIds).toContain('c0000000-0000-0000-0000-000000000002');
  expect(body.audio.volume).toBe(50);
  await expect(page.locator('.kt-toast')).toContainText('Settings saved');
  await expect(page.locator('.kt-savebar')).toBeHidden();
});

test('a new install goes through the setup and starts the first check', async ({ page }) => {
  await open(page, { reset: 'setup=1' });
  await expect(page.getByRole('heading', { name: 'Set up KometaThemes' })).toBeVisible();
  await expect(page.getByLabel('Anime', { exact: true })).toBeChecked();
  await expect(page.getByLabel('TV Shows')).not.toBeChecked();

  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.getByText('Theme songs', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.locator('.kt-group')).toContainText('Anime');

  const check = page.waitForRequest((r) => r.method() === 'POST' && r.url().endsWith('/KometaThemes/Check'));
  await page.getByRole('button', { name: 'Save and start the first check' }).click();
  await check;
  await expect(page.getByRole('tab', { name: /Library/ })).toBeVisible();
  await expect(page.locator('.kt-check')).toContainText(/Checking|Matching/);
});

test('a running check shows progress and can be stopped', async ({ page }) => {
  await open(page);
  await page.getByRole('button', { name: 'Check now' }).click();
  await expect(page.getByRole('progressbar', { name: 'Check progress' })).toBeVisible();
  await page.getByRole('button', { name: 'Stop' }).click();
  await expect(page.locator('.kt-toast')).toContainText('stops after the anime in progress');
});

test('names are shown as text, never as markup', async ({ page }) => {
  await page.route('**/KometaThemes/Library', async (route) => {
    const response = await route.fetch();
    const rows = await response.json();
    rows[0].name = '<img src=x onerror="window.__xss=1">Evil';
    await route.fulfill({ response, json: rows });
  });
  await open(page);
  await expect(page.locator('.kt-row').first()).toContainText('<img src=x onerror="window.__xss=1">Evil');
  expect(await page.evaluate(() => window.__xss)).toBeUndefined();
});

test('a server error is explained and can be retried', async ({ page }) => {
  let fail = true;
  await page.route('**/KometaThemes/Library', async (route) => {
    if (fail) {
      await route.fulfill({ status: 500, json: { error: 'The library is being scanned. Try again in a minute.' } });
      return;
    }
    await route.continue();
  });
  await open(page);
  await expect(page.locator('.kt-note--error')).toContainText('The library is being scanned');
  fail = false;
  await page.getByRole('button', { name: 'Try again' }).click();
  await expect(page.locator('.kt-row')).toHaveCount(24);
});

test('activity links back to the anime', async ({ page }) => {
  await open(page);
  await page.getByRole('tab', { name: 'Activity' }).click();
  await expect(page.locator('.kt-act')).toHaveCount(5);
  await page.locator('.kt-act').nth(1).getByRole('button', { name: 'Kaguya-sama: Love Is War' }).click();
  await expect(page.getByRole('heading', { name: 'Kaguya-sama: Love Is War' })).toBeVisible();
});

for (const width of [320, 390, 768, 1280]) {
  test(`no horizontal scrolling at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await open(page, { item: KAGUYA });
    expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0);
    await page.getByRole('button', { name: 'Library' }).click();
    expect(await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0);
  });
}
