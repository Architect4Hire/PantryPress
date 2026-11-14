import { RecipeContent, RecipeDetail, RecipeQuantity } from '../../core/recipes/recipes-client';
import { readAmount, writeAmount } from './recipe-amount';
import { readMinutes, TimeText, writeMinutes } from './recipe-minutes';

/**
 * What the recipe editor's details, times, and yield sections edit (PP4.13), exactly as typed. Everything else in a
 * recipe - its ingredients, steps, equipment, notes, and nutrition - goes back to the server as the server last gave it,
 * with its ids, so a save from these sections never drops a part.
 */
export interface RecipeFields {
  readonly title: string;
  readonly summary: string;
  readonly videoUrl: string;
  readonly courses: readonly string[];
  readonly cuisines: readonly string[];
  readonly diets: readonly string[];
  readonly allergens: readonly string[];
  readonly keywords: string;
  readonly prep: TimeText;
  readonly cook: TimeText;
  readonly additional: TimeText;
  readonly additionalTimeLabel: string;
  readonly yieldLow: string;
  readonly yieldHigh: string;
  readonly yieldUnit: string;
}

/**
 * The parts of the form that change together, and are kept or given up together when the recipe changed somewhere else:
 * the ones the person edited win over the newer copy, and the rest take the newer copy's.
 */
export type RecipeFieldGroup =
  | 'title'
  | 'summary'
  | 'videoUrl'
  | 'courses'
  | 'cuisines'
  | 'diets'
  | 'allergens'
  | 'keywords'
  | 'prep'
  | 'cook'
  | 'additional'
  | 'yield';

const GROUP_FIELDS: Readonly<Record<RecipeFieldGroup, readonly (keyof RecipeFields)[]>> = {
  title: ['title'],
  summary: ['summary'],
  videoUrl: ['videoUrl'],
  courses: ['courses'],
  cuisines: ['cuisines'],
  diets: ['diets'],
  allergens: ['allergens'],
  keywords: ['keywords'],
  prep: ['prep'],
  cook: ['cook'],
  additional: ['additional', 'additionalTimeLabel'],
  yield: ['yieldLow', 'yieldHigh', 'yieldUnit'],
};

export const RECIPE_FIELD_GROUPS = Object.keys(GROUP_FIELDS) as readonly RecipeFieldGroup[];

/** Which groups differ between two forms. */
export function changedGroups(before: RecipeFields, after: RecipeFields): RecipeFieldGroup[] {
  return RECIPE_FIELD_GROUPS.filter((group) =>
    GROUP_FIELDS[group].some((field) => !same(before[field], after[field])),
  );
}

/** `onto` with the given groups taken from `from`. */
export function withGroups(
  onto: RecipeFields,
  from: RecipeFields,
  groups: Iterable<RecipeFieldGroup>,
): RecipeFields {
  const next: Record<string, unknown> = { ...onto };
  for (const group of groups) {
    for (const field of GROUP_FIELDS[group]) {
      next[field] = from[field];
    }
  }
  return next as unknown as RecipeFields;
}

/** An empty form, for a new recipe. */
export const EMPTY_FIELDS: RecipeFields = {
  title: '',
  summary: '',
  videoUrl: '',
  courses: [],
  cuisines: [],
  diets: [],
  allergens: [],
  keywords: '',
  prep: { hours: '', minutes: '' },
  cook: { hours: '', minutes: '' },
  additional: { hours: '', minutes: '' },
  additionalTimeLabel: '',
  yieldLow: '',
  yieldHigh: '',
  yieldUnit: '',
};

/** A recipe as the form shows it. */
export function fieldsFrom(recipe: RecipeDetail): RecipeFields {
  return {
    title: recipe.title,
    summary: recipe.summary ?? '',
    videoUrl: recipe.videoUrl ?? '',
    courses: recipe.courses,
    cuisines: recipe.cuisines,
    diets: recipe.diets,
    allergens: recipe.allergens,
    keywords: writeKeywords(recipe.keywords),
    prep: writeMinutes(recipe.prepMinutes),
    cook: writeMinutes(recipe.cookMinutes),
    additional: writeMinutes(recipe.additionalMinutes),
    additionalTimeLabel: recipe.additionalTimeLabel ?? '',
    yieldLow: recipe.yield?.low ? writeAmount(recipe.yield.low) : '',
    yieldHigh:
      recipe.yield?.kind === 'range' && recipe.yield.high ? writeAmount(recipe.yield.high) : '',
    yieldUnit: recipe.yieldUnit ?? '',
  };
}

