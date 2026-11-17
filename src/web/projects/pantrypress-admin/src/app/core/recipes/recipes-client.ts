import { inject, Injectable } from '@angular/core';
import { ApiClient } from '../api/api-client';
import { apiPath } from '../api/api-path';
import { ApiResult, isRecord, UNEXPECTED_CODE, UNEXPECTED_TITLE } from '../api/api-problem';

/** An exact amount (PP-D05): a fraction in lowest terms, both terms 1 to 1,000,000. Never a floating-point number. */
export interface RecipeFraction {
  readonly numerator: number;
  readonly denominator: number;
}

/** How a quantity is written, as the API spells it. A yield is never `to-taste`. */
export type QuantityKind = 'amount' | 'range' | 'to-taste';

/** An amount, a range (`high` above `low`), or to taste (neither). */
export interface RecipeQuantity {
  readonly kind: QuantityKind;
  readonly low: RecipeFraction | null;
  readonly high: RecipeFraction | null;
}

/** Where a part sits in a line, in UTF-16 code units, the way JavaScript indexes a string. */
export interface RecipeTextSpan {
  readonly start: number;
  readonly length: number;
}

/** Nutrition per serving, exactly as the blogger entered it (RCP-006). Each amount is 0 to 9,999,999.99. */
export interface RecipeNutrition {
  readonly servingSize: string | null;
  readonly calories: number | null;
  readonly fatGrams: number | null;
  readonly saturatedFatGrams: number | null;
  readonly transFatGrams: number | null;
  readonly unsaturatedFatGrams: number | null;
  readonly cholesterolMilligrams: number | null;
  readonly sodiumMilligrams: number | null;
  readonly carbohydrateGrams: number | null;
  readonly fiberGrams: number | null;
  readonly sugarGrams: number | null;
  readonly proteinGrams: number | null;
}

/** One ingredient line to save: the blogger's words alone. The server reads it again; nothing parsed is ever sent. */
export interface RecipeIngredientLineInput {
  readonly id: string | null;
  readonly originalText: string;
}

export interface RecipeIngredientGroupInput {
  readonly id: string | null;
  readonly heading: string | null;
  readonly lines: readonly RecipeIngredientLineInput[];
}

export interface RecipeStepInput {
  readonly id: string | null;
  readonly text: string;
}

export interface RecipeStepGroupInput {
  readonly id: string | null;
  readonly heading: string | null;
  readonly steps: readonly RecipeStepInput[];
}

/**
 * A whole recipe as the editor saves it (`RecipeContent`): every field and every part, in order; what is left out is
 * cleared. A part keeps its row by sending the `id` the recipe gave it; a new part sends `null`, and create sends none.
 */
export interface RecipeContent {
  readonly title: string;
  readonly summary: string | null;
  readonly yield: RecipeQuantity | null;
  readonly yieldUnit: string | null;
  readonly prepMinutes: number | null;
  readonly cookMinutes: number | null;
  readonly additionalMinutes: number | null;
  readonly additionalTimeLabel: string | null;
  readonly videoUrl: string | null;
  readonly nutrition: RecipeNutrition | null;
  readonly ingredientGroups: readonly RecipeIngredientGroupInput[];
  readonly stepGroups: readonly RecipeStepGroupInput[];
  readonly equipment: readonly string[];
  readonly notes: readonly string[];
  readonly keywords: readonly string[];
  readonly courses: readonly string[];
  readonly cuisines: readonly string[];
  readonly diets: readonly string[];
  readonly allergens: readonly string[];
}

/** Why an unparsed line could not be read (`IngredientParseProblem`), a closed wire contract (recipes.md, PP4.6). */
export type IngredientParseProblem =
  | 'empty'
  | 'too-long'
  | 'ill-formed-text'
  | 'unbalanced-parentheses'
  | 'unreadable-quantity'
  | 'reversed-range'
  | 'quantity-out-of-range'
  | 'ambiguous-quantity'
  | 'no-item'
  | 'item-too-long'
  | 'note-too-long';

