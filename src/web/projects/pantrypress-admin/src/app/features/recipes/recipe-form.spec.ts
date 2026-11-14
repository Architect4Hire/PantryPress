import {
  changedGroups,
  differsFrom,
  EMPTY_FIELDS,
  fieldErrorsFrom,
  fieldsFrom,
  NOT_A_NUMBER,
  NOT_A_TIME,
  placeErrors,
  readFields,
  readKeywords,
  restoreFields,
  withGroups,
} from './recipe-form';
import { recipeDetail } from './recipe-fixtures.spec-helper';

describe('the recipe form (PP4.13)', () => {
  const base = recipeDetail();

  it('shows a recipe as typed text', () => {
    expect(fieldsFrom(base)).toEqual({
      title: 'Apple pie',
      summary: 'A deep-dish pie.',
      videoUrl: '',
      courses: ['dessert'],
      cuisines: ['american'],
      diets: [],
      allergens: [],
      keywords: 'pie, fall baking',
      prep: { hours: '', minutes: '30' },
      cook: { hours: '1', minutes: '15' },
      additional: { hours: '', minutes: '' },
      additionalTimeLabel: '',
      yieldLow: '8',
      yieldHigh: '',
      yieldUnit: 'servings',
    });
  });

  it('saves every part it does not edit exactly as the server gave it, with its ids', () => {
    const read = readFields(fieldsFrom(base), base);
    expect(read.ok).toBe(true);
    if (!read.ok) return;
    expect(read.content.ingredientGroups).toEqual([
      { id: 'g1', heading: 'For the crust', lines: [{ id: 'l1', originalText: '2 ½ cups flour' }] },
    ]);
    expect(read.content.stepGroups).toEqual([
      { id: 's1', heading: null, steps: [{ id: 'st1', text: 'Make the crust.' }] },
    ]);
    expect(read.content.nutrition).toBe(base.nutrition);
    expect(read.content.equipment).toEqual(['9-inch pie dish']);
    expect(read.content.notes).toEqual(['Keeps two days.']);
    // No parsed part of a line is ever sent (recipes.md, PP4.6).
    expect(JSON.stringify(read.content)).not.toContain('quantitySpan');
  });

  it('sends text exactly as typed, and empty text as none', () => {
    const read = readFields(
      {
        ...fieldsFrom(base),
        title: '  Apple pie ',
        summary: '   ',
        yieldUnit: '',
        videoUrl: 'https://youtu.be/x',
      },
      base,
    );
    expect(read.ok && read.content.title).toBe('  Apple pie ');
    expect(read.ok && read.content.summary).toBeNull();
    expect(read.ok && read.content.yieldUnit).toBeNull();
    expect(read.ok && read.content.videoUrl).toBe('https://youtu.be/x');
  });

  it('reads times in minutes, zero as entered and empty as none', () => {
    const read = readFields(
      {
        ...EMPTY_FIELDS,
        title: 'Toast',
        prep: { hours: '', minutes: '0' },
        cook: { hours: '1', minutes: '5' },
        additional: { hours: '', minutes: '' },
        additionalTimeLabel: 'Rising',
      },
      null,
    );
    expect(read.ok).toBe(true);
    if (!read.ok) return;
    expect([
      read.content.prepMinutes,
      read.content.cookMinutes,
      read.content.additionalMinutes,
    ]).toEqual([0, 65, null]);
    // The label goes with its time: with none it is not sent.
    expect(read.content.additionalTimeLabel).toBeNull();
  });

  it('reads a yield as an amount, a range, or none', () => {
    const yieldOf = (low: string, high: string) => {
      const read = readFields(
        { ...EMPTY_FIELDS, title: 'T', yieldLow: low, yieldHigh: high },
        null,
      );
      return read.ok ? read.content.yield : 'refused';
    };
    expect(yieldOf('', '')).toBeNull();
    expect(yieldOf('1½', '')).toEqual({
      kind: 'amount',
      low: { numerator: 3, denominator: 2 },
      high: null,
    });
    expect(yieldOf('2', '3')).toEqual({
      kind: 'range',
      low: { numerator: 2, denominator: 1 },
      high: { numerator: 3, denominator: 1 },
    });
    // Whether a range rises is the server's to say; the form passes it on.
    expect(yieldOf('3', '2')).toEqual({
      kind: 'range',
      low: { numerator: 3, denominator: 1 },
      high: { numerator: 2, denominator: 1 },
    });
    expect(yieldOf('', '3')).toEqual({
      kind: 'range',
      low: null,
      high: { numerator: 3, denominator: 1 },
    });
  });

  it('refuses only what it cannot put into the API’s form, at the API’s own paths', () => {
    const read = readFields(
      {
        ...EMPTY_FIELDS,
        title: 'T',
        yieldLow: 'eight',
        yieldHigh: '1,5',
        cook: { hours: 'one', minutes: '' },
      },
      null,
    );
    expect(read).toEqual({
      ok: false,
      errors: {
        cookMinutes: [NOT_A_TIME],
        'yield.low': [NOT_A_NUMBER],
        'yield.high': [NOT_A_NUMBER],
      },
    });
  });

  it('keeps keywords as the server holds them until they are edited', () => {
    const withComma = recipeDetail({ keywords: ['pie, deep dish', 'fall'] });
    const read = readFields(fieldsFrom(withComma), withComma);
    expect(read.ok && read.content.keywords).toEqual(['pie, deep dish', 'fall']);
    expect(readKeywords(' pie ,, fall baking,')).toEqual(['pie', 'fall baking']);
    // Edited, a stored keyword with a comma of its own stays one keyword while its words are unchanged.
    const edited = readFields(
      { ...fieldsFrom(withComma), keywords: 'pie, deep dish, fall, apples' },
      withComma,
    );
    expect(edited.ok && edited.content.keywords).toEqual(['pie, deep dish', 'fall', 'apples']);
    expect(readKeywords('pie,  deep dish', ['pie, deep dish'])).toEqual(['pie', 'deep dish']);
  });

  it('sees no change in the same recipe written two ways', () => {
    expect(differsFrom({ ...fieldsFrom(base), yieldLow: '8.0' }, base)).toBe(false);
    expect(differsFrom({ ...fieldsFrom(base), cook: { hours: '', minutes: '75' } }, base)).toBe(
      false,
    );
    expect(differsFrom({ ...fieldsFrom(base), yieldLow: '9' }, base)).toBe(true);
    expect(differsFrom({ ...fieldsFrom(base), yieldLow: 'nine' }, base)).toBe(true);
  });

  it('tracks edits by group, and lays edited groups over a newer copy', () => {
    const mine = { ...fieldsFrom(base), title: 'Mine', yieldUnit: 'slices' };
    expect(changedGroups(fieldsFrom(base), mine)).toEqual(['title', 'yield']);
    const theirs = { ...fieldsFrom(base), summary: 'Theirs', yieldLow: '10' };
    const merged = withGroups(theirs, mine, ['title', 'yield']);
    expect(merged.title).toBe('Mine');
    expect(merged.summary).toBe('Theirs');
    // A group goes whole: the yield is the person's, amount and word together.
    expect([merged.yieldLow, merged.yieldUnit]).toEqual(['8', 'slices']);
  });

  it('reads a form kept on this device back only when every field is the right shape', () => {
    const kept = JSON.parse(JSON.stringify(fieldsFrom(base))) as Record<string, unknown>;
    expect(restoreFields(kept)).toEqual(fieldsFrom(base));
    expect(restoreFields({ ...kept, title: 3 })).toBeNull();
    expect(restoreFields({ ...kept, courses: [1] })).toBeNull();
    expect(restoreFields({ ...kept, prep: { hours: '1' } })).toBeNull();
  });

  it('places the server’s errors under their boxes, and the rest where they can be read', () => {
    const errors = fieldErrorsFrom({
      'content.title': ['Give the recipe a title.'],
      'content.yield.high': ['Make the high end of the range more than the low end.'],
      'content.yield.low': ['Enter an amount above zero.'],
      'content.prepMinutes': ['Enter a time from 0 to 43,200 minutes.'],
      'content.keywords[3]': ['Keep this to 60 characters.'],
      'content.ingredientGroups[0].lines[0].originalText': ['Keep this to 500 characters.'],
    });
    expect(placeErrors(errors)).toEqual({
      slots: {
        title: 'Give the recipe a title.',
        yieldHigh: 'Make the high end of the range more than the low end.',
        yieldLow: 'Enter an amount above zero.',
        prep: 'Enter a time from 0 to 43,200 minutes.',
        keywords: 'Keep this to 60 characters.',
      },
      elsewhere: ['Keep this to 500 characters.'],
    });
  });
});
