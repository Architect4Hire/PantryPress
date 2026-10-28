import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
} from '@angular/core';
import { nextId } from '../field/field-context';

/**
 * How far along something is (PP3.4), on the browser's own `<progress>`, named by its visible label. With a `value`
 * it shows how much is done, and can show the percentage in words; with none it says only that work is going on,
 * and its movement stops when the reader asks for reduced motion.
 */
@Component({
  selector: 'pp-progress',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="top">
      <span class="label" [id]="labelId">{{ label() }}</span>
      @if (showValue() && percent() !== null) {
        <span class="value" aria-hidden="true">{{ percent() }}%</span>
      }
    </div>
    <progress
      class="bar"
      [attr.aria-labelledby]="labelId"
      [attr.value]="clamped()"
      [attr.max]="max()"
    ></progress>
  `,
  styleUrl: './progress.css',
})
export class PpProgress {
  readonly label = input.required<string>();
  readonly value = input<number>();
  readonly max = input(100);
  readonly showValue = input(false, { transform: booleanAttribute });

  protected readonly labelId = nextId('progress');
  /** A max of zero or less measures nothing, so the bar says only that work goes on. */
  protected readonly clamped = computed(() => {
    const value = this.value();
    const max = this.max();
    return value === undefined || !(max > 0) ? null : Math.min(Math.max(value, 0), max);
  });
  protected readonly percent = computed(() => {
    const value = this.clamped();
    return value === null ? null : Math.round((value / this.max()) * 100);
  });
}
