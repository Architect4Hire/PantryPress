import {
  afterEveryRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  input,
} from '@angular/core';
import { arrowStep } from '../shared/keys';

/** What the arrow keys move between. A text field or a select keeps its own arrow keys and its own tab stop. */
const ITEMS = 'button:not([disabled]), a[href]';

/**
 * A row of related controls with one tab stop (PP3.4): the arrow keys (turned around in a right-to-left page), Home,
 * and End move between its buttons and links, and Tab leaves the row. The `label` names it. The tab stop is checked
 * after every render, in the browser only, so a control added, removed, or enabled never leaves the row with none
 * or two, and a page rendered on the server keeps every control reachable.
 */
@Component({
  selector: 'pp-toolbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<ng-content />',
  host: {
    role: 'toolbar',
    'aria-orientation': 'horizontal',
    '[attr.aria-label]': 'label()',
    '(keydown)': 'move($event)',
    '(focusin)': 'focused($event)',
  },
  styles: `
    :host {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-1);
      align-items: center;
    }
  `,
})
export class PpToolbar {
  readonly label = input.required<string>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private stop: HTMLElement | null = null;

  constructor() {
    afterEveryRender(() => this.stopAt(this.stop));
  }

  private items(): HTMLElement[] {
    return [...this.host.querySelectorAll<HTMLElement>(ITEMS)];
  }

  /** Exactly one item is the row's tab stop: the one asked for while it is still there, else the first. */
  private stopAt(wanted: HTMLElement | null): void {
    const items = this.items();
    this.stop = wanted && items.includes(wanted) ? wanted : (items[0] ?? null);
    for (const item of items) {
      item.tabIndex = item === this.stop ? 0 : -1;
    }
    // A control disabled since it was made the stop keeps no tab index of its own.
    for (const off of this.host.querySelectorAll<HTMLElement>('button[disabled][tabindex]')) {
      off.removeAttribute('tabindex');
    }
  }

  protected focused(event: FocusEvent): void {
    const item = this.items().find((candidate) => candidate === event.target);
    if (item) {
      this.stopAt(item);
    }
  }

  protected move(event: KeyboardEvent): void {
    const items = this.items();
    const from = items.findIndex((item) => item === event.target);
    const to = from < 0 ? null : arrowStep(event, this.host, from, items.length);
    if (to === null) {
      return;
    }
    event.preventDefault();
    this.stopAt(items[to]);
    items[to].focus();
  }
}