/** Field errors by the API's own paths (`title`, `yield.low`, `prepMinutes`), without the save's `content.` prefix. */
export type FieldErrors = Readonly<Record<string, readonly string[]>>;

export type ReadFields =
  | { readonly ok: true; readonly content: RecipeContent }
  | { readonly ok: false; readonly errors: FieldErrors };

/** Messages for what this screen cannot put into the API's form at all. Anything readable is the server's to judge. */
export const NOT_A_NUMBER = 'Enter a number, like 8, 1.5, or 1 1/2.';
export const NOT_A_TIME = 'Enter whole hours and minutes, like 1 and 30.';

/**
 * The whole recipe to save: `base`, the server's last answer, with this form laid over it. Text with nothing in it is
 * left out (null), since there are no words to keep; any other text is sent exactly as typed.
 */
export function readFields(fields: RecipeFields, base: RecipeDetail | null): ReadFields {
  const errors: Record<string, string[]> = {};
  const fail = (path: string, message: string) => (errors[path] = [message]);

  const minutes = (time: TimeText, path: string): number | null => {
    const read = readMinutes(time);
    if (read.kind === 'unreadable') {
      fail(path, NOT_A_TIME);
      return null;
    }
    return read.kind === 'minutes' ? read.value : null;
  };
  const prepMinutes = minutes(fields.prep, 'prepMinutes');
  const cookMinutes = minutes(fields.cook, 'cookMinutes');
  const additionalMinutes = minutes(fields.additional, 'additionalMinutes');

  const low = readAmount(fields.yieldLow);
  const high = readAmount(fields.yieldHigh);
  if (low.kind === 'unreadable') {
    fail('yield.low', NOT_A_NUMBER);
  }
  if (high.kind === 'unreadable') {
    fail('yield.high', NOT_A_NUMBER);
  }
  let quantity: RecipeQuantity | null = null;
  if (low.kind === 'amount') {
    quantity =
      high.kind === 'amount'
        ? { kind: 'range', low: low.value, high: high.value }
        : { kind: 'amount', low: low.value, high: null };
  } else if (low.kind === 'empty' && high.kind === 'amount') {
    // Only a high end: the server asks for the low one in its own words.
    quantity = { kind: 'range', low: null, high: high.value };
  }

  if (Object.keys(errors).length > 0) {
    return { ok: false, errors };
  }

  const keywords =
    base !== null && fields.keywords === writeKeywords(base.keywords)
      ? base.keywords
      : readKeywords(fields.keywords, base?.keywords ?? []);

  return {
    ok: true,
    content: {
      title: fields.title,
      summary: text(fields.summary),
      yield: quantity,
      yieldUnit: text(fields.yieldUnit),
      prepMinutes,
      cookMinutes,
      additionalMinutes,
      // The label belongs to the additional time: with no time it is not shown, so it is not sent, but kept in the form.
      additionalTimeLabel: additionalMinutes === null ? null : text(fields.additionalTimeLabel),
      videoUrl: text(fields.videoUrl),
      nutrition: base?.nutrition ?? null,
      ingredientGroups: (base?.ingredientGroups ?? []).map((group) => ({
        id: group.id,
        heading: group.heading,
        lines: group.lines.map((line) => ({ id: line.id, originalText: line.originalText })),
      })),
      stepGroups: (base?.stepGroups ?? []).map((group) => ({
        id: group.id,
        heading: group.heading,
        steps: group.steps.map((step) => ({ id: step.id, text: step.text })),
      })),
      equipment: base?.equipment ?? [],
      notes: base?.notes ?? [],
      keywords,
      courses: fields.courses,
      cuisines: fields.cuisines,
      diets: fields.diets,
      allergens: fields.allergens,
    },
  };
}

