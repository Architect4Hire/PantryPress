import { ChangeDetectionStrategy, Component } from '@angular/core';
import { PpControl } from '../control/control';

/** A multi-line text input on the browser's own `<textarea>` (PP3.3). Put it in a `pp-field` for its label. */
@Component({
  selector: 'textarea[pp-textarea]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '',
  styleUrls: ['../control/control.css', './textarea.css'],
})
export class PpTextarea extends PpControl {}
