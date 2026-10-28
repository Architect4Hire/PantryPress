import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { PpButtonBase } from '../button/button-base';

/**
 * A square button that shows only an icon (PP3.3). Its words are the required `label`, spoken as its name, so it
 * cannot exist without one: `<button pp-icon-button label="Close"><svg aria-hidden="true">…</svg></button>`.
 */
@Component({
  selector: 'button[pp-icon-button], a[pp-icon-button]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (busy()) {
      <span class="spinner" aria-hidden="true"></span>
    } @else {
      <ng-content />
    }
  `,
  styleUrls: ['../button/button.css', './icon-button.css'],
  host: { '[attr.aria-label]': 'label()' },
})
export class PpIconButton extends PpButtonBase {
  readonly label = input.required<string>();
}
