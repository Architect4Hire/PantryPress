import AxeBuilder from '@axe-core/playwright';
import { expect, Page, Route, test } from '@playwright/test';

/*
 * The recipe library in a browser (PP4.12b), with the gateway and the API stood in for at the network: searching and
 * filtering by keyboard, archiving with undo, restoring from the Archived tab, and axe with contrast on in both modes.
 * What the server finds for a search and who may archive what are the API's, proven in its tests; here only what the
 * screen asks for and does with each answer is.
 */

const WCAG = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];
const VIEW = {
  stage: 'complete',
  accountId: '7f1f5c9a-1d2b-4c3d-9e4f-5a6b7c8d9e0f',
  email: 'cook@example.test',
  emailVerified: true,
  secondFactorVerified: false,
};
const APPLE = {
  slug: 'apple-pie',
  name: 'Apple Pie Club',
  status: 'live',
  role: 'editor',
  host: 'apple-pie.pantrypress.app',
  tagline: '',
  timeZone: 'America/Chicago',
  locale: 'en-US',
  createdAt: '2026-10-01T12:00:00Z',
};
const VOCABULARY = {
  courses: [
    { code: 'dessert', name: 'Dessert' },
    { code: 'bread', name: 'Bread' },
  ],
  cuisines: [{ code: 'american', name: 'American' }],
  diets: [{ code: 'vegan', name: 'Vegan' }],
  allergens: [],
};

interface Recipe {
  id: string;
  title: string;
  course: string;
  archived: boolean;
  canEdit: boolean;
}

/** Answers the admin surface for one browser, with a recipe list it filters the way the API would. */
async function standIn(page: Page) {
  const recipes: Recipe[] = [
    { id: 'r1', title: 'Apple pie', course: 'dessert', archived: false, canEdit: true },
    { id: 'r2', title: 'Plum tart', course: 'dessert', archived: false, canEdit: false },
    { id: 'r3', title: 'Rye bread', course: 'bread', archived: false, canEdit: true },
  ];
  const requests: string[] = [];
  const json = (route: Route, status: number, body?: unknown) =>
    route.fulfill({
      status,
      contentType: status >= 400 ? 'application/problem+json' : 'application/json',
      body: body === undefined ? '' : JSON.stringify(body),
    });
  await page.route('**/api/v1/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname.replace('/api/v1/', '');
    requests.push(`${request.method()} ${path}${url.search}`);
    if (path === 'account/session') {
      return json(route, 200, VIEW);
    }
    if (path === 'sites') {
      return json(route, 200, { items: [APPLE], nextCursor: null });
    }
    if (path === 'sites/apple-pie') {
      return json(route, 200, APPLE);
    }
    if (path === 'sites/apple-pie/recipes/vocabulary') {
      return json(route, 200, VOCABULARY);
    }
    if (path === 'sites/apple-pie/recipes' && request.method() === 'GET') {
      const archived = url.searchParams.get('archived') === 'true';
      const search = (url.searchParams.get('search') ?? '').toLowerCase();
      const course = url.searchParams.get('course');
      const items = recipes
        .filter((r) => r.archived === archived)
        .filter((r) => r.title.toLowerCase().includes(search))
        .filter((r) => course === null || r.course === course)
        .map((r) => ({
          id: r.id,
          title: r.title,
          updatedAt: '2026-11-14T18:30:00Z',
          archivedAt: r.archived ? '2026-11-14T19:00:00Z' : null,
          canEdit: r.canEdit,
        }));
      return json(route, 200, { items, nextCursor: null });
    }
    const change = /^sites\/apple-pie\/recipes\/(\w+)\/(archive|restore)$/.exec(path);
    if (change && request.method() === 'POST') {
      const target = recipes.find((r) => r.id === change[1]);
      if (target) {
        target.archived = change[2] === 'archive';
      }
      return route.fulfill({ status: 204 });
    }
    return json(route, 404, { status: 404, code: 'not-found', title: "We couldn't find that." });
  });
  return { requests };
}

const rows = (page: Page) => page.locator('.recipe .title');

