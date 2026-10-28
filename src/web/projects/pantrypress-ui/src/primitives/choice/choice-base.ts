import { booleanAttribute, computed, Directive, input, signal } from '@angular/core';
import { ControlValueAccessor } from '@angular/forms';

/**
 * What a checkbox, a switch, and a radio group share (PP3.3): each works in Angular's reactive forms, and each can be
 * disabled by its `disabled` input or by its form control.
 */
@Directive()
export abstract class PpChoiceBase<T> implements ControlValueAccessor {
  readonly disabled = input(false, { transform: booleanAttribute });

  private readonly formDisabled = signal(false);
  protected readonly isDisabled = computed(() => this.disabled() || this.formDisabled());

  private onChange: (value: T) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  abstract writeValue(value: T): void;

  registerOnChange(fn: (value: T) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.formDisabled.set(isDisabled);
  }

  protected changed(value: T): void {
    this.onChange(value);
  }
}