/** A form kept on this device, read back: every field the right shape, or nothing. */
export function restoreFields(value: Readonly<Record<string, unknown>>): RecipeFields | null {
  const texts = ['title', 'summary', 'videoUrl', 'keywords', 'additionalTimeLabel'] as const;
  const amounts = ['yieldLow', 'yieldHigh', 'yieldUnit'] as const;
  const lists = ['courses', 'cuisines', 'diets', 'allergens'] as const;
  const times = ['prep', 'cook', 'additional'] as const;
  const isText = (v: unknown): v is string => typeof v === 'string';
  const isTime = (v: unknown): v is TimeText =>
    typeof v === 'object' &&
    v !== null &&
    isText((v as Record<string, unknown>)['hours']) &&
    isText((v as Record<string, unknown>)['minutes']);
  if (
    ![...texts, ...amounts].every((name) => isText(value[name])) ||
    !lists.every(
      (name) => Array.isArray(value[name]) && (value[name] as unknown[]).every(isText),
    ) ||
    !times.every((name) => isTime(value[name]))
  ) {
    return null;
  }
  const time = (name: (typeof times)[number]) => {
    const t = value[name] as TimeText;
    return { hours: t.hours, minutes: t.minutes };
  };
  return {
    title: value['title'] as string,
    summary: value['summary'] as string,
    videoUrl: value['videoUrl'] as string,
    courses: value['courses'] as string[],
    cuisines: value['cuisines'] as string[],
    diets: value['diets'] as string[],
    allergens: value['allergens'] as string[],
    keywords: value['keywords'] as string,
    prep: time('prep'),
    cook: time('cook'),
    additional: time('additional'),
    additionalTimeLabel: value['additionalTimeLabel'] as string,
    yieldLow: value['yieldLow'] as string,
    yieldHigh: value['yieldHigh'] as string,
    yieldUnit: value['yieldUnit'] as string,
  };
}

/** Whether saving this form would change the recipe: the same recipe written two ways (`1.5`, `1½`) is no change. */
export function differsFrom(fields: RecipeFields, base: RecipeDetail): boolean {
  const now = readFields(fields, base);
  const saved = readFields(fieldsFrom(base), base);
  return !now.ok || !saved.ok || JSON.stringify(now.content) !== JSON.stringify(saved.content);
}

/** Keywords as one line, separated by commas. */
export function writeKeywords(keywords: readonly string[]): string {
  return keywords.join(', ');
}

/** The keywords in a line typed with commas between them; the space around each comma is not part of a keyword. */
export function readKeywords(text: string, known: readonly string[] = []): string[] {
  // A keyword the recipe already holds may have a comma of its own (from an import or the publishing API); while its
  // words are still there, unchanged, it stays one keyword, exactly as stored.
  const kept = new Set(known.filter((keyword) => keyword.includes(',')));
  const pieces = text.split(',');
  const keywords: string[] = [];
  for (let i = 0; i < pieces.length; i += 1) {
    let taken = 1;
    for (let j = pieces.length; j > i + 1; j -= 1) {
      if (kept.has(pieces.slice(i, j).join(',').trim())) {
        taken = j - i;
        break;
      }
    }
    const keyword = pieces
      .slice(i, i + taken)
      .join(',')
      .trim();
    if (keyword !== '') {
      keywords.push(keyword);
    }
    i += taken - 1;
  }
  return keywords;
}

/** The API's field errors by path, without the save's `content.` prefix. */
export function fieldErrorsFrom(errors: Readonly<Record<string, readonly string[]>>): FieldErrors {
  const read: Record<string, readonly string[]> = {};
  for (const [path, messages] of Object.entries(errors)) {
    const bare = path.startsWith('content.') ? path.slice('content.'.length) : path;
    read[bare] = [...(read[bare] ?? []), ...messages];
  }
  return read;
}

/** Where each of this screen's errors is shown, by the path's first name; any other path is not this screen's. */
const ERROR_SLOTS: Readonly<Record<string, string>> = {
  title: 'title',
  summary: 'summary',
  videoUrl: 'videoUrl',
  courses: 'courses',
  cuisines: 'cuisines',
  diets: 'diets',
  allergens: 'allergens',
  keywords: 'keywords',
  prepMinutes: 'prep',
  cookMinutes: 'cook',
  additionalMinutes: 'additional',
  additionalTimeLabel: 'additionalTimeLabel',
  yield: 'yieldLow',
  'yield.high': 'yieldHigh',
  yieldUnit: 'yieldUnit',
};

/** The errors gathered by where the screen shows them, and those that belong to no field here. */
export function placeErrors(errors: FieldErrors): {
  readonly slots: Readonly<Record<string, string>>;
  readonly elsewhere: readonly string[];
} {
  const slots: Record<string, string> = {};
  const elsewhere: string[] = [];
  for (const [path, messages] of Object.entries(errors)) {
    // A range's high end has its own box; every other path is placed by its first name.
    const name =
      path === 'yield.high' || path.startsWith('yield.high.')
        ? 'yield.high'
        : (/^[A-Za-z]+/.exec(path)?.[0] ?? '');
    const slot = ERROR_SLOTS[name];
    if (slot === undefined) {
      elsewhere.push(...messages);
    } else {
      slots[slot] = [slots[slot], ...messages].filter((m) => !!m).join(' ');
    }
  }
  return { slots, elsewhere: [...new Set(elsewhere)] };
}

function text(value: string): string | null {
  return value.trim() === '' ? null : value;
}

function same(a: unknown, b: unknown): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}
