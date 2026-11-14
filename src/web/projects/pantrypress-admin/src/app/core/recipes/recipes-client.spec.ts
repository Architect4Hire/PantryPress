import { TestBed } from '@angular/core/testing';
import { ApiClient, ApiReadOptions, ApiWriteOptions } from '../api/api-client';
import { ApiPath } from '../api/api-path';
import { ApiResult } from '../api/api-problem';
import {
  readRecipeDetail,
  readRecipeQuantity,
  readRecipeSummaryPage,
  readRecipeVocabulary,
  RecipeContent,
  RecipesClient,
} from './recipes-client';

const SUMMARY = {
  id: '0b9c8a52-1f3e-4b7a-9d2c-5e6f7a8b9c0d',
  title: 'Apple pie',
  updatedAt: '2026-11-14T12:00:00Z',
  archivedAt: null,
  canEdit: true,
};

const LINE = {
  id: 'line-1',
  originalText: '2½ cups flour, sifted',
  status: 'parsed',
  problem: null,
  quantity: { kind: 'amount', low: { numerator: 5, denominator: 2 }, high: null },
  quantitySpan: { start: 0, length: 2 },
  unitCode: 'cup',
  unitSpan: { start: 3, length: 4 },
  item: 'flour',
  note: 'sifted',
};

const DETAIL = {
  id: SUMMARY.id,
  version: 'AAAAAAAAB9A=',
  title: 'Apple pie',
  summary: 'A deep-dish pie.',
  yield: {
    kind: 'range',
    low: { numerator: 6, denominator: 1 },
    high: { numerator: 8, denominator: 1 },
  },
  yieldUnit: 'servings',
  prepMinutes: 45,
  cookMinutes: 60,
  additionalMinutes: 0,
  additionalTimeLabel: 'Cool',
  videoUrl: null,
  nutrition: {
    servingSize: '1 slice',
    calories: 410.5,
    fatGrams: 19.25,
    saturatedFatGrams: null,
    transFatGrams: null,
    unsaturatedFatGrams: null,
    cholesterolMilligrams: null,
    sodiumMilligrams: 230,
    carbohydrateGrams: null,
    fiberGrams: null,
    sugarGrams: null,
    proteinGrams: 0,
  },
  ingredientGroups: [{ id: 'group-1', heading: 'For the crust', lines: [LINE] }],
  stepGroups: [{ id: 'steps-1', heading: null, steps: [{ id: 'step-1', text: 'Bake.' }] }],
  equipment: ['9-inch pie plate'],
  notes: [],
  keywords: ['fall baking'],
  courses: ['dessert'],
  cuisines: ['american'],
  diets: ['vegetarian'],
  allergens: ['milk', 'wheat'],
  createdAt: '2026-11-01T12:00:00Z',
  updatedAt: SUMMARY.updatedAt,
  archivedAt: null,
  canEdit: true,
};

const VOCABULARY = {
  courses: [{ code: 'dessert', name: 'Dessert' }],
  cuisines: [{ code: 'american', name: 'American' }],
  diets: [{ code: 'vegetarian', name: 'Vegetarian' }],
  allergens: [{ code: 'milk', name: 'Milk' }],
};

const CONTENT: RecipeContent = {
  title: 'Apple pie',
  summary: null,
  yield: null,
  yieldUnit: null,
  prepMinutes: null,
  cookMinutes: null,
  additionalMinutes: null,
  additionalTimeLabel: null,
  videoUrl: null,
  nutrition: null,
  ingredientGroups: [{ id: null, heading: null, lines: [{ id: null, originalText: '1 egg' }] }],
  stepGroups: [],
  equipment: [],
  notes: [],
  keywords: [],
  courses: [],
  cuisines: [],
  diets: [],
  allergens: [],
};

class FakeApi {
  readonly calls: { method: string; path: ApiPath; options: ApiWriteOptions }[] = [];
  answer: ApiResult<unknown> = { ok: true, status: 200, value: null };
  get = (path: ApiPath, options: ApiReadOptions = {}) => this.take('GET', path, options);
  post = (path: ApiPath, options: ApiWriteOptions = {}) => this.take('POST', path, options);
  put = (path: ApiPath, options: ApiWriteOptions = {}) => this.take('PUT', path, options);
  private take(method: string, path: ApiPath, options: ApiWriteOptions) {
    this.calls.push({ method, path, options });
    return Promise.resolve(this.answer);
  }
}

