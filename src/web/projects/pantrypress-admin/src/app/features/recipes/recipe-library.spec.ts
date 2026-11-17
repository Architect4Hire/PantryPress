import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { PpToaster, PpToastOptions } from '@pantrypress/ui';
import axe from 'axe-core';
import { ApiResult } from '../../core/api/api-problem';
import { DocumentLoad } from '../../core/navigation/document-load';
import {
  RecipeListQuery,
  RecipesClient,
  RecipeSummary,
  RecipeSummaryPage,
  RecipeVocabulary,
} from '../../core/recipes/recipes-client';
import { AccountClient } from '../../core/session/account-client';
import { SessionClient, SessionView } from '../../core/session/session-client';
import { SiteDetail, SitesClient } from '../../core/sites/sites-client';
import dashboardRoutes from '../dashboard/dashboard.routes';
import { LIBRARY_PAGE } from './recipe-library';

@Component({ template: '<h1>Sign in</h1>' })
class SignInStandIn {}

const VIEW: SessionView = {
  stage: 'complete',
  accountId: '7f1f5c9a-1d2b-4c3d-9e4f-5a6b7c8d9e0f',
  email: 'cook@example.test',
  emailVerified: true,
  secondFactorVerified: false,
};

const APPLE: SiteDetail = {
  slug: 'apple-pie',
  name: 'Apple Pie Club',
  tagline: '',
  status: 'live',
  role: 'author',
  host: 'apple-pie.pantrypress.app',
  timeZone: 'America/Chicago',
  locale: 'en-US',
  createdAt: '2026-10-01T12:00:00Z',
};

const VOCABULARY: RecipeVocabulary = {
  courses: [
    { code: 'dessert', name: 'Dessert' },
    { code: 'bread', name: 'Bread' },
  ],
  cuisines: [{ code: 'american', name: 'American' }],
  diets: [{ code: 'vegan', name: 'Vegan' }],
  allergens: [],
};

const recipe = (id: string, title: string, extra: Partial<RecipeSummary> = {}): RecipeSummary => ({
  id,
  title,
  updatedAt: '2026-11-14T18:30:00Z',
  archivedAt: null,
  canEdit: true,
  ...extra,
});

const PIE = recipe('r1', 'Apple pie');
const TART = recipe('r2', 'Plum tart', { canEdit: false });

function refused<T>(
  status: number,
  code: string,
  title: string,
  errors?: Record<string, string[]>,
): ApiResult<T> {
  return { ok: false, problem: { status, code, title, ...(errors ? { errors } : {}) } };
}

const page = (
  items: RecipeSummary[],
  nextCursor: string | null = null,
): ApiResult<RecipeSummaryPage> => ({
  ok: true,
  status: 200,
  value: { items, nextCursor },
});

/** Answers from a queue a test fills; an empty queue answers the fallback. */
class Calls<T> {
  readonly made: unknown[][] = [];
  private readonly queue: T[] = [];
  constructor(private readonly fallback: (args: unknown[]) => T) {}
  answer(...answers: T[]): void {
    this.queue.push(...answers);
  }
  take(args: unknown[]): Promise<T> {
    this.made.push(args);
    return Promise.resolve(this.queue.shift() ?? this.fallback(args));
  }
}

class FakeRecipes {
  readonly listCalls = new Calls<ApiResult<RecipeSummaryPage>>(() => page([PIE, TART]));
  readonly vocabularyCalls = new Calls<ApiResult<RecipeVocabulary>>(() => ({
    ok: true,
    status: 200,
    value: VOCABULARY,
  }));
  readonly archiveCalls = new Calls<ApiResult<void>>(() => ({
    ok: true,
    status: 204,
    value: undefined,
  }));
  readonly restoreCalls = new Calls<ApiResult<void>>(() => ({
    ok: true,
    status: 204,
    value: undefined,
  }));
  list(...args: unknown[]) {
    return this.listCalls.take(args);
  }
  vocabulary(...args: unknown[]) {
    return this.vocabularyCalls.take(args);
  }
  archive(...args: unknown[]) {
    return this.archiveCalls.take(args);
  }
  restore(...args: unknown[]) {
    return this.restoreCalls.take(args);
  }
  /** What each list call asked for. */
  queries(): RecipeListQuery[] {
    return this.listCalls.made.map((args) => args[1] as RecipeListQuery);
  }
}

