import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  untracked,
} from '@angular/core';
import { PpToneIcon } from '../status/tone';
import { PpToaster } from '../toast/toaster';

/**
 * Where an autosaved screen's work stands (PNFR-007):
 *
 * - `saved`: everything typed is saved;
 * - `pending`: there are changes that are not saved yet, and a save is about to start;
 * - `saving`: a save is under way;
 * - `retrying`: a save could not get through, and will be tried again by itself;
 * - `failed`: a save was refused, and the screen says what to do.
 */
export type PpSaveState = 'saved' | 'pending' | 'saving' | 'retrying' | 'failed';

const WORDS: Readonly<Record<PpSaveState, string>> = {
  saved: 'Saved',
  pending: 'Changes not saved yet',
  saving: 'Saving…',
  retrying: 'Not saved yet. Trying again…',
  failed: 'Not saved',
};

/**
 * How an autosaved screen's work stands, said in words beside an icon that only adds to them (PNFR-007). It belongs in
 * the page header's actions.
 *
 * It is not a live region of its own: a screen that autosaves every pause would be heard after every pause. It speaks
 * through the toaster's one announcer only when something has changed that the person needs to know about: a save could
 * not get through or was refused, and later, everything was saved again. `detail` is an optional second line that the
 * caller words, such as when the last save happened or where unsaved changes are kept.
 */
@Component({
  selector: 'pp-save-status',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpToneIcon],
  template: `
    @switch (state()) {
      @case ('saved') {
        <pp-tone-icon class="icon" tone="success" />
      }
      @case ('retrying') {
        <pp-tone-icon class="icon" tone="warning" />
      }
      @case ('failed') {
        <pp-tone-icon class="icon" tone="danger" />
      }
      @default {
        <span class="dot" aria-hidden="true"></span>
      }
    }
    <span class="text">
      <span class="words">{{ words() }}</span>
      @if (detail(); as detail) {
        <span class="detail">{{ detail }}</span>
      }
    </span>
  `,
  host: { '[attr.data-state]': 'state()' },
  styles: `
    :host {
      display: inline-flex;
      gap: var(--space-2);
      align-items: flex-start;
      color: var(--muted);
      font-size: var(--text-1);
      line-height: var(--leading-2);
    }

    .icon,
    .dot {
      margin-block-start: 0.125rem;
    }

    .dot {
      flex: none;
      box-sizing: border-box;
      inline-size: 1.25rem;
      block-size: 1.25rem;
      border: 2px solid currentColor;
      border-radius: 50%;
    }

    :host([data-state='saving']) .dot {
      border-style: dashed;
    }

    .text {
      display: flex;
      flex-direction: column;
    }

    .words {
      color: var(--ink);
      font-weight: 600;
    }

    :host([data-state='saved']) .icon {
      color: var(--success-text);
    }

    :host([data-state='retrying']) .icon,
    :host([data-state='retrying']) .words {
      color: var(--warning-text);
    }

    :host([data-state='failed']) .icon,
    :host([data-state='failed']) .words {
      color: var(--danger-text);
    }
  `,
})
export class PpSaveStatus {
  readonly state = input.required<PpSaveState>();
  readonly detail = input<string>();

  protected readonly words = computed(() => WORDS[this.state()]);

  constructor() {
    const toaster = inject(PpToaster);
    // What was last said: trouble is said once as it begins (the retries that follow are the same news, and so is a
    // refusal after them), and the save that ends it is said once too. A first state is never said: it is the page's.
    let first = true;
    let troubled = false;
    effect(() => {
      const state = this.state();
      const detail = untracked(this.detail);
      const said = detail ? `${sentence(WORDS[state])} ${detail}` : WORDS[state];
      const trouble = state === 'retrying' || state === 'failed';
      if (first) {
        first = false;
        troubled = trouble;
        return;
      }
      if (trouble && !troubled) {
        toaster.announce(said);
      } else if (state === 'saved' && troubled) {
        toaster.announce(said);
      }
      if (trouble || state === 'saved') {
        troubled = trouble;
      }
    });
  }
}

/** Words said as a sentence before another, so the two are never run together aloud. */
function sentence(words: string): string {
  return /[.…!?]$/.test(words) ? words : `${words}.`;
}
