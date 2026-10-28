import {
  ChangeDetectionStrategy,
  Component,
  computed,
  contentChildren,
  ElementRef,
  forwardRef,
  inject,
  input,
  model,
  viewChildren,
} from '@angular/core';
import { nextId } from '../field/field-context';
import { arrowStep } from '../shared/keys';

/**
 * Panels of one page shown one at a time (PP3.4), in the ARIA tabs pattern: one tab stop for the whole list, the
 * arrow keys (turned around in a right-to-left page), Home, and End move between tabs, and moving selects. Each
 * `pp-tab` is its panel: `<pp-tabs [(selected)]="key"><pp-tab key="general" label="General">…</pp-tab></pp-tabs>`.
 * With nothing selected, or a key no tab has, the first tab is. Each `pp-tab` is a direct child of its `pp-tabs`.
 * Hidden panels need the app running to be reached, so tabs belong in the dashboard, never on a prerendered page.
 */
@Component({
  selector: 'pp-tabs',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="list" role="tablist" [attr.aria-label]="label() ?? null">
      @for (tab of tabs(); track tab; let index = $index) {
        <button
          #tabButton
          class="tab"
          type="button"
          role="tab"
          [id]="tabId(index)"
          [attr.aria-controls]="panelId(index)"
          [attr.aria-selected]="tab.key() === current() ? 'true' : 'false'"
          [attr.tabindex]="tab.key() === current() ? 0 : -1"
          (click)="selected.set(tab.key())"
          (keydown)="move($event)"
        >
          {{ tab.label() }}
        </button>
      }
    </div>
    <ng-content />
  `,
  styleUrl: './tabs.css',
})
export class PpTabs {
  readonly selected = model<string>();
  /** A name for the list when the page has no heading right above it. */
  readonly label = input<string>();

  readonly tabs = contentChildren(forwardRef(() => PpTab));
  private readonly buttons = viewChildren<ElementRef<HTMLButtonElement>>('tabButton');
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly base = nextId('tabs');

  readonly current = computed(() => {
    const keys = this.tabs().map((tab) => tab.key());
    const selected = this.selected();
    return selected !== undefined && keys.includes(selected) ? selected : keys[0];
  });

  tabId(index: number): string {
    return `${this.base}-tab-${index}`;
  }

  panelId(index: number): string {
    return `${this.base}-panel-${index}`;
  }

  protected move(event: KeyboardEvent): void {
    const tabs = this.tabs();
    const from = tabs.findIndex((tab) => tab.key() === this.current());
    const to = arrowStep(event, this.host, from, tabs.length);
    if (to === null) {
      return;
    }
    event.preventDefault();
    this.selected.set(tabs[to].key());
    this.buttons()[to]?.nativeElement.focus();
  }
}

/** One tab's label and its panel. The panel is hidden unless its tab is selected, and is reachable by Tab itself. */
@Component({
  selector: 'pp-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<ng-content />',
  host: {
    role: 'tabpanel',
    tabindex: '0',
    '[id]': 'tabs.panelId(index())',
    '[attr.aria-labelledby]': 'tabs.tabId(index())',
    '[hidden]': 'tabs.current() !== key()',
  },
  styles: `
    :host {
      display: block;
      padding-block-start: var(--space-5);
    }

    :host([hidden]) {
      display: none;
    }

    :host(:focus-visible) {
      outline: 2px solid var(--focus);
      outline-offset: 2px;
    }
  `,
})
export class PpTab {
  readonly key = input.required<string>();
  readonly label = input.required<string>();

  protected readonly tabs = inject(PpTabs);
  protected readonly index = computed(() => this.tabs.tabs().indexOf(this));
}