const PARSE_PROBLEMS: readonly string[] = [
  'empty',
  'too-long',
  'ill-formed-text',
  'unbalanced-parentheses',
  'unreadable-quantity',
  'reversed-range',
  'quantity-out-of-range',
  'ambiguous-quantity',
  'no-item',
  'item-too-long',
  'note-too-long',
] satisfies IngredientParseProblem[];

/** Whether the server could read a line, as the API spells it. An unparsed line is kept as written and flagged. */
export type IngredientParseStatus = 'parsed' | 'unparsed';

/** One ingredient line: the blogger's words, and what the server read from them (RCP-002). */
export interface RecipeIngredientLineDetail {
  readonly id: string;
  readonly originalText: string;
  readonly status: IngredientParseStatus;
  /** Why an unparsed line could not be read, in the API's kebab-case (`ambiguous-quantity`); null when parsed. */
  readonly problem: IngredientParseProblem | null;
  readonly quantity: RecipeQuantity | null;
  readonly quantitySpan: RecipeTextSpan | null;
  readonly unitCode: string | null;
  readonly unitSpan: RecipeTextSpan | null;
  readonly item: string | null;
  readonly note: string | null;
}

export interface RecipeIngredientGroupDetail {
  readonly id: string;
  readonly heading: string | null;
  readonly lines: readonly RecipeIngredientLineDetail[];
}

export interface RecipeStepDetail {
  readonly id: string;
  readonly text: string;
}

export interface RecipeStepGroupDetail {
  readonly id: string;
  readonly heading: string | null;
  readonly steps: readonly RecipeStepDetail[];
}

/** A recipe's working copy, whole (`RecipeDetail`). Send `version` back unchanged with the next save. */
export interface RecipeDetail {
  readonly id: string;
  readonly version: string;
  readonly title: string;
  readonly summary: string | null;
  readonly yield: RecipeQuantity | null;
  readonly yieldUnit: string | null;
  readonly prepMinutes: number | null;
  readonly cookMinutes: number | null;
  readonly additionalMinutes: number | null;
  readonly additionalTimeLabel: string | null;
  readonly videoUrl: string | null;
  readonly nutrition: RecipeNutrition | null;
  readonly ingredientGroups: readonly RecipeIngredientGroupDetail[];
  readonly stepGroups: readonly RecipeStepGroupDetail[];
  readonly equipment: readonly string[];
  readonly notes: readonly string[];
  readonly keywords: readonly string[];
  readonly courses: readonly string[];
  readonly cuisines: readonly string[];
  readonly diets: readonly string[];
  readonly allergens: readonly string[];
  readonly createdAt: string;
  readonly updatedAt: string;
  readonly archivedAt: string | null;
  /** Whether the caller may change, archive, and restore it. Shown, never enforced here: the API decides. */
  readonly canEdit: boolean;
}

/** One recipe in a list (`RecipeSummary`). */
export interface RecipeSummary {
  readonly id: string;
  readonly title: string;
  readonly updatedAt: string;
  readonly archivedAt: string | null;
  readonly canEdit: boolean;
}

/** A page of recipes, most recently edited first; `nextCursor` is null on the last. */
export interface RecipeSummaryPage {
  readonly items: readonly RecipeSummary[];
  readonly nextCursor: string | null;
}

/**
 * Which recipes to list (PP4.12a). A cursor continues only the list it came from, so it is sent with the same
 * `archived`, `search`, and codes as the page that gave it; the API refuses it under any other.
 */
export interface RecipeListQuery {
  readonly limit?: number;
  readonly cursor?: string;
  readonly archived?: boolean;
  /** Text the title contains, as typed, ignoring case and accents; at most `MAX_SEARCH_LENGTH` once trimmed. */
  readonly search?: string;
  readonly course?: string;
  readonly cuisine?: string;
  readonly diet?: string;
}

/** One word of the recipe vocabulary: the code a recipe stores, and what it is called. */
export interface RecipeVocabularyWord {
  readonly code: string;
  readonly name: string;
}

/** The words a recipe is filed under, each list in the vocabulary's own order. The same for every site. */
export interface RecipeVocabulary {
  readonly courses: readonly RecipeVocabularyWord[];
  readonly cuisines: readonly RecipeVocabularyWord[];
  readonly diets: readonly RecipeVocabularyWord[];
  readonly allergens: readonly RecipeVocabularyWord[];
}

