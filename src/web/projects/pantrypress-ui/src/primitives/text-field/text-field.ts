import { ChangeDetectionStrategy, Component } from '@angular/core';
import { PpControl } from '../control/control';

/**
 * A single-line text input on the browser's own `<input>` (PP3.3), so `type`, `autocomplete`, `inputmode`,
 * `required`, and Angular's reactive forms work as they always do. Put it in a `pp-field` for its label.
 */
@Component({
  selector: 'input[pp-text-field]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '',
  styleUrl: '../control/control.css',
})
export class PpTextField extends PpControl {}