class FakeSites {
  get(slug: string): Promise<ApiResult<SiteDetail>> {
    return Promise.resolve(
      slug === APPLE.slug
        ? { ok: true, status: 200, value: APPLE }
        : refused(404, 'site-not-found', 'No site.'),
    );
  }
  list() {
    return Promise.resolve({ ok: true, status: 200, value: { items: [], nextCursor: null } });
  }
}

class FakeSessions {
  readonly session = signal<SessionView | null>(VIEW);
  signOut() {
    return Promise.resolve({ ok: true, status: 204, value: undefined });
  }
}

async function open(url: string, prepare: (recipes: FakeRecipes) => void = () => undefined) {
  const recipes = new FakeRecipes();
  prepare(recipes);
  TestBed.configureTestingModule({
    providers: [
      { provide: RecipesClient, useValue: recipes },
      { provide: SitesClient, useValue: new FakeSites() },
      { provide: SessionClient, useValue: new FakeSessions() },
      { provide: AccountClient, useValue: {} },
      { provide: DocumentLoad, useValue: { to: () => undefined } },
      provideRouter([
        { path: 'sign-in', component: SignInStandIn },
        { path: 'dashboard', children: dashboardRoutes },
      ]),
    ],
  });
  const toaster = TestBed.inject(PpToaster);
  const toasts: PpToastOptions[] = [];
  vi.spyOn(toaster, 'show').mockImplementation((options) => {
    toasts.push(options);
    return toasts.length;
  });
  const harness = await RouterTestingHarness.create();
  const router = TestBed.inject(Router);
  const settle = async () => {
    for (let i = 0; i < 4; i += 1) {
      await harness.fixture.whenStable();
      harness.fixture.detectChanges();
    }
  };
  await router.navigateByUrl(url);
  await settle();
  const root = () => harness.routeNativeElement as HTMLElement;
  const main = () => root().querySelector('main') as HTMLElement;
  const button = (name: string) => {
    const match = [...main().querySelectorAll<HTMLButtonElement>('button')].find(
      (b) => (b.getAttribute('aria-label') ?? b.textContent?.trim()) === name,
    );
    if (!match) {
      throw new Error(`No button named '${name}'.`);
    }
    return match;
  };
  return {
    recipes,
    toasts,
    settle,
    root,
    main,
    button,
    url: () => router.url,
    titles: () => [...main().querySelectorAll('.recipe .title')].map((t) => t.textContent?.trim()),
    select: (label: string) =>
      [...main().querySelectorAll('pp-field')]
        .find((f) => f.querySelector('label')?.textContent?.trim().startsWith(label))
        ?.querySelector('select') as HTMLSelectElement | undefined,
    go: async (to: string) => {
      await router.navigateByUrl(to);
      await settle();
    },
  };
}

const LIBRARY = '/dashboard/sites/apple-pie/recipes';

