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
import { PpFieldError } from '../field/field-error';

export interface PpRadioOption<T> {
  readonly value: T;
  readonly label: string;
  readonly hint?: string;
}

/**
 * One choice from a few, all shown (PP3.3): a `fieldset` whose `legend` is the question, over the browser's own radio
 * buttons sharing one name, so the arrow keys move between them natively. Options are typed values compared with
 * `Object.is`. Use it in a reactive form; the caller gives the error in plain words.
 */
@Component({
  selector: 'pp-radio-group',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpFieldError],
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => PpRadioGroup), multi: true },
  ],
  template: `
    <fieldset
      class="group"
      role="radiogroup"
      [attr.aria-describedby]="describedBy()"
      [attr.aria-invalid]="error() ? 'true' : null"
      [attr.aria-required]="required() ? 'true' : null"
      (focusout)="left($event)"
    >
      <legend class="legend">
        {{ legend() }}
        @if (optional()) {
          <span class="optional">(optional)</span>
        }
      </legend>
      @if (hint(); as hint) {
        <p class="hint group-hint" [id]="hintId">{{ hint }}</p>
      }
      @for (option of options(); track $index) {
        <div class="option">
          <label class="row">
            <input
              #radio
              class="radio"
              type="radio"
              [name]="name"
              [checked]="isSelected(option)"
              [disabled]="isDisabled()"
              [attr.aria-describedby]="option.hint ? optionHintId($index) : null"
              (change)="select(option)"
            />
            <span class="text">{{ option.label }}</span>
          </label>
          @if (option.hint) {
            <p class="hint" [id]="optionHintId($index)">{{ option.hint }}</p>
          }
        </div>
      }
      @if (error(); as error) {
        <pp-field-error [errorId]="errorId" [message]="error" />
      }
    </fieldset>
  `,
  styleUrls: ['../choice/choice.css', './radio-group.css'],
})
export class PpRadioGroup<T> extends PpChoiceBase<T | null> {
  readonly legend = input.required<string>();
  readonly options = input.required<readonly PpRadioOption<T>[]>();
  readonly hint = input<string>();
  readonly error = input<string>();
  readonly optional = input(false, { transform: booleanAttribute });
  readonly required = input(false, { transform: booleanAttribute });

  protected readonly name = nextId('radio');
  protected readonly hintId = `${this.name}-hint`;
  protected readonly errorId = `${this.name}-error`;
  private readonly value = signal<T | null>(null);
  private readonly radios = viewChildren<ElementRef<HTMLInputElement>>('radio');

  protected readonly describedBy = computed(
    () =>
      [this.hint() ? this.hintId : null, this.error() ? this.errorId : null]
        .filter((id) => id !== null)
        .join(' ') || null,
  );

  constructor() {
    super();
    // The binding gives a prerendered page its first state; this keeps the element in step after that, including a
    // change undone before the view next refreshed, which a binding alone would miss.
    effect(() => {
      const options = this.options();
      this.radios().forEach((radio, index) => {
        radio.nativeElement.checked = index < options.length && this.isSelected(options[index]);
      });
    });
  }

  writeValue(value: T | null): void {
    this.value.set(value ?? null);
  }

  protected isSelected(option: PpRadioOption<T>): boolean {
    return Object.is(option.value, this.value());
  }

  protected optionHintId(index: number): string {
    return `${this.name}-option-${index}-hint`;
  }

  /** Touched once focus leaves the whole group, not while the arrow keys move between its radios. */
  protected left(event: FocusEvent): void {
    const group = event.currentTarget as HTMLElement;
    const next = event.relatedTarget as Node | null;
    if (!next || !group.contains(next)) {
      this.onTouched();
    }
  }

  protected select(option: PpRadioOption<T>): void {
    this.value.set(option.value);
    this.changed(option.value);
  }
}
