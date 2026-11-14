import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { nextId } from '../field/field-context';
import { PpFieldError } from '../field/field-error';

/**
 * A few fields that answer one question together, such as the hours and minutes of one time (PP4.13): a `fieldset` whose
 * `legend` is the question, the fields projected inside it, and one error for all of them under them. Each projected
 * control that the error is about binds `[aria-describedby]="set.describedBy()"` and `[aria-invalid]="set.invalid()"`
 * (with `#set` on this element), so the error is heard from whichever box the person is in, said once on the screen.
 */
@Component({
  selector: 'pp-fieldset',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpFieldError],
  exportAs: 'ppFieldset',
  template: `
    <fieldset class="group">
      <legend class="legend" [id]="legendId">{{ legend() }}</legend>
      <div class="fields"><ng-content /></div>
      <ng-content select="[ppFieldsetAfter]" />
      @if (error(); as error) {
        <pp-field-error class="error" [errorId]="errorId" [message]="error" />
      }
    </fieldset>
  `,
  styles: `
    :host {
      display: block;
    }

    .group {
      min-inline-size: 0;
      margin: 0;
      padding: 0;
      border: 0;
    }

    .legend {
      margin-block-end: var(--space-2);
      padding: 0;
      color: var(--ink);
      font-size: var(--text-2);
      font-weight: 600;
      line-height: var(--leading-1);
    }

    .fields {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
    }

    .error {
      margin-block-start: var(--space-2);
    }
  `,
})
export class PpFieldset {
  readonly legend = input.required<string>();
  readonly error = input<string>();

  readonly legendId = nextId('fieldset');
  protected readonly errorId = `${this.legendId}-error`;

  /** The ids a projected control is described by: the set's error, when there is one. */
  readonly describedBy = computed(() => (this.error() ? this.errorId : undefined));
  /** Whether the set's controls are marked invalid. */
  readonly invalid = computed(() => !!this.error());
}
