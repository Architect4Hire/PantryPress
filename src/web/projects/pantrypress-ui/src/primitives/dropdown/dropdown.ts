import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  input,
  model,
  viewChild,
} from '@angular/core';
import { PpButton } from '../button/button';
import { PpButtonSize, PpButtonVariant } from '../button/button-base';
import { nextId } from '../field/field-context';

/**
 * A button that shows and hides a short panel of links and buttons (PP3.9): a site switcher, an account menu. It is
 * the disclosure pattern, not an ARIA menu: the button says `aria-expanded` and names the panel it controls, the items
 * are ordinary links and buttons in the tab order, and Tab moves through them. Escape closes it and puts focus back on
 * the button; so does choosing an item. Escape works while it is open wherever focus is, since Safari leaves focus on
 * the page when the button is pressed. A press outside closes it, and so does Tab leaving it. The panel is `hidden`
 * while closed, so a server render draws it closed and every item stays out of reach until it opens.
 *
 * The button's name is `prefix` and `label` together, both shown, so the words a reader sees are the words a speech
 * user says (2.5.3): "Site" and "Apple Pie Club" name a site switcher better than the site's name alone. Both sit in
 * one inline run of text with a real space between them, so the name and the visible words match exactly; as two flex
 * items, their text would run together.
 */
@Component({
  selector: 'pp-dropdown',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpButton],
  template: `
    <button
      #trigger
      pp-button
      class="trigger"
      [variant]="variant()"
      [size]="size()"
      [attr.aria-expanded]="open() ? 'true' : 'false'"
      [attr.aria-controls]="panelId"
      [attr.aria-describedby]="caption() ? captionId : null"
      (click)="toggle()"
    >
      <span class="text">
        @if (prefix(); as prefix) {
          <span class="prefix">{{ prefix }}</span
          >&ngsp;
        }
        <span class="label">{{ label() }}</span></span
      >
      <svg
        class="chevron"
        viewBox="0 0 20 20"
        width="16"
        height="16"
        aria-hidden="true"
        focusable="false"
      >
        <path
          d="M5.5 7.5 10 12l4.5-4.5"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      </svg>
    </button>
    <!-- eslint-disable-next-line @angular-eslint/template/click-events-have-key-events, @angular-eslint/template/interactive-supports-focus -- a click that bubbles up from an item, which is a link or button with its own keys -->
    <div
      class="popup"
      [id]="panelId"
      [attr.data-align]="align()"
      [hidden]="!open()"
      (click)="chosen($event)"
    >
      @if (caption(); as caption) {
        <p class="caption" [id]="captionId">{{ caption }}</p>
      }
      <ng-content />
    </div>
  `,
  host: {
    '[attr.data-open]': 'open() ? "" : null',
    '(keydown.escape)': 'escape($event)',
    // Safari does not focus a pressed button, so after a press Escape reaches the page, not the dropdown.
    '(document:keydown.escape)': 'escape($event)',
    '(focusout)': 'left($event)',
    '(document:pointerdown)': 'outside($event)',
  },
  styleUrl: './dropdown.css',
})
export class PpDropdown {
  /** The button's words: what is chosen now, or what the panel is for. */
  readonly label = input.required<string>();
  /** Shown before the label in quieter type, and part of the button's name ("Site"). */
  readonly prefix = input<string>();
  /**
   * A line of plain text at the top of the panel ("Signed in as …"). It is never focusable, so Tab would pass it by: it
   * describes the button instead, and is heard with it.
   */
  readonly caption = input<string>();
  /** Which edge of the button the panel lines up with: `start` under the button's start, `end` under its end. */
  readonly align = input<'start' | 'end'>('start');
  readonly variant = input<PpButtonVariant>('secondary');
  readonly size = input<PpButtonSize>('md');
  readonly open = model(false);

  protected readonly panelId = nextId('dropdown');
  protected readonly captionId = `${this.panelId}-caption`;
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly trigger = viewChild.required('trigger', { read: ElementRef });

  /** Closes the panel and, when focus was inside it, puts focus back on the button. */
  close(): void {
    if (!this.open()) {
      return;
    }
    const focusWasInside = this.host.contains(this.host.ownerDocument.activeElement);
    this.open.set(false);
    if (focusWasInside) {
      (this.trigger().nativeElement as HTMLElement).focus();
    }
  }

  protected toggle(): void {
    this.open.update((open) => !open);
  }

  protected escape(event: Event): void {
    if (!this.open()) {
      return;
    }
    // Inside a dialog, the first Escape closes the panel only.
    event.preventDefault();
    event.stopPropagation();
    this.close();
  }

  protected chosen(event: MouseEvent): void {
    const target = event.target instanceof Element ? event.target : null;
    if (target?.closest('a[href], button')) {
      this.close();
    }
  }

  // Tab leaving the panel names where focus went. A press that focuses nothing (Safari does not focus a pressed
  // button) names nothing, and is left to `outside`, so a press on an item is never lost to the panel closing first.
  protected left(event: FocusEvent): void {
    const to = event.relatedTarget;
    if (this.open() && to instanceof Node && !this.host.contains(to)) {
      this.open.set(false);
    }
  }

  protected outside(event: Event): void {
    if (this.open() && event.target instanceof Node && !this.host.contains(event.target)) {
      this.open.set(false);
    }
  }
}

/**
 * One choice in a `pp-dropdown`: `<a pp-dropdown-item routerLink="…">` to go somewhere, `<button pp-dropdown-item>` to
 * do something. A link marked `aria-current` shows it in weight and a bar at its start, never by color alone. A
 * `<button>` is `type="button"`, so none submits a form by accident.
 */
@Component({
  selector: 'a[pp-dropdown-item], button[pp-dropdown-item]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<ng-content />',
  host: {
    '[attr.type]': 'isButton ? "button" : null',
  },
  styleUrl: './dropdown-item.css',
})
export class PpDropdownItem {
  protected readonly isButton =
    inject<ElementRef<HTMLElement>>(ElementRef).nativeElement.tagName.toLowerCase() === 'button';
}
