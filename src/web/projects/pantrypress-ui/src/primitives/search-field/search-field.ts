import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  forwardRef,
  input,
  model,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { PpButton } from '../button/button';
import { nextId } from '../field/field-context';
import { PpIconButton } from '../icon-button/icon-button';
import { PpCloseIcon } from '../status/tone';

/**
 * Words to look for in a list (PP3.5): a search landmark holding a labelled `<input type="search">` and a Search
 * button. `[(query)]` follows every keystroke; `searched` says the trimmed words when the reader presses Enter or
 * Search, and says `''` once the field is emptied, by its Clear button, by Escape, or by deleting the words, so the
 * whole list comes back. The library keeps no timer, so a screen that searches as the reader types waits in the app.
 * Works in a reactive form (a string). A `div` with the search role rather than a `form`, so it may sit in one.
 */
@Component({
  selector: 'pp-search-field',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpButton, PpIconButton, PpCloseIcon],
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => PpSearchField), multi: true },
  ],
  template: `
    <div class="search" role="search" [attr.aria-label]="label()">
      <label class="label" [for]="inputId">{{ label() }}</label>
      @if (hint(); as hint) {
        <p class="hint" [id]="hintId">{{ hint }}</p>
      }
      <div class="row">
        <div class="box">
          <input
            #box
            class="input"
            type="search"
            enterkeyhint="search"
            autocomplete="off"
            [id]="inputId"
            [value]="query()"
            [disabled]="isDisabled()"
            [attr.aria-describedby]="hint() ? hintId : null"
            (input)="typed($event)"
            (keydown)="key($event)"
            (blur)="onTouched()"
          />
          @if (query() !== '' && !isDisabled()) {
            <button
              pp-icon-button
              class="clear"
              label="Clear search"
              variant="quiet"
              size="sm"
              (click)="clear()"
            >
              <pp-close-icon />
            </button>
          }
        </div>
        <button pp-button [disabled]="isDisabled()" (click)="submit()">Search</button>
      </div>
    </div>
  `,
  styleUrl: './search-field.css',
})
export class PpSearchField implements ControlValueAccessor {
  /** What is searched, in the reader's words: "Search posts". It names the field and the search landmark. */
  readonly label = input.required<string>();
  readonly hint = input<string>();
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly query = model('');
  readonly searched = output<string>();

  protected readonly inputId = nextId('search');
  protected readonly hintId = `${this.inputId}-hint`;
  private readonly box = viewChild.required<ElementRef<HTMLInputElement>>('box');
  private readonly formDisabled = signal(false);
  protected readonly isDisabled = computed(() => this.disabled() || this.formDisabled());

  private onChange: (value: string) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  constructor() {
    // The binding gives a prerendered page its first words; this keeps the element in step after that, including a
    // change undone before the view next refreshed, which a binding alone would miss.
    effect(() => {
      const box = this.box().nativeElement;
      if (box.value !== this.query()) {
        box.value = this.query();
      }
    });
  }

  writeValue(value: string | null): void {
    this.query.set(value ?? '');
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.formDisabled.set(isDisabled);
  }

  protected typed(event: Event): void {
    const had = this.query() !== '';
    this.set((event.target as HTMLInputElement).value);
    // The browser's own clear mark and Escape both arrive here as an emptied field.
    if (had && this.query() === '') {
      this.searched.emit('');
    }
  }

  protected key(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      this.submit();
    } else if (event.key === 'Escape' && this.query() !== '') {
      // Escape empties the field first; only a second Escape reaches a dialog around it.
      event.preventDefault();
      this.clear();
    }
  }

  protected submit(): void {
    if (!this.isDisabled()) {
      this.searched.emit(this.query().trim());
    }
  }

  protected clear(): void {
    this.set('');
    this.box().nativeElement.focus();
    this.searched.emit('');
  }

  private set(value: string): void {
    this.query.set(value);
    this.onChange(value);
  }
}
