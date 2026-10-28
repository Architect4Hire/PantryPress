import { ChangeDetectionStrategy, Component } from '@angular/core';
import { PpControl } from '../control/control';

/**
 * A choice from a list on the browser's own `<select>` (PP3.3): its options are native `<option>`s, so the keyboard,
 * screen readers, and phones all treat it as they treat every select. Put it in a `pp-field` for its label.
 */
@Component({
  selector: 'select[pp-select]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<ng-content />',
  styleUrl: '../control/control.css',
})
export class PpSelect extends PpControl {}
