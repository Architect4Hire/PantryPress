import {
  afterNextRender,
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  output,
} from '@angular/core';
import { PpIconButton } from '../icon-button/icon-button';
import { PpCloseIcon, PpTone, PpToneIcon, TONE_WORDS } from '../status/tone';
import { PpToaster } from '../toast/toaster';

/**
 * A status message in the page's flow (PP3.4): a surface panel with a border and icon in the tone's text color and
 * the tone said in words. One that appears after the page loads sets `announce`, so it is heard: a warning or an
 * error is an alert; a note or a success is said politely through the toaster's announcer once it appears, since a
 * polite live region added together with its words is often not heard. A `dismissible` one has a Dismiss button and
 * says `dismissed`.
 */
@Component({
  selector: 'pp-notice',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpIconButton, PpCloseIcon, PpToneIcon],
  template: `
    <pp-tone-icon class="icon" [tone]="tone()" />
    <div class="content">
      @if (heading(); as heading) {
        <p class="heading">
          <span class="visually-hidden">{{ word() }}: </span>{{ heading }}
        </p>
      } @else {
        <span class="visually-hidden">{{ word() }}: </span>
      }
      <ng-content />
    </div>
    @if (dismissible()) {
      <button pp-icon-button label="Dismiss" variant="quiet" size="sm" (click)="dismissed.emit()">
        <pp-close-icon />
      </button>
    }
  `,
  styleUrls: ['../shared/visually-hidden.css', './notice.css'],
  host: {
    '[attr.data-tone]': 'tone()',
    '[attr.role]': 'role()',
  },
})
export class PpNotice {
  readonly tone = input<PpTone>('info');
  readonly heading = input<string>();
  readonly announce = input(false, { transform: booleanAttribute });
  readonly dismissible = input(false, { transform: booleanAttribute });
  readonly dismissed = output<void>();

  protected readonly word = computed(() => TONE_WORDS[this.tone()]);
  protected readonly role = computed(() =>
    this.announce() && (this.tone() === 'danger' || this.tone() === 'warning') ? 'alert' : null,
  );

  constructor() {
    const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    const toaster = inject(PpToaster);
    afterNextRender(() => {
      if (this.announce() && this.role() === null) {
        const words = host.querySelector('.content')?.textContent?.replace(/\s+/g, ' ').trim();
        if (words) {
          toaster.announce(words);
        }
      }
    });
  }
}
