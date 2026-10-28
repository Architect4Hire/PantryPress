import { ChangeDetectionStrategy, Component } from '@angular/core';
import { PpButtonBase } from './button-base';

/**
 * A button with words (PP3.3): `<button pp-button variant="primary">Save</button>`, or on a link,
 * `<a pp-button routerLink="/sign-up">Sign up</a>`. One primary per step.
 */
@Component({
  selector: 'button[pp-button], a[pp-button]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (busy()) {
      <span class="spinner" aria-hidden="true"></span>
    }
    <ng-content />
  `,
  styleUrl: './button.css',
})
export class PpButton extends PpButtonBase {}
