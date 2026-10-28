import { ChangeDetectionStrategy, Component } from '@angular/core';
import { PpModalBase } from '../dialog/modal-base';
import { PpIconButton } from '../icon-button/icon-button';
import { PpCloseIcon } from '../status/tone';
import { PpAnnouncer } from '../toast/announcer';

/**
 * A panel that slides in from the page's end edge for a side task, such as filters or details (PP3.4). It behaves as
 * a dialog does: modal, Escape closes it, and focus goes back to where it was. It arrives at once when the reader
 * asks for reduced motion.
 */
@Component({
  selector: 'pp-drawer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpIconButton, PpCloseIcon, PpAnnouncer],
  template: `
    <!-- eslint-disable-next-line @angular-eslint/template/click-events-have-key-events, @angular-eslint/template/interactive-supports-focus -- a click on the backdrop; the keyboard's way out is Escape, which the browser gives every modal dialog -->
    <dialog
      #dialog
      class="drawer"
      [attr.aria-labelledby]="headingId"
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
  styleUrls: ['../dialog/modal.css', './drawer.css'],
})
export class PpDrawer extends PpModalBase {}
