import AxeBuilder from '@axe-core/playwright';
import { expect, Page, Route, test } from '@playwright/test';

/*
 * The recipe editor in a browser (PP4.13, PNFR-007), with the gateway and the API stood in for at the network: making a
 * recipe by keyboard, saving every pause, keeping what was typed through a lost connection and a reload, a recipe changed
 * somewhere else, one the caller may only read, and axe with contrast on in both modes. What the server keeps and
 * refuses is proven in its own tests; here only what the screen sends and does with each answer is.
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
  allergens: [{ code: 'milk', name: 'Milk' }],
};

const PIE_ID = '3c1d6e0a-4b5f-4e2a-9d8c-7b6a5f4e3d2c';
const NEW_ID = 'b0f2c1e4-0000-4000-8000-000000000001';
const LIBRARY = '/dashboard/sites/apple-pie/recipes';

type Recipe = Record<string, unknown> & { id: string; version: string; canEdit: boolean };

function recipe(id: string, content: Record<string, unknown>, extra: Partial<Recipe> = {}): Recipe {
  return {
    summary: null,
    yield: null,
    yieldUnit: null,
    prepMinutes: null,
    cookMinutes: null,
    additionalMinutes: null,
    additionalTimeLabel: null,
    videoUrl: null,
    nutrition: null,
    ingredientGroups: [],
    stepGroups: [],
    equipment: [],
    notes: [],
    keywords: [],
    courses: [],
    cuisines: [],
    diets: [],
    allergens: [],
    ...content,
    id,
    version: 'v1',
    createdAt: '2026-11-01T12:00:00Z',
    updatedAt: '2026-11-14T18:30:00Z',
    archivedAt: null,
    canEdit: true,
    ...extra,
  };
}

/** Answers the admin surface for one browser, keeping recipes the way the API would, versions and replays included. */
async function standIn(page: Page) {
  const recipes = new Map<string, Recipe>([
    [
      PIE_ID,
      recipe(PIE_ID, {
        title: 'Apple pie',
        summary: 'A deep-dish pie.',
        prepMinutes: 30,
        cookMinutes: 75,
        yield: { kind: 'amount', low: { numerator: 8, denominator: 1 }, high: null },
        yieldUnit: 'servings',
        courses: ['dessert'],
      }),
    ],
    ['r2', recipe('r2', { title: 'Plum tart' }, { canEdit: false })],
  ]);
  const replays = new Map<string, Recipe>();
  const saves: { method: string; key: string | null; body: Record<string, unknown> }[] = [];
  const state = { offline: false, versions: 1 };

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
    const method = request.method();
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
    if (path === 'sites/apple-pie/recipes' && method === 'GET') {
      const items = [...recipes.values()].map((r) => ({
        id: r.id,
        title: r.title,
        updatedAt: r.updatedAt,
        archivedAt: null,
        canEdit: r.canEdit,
      }));
      return json(route, 200, { items, nextCursor: null });
    }
    if ((method === 'POST' || method === 'PUT') && state.offline) {
      return route.abort('internetdisconnected');
    }
    const key = request.headers()['idempotency-key'] ?? null;
    if (path === 'sites/apple-pie/recipes' && method === 'POST') {
      const body = request.postDataJSON() as Record<string, unknown>;
      saves.push({ method, key, body });
      const made =
        replays.get(key ?? '') ?? recipe(NEW_ID, body, { version: `v${++state.versions}` });
      replays.set(key ?? '', made);
      recipes.set(made.id, made);
      return json(route, 201, made);
    }
    const one = /^sites\/apple-pie\/recipes\/([\w-]+)$/.exec(path);
    if (one) {
      const current = recipes.get(one[1]);
      if (!current) {
        return json(route, 404, { status: 404, code: 'recipe-not-found', title: 'No recipe.' });
      }
      if (method === 'GET') {
        return json(route, 200, current);
      }
      if (method === 'PUT') {
        const body = request.postDataJSON() as {
          version: string;
          content: Record<string, unknown>;
        };
        saves.push({ method, key, body });
        const replay = replays.get(key ?? '');
        if (replay) {
          return json(route, 200, replay);
        }
        if (body.version !== current.version) {
          return json(route, 409, {
            status: 409,
            code: 'recipe-changed',
            title: 'This recipe changed since you opened it.',
          });
        }
        const next = { ...current, ...body.content, version: `v${++state.versions}` };
        recipes.set(next.id, next);
        replays.set(key ?? '', next);
        return json(route, 200, next);
      }
    }
    return json(route, 404, { status: 404, code: 'not-found', title: "We couldn't find that." });
  });

  return {
    recipes,
    saves,
    state,
    /** Someone else saves the recipe, moving its version. */
    changeElsewhere(id: string, content: Record<string, unknown>) {
      const current = recipes.get(id)!;
      recipes.set(id, { ...current, ...content, version: `v${++state.versions}` });
    },
  };
}

