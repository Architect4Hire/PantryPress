import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { PpTone } from '../status/tone';

/**
 * A short status beside an item, such as "Published" or "Draft" (PP3.4). The words carry the meaning and the color
 * only adds to it. A toned pill is its status fill with that status's ink; a neutral one is a bordered surface.
 */
@Component({
  selector: 'pp-status-pill',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<ng-content />',
  host: { '[attr.data-tone]': 'tone()' },
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      box-sizing: border-box;
      min-block-size: 1.5rem;
      padding: 0 var(--space-3);
      border: 1px solid transparent;
      border-radius: var(--r-full);
      font-size: var(--text-1);
      font-weight: 600;
      line-height: var(--leading-1);
      white-space: nowrap;
    }

    :host([data-tone='neutral']) {
      border-color: var(--line);
      background: var(--surface);
      color: var(--muted);
    }

    :host([data-tone='info']) {
      background: var(--info);
      color: var(--info-ink);
    }

    :host([data-tone='success']) {
      background: var(--success);
      color: var(--success-ink);
    }

    :host([data-tone='warning']) {
      background: var(--warning);
      color: var(--warning-ink);
    }

    :host([data-tone='danger']) {
      background: var(--danger);
      color: var(--danger-ink);
    }
  `,
})
export class PpStatusPill {
  readonly tone = input<PpTone | 'neutral'>('neutral');
}