/** No recipe on this site has that id - unknown, or another site's (404): one answer. */
export const RECIPE_NOT_FOUND_CODE = 'recipe-not-found';

/** The caller is an author, and someone else made this recipe (403). */
export const RECIPE_NOT_YOURS_CODE = 'recipe-not-yours';

/** The recipe is archived; restore it before saving (409). */
export const RECIPE_ARCHIVED_CODE = 'recipe-archived';

/** The recipe changed since this version was read (409). Read it again, and save with a new `Idempotency-Key`. */
export const RECIPE_CHANGED_CODE = 'recipe-changed';

/** The longest search the list takes, once trimmed (`RecipeListFilter.MaxSearchLength`). */
export const MAX_SEARCH_LENGTH = 100;

/**
 * A site's recipes (PP4.11, PP4.12a) on ApiService's admin surface, through the gateway. Every call names the site by
 * the route's slug alone, and the site policy decides (auth.md); a recipe is named by its id within that site. No call
 * sends a site id, and no ingredient line is sent parsed: the server reads every line again.
 *
 * Every write takes the caller's `Idempotency-Key`: one per thing the person did, sent again with its retry, and
 * renewed after a refusal the person can fix, since the API replays a stored refusal for a day (PP1.9).
 */
@Injectable({ providedIn: 'root' })
export class RecipesClient {
  private readonly api = inject(ApiClient);

  /** A page of the site's recipes; only the options given are sent. */
  async list(slug: string, options: RecipeListQuery = {}): Promise<ApiResult<RecipeSummaryPage>> {
    // Only the list's own parameters, by name: an object carrying anything else - a screen's state, a site - sends none
    // of it. An empty text is no filter, and a limit that is not a whole number is not sent.
    const query: Record<string, string | number | boolean> = {};
    if (Number.isInteger(options.limit)) {
      query['limit'] = options.limit as number;
    }
    if (options.archived !== undefined) {
      query['archived'] = options.archived;
    }
    for (const name of ['cursor', 'search', 'course', 'cuisine', 'diet'] as const) {
      const value = options[name];
      if (typeof value === 'string' && value !== '') {
        query[name] = value;
      }
    }
    return read(
      await this.api.get<unknown>(apiPath`sites/${slug}/recipes`, { query }),
      readRecipeSummaryPage,
    );
  }

  /** The words a recipe is filed under. Platform reference data, the same on every site. */
  async vocabulary(slug: string): Promise<ApiResult<RecipeVocabulary>> {
    return read(
      await this.api.get<unknown>(apiPath`sites/${slug}/recipes/vocabulary`),
      readRecipeVocabulary,
    );
  }

  /**
   * One recipe's working copy, archived or not. 404 `recipe-not-found` for a well-formed id that is not this site's; an id
   * that is not a GUID never reaches the route and gets the plain 404 `not-found`, so a caller treats both as not found.
   */
  async get(slug: string, id: string): Promise<ApiResult<RecipeDetail>> {
    return read(
      await this.api.get<unknown>(apiPath`sites/${slug}/recipes/${id}`),
      readRecipeDetail,
    );
  }

  /** Creates a recipe made by the caller (201). Its parts send no ids. */
  async create(
    slug: string,
    content: RecipeContent,
    idempotencyKey: string,
  ): Promise<ApiResult<RecipeDetail>> {
    return read(
      await this.api.post<unknown>(apiPath`sites/${slug}/recipes`, {
        body: content,
        idempotencyKey,
      }),
      readRecipeDetail,
    );
  }

  /** Replaces the working copy, if it is still `version`. Send the answer's `version` with the next save. */
  async update(
    slug: string,
    id: string,
    version: string,
    content: RecipeContent,
    idempotencyKey: string,
  ): Promise<ApiResult<RecipeDetail>> {
    return read(
      await this.api.put<unknown>(apiPath`sites/${slug}/recipes/${id}`, {
        body: { version, content },
        idempotencyKey,
      }),
      readRecipeDetail,
    );
  }

