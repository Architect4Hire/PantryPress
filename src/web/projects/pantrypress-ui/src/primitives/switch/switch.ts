import {
  ChangeDetectionStrategy,
  Component,
  effect,
  ElementRef,
  forwardRef,
  input,
  model,
  viewChild,
} from '@angular/core';
import { NG_VALUE_ACCESSOR } from '@angular/forms';
import { PpChoiceBase } from '../choice/choice-base';
import { nextId } from '../field/field-context';

/**
 * An on-or-off setting that takes effect at once (PP3.3), on the browser's own checkbox with `role="switch"`, so it is
 * announced as on or off. On and off differ by the thumb's place, not by color alone. Use it in a reactive form (a
 * boolean) or with `[(checked)]`.
 */
@Component({
  selector: 'pp-switch',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => PpSwitch), multi: true }],
  template: `
    <label class="row">
      <input
        #box
        class="switch"
        type="checkbox"
        role="switch"
        [checked]="checked()"
        [disabled]="isDisabled()"
        [attr.aria-describedby]="hint() ? hintId : null"
        (change)="toggle($event)"
        (blur)="onTouched()"
      />
      <span class="text">{{ label() }}</span>
    </label>
    @if (hint(); as hint) {
      <p class="hint" [id]="hintId">{{ hint }}</p>
    }
  `,
  styleUrls: ['../choice/choice.css', './switch.css'],
})
export class PpSwitch extends PpChoiceBase<boolean> {
  readonly label = input.required<string>();
  readonly hint = input<string>();
  readonly checked = model(false);

  protected readonly hintId = `${nextId('switch')}-hint`;
  private readonly box = viewChild.required<ElementRef<HTMLInputElement>>('box');

  constructor() {
    super();
    // The binding gives a prerendered page its first state; this keeps the element in step after that, including a
    // change undone before the view next refreshed, which a binding alone would miss.
    effect(() => {
      this.box().nativeElement.checked = this.checked();
    });
  }

  writeValue(value: boolean): void {
    this.checked.set(!!value);
  }

  protected toggle(event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    this.checked.set(checked);
    this.changed(checked);
  }
}
