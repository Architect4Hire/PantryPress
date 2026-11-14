import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { PpToaster } from '@pantrypress/ui';
import axe from 'axe-core';
import { ApiResult } from '../../core/api/api-problem';
import { DocumentLoad } from '../../core/navigation/document-load';
import { RecipeDrafts } from '../../core/recipes/recipe-drafts';
import {
  RecipeContent,
  RecipeDetail,
  RecipesClient,
  RecipeVocabulary,
} from '../../core/recipes/recipes-client';
import { AccountClient } from '../../core/session/account-client';
import { SessionClient, SessionView } from '../../core/session/session-client';
import { SiteDetail, SitesClient } from '../../core/sites/sites-client';
import dashboardRoutes from '../dashboard/dashboard.routes';
import { SAVE_DELAY_MS } from './recipe-autosave';
import { fieldsFrom } from './recipe-form';
import { recipeDetail } from './recipe-fixtures.spec-helper';

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
  role: 'editor',
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
  allergens: [{ code: 'milk', name: 'Milk' }],
};

const PIE = recipeDetail();
const EDITOR = `/dashboard/sites/apple-pie/recipes/${PIE.id}`;
const NEW = '/dashboard/sites/apple-pie/recipes/new';

function refused<T>(
  status: number,
  code: string,
  title: string,
  errors?: Record<string, string[]>,
): ApiResult<T> {
  return { ok: false, problem: { status, code, title, ...(errors ? { errors } : {}) } };
}

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

/** The server's answer to a save it took: the recipe as sent, under the next version. */
let versions = 0;
const saved = (content: RecipeContent, id = PIE.id): ApiResult<RecipeDetail> => ({
  ok: true,
  status: 200,
  value: {
    ...PIE,
    ...content,
    id,
    version: `v${++versions}`,
    ingredientGroups: [],
    stepGroups: [],
  } as RecipeDetail,
});

