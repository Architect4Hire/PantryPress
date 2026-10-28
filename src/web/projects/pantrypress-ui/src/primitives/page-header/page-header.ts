import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * The top of a dashboard page (PP3.4): its one `h1`, an optional sentence, and the page's actions, projected with
 * `ppPageActions`. At most one of those actions is primary. On a narrow screen the actions wrap under the heading.
 */
@Component({
  selector: 'pp-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="text">
      <h1 class="heading">{{ heading() }}</h1>
      @if (description(); as description) {
        <p class="description">{{ description }}</p>
      }
    </div>
    <div class="actions"><ng-content select="[ppPageActions]" /></div>
  `,
  styles: `
    :host {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-4);
      align-items: flex-end;
      justify-content: space-between;
      margin-block-end: var(--space-6);
    }

    .text {
      flex: 1 1 20rem;
      min-inline-size: 0;
    }

    .heading {
      margin: 0;
      color: var(--ink);
      font-family: var(--f-display);
      font-size: var(--text-6);
      font-weight: var(--display-weight);
      letter-spacing: var(--display-track);
      line-height: var(--leading-1);
      text-wrap: balance;
    }

    .description {
      max-inline-size: var(--measure);
      margin: var(--space-2) 0 0;
      color: var(--muted);
      font-size: var(--text-2);
      line-height: var(--leading-2);
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
    }

    .actions:empty {
      display: none;
    }
  `,
})
export class PpPageHeader {
  readonly heading = input.required<string>();
  readonly description = input<string>();
}