  /** Takes the recipe out of the library (204). What readers see is unchanged; archiving it again changes nothing. */
  async archive(slug: string, id: string, idempotencyKey: string): Promise<ApiResult<void>> {
    return empty(
      await this.api.post<unknown>(apiPath`sites/${slug}/recipes/${id}/archive`, {
        idempotencyKey,
      }),
    );
  }

  /** Puts an archived recipe back in the library (204). Restoring one already there changes nothing. */
  async restore(slug: string, id: string, idempotencyKey: string): Promise<ApiResult<void>> {
    return empty(
      await this.api.post<unknown>(apiPath`sites/${slug}/recipes/${id}/restore`, {
        idempotencyKey,
      }),
    );
  }

  /** A copy made by the caller, in the library, titled "Copy of …" where that fits (201). */
  async duplicate(
    slug: string,
    id: string,
    idempotencyKey: string,
  ): Promise<ApiResult<RecipeDetail>> {
    return read(
      await this.api.post<unknown>(apiPath`sites/${slug}/recipes/${id}/duplicate`, {
        idempotencyKey,
      }),
      readRecipeDetail,
    );
  }
}

const UNREADABLE = (status: number): ApiResult<never> => ({
  ok: false,
  problem: { status, code: UNEXPECTED_CODE, title: UNEXPECTED_TITLE },
});

function read<T>(result: ApiResult<unknown>, reader: (body: unknown) => T | null): ApiResult<T> {
  if (!result.ok) {
    return result;
  }
  const value = reader(result.value);
  return value === null ? UNREADABLE(result.status) : { ok: true, status: result.status, value };
}

function empty(result: ApiResult<unknown>): ApiResult<void> {
  return result.ok ? { ok: true, status: result.status, value: undefined } : result;
}

// ---- Readers: each answer is checked field by field, and anything else is not one. ----

type Reader<T> = (value: unknown) => T | null;

const isString = (value: unknown): value is string => typeof value === 'string';
const isParseProblem = (value: unknown): value is IngredientParseProblem =>
  typeof value === 'string' && PARSE_PROBLEMS.includes(value);
// The API writes UTC ISO 8601 with an offset (api-contract.md); anything a Date cannot read is not one.
const isTimestamp = (value: unknown): value is string =>
  typeof value === 'string' &&
  /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/.test(value) &&
  !Number.isNaN(Date.parse(value));
const isWhole = (value: unknown): value is number =>
  typeof value === 'number' && Number.isInteger(value);
const isAmount = (value: unknown): value is number =>
  typeof value === 'number' && Number.isFinite(value);

function optional<T>(value: unknown, test: (v: unknown) => v is T): T | null | undefined {
  if (value === null || value === undefined) {
    return null;
  }
  return test(value) ? value : undefined;
}

function optionalOf<T>(value: unknown, reader: Reader<T>): T | null | undefined {
  if (value === null || value === undefined) {
    return null;
  }
  return reader(value) ?? undefined;
}

function listOf<T>(value: unknown, reader: Reader<T>): T[] | null {
  if (!Array.isArray(value)) {
    return null;
  }
  const items: T[] = [];
  for (const item of value as unknown[]) {
    const read = reader(item);
    if (read === null) {
      return null;
    }
    items.push(read);
  }
  return items;
}

const readString: Reader<string> = (value) => (isString(value) ? value : null);

/** The API's `RecipeFraction`: two whole numbers above zero. */
export const readRecipeFraction: Reader<RecipeFraction> = (body) =>
  isRecord(body) &&
  isWhole(body['numerator']) &&
  isWhole(body['denominator']) &&
  body['numerator'] > 0 &&
  body['denominator'] > 0
    ? { numerator: body['numerator'], denominator: body['denominator'] }
    : null;

/** The API's `RecipeQuantity`: an amount has a low end only, a range both, to taste neither. */
export const readRecipeQuantity: Reader<RecipeQuantity> = (body) => {
  if (!isRecord(body)) {
    return null;
  }
  const low = optionalOf(body['low'], readRecipeFraction);
  const high = optionalOf(body['high'], readRecipeFraction);
  if (low === undefined || high === undefined) {
    return null;
  }
  switch (body['kind']) {
    case 'amount':
      return low !== null && high === null ? { kind: 'amount', low, high } : null;
    case 'range':
      return low !== null && high !== null ? { kind: 'range', low, high } : null;
    case 'to-taste':
      return low === null && high === null ? { kind: 'to-taste', low, high } : null;
    default:
      return null;
  }
};

