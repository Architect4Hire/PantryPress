import { InjectionToken, Signal } from '@angular/core';

/**
 * What a field wrapper tells the one control projected into it (PP3.3): the id its label points at, the hint and
 * error that describe it, and whether it is invalid. The control reads it through `PP_FIELD`, as Material's
 * form field does, because the control sits inside the wrapper in its parent's template.
 */
export interface PpFieldContext {
  readonly controlId: string;
  readonly describedBy: Signal<string | null>;
  readonly invalid: Signal<boolean>;
}

export const PP_FIELD = new InjectionToken<PpFieldContext>('PP_FIELD');

let issued = 0;

/** A page-unique id. A counter, not a random value, so a prerendered page is the same on every build. */
export function nextId(kind: string): string {
  issued += 1;
  return `pp-${kind}-${issued}`;
}
