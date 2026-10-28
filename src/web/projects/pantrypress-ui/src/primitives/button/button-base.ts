import { booleanAttribute, Directive, ElementRef, inject, input } from '@angular/core';

export type PpButtonVariant = 'primary' | 'secondary' | 'quiet' | 'danger';
export type PpButtonSize = 'md' | 'sm';

/**
 * What a button and an icon button share (PP3.3). Each sits on the browser's own `<button>` or `<a>`, so forms,
 * `routerLink`, and the keyboard behave natively. A `<button>` is `type="button"` unless it says otherwise, so no
 * button submits a form by accident. Secondary is the default: a primary action is chosen, never inherited.
 */
@Directive({
  host: {
    '[attr.data-variant]': 'variant()',
    '[attr.data-size]': 'size()',
    '[attr.type]': 'isButton ? type() : null',
    '[attr.aria-busy]': 'busy() ? "true" : null',
    '[attr.aria-disabled]': 'busy() ? "true" : null',
  },
})
export abstract class PpButtonBase {
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  protected readonly isButton = this.element.tagName.toLowerCase() === 'button';

  readonly variant = input<PpButtonVariant>('secondary');
  readonly size = input<PpButtonSize>('md');
  readonly type = input<'button' | 'submit' | 'reset'>('button');
  /** Working on it: still focusable and announced, but a press does nothing until it is done. */
  readonly busy = input(false, { transform: booleanAttribute });

  constructor() {
    // Capture runs before every ordinary listener on the element, the caller's (click) included, which a host
    // listener does not. The listener lives and dies with the element.
    this.element.addEventListener('click', (event) => this.guard(event), { capture: true });
  }

  private guard(event: Event): void {
    if (this.busy()) {
      event.preventDefault();
      event.stopImmediatePropagation();
    }
  }
}
