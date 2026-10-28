import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
} from '@angular/core';
import { nextId, PP_FIELD, PpFieldContext } from './field-context';
import { PpFieldError } from './field-error';

/**
 * A label, an optional hint, and an optional error around exactly one projected control: `input[pp-text-field]`,
 * `textarea[pp-textarea]`, or `select[pp-select]` (PP3.3). The hint sits above the control and the error below it;
 * both describe the control, and an error marks it invalid. Optional fields say so in words; a required one sets
 * the native `required` on its control.
 */
@Component({
  selector: 'pp-field',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpFieldError],
  providers: [{ provide: PP_FIELD, useExisting: PpField }],
  template: `
    <label class="label" [attr.for]="controlId">
      {{ label() }}
      @if (optional()) {
        <span class="optional">(optional)</span>
      }
    </label>
    @if (hint(); as hint) {
      <p class="hint" [id]="hintId">{{ hint }}</p>
    }
    <ng-content />
    @if (error(); as error) {
      <pp-field-error [errorId]="errorId" [message]="error" />
    }
  `,
  styleUrl: './field.css',
})
export class PpField implements PpFieldContext {
  readonly label = input.required<string>();
  readonly hint = input<string>();
  readonly error = input<string>();
  readonly optional = input(false, { transform: booleanAttribute });

  readonly controlId = nextId('field');
  protected readonly hintId = `${this.controlId}-hint`;
  protected readonly errorId = `${this.controlId}-error`;

  readonly invalid = computed(() => !!this.error());
  readonly describedBy = computed(
    () =>
      [this.hint() ? this.hintId : null, this.error() ? this.errorId : null]
        .filter((id) => id !== null)
        .join(' ') || null,
  );
}