describe('the recipe library (PP4.12b)', () => {
  it('lists the first page of the library with the site named, and says who may change what', async () => {
    const view = await open(LIBRARY);
    expect(view.main().querySelector('h1')?.textContent?.trim()).toBe('Recipes');
    expect(document.title).toBe('Recipes - Apple Pie Club - PantryPress');
    expect(view.recipes.listCalls.made[0]).toEqual([
      'apple-pie',
      { limit: LIBRARY_PAGE, search: '', course: '', cuisine: '', diet: '' },
    ]);
    expect(view.titles()).toEqual(['Apple pie', 'Plum tart']);
    expect(view.button('Archive Apple pie')).toBeTruthy();
    expect(() => view.button('Archive Plum tart')).toThrow();
    expect(
      view.main().querySelector('.recipe:nth-child(2) pp-status-pill')?.textContent?.trim(),
    ).toBe('View only');
    expect(view.main().querySelector('time')?.getAttribute('datetime')).toBe(PIE.updatedAt);
    expect(view.main().querySelector('time')?.textContent).toBe('Nov 14, 2026');
  });

  it('links every recipe to its editor, and offers a new one at the top (PP4.13)', async () => {
    const view = await open(LIBRARY);
    expect(
      [...view.main().querySelectorAll<HTMLAnchorElement>('.recipe a.title')].map((a) => [
        a.textContent?.trim(),
        a.getAttribute('href'),
      ]),
    ).toEqual([
      ['Apple pie', `${LIBRARY}/r1`],
      ['Plum tart', `${LIBRARY}/r2`],
    ]);
    const add = view.main().querySelector<HTMLAnchorElement>('pp-page-header a');
    expect([add?.textContent?.trim(), add?.getAttribute('href')]).toEqual([
      'New recipe',
      `${LIBRARY}/new`,
    ]);
  });

  it('reads the search, filters, and tab from the address, and asks the server for exactly those', async () => {
    const view = await open(`${LIBRARY}?q=pie&course=dessert&diet=vegan&archived=true`);
    expect(view.recipes.queries()[0]).toEqual({
      limit: LIBRARY_PAGE,
      archived: true,
      search: 'pie',
      course: 'dessert',
      cuisine: '',
      diet: 'vegan',
    });
    expect(view.select('Course')?.value).toBe('dessert');
    expect(view.select('Diet')?.value).toBe('vegan');
    expect(view.main().querySelector<HTMLInputElement>('input[type="search"]')?.value).toBe('pie');
    expect(
      view.main().querySelector('[role="tab"][aria-selected="true"]')?.textContent?.trim(),
    ).toBe('Archived');
  });

  it('puts each search and filter in the address, so Back and a reload keep them', async () => {
    const view = await open(LIBRARY);
    const course = view.select('Course')!;
    course.value = 'bread';
    course.dispatchEvent(new Event('change'));
    await view.settle();
    expect(view.url()).toBe(`${LIBRARY}?course=bread`);
    expect(view.recipes.queries().at(-1)).toMatchObject({ course: 'bread' });

    const box = view.main().querySelector<HTMLInputElement>('input[type="search"]')!;
    box.value = 'tart';
    box.dispatchEvent(new Event('input'));
    view.button('Search').click();
    await view.settle();
    expect(view.url()).toBe(`${LIBRARY}?course=bread&q=tart`);
    expect(view.recipes.queries().at(-1)).toMatchObject({ course: 'bread', search: 'tart' });

    view.main().querySelectorAll<HTMLButtonElement>('[role="tab"]')[1].click();
    await view.settle();
    expect(view.url()).toBe(`${LIBRARY}?course=bread&q=tart&archived=true`);
    expect(view.recipes.queries().at(-1)).toMatchObject({ archived: true, search: 'tart' });
  });

  it('pages forward by the cursor given and back by the one it came from, with the same filters', async () => {
    const view = await open(`${LIBRARY}?course=dessert`, (r) =>
      r.listCalls.answer(
        page([PIE], 'c2'),
        page([TART], 'c3'),
        page([recipe('r3', 'Rye')]),
        page([TART], 'c3'),
      ),
    );
    view.button('Next').click();
    await view.settle();
    expect(view.titles()).toEqual(['Plum tart']);
    expect(view.main().querySelector('.page')?.textContent?.trim()).toBe('Page 2');
    view.button('Next').click();
    await view.settle();
    view.button('Previous').click();
    await view.settle();
    expect(view.recipes.queries().map((q) => [q.cursor, q.course])).toEqual([
      [undefined, 'dessert'],
      ['c2', 'dessert'],
      ['c3', 'dessert'],
      ['c2', 'dessert'],
    ]);
    expect(view.titles()).toEqual(['Plum tart']);
  });

  it('shows the empty library, an empty archive, and no matches, each with its own next step', async () => {
    const empty = await open(LIBRARY, (r) => r.listCalls.answer(page([])));
    expect(
      empty
        .main()
        .querySelector('pp-empty-state:not([hidden]) h2, .empty:not([hidden]) h2')
        ?.textContent?.trim(),
    ).toBe('No recipes yet');
    // The empty library's next step is the first recipe (PP4.13).
    expect(
      [...empty.main().querySelectorAll<HTMLAnchorElement>('pp-empty-state a')].map((a) => [
        a.textContent?.trim(),
        a.getAttribute('href'),
      ]),
    ).toEqual([['New recipe', `${LIBRARY}/new`]]);
    TestBed.resetTestingModule();

    const archived = await open(`${LIBRARY}?archived=true`, (r) => r.listCalls.answer(page([])));
    expect(archived.main().querySelector('.empty h2')?.textContent?.trim()).toBe(
      'Nothing archived',
    );
    TestBed.resetTestingModule();

    const none = await open(`${LIBRARY}?q=zzz`, (r) => r.listCalls.answer(page([])));
    expect(none.main().querySelector('.empty h2')?.textContent?.trim()).toBe('No recipes match');
    none.button('Clear search and filters').click();
    await none.settle();
    expect(none.url()).toBe(LIBRARY);
  });

  it('shows a list that could not be loaded, and loads it again on Try again', async () => {
    const view = await open(LIBRARY, (r) =>
      r.listCalls.answer(refused(0, 'unreachable', 'No answer.')),
    );
    expect(view.main().textContent).toContain("Your recipes couldn't be loaded.");
    view.button('Try again').click();
    await view.settle();
    expect(view.titles()).toEqual(['Apple pie', 'Plum tart']);
  });

  it('keeps searching when the filters cannot be had, and offers to fetch them again', async () => {
    const view = await open(LIBRARY, (r) =>
      r.vocabularyCalls.answer(refused(500, 'unexpected-error', 'Oops.')),
    );
    expect(view.main().textContent).toContain("Filters aren't available right now.");
    expect(view.select('Course')).toBeUndefined();
    expect(view.main().querySelector('input[type="search"]')).not.toBeNull();
    expect(view.titles()).toEqual(['Apple pie', 'Plum tart']);
    view.button('Try again').click();
    await view.settle();
    expect(view.select('Course')).toBeDefined();
  });

  it('says in the API words why a search or filter was refused, and offers to clear them', async () => {
    const view = await open(`${LIBRARY}?course=nope`, (r) =>
      r.listCalls.answer(
        refused(400, 'validation-failed', 'Check the highlighted fields.', {
          course: ['Choose a course from the list.'],
        }),
      ),
    );
    expect(view.main().textContent).toContain('Choose a course from the list.');
    expect(view.titles()).toEqual([]);
    view.button('Clear search and filters').click();
    await view.settle();
    expect(view.url()).toBe(LIBRARY);
    expect(view.titles()).toEqual(['Apple pie', 'Plum tart']);
  });

  it('starts again from the first page when a cursor is no longer good', async () => {
    const view = await open(LIBRARY, (r) =>
      r.listCalls.answer(
        page([PIE], 'c2'),
        refused(400, 'validation-failed', 'Check.', {
          cursor: ['Start again from the first page.'],
        }),
        page([PIE, TART]),
      ),
    );
    view.button('Next').click();
    await view.settle();
    expect(view.recipes.queries().map((q) => q.cursor)).toEqual([undefined, 'c2', undefined]);
    expect(view.titles()).toEqual(['Apple pie', 'Plum tart']);
  });

  it('archives with a key of its own, offers to undo, and moves focus to the list', async () => {
    const view = await open(LIBRARY, (r) => r.listCalls.answer(page([PIE, TART]), page([TART])));
    view.button('Archive Apple pie').click();
    await view.settle();
    const [slug, id, key] = view.recipes.archiveCalls.made[0] as string[];
    expect([slug, id]).toEqual(['apple-pie', 'r1']);
    expect(key).toMatch(/^recipe-archive-[0-9a-f-]{36}$/);
    expect(view.toasts[0]).toMatchObject({
      message: '“Apple pie” moved to Archived.',
      tone: 'success',
    });
    expect(document.activeElement?.tagName).toBe('PP-LIST');
    expect(view.titles()).toEqual(['Plum tart']);

    view.toasts[0].action!.run();
    await view.settle();
    const [, restored, restoreKey] = view.recipes.restoreCalls.made[0] as string[];
    expect(restored).toBe('r1');
    expect(restoreKey).not.toBe(key);
    expect(view.toasts[1]).toMatchObject({ message: '“Apple pie” is back in your library.' });
  });

  it('restores from the Archived tab', async () => {
    const view = await open(`${LIBRARY}?archived=true`, (r) =>
      r.listCalls.answer(
        page([recipe('r1', 'Apple pie', { archivedAt: '2026-11-14T19:00:00Z' })]),
        page([]),
      ),
    );
    view.button('Restore Apple pie').click();
    await view.settle();
    expect(view.recipes.restoreCalls.made[0].slice(0, 2)).toEqual(['apple-pie', 'r1']);
    expect(view.main().querySelector('.empty h2')?.textContent?.trim()).toBe('Nothing archived');
  });

  it('shows a refused archive in the API words and reads the list again', async () => {
    const view = await open(LIBRARY, (r) =>
      r.archiveCalls.answer(
        refused(
          403,
          'recipe-not-yours',
          'Only an editor or owner can change a recipe someone else made.',
        ),
      ),
    );
    view.button('Archive Apple pie').click();
    await view.settle();
    expect(view.toasts[0]).toEqual({
      message: 'Only an editor or owner can change a recipe someone else made.',
      tone: 'danger',
    });
    expect(view.recipes.listCalls.made).toHaveLength(2);
  });

  it('sends a session that ended to sign in, coming back here', async () => {
    const view = await open(`${LIBRARY}?q=pie`, (r) =>
      r.listCalls.answer(refused(401, 'session-ended', 'Your session ended.')),
    );
    expect(view.url()).toMatch(/^\/sign-in\?next=/);
    expect(decodeURIComponent(view.url())).toContain(`${LIBRARY}?q=pie`);
  });

  // From design review: a search, filter, or tab change is a move within the screen, so focus stays on the control the
  // reader used, and the tab's title keeps the site's name.
  it('keeps focus on the control used and the site in the title when only the query changes', async () => {
    const view = await open(LIBRARY);
    const course = view.select('Course')!;
    course.focus();
    course.value = 'bread';
    course.dispatchEvent(new Event('change'));
    await view.settle();
    expect(view.url()).toBe(`${LIBRARY}?course=bread`);
    expect(document.activeElement).toBe(view.select('Course'));
    expect(document.title).toBe('Recipes - Apple Pie Club - PantryPress');

    const archived = view.main().querySelectorAll<HTMLButtonElement>('[role="tab"]')[1];
    archived.focus();
    archived.click();
    await view.settle();
    expect(view.url()).toBe(`${LIBRARY}?course=bread&archived=true`);
    expect(document.activeElement).toBe(archived);
  });

  it('steps back a page when the last recipe on a later page is archived', async () => {
    const view = await open(LIBRARY, (r) =>
      r.listCalls.answer(
        page([PIE], 'c2'),
        page([recipe('r3', 'Rye bread')]),
        page([]),
        page([PIE]),
      ),
    );
    view.button('Next').click();
    await view.settle();
    view.button('Archive Rye bread').click();
    await view.settle();
    expect(view.recipes.queries().map((q) => q.cursor)).toEqual([undefined, 'c2', 'c2', undefined]);
    expect(view.titles()).toEqual(['Apple pie']);
    expect(view.main().querySelector('.page')).toBeNull();
  });

  it('keeps focus on the list when Try again takes the filters notice away', async () => {
    const view = await open(LIBRARY, (r) =>
      r.vocabularyCalls.answer(refused(500, 'unexpected-error', 'Oops.')),
    );
    const retry = view.button('Try again');
    retry.focus();
    retry.click();
    await view.settle();
    expect(view.select('Course')).toBeDefined();
    expect(document.activeElement?.tagName).toBe('PP-LIST');
  });

  it('is listed in the site navigation and has no accessibility violations', async () => {
    const view = await open(LIBRARY);
    const link = [...view.root().querySelectorAll<HTMLAnchorElement>('nav a')].find(
      (a) => a.textContent?.trim() === 'Recipes',
    );
    expect(link?.getAttribute('aria-current')).toBe('page');
    const results = await axe.run(view.root());
    expect(results.violations.map((v) => v.id)).toEqual([]);
  });
});
