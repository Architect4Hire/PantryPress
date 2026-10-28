import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  forwardRef,
  inject,
  input,
} from '@angular/core';
import { PpButton } from '../button/button';
import { PpIconButton } from '../icon-button/icon-button';
import { PpCloseIcon, PpToneIcon, TONE_WORDS } from '../status/tone';
import { PpAnnouncer } from './announcer';
import { PpToast, PpToaster } from './toaster';

/** How long a toast that leaves by itself stays, and stays again once the reader stops hovering and focusing it. */
export const PP_TOAST_STAY_MS = 8000;

/**
 * Where toasts appear, once, in the app's shell (PP3.4): a "Notifications" landmark, so a toast that waits can be
 * found, holding the list and the shell's announcer. The list itself is not live: each toast's words are spoken once,
 * through the announcer, without its buttons.
 */
@Component({
  selector: 'pp-toast-region',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [forwardRef(() => PpToastItem), PpAnnouncer],
  template: `
    <section class="region" aria-label="Notifications" (focusin)="entered($event)">
      <ol class="list">
        @for (toast of toaster.toasts(); track toast.id) {
          <li pp-toast [toast]="toast"></li>
        }
      </ol>
    </section>
    <pp-announcer />
  `,
  styleUrl: './toast-region.css',
})
export class PpToastRegion {
  protected readonly toaster = inject(PpToaster);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private returnTo: HTMLElement | null = null;

  /** Where focus came from when it entered the region, so it can go back there when the last toast goes. */
  protected entered(event: FocusEvent): void {
    const from = event.relatedTarget as HTMLElement | null;
    if (!from || !this.host.contains(from)) {
      this.returnTo = from;
    }
  }

  /** Before a toast holding focus goes: the same control in the next toast, else the one before, else back. */
  moveFocusFrom(item: HTMLElement): void {
    const active = item.ownerDocument.activeElement;
    if (!active || !item.contains(active)) {
      return;
    }
    const sibling = (item.nextElementSibling ?? item.previousElementSibling) as HTMLElement | null;
    const label = active.getAttribute('aria-label');
    const same = sibling?.querySelector<HTMLElement>(label ? `[aria-label="${label}"]` : 'button');
    const target = same ?? sibling?.querySelector<HTMLElement>('button') ?? this.returnTo;
    if (target?.isConnected) {
      target.focus();
    }
  }
}

/** One toast. Internal to the region. */
@Component({
  selector: 'li[pp-toast]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpButton, PpIconButton, PpCloseIcon, PpToneIcon],
  template: `
    <pp-tone-icon class="icon" [tone]="toast().tone" />
    <p class="message">
      <span class="visually-hidden">{{ word() }}: </span>{{ toast().message }}
    </p>
    @if (toast().action; as action) {
      <button pp-button variant="quiet" size="sm" (click)="act()">{{ action.label }}</button>
    }
    <button pp-icon-button label="Dismiss" variant="quiet" size="sm" (click)="dismiss()">
      <pp-close-icon />
    </button>
  `,
  styleUrls: ['../shared/visually-hidden.css', './toast.css'],
  host: {
    '[attr.data-tone]': 'toast().tone',
    '(mouseenter)': 'hover(true)',
    '(mouseleave)': 'hover(false)',
    '(focusin)': 'focus(true)',
    '(focusout)': 'focusLeft($event)',
  },
})
export class PpToastItem {
  readonly toast = input.required<PpToast>();

  private readonly toaster = inject(PpToaster);
  private readonly region = inject(PpToastRegion);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private hovered = false;
  private focused = false;

  constructor() {
    // Timers run in the browser only, after the first render, never while a page renders on the server.
    afterNextRender(() => this.restart());
    inject(DestroyRef).onDestroy(() => this.stop());
  }

  protected word(): string {
    return TONE_WORDS[this.toast().tone];
  }

  protected act(): void {
    this.toast().action?.run();
    this.dismiss();
  }

  protected dismiss(): void {
    this.region.moveFocusFrom(this.host);
    this.toaster.dismiss(this.toast().id);
  }

  protected hover(on: boolean): void {
    this.hovered = on;
    this.restart();
  }

  protected focus(on: boolean): void {
    this.focused = on;
    this.restart();
  }

  /** Focus moving between this toast's own buttons has not left it. */
  protected focusLeft(event: FocusEvent): void {
    const next = event.relatedTarget as Node | null;
    if (!next || !this.host.contains(next)) {
      this.focus(false);
    }
  }

  /** Waits while it is hovered or focused; otherwise a toast that leaves by itself starts its full stay again. */
  private restart(): void {
    this.stop();
    if (this.toast().leaves && !this.hovered && !this.focused) {
      // eslint-disable-next-line no-restricted-globals -- browser only (started after render), and it changes no rendered output on the server
      this.timer = setTimeout(() => this.dismiss(), PP_TOAST_STAY_MS);
    }
  }

  private stop(): void {
    if (this.timer !== undefined) {
      clearTimeout(this.timer);
      this.timer = undefined;
    }
  }
}
