import { ChangeDetectionStrategy, Component } from '@angular/core';

/**
 * A raised panel that groups related content (PP3.4). It brings no heading of its own, so the page keeps its heading
 * levels in order: the caller writes the heading that fits where the card sits.
 */
@Component({
  selector: 'pp-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<ng-content />',
  styles: `
    :host {
      display: block;
      box-sizing: border-box;
      padding: var(--space-5);
      border: 1px solid var(--line-subtle);
      border-radius: var(--r);
      background: var(--surface);
      color: var(--ink);
    }
  `,
})
export class PpCard {}
