/**
 * A recipe time as the editor asks for it - hours and minutes - and the whole minutes the API stores (RCP-001, PP4.9).
 * Nothing typed in either box is "not entered" (null); `0` is entered, and shows. Whether a time is in bounds is the
 * server's to say, so anything readable is passed on as read.
 */

/** The two boxes of one time, as typed. */
export interface TimeText {
  readonly hours: string;
  readonly minutes: string;
}

export type MinutesReading =
  | { readonly kind: 'empty' }
  | { readonly kind: 'minutes'; readonly value: number }
  | { readonly kind: 'unreadable' };

const WHOLE = /^[0-9]{1,6}$/;

export function readMinutes(time: TimeText): MinutesReading {
  const hours = time.hours.trim();
  const minutes = time.minutes.trim();
  if (hours === '' && minutes === '') {
    return { kind: 'empty' };
  }
  if ((hours !== '' && !WHOLE.test(hours)) || (minutes !== '' && !WHOLE.test(minutes))) {
    return { kind: 'unreadable' };
  }
  return { kind: 'minutes', value: Number(hours || '0') * 60 + Number(minutes || '0') };
}

/** A stored time in the two boxes: `95` is 1 and 35, `120` is 2 and nothing, `0` is nothing and 0, null is empty. */
export function writeMinutes(value: number | null): TimeText {
  if (value === null) {
    return { hours: '', minutes: '' };
  }
  const hours = Math.floor(value / 60);
  const minutes = value % 60;
  return {
    hours: hours === 0 ? '' : String(hours),
    minutes: hours !== 0 && minutes === 0 ? '' : String(minutes),
  };
}

/**
 * How long, as the recipe card writes it (`RecipeDuration`, PP4.9): `20 min`, `2 hr 20 min`, `1 day 3 hr`, `2 days`, and
 * `0 min`, with days counted from 24 hours and each zero part left out.
 */
export function writeDuration(total: number): string {
  const days = Math.floor(total / 1440);
  const hours = Math.floor((total % 1440) / 60);
  const minutes = total % 60;
  const parts = [
    days === 0 ? null : `${days} ${days === 1 ? 'day' : 'days'}`,
    hours === 0 ? null : `${hours} hr`,
    minutes === 0 ? null : `${minutes} min`,
  ].filter((part) => part !== null);
  return parts.length === 0 ? '0 min' : parts.join(' ');
}

/** The sum of the times entered (PP4.9), or null when none was. */
export function totalMinutes(times: readonly (number | null)[]): number | null {
  const entered = times.filter((time) => time !== null);
  return entered.length === 0 ? null : entered.reduce((sum, time) => sum + time, 0);
}