const readSpan: Reader<RecipeTextSpan> = (body) =>
  isRecord(body) &&
  isWhole(body['start']) &&
  isWhole(body['length']) &&
  body['start'] >= 0 &&
  body['length'] > 0
    ? { start: body['start'], length: body['length'] }
    : null;

const NUTRIENTS = [
  'calories',
  'fatGrams',
  'saturatedFatGrams',
  'transFatGrams',
  'unsaturatedFatGrams',
  'cholesterolMilligrams',
  'sodiumMilligrams',
  'carbohydrateGrams',
  'fiberGrams',
  'sugarGrams',
  'proteinGrams',
] as const;

const readNutrition: Reader<RecipeNutrition> = (body) => {
  if (!isRecord(body)) {
    return null;
  }
  const servingSize = optional(body['servingSize'], isString);
  if (servingSize === undefined) {
    return null;
  }
  const amounts: Partial<Record<(typeof NUTRIENTS)[number], number | null>> = {};
  for (const nutrient of NUTRIENTS) {
    const amount = optional(body[nutrient], isAmount);
    if (amount === undefined) {
      return null;
    }
    amounts[nutrient] = amount;
  }
  return { servingSize, ...(amounts as Record<(typeof NUTRIENTS)[number], number | null>) };
};

const readLine: Reader<RecipeIngredientLineDetail> = (body) => {
  if (!isRecord(body) || !isString(body['id']) || !isString(body['originalText'])) {
    return null;
  }
  const status = body['status'];
  const problem = optional(body['problem'], isParseProblem);
  const quantity = optionalOf(body['quantity'], readRecipeQuantity);
  const quantitySpan = optionalOf(body['quantitySpan'], readSpan);
  const unitCode = optional(body['unitCode'], isString);
  const unitSpan = optionalOf(body['unitSpan'], readSpan);
  const item = optional(body['item'], isString);
  const note = optional(body['note'], isString);
  if (
    (status !== 'parsed' && status !== 'unparsed') ||
    [problem, quantity, quantitySpan, unitCode, unitSpan, item, note].includes(undefined)
  ) {
    return null;
  }
  return {
    id: body['id'],
    originalText: body['originalText'],
    status,
    problem: problem ?? null,
    quantity: quantity ?? null,
    quantitySpan: quantitySpan ?? null,
    unitCode: unitCode ?? null,
    unitSpan: unitSpan ?? null,
    item: item ?? null,
    note: note ?? null,
  };
};

function readGroup<T, K extends string>(
  key: K,
  reader: Reader<T>,
): Reader<{ readonly id: string; readonly heading: string | null } & Record<K, readonly T[]>> {
  return (body) => {
    if (!isRecord(body) || !isString(body['id'])) {
      return null;
    }
    const heading = optional(body['heading'], isString);
    const parts = listOf(body[key], reader);
    if (heading === undefined || parts === null) {
      return null;
    }
    return { id: body['id'], heading, [key]: parts } as {
      readonly id: string;
      readonly heading: string | null;
    } & Record<K, readonly T[]>;
  };
}

const readStep: Reader<RecipeStepDetail> = (body) =>
  isRecord(body) && isString(body['id']) && isString(body['text'])
    ? { id: body['id'], text: body['text'] }
    : null;

const readIngredientGroup: Reader<RecipeIngredientGroupDetail> = readGroup('lines', readLine);
const readStepGroup: Reader<RecipeStepGroupDetail> = readGroup('steps', readStep);

