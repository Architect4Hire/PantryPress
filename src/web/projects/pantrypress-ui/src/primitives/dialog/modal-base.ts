import { DOCUMENT } from '@angular/common';
import {
  afterRenderEffect,
  booleanAttribute,
  DestroyRef,
  Directive,
  ElementRef,
  inject,
  input,
  model,
  viewChild,
} from '@angular/core';
import { nextId } from '../field/field-context';
import { PpToaster } from '../toast/toaster';

const FOCUSABLE = [
  'button:not([disabled])',
  '[href]',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(', ');

/**
 * What a dialog and a drawer share (PP3.4). Each is the browser's own `<dialog>`, opened with `showModal()`, so the
 * page behind is inert and focus stays inside. Escape always closes; a click on the backdrop closes only a
 * `dismissible` one, so nothing typed is lost to a stray click. On opening, focus goes to a control marked
 * `ppInitialFocus` (or `autofocus`, which the template lint refuses), else the first control in the body, else the
 * heading; the footer's actions are never chosen by themselves, so a confirmation marks its least destructive one.
 * On closing, focus goes back to where it was. While open it speaks for the page: the toaster's words are said
 * inside it, since everything outside is inert. Everything here runs after render, in the browser only.
 */
@Directive()
export abstract class PpModalBase {
  readonly open = model(false);
  readonly heading = input.required<string>();
  readonly dismissible = input(false, { transform: booleanAttribute });

  protected readonly headingId = nextId('dialog');
  protected readonly bodyId = `${this.headingId}-body`;
  protected readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  protected readonly toaster = inject(PpToaster);
  private readonly page = inject(DOCUMENT);
  private opener: HTMLElement | null = null;
  private pressStartedOnBackdrop = false;

  constructor() {
    afterRenderEffect({
      write: () => {
        const dialog = this.dialog().nativeElement;
        if (this.open() && !dialog.open) {
          const active = this.page.activeElement;
          this.opener = active && active !== this.page.body ? (active as HTMLElement) : null;
          dialog.showModal();
          this.toaster.speakFrom(this);
          this.initialFocus(dialog)?.focus();
        } else if (!this.open() && dialog.open) {
          dialog.close();
        }
      },
    });
    inject(DestroyRef).onDestroy(() => {
      this.toaster.stopSpeakingFrom(this);
      if (this.dialog().nativeElement.open) {
        this.returnFocus();
      }
    });
  }

  /** Closes it as Escape does; the native `close` event then tidies up. */
  close(): void {
    this.open.set(false);
  }

  /** Whether a click on the backdrop may close it. */
  protected closesOnBackdrop(): boolean {
    return this.dismissible();
  }

  /** The browser closed it (Escape, or a close by code): mark it closed and send focus home. */
  protected closed(): void {
    // A close the browser queued before the dialog was opened again is out of date.
    if (this.dialog().nativeElement.open) {
      return;
    }
    this.open.set(false);
    this.toaster.stopSpeakingFrom(this);
    this.returnFocus();
  }

  protected pressed(event: PointerEvent): void {
    this.pressStartedOnBackdrop = event.target === this.dialog().nativeElement;
  }

  /**
   * A click whose target is the dialog itself landed on the backdrop, outside its frame. It counts only when the
   * press began there too, so selecting text and letting go outside never closes it.
   */
  protected backdrop(event: MouseEvent): void {
    const onBackdrop = event.target === this.dialog().nativeElement;
    if (onBackdrop && this.pressStartedOnBackdrop && this.closesOnBackdrop()) {
      this.close();
    }
    this.pressStartedOnBackdrop = false;
  }

  private returnFocus(): void {
    if (this.opener?.isConnected) {
      this.opener.focus();
    }
    this.opener = null;
  }

  private initialFocus(dialog: HTMLDialogElement): HTMLElement | null {
    const frame = dialog.querySelector<HTMLElement>(':scope > .frame') ?? dialog;
    const usable = (element: HTMLElement | null | undefined) =>
      element && !element.closest('[hidden], [inert], dialog:not([open])') ? element : null;
    const marked = frame.querySelector<HTMLElement>('[ppInitialFocus], [autofocus]');
    const fromMark = marked?.matches(FOCUSABLE)
      ? marked
      : marked?.querySelector<HTMLElement>(FOCUSABLE);
    const fromBody = [
      ...frame.querySelectorAll<HTMLElement>(`:scope > .body :is(${FOCUSABLE})`),
    ].find((element) => usable(element) !== null);
    return usable(fromMark) ?? fromBody ?? frame.querySelector<HTMLElement>('.heading');
  }
}
