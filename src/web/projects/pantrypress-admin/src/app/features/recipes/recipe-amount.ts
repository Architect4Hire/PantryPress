import { RecipeFraction } from '../../core/recipes/recipes-client';

/**
 * A yield's amount as the blogger types it, put into the API's form: an exact fraction (PP-D05), never a floating-point
 * number. This is only a format. Whether an amount is in bounds, or a range rises, is the server's to say, so anything
 * readable is passed on as read, and the server's refusal is shown in its own words.
 *
 * Readable: a whole number (`8`), a decimal (`1.5`), a fraction (`3/4`, `3⁄4`), a mixed number (`1 1/2`, `1½`, `1 ½`),
 * and a vulgar fraction alone (`½`). Digits are ASCII only; a number of more than nine digits is not read, so every
 * step stays an exact whole number.
 */

const VULGAR: Readonly<Record<string, readonly [number, number]>> = {
  '½': [1, 2],
  '⅓': [1, 3],
  '⅔': [2, 3],
  '¼': [1, 4],
  '¾': [3, 4],
  '⅕': [1, 5],
  '⅖': [2, 5],
  '⅗': [3, 5],
  '⅘': [4, 5],
  '⅙': [1, 6],
  '⅚': [5, 6],
  '⅐': [1, 7],
  '⅛': [1, 8],
  '⅜': [3, 8],
  '⅝': [5, 8],
  '⅞': [7, 8],
  '⅑': [1, 9],
  '⅒': [1, 10],
};

/** The fractions written as one character when an amount is shown (as the recipe card writes them, PP4.2). */
const SHOWN: Readonly<Record<string, string>> = {
  '1/2': '½',
  '1/3': '⅓',
  '2/3': '⅔',
  '1/4': '¼',
  '3/4': '¾',
  '1/8': '⅛',
  '3/8': '⅜',
  '5/8': '⅝',
  '7/8': '⅞',
};

const DIGITS = /^[0-9]{1,9}$/;

/** What a typed amount reads as: nothing typed, an amount, or text that is not one. */
export type AmountReading =
  | { readonly kind: 'empty' }
  | { readonly kind: 'amount'; readonly value: RecipeFraction }
  | { readonly kind: 'unreadable' };

const EMPTY: AmountReading = { kind: 'empty' };
const UNREADABLE: AmountReading = { kind: 'unreadable' };

export function readAmount(text: string): AmountReading {
  const typed = text.trim();
  if (typed === '') {
    return EMPTY;
  }
  // A vulgar fraction joined to a whole number (`1½`) or after a space (`1 ½`), or alone.
  const last = typed.at(-1) as string;
  const vulgar = VULGAR[last];
  if (vulgar !== undefined) {
    const whole = typed.slice(0, -1).trimEnd();
    if (whole === '') {
      return amount(vulgar[0], vulgar[1]);
    }
    return DIGITS.test(whole)
      ? amount(Number(whole) * vulgar[1] + vulgar[0], vulgar[1])
      : UNREADABLE;
  }
  const parts = typed.split(/\s+/);
  if (parts.length === 2) {
    const fraction = readFraction(parts[1]);
    return DIGITS.test(parts[0]) && fraction !== null
      ? amount(Number(parts[0]) * fraction[1] + fraction[0], fraction[1])
      : UNREADABLE;
  }
  if (parts.length !== 1) {
    return UNREADABLE;
  }
  if (DIGITS.test(typed)) {
    return amount(Number(typed), 1);
  }
  const decimal = /^([0-9]{0,9})\.([0-9]{1,6})$/.exec(typed);
  if (decimal !== null) {
    const places = decimal[2].length;
    const denominator = 10 ** places;
    return amount(Number(decimal[1] || '0') * denominator + Number(decimal[2]), denominator);
  }
  const fraction = readFraction(typed);
  return fraction === null ? UNREADABLE : amount(fraction[0], fraction[1]);
}

/** An amount as the editor shows it: `8`, `½`, `1½`, `2 5/16`. */
export function writeAmount(value: RecipeFraction): string {
  const whole = Math.floor(value.numerator / value.denominator);
  const rest = value.numerator - whole * value.denominator;
  if (rest === 0) {
    return String(whole);
  }
  const fraction = `${rest}/${value.denominator}`;
  const shown = SHOWN[fraction];
  if (whole === 0) {
    return shown ?? fraction;
  }
  return shown === undefined ? `${whole} ${fraction}` : `${whole}${shown}`;
}

function readFraction(text: string): readonly [number, number] | null {
  const match = /^([0-9]{1,9})[/⁄]([0-9]{1,9})$/.exec(text);
  return match === null ? null : [Number(match[1]), Number(match[2])];
}

function amount(numerator: number, denominator: number): AmountReading {
  // A whole number and a nine-digit fraction can pass what a double holds exactly; such an amount is not read at all.
  if (!Number.isSafeInteger(numerator) || !Number.isSafeInteger(denominator)) {
    return UNREADABLE;
  }
  // Zero, and a zero denominator, are passed on whole so the server says why; only lowest terms are this file's.
  if (numerator === 0 || denominator === 0) {
    return { kind: 'amount', value: { numerator, denominator } };
  }
  const divisor = gcd(numerator, denominator);
  return {
    kind: 'amount',
    value: { numerator: numerator / divisor, denominator: denominator / divisor },
  };
}

function gcd(a: number, b: number): number {
  while (b !== 0) {
    [a, b] = [b, a % b];
  }
  return a;
}