/** The API's `RecipeSummary`, checked field by field. */
export const readRecipeSummary: Reader<RecipeSummary> = (body) => {
  if (
    !isRecord(body) ||
    !isString(body['id']) ||
    !isString(body['title']) ||
    !isTimestamp(body['updatedAt']) ||
    typeof body['canEdit'] !== 'boolean'
  ) {
    return null;
  }
  const archivedAt = optional(body['archivedAt'], isTimestamp);
  if (archivedAt === undefined) {
    return null;
  }
  return {
    id: body['id'],
    title: body['title'],
    updatedAt: body['updatedAt'],
    archivedAt,
    canEdit: body['canEdit'],
  };
};

/** The API's `RecipeSummaryPage`: every item a `RecipeSummary`, and a string cursor or none. */
export const readRecipeSummaryPage: Reader<RecipeSummaryPage> = (body) => {
  if (!isRecord(body)) {
    return null;
  }
  const items = listOf(body['items'], readRecipeSummary);
  const nextCursor = optional(body['nextCursor'], isString);
  return items === null || nextCursor === undefined ? null : { items, nextCursor };
};

const readWord: Reader<RecipeVocabularyWord> = (body) =>
  isRecord(body) && isString(body['code']) && isString(body['name'])
    ? { code: body['code'], name: body['name'] }
    : null;

/** The API's `RecipeVocabulary`: four lists of words. */
export const readRecipeVocabulary: Reader<RecipeVocabulary> = (body) => {
  if (!isRecord(body)) {
    return null;
  }
  const courses = listOf(body['courses'], readWord);
  const cuisines = listOf(body['cuisines'], readWord);
  const diets = listOf(body['diets'], readWord);
  const allergens = listOf(body['allergens'], readWord);
  return courses && cuisines && diets && allergens ? { courses, cuisines, diets, allergens } : null;
};

/** The API's `RecipeDetail`, checked field by field. */
export const readRecipeDetail: Reader<RecipeDetail> = (body) => {
  const summary = readRecipeSummary(body);
  if (
    summary === null ||
    !isRecord(body) ||
    !isString(body['version']) ||
    !isTimestamp(body['createdAt'])
  ) {
    return null;
  }
  const text = (name: string) => optional(body[name], isString);
  const minutes = (name: string) => optional(body[name], isWhole);
  const summaryText = text('summary');
  const yieldUnit = text('yieldUnit');
  const additionalTimeLabel = text('additionalTimeLabel');
  const videoUrl = text('videoUrl');
  const prepMinutes = minutes('prepMinutes');
  const cookMinutes = minutes('cookMinutes');
  const additionalMinutes = minutes('additionalMinutes');
  const quantity = optionalOf(body['yield'], readRecipeQuantity);
  const nutrition = optionalOf(body['nutrition'], readNutrition);
  const ingredientGroups = listOf(body['ingredientGroups'], readIngredientGroup);
  const stepGroups = listOf(body['stepGroups'], readStepGroup);
  const lists = (
    ['equipment', 'notes', 'keywords', 'courses', 'cuisines', 'diets', 'allergens'] as const
  ).map((name) => listOf(body[name], readString));
  if (
    [
      summaryText,
      yieldUnit,
      additionalTimeLabel,
      videoUrl,
      prepMinutes,
      cookMinutes,
      additionalMinutes,
      quantity,
      nutrition,
    ].includes(undefined) ||
    ingredientGroups === null ||
    stepGroups === null ||
    lists.includes(null)
  ) {
    return null;
  }
  const [equipment, notes, keywords, courses, cuisines, diets, allergens] = lists as string[][];
  return {
    id: summary.id,
    version: body['version'],
    title: summary.title,
    summary: summaryText ?? null,
    yield: quantity ?? null,
    yieldUnit: yieldUnit ?? null,
    prepMinutes: prepMinutes ?? null,
    cookMinutes: cookMinutes ?? null,
    additionalMinutes: additionalMinutes ?? null,
    additionalTimeLabel: additionalTimeLabel ?? null,
    videoUrl: videoUrl ?? null,
    nutrition: nutrition ?? null,
    ingredientGroups,
    stepGroups,
    equipment,
    notes,
    keywords,
    courses,
    cuisines,
    diets,
    allergens,
    createdAt: body['createdAt'],
    updatedAt: summary.updatedAt,
    archivedAt: summary.archivedAt,
    canEdit: summary.canEdit,
  };
};