async function violations(page: Page): Promise<string[]> {
  const results = await new AxeBuilder({ page }).withTags(WCAG).analyze();
  return results.violations.map(
    (v) => `${v.id}: ${v.nodes.map((node) => node.target.join(' ')).join(', ')}`,
  );
}

test('searches and filters by keyboard, and the address keeps them', async ({ page }) => {
  const api = await standIn(page);
  await page.goto('/dashboard/sites/apple-pie/recipes');
  await expect(page.getByRole('heading', { level: 1, name: 'Recipes' })).toBeVisible();
  await expect(rows(page)).toHaveText(['Apple pie', 'Plum tart', 'Rye bread']);
  await expect(page.getByText('View only')).toHaveCount(1);

  await page.getByRole('combobox', { name: 'Course' }).selectOption('dessert');
  await expect(page).toHaveURL(/\?course=dessert$/);
  await expect(rows(page)).toHaveText(['Apple pie', 'Plum tart']);

  const search = page.getByRole('searchbox', { name: 'Search recipes' });
  await search.fill('plum');
  await search.press('Enter');
  await expect(page).toHaveURL(/\?course=dessert&q=plum$/);
  await expect(rows(page)).toHaveText(['Plum tart']);
  // A search is a move within the screen: focus stays in the box the reader typed in.
  await expect(search).toBeFocused();
  expect(api.requests).toContain('GET sites/apple-pie/recipes?limit=25&search=plum&course=dessert');

  await page.reload();
  await expect(rows(page)).toHaveText(['Plum tart']);
  await expect(page.getByRole('combobox', { name: 'Course' })).toHaveValue('dessert');

  await search.fill('nothing like it');
  await search.press('Enter');
  await expect(page.getByRole('heading', { name: 'No recipes match' })).toBeVisible();
  await page.getByRole('button', { name: 'Clear search and filters' }).click();
  await expect(page).toHaveURL(/\/recipes$/);
  await expect(rows(page)).toHaveText(['Apple pie', 'Plum tart', 'Rye bread']);
});

test('archives with undo, and restores from the Archived tab', async ({ page }) => {
  await standIn(page);
  await page.goto('/dashboard/sites/apple-pie/recipes');
  await page.getByRole('button', { name: 'Archive Apple pie' }).click();
  await expect(
    page.getByRole('region', { name: 'Notifications' }).getByText('“Apple pie” moved to Archived.'),
  ).toBeVisible();
  await expect(rows(page)).toHaveText(['Plum tart', 'Rye bread']);

  await page.getByRole('button', { name: 'Undo' }).click();
  await expect(rows(page)).toHaveText(['Apple pie', 'Plum tart', 'Rye bread']);

  await page.getByRole('button', { name: 'Archive Rye bread' }).click();
  await expect(rows(page)).toHaveText(['Apple pie', 'Plum tart']);
  const archived = page.getByRole('tab', { name: 'Archived' });
  await page.getByRole('tab', { name: 'Library' }).focus();
  await page.keyboard.press('ArrowRight');
  await expect(archived).toHaveAttribute('aria-selected', 'true');
  await expect(page).toHaveURL(/\?archived=true$/);
  await expect(rows(page)).toHaveText(['Rye bread']);
  // The tabs pattern holds across the address change: focus stays on the tab, and the arrows keep moving.
  await expect(archived).toBeFocused();
  await page.keyboard.press('ArrowLeft');
  await expect(page.getByRole('tab', { name: 'Library' })).toBeFocused();
  await expect(rows(page)).toHaveText(['Apple pie', 'Plum tart']);
  await page.keyboard.press('ArrowRight');
  await expect(archived).toBeFocused();
  await expect(rows(page)).toHaveText(['Rye bread']);
  await page.getByRole('button', { name: 'Restore Rye bread' }).click();
  await expect(page.getByRole('heading', { name: 'Nothing archived' })).toBeVisible();
});

for (const mode of ['light', 'dark'] as const) {
  test(`the library has no accessibility violations in ${mode} mode`, async ({ page }) => {
    await page.emulateMedia({ colorScheme: mode });
    await standIn(page);
    await page.goto('/dashboard/sites/apple-pie/recipes');
    await expect(rows(page)).toHaveCount(3);
    expect(await violations(page)).toEqual([]);
  });
}