describe('RecipesClient', () => {
  let api: FakeApi;
  let client: RecipesClient;

  beforeEach(() => {
    api = new FakeApi();
    TestBed.configureTestingModule({ providers: [{ provide: ApiClient, useValue: api }] });
    client = TestBed.inject(RecipesClient);
  });

  it('lists a page, sending only the search and filters given and never an empty one', async () => {
    api.answer = { ok: true, status: 200, value: { items: [SUMMARY], nextCursor: 'c2' } };
    const result = await client.list('apple-pie', {
      limit: 25,
      cursor: 'c1',
      archived: true,
      search: 'pie',
      course: 'dessert',
      cuisine: '',
      diet: '',
    });
    expect(api.calls[0]).toEqual({
      method: 'GET',
      path: 'sites/apple-pie/recipes',
      options: {
        query: { limit: 25, cursor: 'c1', archived: true, search: 'pie', course: 'dessert' },
      },
    });
    expect(result).toEqual({
      ok: true,
      status: 200,
      value: { items: [SUMMARY], nextCursor: 'c2' },
    });
    await client.list('apple-pie');
    expect(api.calls[1].options).toEqual({ query: {} });
  });

  it('names the site and the recipe each as one encoded segment', async () => {
    api.answer = { ok: true, status: 200, value: DETAIL };
    await client.get('a/b', 'c?d');
    expect(api.calls[0].path).toBe('sites/a%2Fb/recipes/c%3Fd');
  });

  it('reads the vocabulary at its own route', async () => {
    api.answer = { ok: true, status: 200, value: VOCABULARY };
    const result = await client.vocabulary('apple-pie');
    expect(api.calls[0]).toMatchObject({
      method: 'GET',
      path: 'sites/apple-pie/recipes/vocabulary',
    });
    expect(result.ok && result.value).toEqual(VOCABULARY);
  });

  it('sends every write with the caller key, and a save with its version beside the content', async () => {
    api.answer = { ok: true, status: 200, value: DETAIL };
    await client.create('apple-pie', CONTENT, 'k1');
    await client.update('apple-pie', SUMMARY.id, 'v1', CONTENT, 'k2');
    await client.duplicate('apple-pie', SUMMARY.id, 'k3');
    expect(api.calls.map((c) => [c.method, c.path, c.options])).toEqual([
      ['POST', 'sites/apple-pie/recipes', { body: CONTENT, idempotencyKey: 'k1' }],
      [
        'PUT',
        `sites/apple-pie/recipes/${SUMMARY.id}`,
        { body: { version: 'v1', content: CONTENT }, idempotencyKey: 'k2' },
      ],
      ['POST', `sites/apple-pie/recipes/${SUMMARY.id}/duplicate`, { idempotencyKey: 'k3' }],
    ]);
  });

  it('archives and restores with no body, answering nothing on a 204', async () => {
    api.answer = { ok: true, status: 204, value: undefined };
    expect(await client.archive('apple-pie', SUMMARY.id, 'k1')).toEqual({
      ok: true,
      status: 204,
      value: undefined,
    });
    expect(await client.restore('apple-pie', SUMMARY.id, 'k2')).toEqual({
      ok: true,
      status: 204,
      value: undefined,
    });
    expect(api.calls.map((c) => [c.path, c.options])).toEqual([
      [`sites/apple-pie/recipes/${SUMMARY.id}/archive`, { idempotencyKey: 'k1' }],
      [`sites/apple-pie/recipes/${SUMMARY.id}/restore`, { idempotencyKey: 'k2' }],
    ]);
  });

  // From the contract check: only the list's own parameters are sent, whatever else the object carries.
  it('sends only the named list parameters, and no limit that is not a whole number', async () => {
    api.answer = { ok: true, status: 200, value: { items: [], nextCursor: null } };
    const screenState = { tab: 'archived', siteId: 'x', search: 'pie', limit: Number.NaN };
    await client.list('apple-pie', screenState);
    expect(api.calls[0].options).toEqual({ query: { search: 'pie' } });
  });

  it('reads the detail a create answers', async () => {
    api.answer = { ok: true, status: 201, value: DETAIL };
    expect(await client.create('apple-pie', CONTENT, 'k')).toEqual({
      ok: true,
      status: 201,
      value: DETAIL,
    });
  });

  it('passes a refusal through as it came', async () => {
    const problem = {
      status: 403,
      code: 'recipe-not-yours',
      title: 'Only an editor or owner can change it.',
    };
    api.answer = { ok: false, problem };
    expect(await client.archive('apple-pie', SUMMARY.id, 'k')).toEqual({ ok: false, problem });
    expect(await client.list('apple-pie')).toEqual({ ok: false, problem });
  });

  it('turns an answer it cannot read into the unexpected problem, keeping the status', async () => {
    api.answer = {
      ok: true,
      status: 200,
      value: { items: [{ ...SUMMARY, canEdit: 'yes' }], nextCursor: null },
    };
    const result = await client.list('apple-pie');
    expect(result).toEqual({
      ok: false,
      problem: expect.objectContaining({ status: 200, code: 'unexpected-error' }),
    });
  });
});