const status = (page: Page) => page.locator('pp-save-status .words');

async function violations(page: Page): Promise<string[]> {
  const results = await new AxeBuilder({ page }).withTags(WCAG).analyze();
  return results.violations.map(
    (v) => `${v.id}: ${v.nodes.map((node) => node.target.join(' ')).join(', ')}`,
  );
}

test('makes a recipe by keyboard: it saves with its title, gets its address, and keeps focus', async ({
  page,
}) => {
  const api = await standIn(page);
  await page.goto(LIBRARY);
  await page.getByRole('link', { name: 'New recipe' }).first().click();
  await expect(page.getByRole('heading', { level: 1, name: 'New recipe' })).toBeVisible();
  await expect(page).toHaveURL(`${LIBRARY}/new`);

  const title = page.getByRole('textbox', { name: 'Title' });
  await title.focus();
  await page.keyboard.type('Plum tart');
  await expect(status(page)).toHaveText('Saved');
  await expect(page).toHaveURL(`${LIBRARY}/${NEW_ID}`);
  // The address changed under the person typing; focus stayed in the box.
  await expect(title).toBeFocused();
  await page.keyboard.type(', rustic');
  await expect(status(page)).toHaveText('Saved');
  expect(api.saves.map((s) => s.method)).toEqual(['POST', 'PUT']);
  expect(api.recipes.get(NEW_ID)?.['title']).toBe('Plum tart, rustic');

  await page.reload();
  await expect(page.getByRole('textbox', { name: 'Title' })).toHaveValue('Plum tart, rustic');
});

test('saves each pause in times and yield, within two seconds', async ({ page }) => {
  const api = await standIn(page);
  await page.goto(`${LIBRARY}/${PIE_ID}`);
  await expect(page.getByRole('heading', { level: 1, name: 'Apple pie' })).toBeVisible();
  await expect(page.getByText('Total time:')).toHaveText('Total time: 1 hr 45 min');

  const additional = page.getByRole('group', { name: 'Additional time' });
  await additional.getByRole('textbox', { name: 'Minutes' }).fill('45');
  await page.getByRole('textbox', { name: "What it's for" }).fill('Cooling');
  await page.getByRole('textbox', { name: 'Up to' }).fill('10');
  const started = Date.now();
  await expect(status(page)).toHaveText('Saved', { timeout: 5000 });
  expect(Date.now() - started).toBeLessThan(4000);
  const saved = api.recipes.get(PIE_ID)!;
  expect([saved['additionalMinutes'], saved['additionalTimeLabel']]).toEqual([45, 'Cooling']);
  expect(saved['yield']).toEqual({
    kind: 'range',
    low: { numerator: 8, denominator: 1 },
    high: { numerator: 10, denominator: 1 },
  });
  await expect(page.getByText('Total time:')).toHaveText('Total time: 2 hr 30 min');
});

test('keeps what was typed through a lost connection and a reload, then saves it', async ({
  page,
}) => {
  const api = await standIn(page);
  await page.goto(`${LIBRARY}/${PIE_ID}`);
  await expect(status(page)).toHaveText('Saved');

  api.state.offline = true;
  await page.getByRole('textbox', { name: 'Summary' }).fill('Typed with no connection.');
  await expect(status(page)).toHaveText('Not saved yet. Trying again…');
  await expect(page.locator('pp-save-status .detail')).toHaveText(
    'Your changes are kept on this device.',
  );

  await page.reload();
  await expect(page.getByRole('textbox', { name: 'Summary' })).toHaveValue(
    'Typed with no connection.',
  );

  api.state.offline = false;
  await expect(status(page)).toHaveText('Saved', { timeout: 40_000 });
  expect(api.recipes.get(PIE_ID)?.['summary']).toBe('Typed with no connection.');
  // A save that may have landed went again under its own key.
  const keys = api.saves.map((s) => s.key);
  expect(new Set(keys).size).toBe(1);
});

