import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  forwardRef,
  input,
  signal,
  viewChildren,
} from '@angular/core';
import { NG_VALUE_ACCESSOR } from '@angular/forms';
import { PpChoiceBase } from '../choice/choice-base';
import { nextId } from '../field/field-context';

export interface PpChipOption<T> {
  readonly value: T;
  readonly label: string;
}

/**
 * A row of filters, drawn as chips (PP3.5): a `fieldset` whose `legend` names what they filter, over the browser's
 * own checkboxes, so Tab and Space work and each chip is said as checked or not. A chosen chip shows a check mark as
 * well as its fill, so it is never told by color alone. By default any number may be chosen and the value is an
 * array, in the options' order; `multiple="false"` makes them radios sharing one name, with the arrow keys moving
 * between them, and the value one option or null. Options are typed values compared with `Object.is`. Use it in a
 * reactive form typed for the mode: `FormControl<readonly T[]>` by default, `FormControl<T | null>` with
 * `multiple="false"`; the type cannot tell the two apart, so the form's type is the caller's to match.
 */
@Component({
  selector: 'pp-chip-group',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => PpChipGroup), multi: true },
  ],
  template: `
    <fieldset
      class="group"
      [attr.role]="multiple() ? null : 'radiogroup'"
      (focusout)="left($event)"
    >
      <legend class="legend">{{ legend() }}</legend>
      <div class="chips">
        @for (option of options(); track $index) {
          <label class="chip">
            <input
              #choice
              class="visually-hidden"
              [attr.type]="multiple() ? 'checkbox' : 'radio'"
              [name]="name"
              [checked]="isSelected(option)"
              [disabled]="isDisabled()"
              (change)="toggle(option, $event)"
            />
            <svg
              class="check"
              viewBox="0 0 20 20"
              width="16"
              height="16"
              aria-hidden="true"
              focusable="false"
            >
              <path
                fill="none"
                stroke="currentColor"
                stroke-width="2.2"
                stroke-linecap="round"
                stroke-linejoin="round"
                d="M4.5 10.5l3.5 3.5 7.5-8"
              />
            </svg>
            <span class="text">{{ option.label }}</span>
          </label>
        }
      </div>
    </fieldset>
  `,
  styleUrls: ['../shared/visually-hidden.css', './chip-group.css'],
})
export class PpChipGroup<T> extends PpChoiceBase<readonly T[] | T | null> {
  readonly legend = input.required<string>();
  readonly options = input.required<readonly PpChipOption<T>[]>();
  readonly multiple = input(true, { transform: booleanAttribute });

  protected readonly name = nextId('chips');
  private readonly value = signal<readonly T[] | T | null>(null);
  /** The chosen values, read as the group is now, so a form's value never depends on when `multiple` was set. */
  private readonly selected = computed<readonly T[]>(() => {
    const value = this.value();
    if (this.multiple()) {
      return Array.isArray(value) ? (value as readonly T[]) : [];
    }
    return value === null || value === undefined ? [] : [value as T];
  });
  private readonly choices = viewChildren<ElementRef<HTMLInputElement>>('choice');

  constructor() {
    super();
    // The binding gives a prerendered page its first state; this keeps the element in step after that, including a
    // change undone before the view next refreshed, which a binding alone would miss.
    effect(() => {
      const options = this.options();
      this.choices().forEach((choice, index) => {
        choice.nativeElement.checked = index < options.length && this.isSelected(options[index]);
      });
    });
  }

  writeValue(value: readonly T[] | T | null): void {
    this.value.set(value ?? null);
  }

  protected isSelected(option: PpChipOption<T>): boolean {
    return this.selected().some((value) => Object.is(value, option.value));
  }

  /** Touched once focus leaves the whole group, not while Tab or the arrow keys move between its chips. */
  protected left(event: FocusEvent): void {
    const group = event.currentTarget as HTMLElement;
    const next = event.relatedTarget as Node | null;
    if (!next || !group.contains(next)) {
      this.onTouched();
    }
  }

  protected toggle(option: PpChipOption<T>, event: Event): void {
    if (!this.multiple()) {
      this.value.set(option.value);
      this.changed(option.value);
      return;
    }
    const checked = (event.target as HTMLInputElement).checked;
    const chosen = this.options()
      .filter((candidate) =>
        candidate === option
          ? checked
          : this.selected().some((value) => Object.is(value, candidate.value)),
      )
      .map((candidate) => candidate.value);
    this.value.set(chosen);
    this.changed(chosen);
  }
}
