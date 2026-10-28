import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { PpIconButton } from '../icon-button/icon-button';
import { PpCloseIcon } from '../status/tone';
import { PpAnnouncer } from '../toast/announcer';
import { PpModalBase } from './modal-base';

/**
 * A modal question or task (PP3.4): `<pp-dialog [(open)]="asking" heading="Delete this post?" kind="alert">`, with
 * the body projected and the actions in `ppDialogActions`. `kind="alert"` is for a question that needs an answer
 * before anything else, such as confirming something that cannot be undone: its body is read with its heading, it
 * never closes on a backdrop click, and it marks its least destructive action `ppInitialFocus`.
 */
@Component({
  selector: 'pp-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpIconButton, PpCloseIcon, PpAnnouncer],
  template: `
    <!-- eslint-disable-next-line @angular-eslint/template/click-events-have-key-events, @angular-eslint/template/interactive-supports-focus -- a click on the backdrop; the keyboard's way out is Escape, which the browser gives every modal dialog -->
    <dialog
      #dialog
      class="dialog"
      [attr.role]="kind() === 'alert' ? 'alertdialog' : null"
      [attr.aria-labelledby]="headingId"
      [attr.aria-describedby]="kind() === 'alert' ? bodyId : null"
      (close)="closed()"
      (pointerdown)="pressed($event)"
      (click)="backdrop($event)"
    >
      <div class="frame">
        <header class="header">
          <h2 class="heading" [id]="headingId" tabindex="-1">{{ heading() }}</h2>
          <button pp-icon-button label="Close" variant="quiet" size="sm" (click)="close()">
            <pp-close-icon />
          </button>
        </header>
        <div class="body" [id]="bodyId"><ng-content /></div>
        <footer class="footer"><ng-content select="[ppDialogActions]" /></footer>
        <pp-announcer [owner]="this" />
      </div>
    </dialog>
  `,
  styleUrls: ['./modal.css', './dialog.css'],
  host: { '[attr.data-size]': 'size()' },
})
export class PpDialog extends PpModalBase {
  readonly kind = input<'dialog' | 'alert'>('dialog');
  readonly size = input<'sm' | 'md' | 'lg'>('md');

  protected override closesOnBackdrop(): boolean {
    return this.kind() !== 'alert' && super.closesOnBackdrop();
  }
}
