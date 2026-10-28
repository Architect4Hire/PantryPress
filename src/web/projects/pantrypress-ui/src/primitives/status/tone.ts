import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** A status a notice, toast, or pill can carry (PP3.4). Each draws with its `-text` role on a surface, never alone. */
export type PpTone = 'info' | 'success' | 'warning' | 'danger';

/** The word a screen reader hears before a status message, so the tone is never told by color or icon alone. */
export const TONE_WORDS: Readonly<Record<PpTone, string>> = {
  info: 'Note',
  success: 'Success',
  warning: 'Warning',
  danger: 'Error',
};

/** The icon for a tone, in the current color, hidden from screen readers (the tone's word is spoken instead). */
@Component({
  selector: 'pp-tone-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg viewBox="0 0 20 20" width="20" height="20" aria-hidden="true" focusable="false">
      @switch (tone()) {
        @case ('success') {
          <path
            fill="currentColor"
            d="M10 1.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17Zm4.2 6.3-5 5a.9.9 0 0 1-1.3 0l-2.4-2.4 1.3-1.3 1.8 1.8 4.3-4.4 1.3 1.3Z"
          />
        }
        @case ('warning') {
          <path
            fill="currentColor"
            d="M11.3 2.3a1.5 1.5 0 0 0-2.6 0L1.2 15.8A1.5 1.5 0 0 0 2.5 18h15a1.5 1.5 0 0 0 1.3-2.2L11.3 2.3ZM9.1 7h1.8v5H9.1V7Zm.9 8.4a1.1 1.1 0 1 1 0-2.2 1.1 1.1 0 0 1 0 2.2Z"
          />
        }
        @case ('danger') {
          <path
            fill="currentColor"
            d="M10 1.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17Zm-.9 4.25h1.8v5.5H9.1v-5.5Zm.9 9.25a1.15 1.15 0 1 1 0-2.3 1.15 1.15 0 0 1 0 2.3Z"
          />
        }
        @default {
          <path
            fill="currentColor"
            d="M10 1.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17Zm.9 13.75H9.1v-5.5h1.8v5.5ZM10 7.75a1.15 1.15 0 1 1 0-2.3 1.15 1.15 0 0 1 0 2.3Z"
          />
        }
      }
    </svg>
  `,
  styles: ':host { display: inline-flex; flex: none; }',
})
export class PpToneIcon {
  readonly tone = input.required<PpTone>();
}

/** A close mark for dismiss buttons, in the current color and hidden from screen readers. */
@Component({
  selector: 'pp-close-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg viewBox="0 0 20 20" width="20" height="20" aria-hidden="true" focusable="false">
      <path
        fill="none"
        stroke="currentColor"
        stroke-width="1.8"
        stroke-linecap="round"
        d="M5 5l10 10M15 5L5 15"
      />
    </svg>
  `,
  styles: ':host { display: inline-flex; }',
})
export class PpCloseIcon {}
