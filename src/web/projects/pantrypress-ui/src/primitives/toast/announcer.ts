import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { PpToaster } from './toaster';

/**
 * The one place the library speaks (PP3.4): a polite live region, present from the first render, that says the
 * toaster's latest announcement. The shell's sits in the toast region and a modal's inside its dialog; only the one
 * the toaster names speaks, so words are heard once, and heard while a dialog makes the rest of the page inert.
 * Internal to the library.
 */
@Component({
  selector: 'pp-announcer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="visually-hidden" aria-live="polite">
      @if (speaking()) {
        @for (announcement of toaster.announcements(); track announcement.id) {
          <p>{{ announcement.text }}</p>
        }
      }
    </div>
  `,
  styleUrl: '../shared/visually-hidden.css',
})
export class PpAnnouncer {
  /** The modal this announcer belongs to, or none for the shell's. */
  readonly owner = input<object | null>(null);

  protected readonly toaster = inject(PpToaster);
  protected readonly speaking = computed(() => this.toaster.speaker() === this.owner());
}