test('stops on a recipe changed somewhere else, and keeps my changes over the newer copy', async ({
  page,
}) => {
  const api = await standIn(page);
  await page.goto(`${LIBRARY}/${PIE_ID}`);
  await expect(status(page)).toHaveText('Saved');
  api.changeElsewhere(PIE_ID, { summary: 'Their summary' });

  await page.getByRole('textbox', { name: 'Title' }).fill('My apple pie');
  await expect(
    page.getByRole('main').getByText('This recipe was changed somewhere else'),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Keep my changes' }).click();
  await expect(status(page)).toHaveText('Saved');
  const saved = api.recipes.get(PIE_ID)!;
  expect([saved['title'], saved['summary']]).toEqual(['My apple pie', 'Their summary']);
  await expect(page.getByRole('textbox', { name: 'Summary' })).toHaveValue('Their summary');
});

test('shows a recipe the caller may not change, with nothing to type in', async ({ page }) => {
  await standIn(page);
  await page.goto(LIBRARY);
  await page.getByRole('link', { name: 'Plum tart' }).click();
  await expect(page.getByRole('main').getByText('You can view this recipe')).toBeVisible();
  // Read-only, never disabled: the words stay reachable, readable in full, and copyable.
  await expect(page.getByRole('textbox', { name: 'Title' })).not.toBeEditable();
  await expect(page.getByRole('textbox', { name: 'Title' })).toBeEnabled();
  await expect(page.locator('pp-save-status')).toHaveCount(0);
});

for (const mode of ['light', 'dark'] as const) {
  test(`the editor's warning and error states have no accessibility violations in ${mode} mode`, async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme: mode });
    const api = await standIn(page);
    await page.goto(`${LIBRARY}/${PIE_ID}`);
    await expect(status(page)).toHaveText('Saved');

    // Errors this screen marks itself, at a time and the yield.
    await page.getByRole('textbox', { name: 'Cook time, hours' }).fill('one');
    await page.getByRole('textbox', { name: 'Makes' }).fill('eight');
    await expect(status(page)).toHaveText('Not saved');
    expect(await violations(page)).toEqual([]);
    await page.getByRole('textbox', { name: 'Cook time, hours' }).fill('1');
    await page.getByRole('textbox', { name: 'Makes' }).fill('8');
    await expect(status(page)).toHaveText('Saved');

    // Trying again with no connection.
    api.state.offline = true;
    await page.getByRole('textbox', { name: 'Summary' }).fill('Offline');
    await expect(status(page)).toHaveText('Not saved yet. Trying again…');
    expect(await violations(page)).toEqual([]);
    api.state.offline = false;
    await expect(status(page)).toHaveText('Saved', { timeout: 40_000 });

    // Changed somewhere else.
    api.changeElsewhere(PIE_ID, { summary: 'Theirs' });
    await page.getByRole('textbox', { name: 'Title' }).fill('Mine');
    await expect(
      page.getByRole('main').getByText('This recipe was changed somewhere else'),
    ).toBeVisible();
    expect(await violations(page)).toEqual([]);

    // One the caller may only read.
    await page.goto(`${LIBRARY}/r2`);
    await expect(page.getByRole('main').getByText('You can view this recipe')).toBeVisible();
    expect(await violations(page)).toEqual([]);
  });

  test(`the editor has no accessibility violations in ${mode} mode`, async ({ page }) => {
    await page.emulateMedia({ colorScheme: mode });
    await standIn(page);
    await page.goto(`${LIBRARY}/${PIE_ID}`);
    await expect(page.getByRole('textbox', { name: 'Title' })).toHaveValue('Apple pie');
    await page.getByText('More details').click();
    await page.getByRole('textbox', { name: 'Summary' }).fill('Changed');
    await expect(status(page)).toHaveText('Saved');
    expect(await violations(page)).toEqual([]);

    await page.goto(`${LIBRARY}/new`);
    await expect(page.getByRole('heading', { level: 1, name: 'New recipe' })).toBeVisible();
    expect(await violations(page)).toEqual([]);
  });
}
