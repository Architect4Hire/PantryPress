import { computed, Directive, inject, input } from '@angular/core';
import { PP_FIELD } from '../field/field-context';

/**
 * What the native text controls share (PP3.3): inside a `pp-field` they take its id, description, and invalid state;
 * outside one they keep the id they were given and need their own accessible name (`aria-label` or a `label`). A
 * caller's own `aria-describedby` and `aria-invalid` are kept and joined with the field's, never replaced.
 */
@Directive({
  host: {
    '[attr.id]': 'controlId()',
    '[attr.aria-describedby]': 'describedBy()',
    '[attr.aria-invalid]': 'invalid() ? "true" : null',
  },
})
export abstract class PpControl {
  protected readonly field = inject(PP_FIELD, { optional: true });

  /** The control's id outside a field. Inside one, the field's id wins, so its label always points here. */
  readonly id = input<string>();
  /* eslint-disable @angular-eslint/no-input-rename -- these must take the native attributes' names, so a caller's
     own value is merged with the field's rather than overwritten */
  /** The caller's own description ids (a character count, say), read after the field's hint and error. */
  readonly callerDescribedBy = input<string | undefined>(undefined, { alias: 'aria-describedby' });
  readonly callerInvalid = input<boolean | 'true' | 'false' | undefined>(undefined, {
    alias: 'aria-invalid',
  });
  /* eslint-enable @angular-eslint/no-input-rename */

  protected readonly controlId = computed(() => this.field?.controlId ?? this.id() ?? null);
  protected readonly describedBy = computed(
    () =>
      [this.field?.describedBy(), this.callerDescribedBy()]
        .filter((ids) => !!ids && ids.trim() !== '')
        .join(' ') || null,
  );
  protected readonly invalid = computed(() => {
    const caller = this.callerInvalid();
    return !!this.field?.invalid() || caller === true || caller === 'true';
  });
}
