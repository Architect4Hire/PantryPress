import { LowerCasePipe, NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  contentChild,
  Directive,
  inject,
  input,
  TemplateRef,
} from '@angular/core';
import { ShowcasePanel } from './showcase-panel';

interface PanelContext {
  readonly $implicit: ShowcasePanel;
}

/** Marks a specimen's states: `<ng-template ppaPanel let-panel>`, drawn once in each mode with that mode's state. */
@Directive({ selector: 'ng-template[ppaPanel]' })
export class PpaPanel {
  readonly template = inject<TemplateRef<PanelContext>>(TemplateRef);

  // eslint-disable-next-line @typescript-eslint/no-unused-vars -- Angular's guard names the context only in its type
  static ngTemplateContextGuard(_panel: PpaPanel, context: unknown): context is PanelContext {
    return true;
  }
}

/**
 * One primitive in the showcase (PP3.6): its name, what it is for, and its states drawn twice, in a light panel and a
 * dark one. Each panel sets its own `data-mode`, so the tokens resolve inside it whatever the page's mode is.
 */
@Component({
  selector: 'ppa-specimen',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LowerCasePipe, NgTemplateOutlet],
  template: `
    <section class="specimen" [id]="anchor()">
      <h2 class="name">{{ heading() }}</h2>
      <p class="use">{{ use() }}</p>
      <div class="pair">
        @for (panel of panels(); track panel.mode) {
          <div
            class="panel"
            role="group"
            [attr.data-mode]="panel.mode"
            [attr.aria-labelledby]="anchor() + '-' + panel.mode"
          >
            <p class="mode" [id]="anchor() + '-' + panel.mode">
              {{ heading() }}, {{ panel.label | lowercase }}
            </p>
            <ng-container
              [ngTemplateOutlet]="states().template"
              [ngTemplateOutletContext]="{ $implicit: panel }"
            />
          </div>
        }
      </div>
    </section>
  `,
  styleUrl: './specimen.css',
})
export class PpaSpecimen {
  readonly anchor = input.required<string>();
  readonly heading = input.required<string>();
  readonly use = input.required<string>();
  readonly panels = input.required<readonly ShowcasePanel[]>();

  protected readonly states = contentChild.required(PpaPanel);
}
