import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * What a page shows when it has nothing in it yet (PP3.4): a heading at the level that fits the page (2 by default),
 * a sentence saying what goes here, and the one obvious next step, projected: "No posts yet" and "Write a post".
 */
@Component({
  selector: 'pp-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @switch (level()) {
      @case (3) {
        <h3 class="heading">{{ heading() }}</h3>
      }
      @case (4) {
        <h4 class="heading">{{ heading() }}</h4>
      }
      @default {
        <h2 class="heading">{{ heading() }}</h2>
      }
    }
    @if (description(); as description) {
      <p class="description">{{ description }}</p>
    }
    <div class="action"><ng-content /></div>
  `,
  styles: `
    :host {
      display: grid;
      justify-items: center;
      gap: var(--space-3);
      padding: var(--space-7) var(--space-5);
      border: 1px dashed var(--line-subtle);
      border-radius: var(--r);
      text-align: center;
    }

    .heading {
      margin: 0;
      color: var(--ink);
      font-family: var(--f-display);
      font-size: var(--text-4);
      font-weight: var(--display-weight);
      line-height: var(--leading-1);
    }

    .description {
      max-inline-size: 36rem;
      margin: 0;
      color: var(--muted);
      font-size: var(--text-2);
      line-height: var(--leading-2);
    }

    .action:empty {
      display: none;
    }
  `,
})
export class PpEmptyState {
  readonly heading = input.required<string>();
  readonly description = input<string>();
  readonly level = input<2 | 3 | 4>(2);
}
