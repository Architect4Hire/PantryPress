import { inject, Injectable, InjectionToken, Signal } from '@angular/core';

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

/**
 * Page-unique ids, counted per application rather than per process: every prerendered page and the browser app each
 * get a fresh root injector, so a page's ids start at 1 wherever it renders and match between server and browser. A
 * counter, never a random value, so a page is the same on every build.
 */
@Injectable({ providedIn: 'root' })
export class PpIdSequence {
  private issued = 0;

  next(kind: string): string {
    this.issued += 1;
    return `pp-${kind}-${this.issued}`;
  }
}

/** The next id for a primitive; call it while the primitive is being created (an injection context). */
export function nextId(kind: string): string {
  return inject(PpIdSequence).next(kind);
}