class FakeRecipes {
  readonly getCalls = new Calls<ApiResult<RecipeDetail>>(() => ({
    ok: true,
    status: 200,
    value: PIE,
  }));
  readonly vocabularyCalls = new Calls<ApiResult<RecipeVocabulary>>(() => ({
    ok: true,
    status: 200,
    value: VOCABULARY,
  }));
  readonly createCalls = new Calls<ApiResult<RecipeDetail>>((args) =>
    saved(args[1] as RecipeContent, 'b0f2c1e4-0000-4000-8000-000000000001'),
  );
  readonly updateCalls = new Calls<ApiResult<RecipeDetail>>((args) =>
    saved(args[3] as RecipeContent),
  );
  readonly restoreCalls = new Calls<ApiResult<void>>(() => ({
    ok: true,
    status: 204,
    value: undefined,
  }));
  get(...args: unknown[]) {
    return this.getCalls.take(args);
  }
  vocabulary(...args: unknown[]) {
    return this.vocabularyCalls.take(args);
  }
  create(...args: unknown[]) {
    return this.createCalls.take(args);
  }
  update(...args: unknown[]) {
    return this.updateCalls.take(args);
  }
  restore(...args: unknown[]) {
    return this.restoreCalls.take(args);
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
  const field = (label: string) =>
    [...main().querySelectorAll('pp-field')].find(
      (f) => f.querySelector('label')?.textContent?.replace('(optional)', '').trim() === label,
    ) as HTMLElement | undefined;
  const input = (label: string, within?: string) => {
    const scope = within
      ? [...main().querySelectorAll('fieldset')].find(
          (f) => f.querySelector('legend')?.textContent?.trim() === within,
        )
      : main();
    const match = [...(scope?.querySelectorAll('pp-field') ?? [])].find(
      (f) => f.querySelector('label')?.textContent?.replace('(optional)', '').trim() === label,
    );
    return match?.querySelector('input, textarea') as HTMLInputElement;
  };
  const type = async (control: HTMLInputElement, text: string) => {
    control.value = text;
    control.dispatchEvent(new Event('input'));
    await settle();
  };
  const button = (name: string) => {
    const match = [...main().querySelectorAll<HTMLElement>('button, a')].find(
      (b) => b.textContent?.trim() === name,
    );
    if (!match) {
      throw new Error(`No button named '${name}'.`);
    }
    return match;
  };
  const pause = async () => {
    await vi.advanceTimersByTimeAsync(SAVE_DELAY_MS);
    await settle();
  };
  return {
    recipes,
    settle,
    root,
    main,
    field,
    input,
    type,
    button,
    pause,
    url: () => router.url,
    h1: () => main().querySelector('h1')?.textContent?.trim(),
    // The words and the detail are two lines, read one after the other.
    status: () => {
      const status = main().querySelector('pp-save-status');
      return status === null
        ? undefined
        : [...status.querySelectorAll('.words, .detail')]
            .map((part) => part.textContent?.trim())
            .join(' ');
    },
    notices: () =>
      [...main().querySelectorAll('pp-notice')].map((n) =>
        n.textContent?.replace(/\s+/g, ' ').trim(),
      ),
    go: async (to: string) => {
      await router.navigateByUrl(to);
      await settle();
    },
  };
}

describe('the recipe editor (PP4.13)', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });
  afterEach(() => {
    vi.useRealTimers();
    localStorage.clear();
  });

  it('opens a recipe in its details, times, and yield, with the site named', async () => {
    const page = await open(EDITOR);
    expect(page.recipes.getCalls.made[0]).toEqual(['apple-pie', PIE.id]);
    expect(page.h1()).toBe('Apple pie');
    expect(document.title).toBe('Apple pie - Recipes - Apple Pie Club - PantryPress');
    expect([...page.main().querySelectorAll('h2')].map((h) => h.textContent?.trim())).toEqual([
      'Details',
      'Times',
      'Yield',
    ]);
    expect(page.input('Title').value).toBe('Apple pie');
    expect(page.input('Title').required).toBe(true);
    expect(page.input('Summary').value).toBe('A deep-dish pie.');
    expect(page.input('Minutes', 'Prep time').value).toBe('30');
    expect([
      page.input('Hours', 'Cook time').value,
      page.input('Minutes', 'Cook time').value,
    ]).toEqual(['1', '15']);
    expect(page.input('Makes').value).toBe('8');
    expect(page.input('Of what').value).toBe('servings');
    expect(page.main().querySelector('.total')?.textContent?.trim()).toBe(
      'Total time: 1 hr 45 min',
    );
    const dessert = [...page.main().querySelectorAll('pp-chip-group label')].find(
      (l) => l.textContent?.trim() === 'Dessert',
    );
    expect(dessert?.querySelector('input')?.checked).toBe(true);
    expect(page.status()).toBe('Saved');
    expect(page.button('All recipes').getAttribute('href')).toBe(
      '/dashboard/sites/apple-pie/recipes',
    );
  });

  it('saves a pause in typing, with the version it read and a key, and says so', async () => {
    const page = await open(EDITOR);
    await page.type(page.input('Title'), 'Apple crumble pie');
    expect(page.status()).toBe('Changes not saved yet');
    expect(page.recipes.updateCalls.made).toHaveLength(0);
    await page.pause();
    const [slug, id, version, content, key] = page.recipes.updateCalls.made[0] as [
      string,
      string,
      string,
      RecipeContent,
      string,
    ];
    expect([slug, id, version]).toEqual(['apple-pie', PIE.id, PIE.version]);
    expect(key).toMatch(/^recipe-save-/);
    expect(content.title).toBe('Apple crumble pie');
    // The parts this screen does not edit go back exactly, with their ids.
    expect(content.ingredientGroups).toEqual([
      { id: 'g1', heading: 'For the crust', lines: [{ id: 'l1', originalText: '2 ½ cups flour' }] },
    ]);
    expect(page.status()).toBe('Saved');
    expect(page.h1()).toBe('Apple crumble pie');
  });

  it('keeps what is typed on this device until the server has it', async () => {
    const page = await open(EDITOR, (recipes) =>
      recipes.updateCalls.answer(refused(0, 'unreachable', "We couldn't reach PantryPress.")),
    );
    await page.type(page.input('Summary'), 'Typed offline.');
    const kept = TestBed.inject(RecipeDrafts).read(VIEW.accountId, 'apple-pie', PIE.id);
    expect(kept?.fields['summary']).toBe('Typed offline.');
    await page.pause();
    expect(page.status()).toBe(
      'Not saved yet. Trying again… Your changes are kept on this device.',
    );
  });

  it('shows the additional time’s label only once there is an additional time', async () => {
    const page = await open(EDITOR);
    expect(page.field("What it's for")).toBeUndefined();
    await page.type(page.input('Minutes', 'Additional time'), '45');
    expect(page.field("What it's for")).toBeDefined();
    expect(page.main().querySelector('.total')?.textContent?.trim()).toBe(
      'Total time: 2 hr 30 min',
    );
  });

  it('shows the server’s refusal under the box it is about, and marks the box', async () => {
    const page = await open(EDITOR, (recipes) =>
      recipes.updateCalls.answer(
        refused(400, 'validation-failed', 'Check the recipe.', {
          'content.yield.high': ['Make the high end of the range more than the low end.'],
        }),
      ),
    );
    await page.type(page.input('Up to'), '2');
    await page.pause();
    expect(page.field('Up to')?.textContent).toContain(
      'Make the high end of the range more than the low end.',
    );
    expect(page.input('Up to').getAttribute('aria-invalid')).toBe('true');
    expect(page.status()).toBe("Not saved Fix what's marked below.");
  });

  it('marks a box it cannot read, and sends nothing', async () => {
    const page = await open(EDITOR);
    await page.type(page.input('Makes'), 'eight');
    await page.pause();
    expect(page.recipes.updateCalls.made).toHaveLength(0);
    expect(page.field('Makes')?.textContent).toContain('Enter a number, like 8, 1.5, or 1 1/2.');
  });

  it('offers to keep my changes over a copy changed somewhere else, and does', async () => {
    const page = await open(EDITOR, (recipes) => {
      recipes.updateCalls.answer(refused(409, 'recipe-changed', 'This recipe changed.'));
      recipes.getCalls.answer(
        { ok: true, status: 200, value: PIE },
        {
          ok: true,
          status: 200,
          value: recipeDetail({ version: 'theirs', summary: 'Their summary' }),
        },
      );
    });
    await page.type(page.input('Title'), 'Mine');
    await page.pause();
    expect(page.notices()[0]).toContain('This recipe was changed somewhere else');
    page.button('Keep my changes').click();
    await page.settle();
    await vi.advanceTimersByTimeAsync(0);
    await page.settle();
    const [, , version, content] = page.recipes.updateCalls.made[1] as [
      string,
      string,
      string,
      RecipeContent,
    ];
    expect(version).toBe('theirs');
    expect(content.title).toBe('Mine');
    expect(content.summary).toBe('Their summary');
    expect(page.input('Summary').value).toBe('Their summary');
  });

  it('shows a recipe the caller may not change, with nothing to type in', async () => {
    const page = await open(EDITOR, (recipes) =>
      recipes.getCalls.answer({ ok: true, status: 200, value: recipeDetail({ canEdit: false }) }),
    );
    expect(page.notices()[0]).toContain('You can view this recipe');
    // Read-only, not disabled: every word can still be reached, read in full, and copied.
    expect(page.input('Title').readOnly).toBe(true);
    expect(page.input('Title').disabled).toBe(false);
    expect(page.input('Minutes', 'Prep time').readOnly).toBe(true);
    expect(page.input('Summary').readOnly).toBe(true);
    // The chips have no read-only state, so they alone are disabled.
    expect(page.main().querySelector<HTMLInputElement>('pp-chip-group input')?.disabled).toBe(true);
    expect(page.main().querySelector('pp-save-status')).toBeNull();
  });

  it('offers to restore an archived recipe, and then it can be edited', async () => {
    const page = await open(EDITOR, (recipes) =>
      recipes.getCalls.answer(
        { ok: true, status: 200, value: recipeDetail({ archivedAt: '2026-11-14T19:00:00Z' }) },
        { ok: true, status: 200, value: recipeDetail({ version: 'restored' }) },
      ),
    );
    expect(page.notices()[0]).toContain('This recipe is archived');
    expect(page.input('Title').readOnly).toBe(true);
    page.button('Restore').click();
    await page.settle();
    expect(page.recipes.restoreCalls.made[0]?.slice(0, 2)).toEqual(['apple-pie', PIE.id]);
    expect(page.input('Title').readOnly).toBe(false);
    expect(page.notices()).toEqual([]);
  });

  it.each(['recipe-not-found', 'not-found'])('says a recipe is not there for %s', async (code) => {
    const page = await open(EDITOR, (recipes) =>
      recipes.getCalls.answer(refused(404, code, 'Not found.')),
    );
    expect(page.main().querySelector('pp-empty-state')?.textContent).toContain(
      "We couldn't find that recipe",
    );
    expect(page.button('Go to your recipes').getAttribute('href')).toBe(
      '/dashboard/sites/apple-pie/recipes',
    );
  });

  it('says when the recipe could not be loaded, and tries again', async () => {
    const page = await open(EDITOR, (recipes) =>
      recipes.getCalls.answer(refused(503, 'unexpected-error', 'Something went wrong.')),
    );
    expect(page.notices()[0]).toContain("The recipe couldn't be loaded");
    page.button('Try again').click();
    await page.settle();
    expect(page.input('Title').value).toBe('Apple pie');
  });

  it('goes to sign in when the session has ended', async () => {
    const page = await open(EDITOR, (recipes) =>
      recipes.getCalls.answer(refused(401, 'session-ended', 'Signed out.')),
    );
    expect(page.url()).toMatch(/^\/sign-in\?/);
  });

  it('keeps every choice when the words cannot be loaded, and offers to try again', async () => {
    const page = await open(EDITOR, (recipes) =>
      recipes.vocabularyCalls.answer(refused(503, 'unexpected-error', 'Down.')),
    );
    expect(page.main().querySelector('pp-chip-group')).toBeNull();
    expect(page.notices()[0]).toContain("can't be shown right now");
    await page.type(page.input('Title'), 'Still saves');
    await page.pause();
    const content = page.recipes.updateCalls.made[0][3] as RecipeContent;
    expect(content.courses).toEqual(['dessert']);
    page.button('Try again').click();
    await page.settle();
    expect(page.main().querySelectorAll('pp-chip-group').length).toBeGreaterThan(0);
  });

  it('keeps More details open while its error goes away under the person typing', async () => {
    const page = await open(EDITOR, (recipes) =>
      recipes.updateCalls.answer(
        refused(400, 'validation-failed', 'Check the recipe.', {
          'content.videoUrl': [
            'Use the address of a video on YouTube or Vimeo, starting https://.',
          ],
        }),
      ),
    );
    const details = page.main().querySelector('details')!;
    expect(details.open).toBe(false);
    await page.type(page.input('Video'), 'https://example.com/v');
    await page.pause();
    expect(details.open).toBe(true);
    expect(page.input('Video').getAttribute('aria-invalid')).toBe('true');
    const video = page.input('Video');
    video.focus();
    await page.type(video, 'https://youtu.be/abc');
    await page.pause();
    expect(page.field('Video')?.querySelector('pp-field-error')).toBeNull();
    expect(details.open).toBe(true);
    expect(document.activeElement).toBe(page.input('Video'));
  });

  it('says a time it cannot read once, from either of its boxes', async () => {
    const page = await open(EDITOR);
    await page.type(page.input('Hours', 'Cook time'), 'one');
    await page.pause();
    const set = [...page.main().querySelectorAll('pp-fieldset')].find(
      (f) => f.querySelector('legend')?.textContent?.trim() === 'Cook time',
    )!;
    const errors = set.querySelectorAll('pp-field-error');
    expect(errors).toHaveLength(1);
    for (const box of set.querySelectorAll('input')) {
      expect(box.getAttribute('aria-invalid')).toBe('true');
      expect(box.getAttribute('aria-describedby')?.split(' ')).toContain(errors[0].id);
    }
    expect(page.input('Hours', 'Cook time').getAttribute('aria-label')).toBe('Cook time, hours');
  });

  it('puts a refused course under the course chips, inside their group', async () => {
    const page = await open(EDITOR, (recipes) =>
      recipes.updateCalls.answer(
        refused(400, 'validation-failed', 'Check the recipe.', {
          'content.courses': ['Choose up to 5 courses.'],
        }),
      ),
    );
    await page.type(page.input('Title'), 'Many courses');
    await page.pause();
    const course = page.main().querySelector('pp-chip-group fieldset')!;
    expect(course.querySelector('pp-field-error')?.textContent).toContain(
      'Choose up to 5 courses.',
    );
    expect(course.getAttribute('aria-invalid')).toBe('true');
  });

  it('offers to undo setting my changes aside for the newer copy', async () => {
    const page = await open(EDITOR, (recipes) => {
      recipes.updateCalls.answer(refused(409, 'recipe-changed', 'This recipe changed.'));
      recipes.getCalls.answer(
        { ok: true, status: 200, value: PIE },
        { ok: true, status: 200, value: recipeDetail({ version: 'theirs', title: 'Their title' }) },
      );
    });
    const show = vi.spyOn(TestBed.inject(PpToaster), 'show');
    await page.type(page.input('Title'), 'Mine');
    await page.pause();
    page.button('Use the newer copy').click();
    await page.settle();
    expect(page.input('Title').value).toBe('Their title');
    const toast = show.mock.calls.at(-1)![0];
    expect(toast.action?.label).toBe('Undo');
    toast.action!.run();
    await page.settle();
    expect(page.input('Title').value).toBe('Mine');
    expect(page.notices()[0]).toContain('This recipe was changed somewhere else');
  });

  describe('a new recipe', () => {
    it('makes the recipe once it has a title, gives it its address, and keeps focus where the person types', async () => {
      const page = await open(NEW);
      expect(page.h1()).toBe('New recipe');
      expect(page.field('Title')?.textContent).toContain(
        'Your recipe is saved as soon as it has a title.',
      );
      expect(page.main().querySelector('pp-save-status')).toBeNull();

      await page.type(page.input('Summary'), 'Before a title');
      await page.pause();
      expect(page.recipes.createCalls.made).toHaveLength(0);

      const title = page.input('Title');
      title.focus();
      await page.type(title, 'Plum tart');
      await page.pause();
      expect(page.recipes.createCalls.made).toHaveLength(1);
      const [slug, content] = page.recipes.createCalls.made[0] as [string, RecipeContent];
      expect(slug).toBe('apple-pie');
      expect(content).toMatchObject({ title: 'Plum tart', summary: 'Before a title' });
      expect(page.url()).toBe(
        '/dashboard/sites/apple-pie/recipes/b0f2c1e4-0000-4000-8000-000000000001',
      );
      expect(document.activeElement).toBe(page.input('Title'));
      expect(page.h1()).toBe('Plum tart');
      // The same screen goes on: the next pause is an update, never another create or a reload.
      expect(page.recipes.getCalls.made).toHaveLength(0);
      await page.type(page.input('Title'), 'Plum tart, rustic');
      await page.pause();
      expect(page.recipes.createCalls.made).toHaveLength(1);
      expect(page.recipes.updateCalls.made).toHaveLength(1);
    });

    it('picks up a new recipe started here before, and can start a blank one instead', async () => {
      // Left by an earlier visit, under this account and site.
      localStorage.setItem(
        `pp-recipe-draft:${VIEW.accountId}:apple-pie:new`,
        JSON.stringify({
          recipeId: null,
          baseVersion: null,
          fields: { ...fieldsFrom(PIE), title: '', summary: 'Started earlier' },
          edited: ['summary'],
          outstanding: null,
        }),
      );
      const page = await open(NEW);
      expect(page.notices()[0]).toContain('We kept the recipe you started');
      expect(page.input('Summary').value).toBe('Started earlier');
      page.button('Start a blank recipe').click();
      await page.settle();
      expect(page.input('Summary').value).toBe('');
      expect(TestBed.inject(RecipeDrafts).read(VIEW.accountId, 'apple-pie', null)).toBeNull();
    });
  });

  it('passes axe on a recipe and on a new one (contrast is the browser run’s)', async () => {
    for (const url of [EDITOR, NEW]) {
      TestBed.resetTestingModule();
      const page = await open(url);
      page.main().querySelector('details')?.setAttribute('open', '');
      const results = await axe.run(page.root(), {
        runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] },
        rules: { 'color-contrast': { enabled: false } },
      });
      expect(
        results.violations.map(
          (v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`,
        ),
      ).toEqual([]);
    }
  }, 30_000);
});
