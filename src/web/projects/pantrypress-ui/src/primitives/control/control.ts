import { computed, Directive, inject, input } from '@angular/core';
import { PP_FIELD } from '../field/field-context';

/**
 * What the native text controls share (PP3.3): inside a `pp-field` they take its id, description, and invalid state;
 * outside one they keep the id they were given and need their own accessible name (`aria-label` or a `label`).
 */
@Directive({
  host: {
    '[attr.id]': 'controlId()',
    '[attr.aria-describedby]': 'field?.describedBy() ?? null',
    '[attr.aria-invalid]': 'field?.invalid() ? "true" : null',
  },
})
export abstract class PpControl {
  protected readonly field = inject(PP_FIELD, { optional: true });

  /** The control's id outside a field. Inside one, the field's id wins, so its label always points here. */
  readonly id = input<string>();

  protected readonly controlId = computed(() => this.field?.controlId ?? this.id() ?? null);
}
