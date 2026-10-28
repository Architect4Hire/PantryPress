import { computed, Injectable, signal } from '@angular/core';
import { PpTone, TONE_WORDS } from '../status/tone';

export interface PpToastAction {
  readonly label: string;
  readonly run: () => void;
}

export interface PpToastOptions {
  readonly message: string;
  readonly tone?: PpTone;
  readonly action?: PpToastAction;
}

export interface PpToast {
  readonly id: number;
  readonly message: string;
  readonly tone: PpTone;
  readonly action?: PpToastAction;
  /** Whether it goes by itself: only a success or a note with nothing to do. Anything else waits to be closed. */
  readonly leaves: boolean;
}

interface PpAnnouncement {
  readonly id: number;
  readonly text: string;
}

/** At most this many toasts that leave by themselves show at once; a toast that waits is never sent away. */
export const PP_TOAST_LIMIT = 3;

/**
 * Short messages about what just happened (PP3.4), drawn by the one `pp-toast-region` in the shell. Everything said
 * aloud - each toast's tone and words, and `announce()`, for progress a busy button cannot say - goes through one
 * polite announcer, drawn in the shell, or inside the open dialog while one is open, because a modal dialog makes
 * the rest of the page inert and so unheard. Only the latest announcement is kept, so two in one moment say the
 * second. A toast never carries something the reader must act on before going on: that is a notice or a dialog.
 */
@Injectable({ providedIn: 'root' })
export class PpToaster {
  private readonly shown = signal<readonly PpToast[]>([]);
  private readonly spoken = signal<readonly PpAnnouncement[]>([]);
  private readonly modals = signal<readonly object[]>([]);
  private issued = 0;

  readonly toasts = this.shown.asReadonly();
  readonly announcements = this.spoken.asReadonly();
  /** The open modal that speaks for the page, the one opened last; none when no modal is open. */
  readonly speaker = computed(() => this.modals().at(-1) ?? null);

  show(options: PpToastOptions): number {
    const tone = options.tone ?? 'info';
    const toast: PpToast = {
      id: ++this.issued,
      message: options.message,
      tone,
      action: options.action,
      leaves: !options.action && (tone === 'success' || tone === 'info'),
    };
    this.shown.update((toasts) => {
      const next = [...toasts, toast];
      const leaving = next.filter((candidate) => candidate.leaves);
      const surplus = new Set(leaving.slice(0, Math.max(0, leaving.length - PP_TOAST_LIMIT)));
      return next.filter((candidate) => !surplus.has(candidate));
    });
    this.announce(`${TONE_WORDS[tone]}: ${options.message}`);
    return toast.id;
  }

  dismiss(id: number): void {
    this.shown.update((toasts) => toasts.filter((toast) => toast.id !== id));
  }

  /** Each announcement is a new node in the live region, so the same words said twice are heard twice. */
  announce(text: string): void {
    this.spoken.set([{ id: ++this.issued, text }]);
  }

  /** A modal opened and now speaks for the page. Internal to the library's dialogs. */
  speakFrom(modal: object): void {
    this.modals.update((modals) => [...modals.filter((open) => open !== modal), modal]);
  }

  /** A modal closed; the one under it, or the shell, speaks again. Internal to the library's dialogs. */
  stopSpeakingFrom(modal: object): void {
    this.modals.update((modals) => modals.filter((open) => open !== modal));
  }
}