describe('the recipe readers', () => {
  it('read a whole recipe exactly as the API writes it', () => {
    expect(readRecipeDetail(DETAIL)).toEqual(DETAIL);
  });

  it('read an unparsed line with its reason and nothing parsed', () => {
    const unparsed = {
      ...LINE,
      originalText: '2 3 cups',
      status: 'unparsed',
      problem: 'ambiguous-quantity',
      quantity: null,
      quantitySpan: null,
      unitCode: null,
      unitSpan: null,
      item: null,
      note: null,
    };
    const detail = { ...DETAIL, ingredientGroups: [{ id: 'g', heading: null, lines: [unparsed] }] };
    expect(readRecipeDetail(detail)?.ingredientGroups[0].lines[0]).toEqual(unparsed);
  });

  it.each([
    ['a missing version', { ...DETAIL, version: undefined }],
    ['a fractional time', { ...DETAIL, prepMinutes: 4.5 }],
    ['a list that is not one', { ...DETAIL, notes: 'none' }],
    ['a word that is not text', { ...DETAIL, courses: [1] }],
    ['a nutrient that is text', { ...DETAIL, nutrition: { ...DETAIL.nutrition, calories: '410' } }],
    [
      'an unknown line status',
      {
        ...DETAIL,
        ingredientGroups: [{ id: 'g', heading: null, lines: [{ ...LINE, status: 'maybe' }] }],
      },
    ],
    [
      'a step without text',
      { ...DETAIL, stepGroups: [{ id: 's', heading: null, steps: [{ id: 'x' }] }] },
    ],
    [
      'an empty span',
      {
        ...DETAIL,
        ingredientGroups: [
          { id: 'g', heading: null, lines: [{ ...LINE, unitSpan: { start: 0, length: 0 } }] },
        ],
      },
    ],
    [
      'a parse problem the contract does not name',
      {
        ...DETAIL,
        ingredientGroups: [
          { id: 'g', heading: null, lines: [{ ...LINE, status: 'unparsed', problem: 'odd' }] },
        ],
      },
    ],
    ['an edit time that is no time', { ...DETAIL, updatedAt: 'yesterday' }],
    ['an archive time with no offset', { ...DETAIL, archivedAt: '2026-11-14T12:00:00' }],
  ])('refuse a recipe with %s', (_, body) => {
    expect(readRecipeDetail(body)).toBeNull();
  });

  it.each([
    [{ kind: 'amount', low: { numerator: 1, denominator: 2 }, high: null }, true],
    [{ kind: 'amount', low: null, high: null }, false],
    [
      {
        kind: 'range',
        low: { numerator: 2, denominator: 1 },
        high: { numerator: 3, denominator: 1 },
      },
      true,
    ],
    [{ kind: 'range', low: { numerator: 2, denominator: 1 }, high: null }, false],
    [{ kind: 'to-taste', low: null, high: null }, true],
    [{ kind: 'to-taste', low: { numerator: 1, denominator: 1 }, high: null }, false],
    [{ kind: 'amount', low: { numerator: 0.5, denominator: 1 }, high: null }, false],
    [{ kind: 'amount', low: { numerator: 0, denominator: 1 }, high: null }, false],
    [{ kind: 'some', low: null, high: null }, false],
  ])('read a quantity only in its own shape (%j)', (body, readable) => {
    expect(readRecipeQuantity(body) !== null).toBe(readable);
  });

  it('read a page and a vocabulary, and refuse either when a part is not one', () => {
    expect(readRecipeSummaryPage({ items: [SUMMARY], nextCursor: null })).toEqual({
      items: [SUMMARY],
      nextCursor: null,
    });
    expect(readRecipeSummaryPage({ items: [SUMMARY], nextCursor: 3 })).toBeNull();
    expect(
      readRecipeSummaryPage({ items: [{ ...SUMMARY, archivedAt: 5 }], nextCursor: null }),
    ).toBeNull();
    expect(readRecipeVocabulary(VOCABULARY)).toEqual(VOCABULARY);
    expect(readRecipeVocabulary({ ...VOCABULARY, diets: [{ code: 'vegan' }] })).toBeNull();
    expect(readRecipeVocabulary({ ...VOCABULARY, allergens: undefined })).toBeNull();
  });
});
