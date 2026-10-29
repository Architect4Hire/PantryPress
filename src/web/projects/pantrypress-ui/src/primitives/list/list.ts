import {
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  linkedSignal,
  model,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { PpButton } from '../button/button';
import { PpField } from '../field/field';
import { PpNotice } from '../notice/notice';
import { PpProgress } from '../progress/progress';
import { PpSelect } from '../select/select';
import { PpToaster } from '../toast/toaster';

export interface PpSortOption<K> {
  readonly value: K;
  readonly label: string;
}

/** Where a list's data stands: being fetched, in hand, or not to be had. */
export type PpListStatus = 'loading' | 'ready' | 'error';

/** Which way the reader paged. The app keeps the cursors: each list answers a page with the cursor of the next. */
export type PpPageTurn = 'previous' | 'next';

/**
 * Everything around a list the app draws (PP3.5): a table, a grid, or rows, projected as they are. Above it, the
 * search field (`ppListSearch`), the filters (`ppListFilters`), and a "Sort by" select over typed `sortOptions`
 * with `[(sort)]`; below it, Previous and Next with the page's number, since every list pages by cursor and never
 * knows how many pages there are. It is a region named by its `label`.
 *
 * - `status` drives what shows. The first load shows a progress bar; a later one keeps the rows in view, marked busy,
 *   until the new ones arrive. An error shows the app's words and Try again (`retry`). `empty` shows the projected
 *   `ppListEmpty` content, a `pp-empty-state` with the one next step.
 * - Paging moves focus to the top of the rows, so the reader never stays at the bottom of a page that went away, and
 *   only the button pressed shows it working. Try again, and rows hidden while focus is in them, leave it on the list.
 * - Each time new results arrive, after the first, `resultsText` is said once through the announcer: "12 posts",
 *   "Page 2 of drafts". Set it with the status that brings them.
 */
@Component({
  selector: 'pp-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpButton, PpField, PpNotice, PpProgress, PpSelect],
  template: `
    <div class="controls">
      <div class="search"><ng-content select="[ppListSearch]" /></div>
      <div class="filters"><ng-content select="[ppListFilters]" /></div>
      @if (sortOptions().length > 0) {
        <pp-field class="sort" [label]="sortLabel()">
          <select pp-select (change)="sorted($event)">
            @for (option of sortOptions(); track $index) {
              <option
                [value]="$index"
                [selected]="option === currentSort()"
                [attr.selected]="option === currentSort() ? '' : null"
              >
                {{ option.label }}
              </option>
            }
          </select>
        </pp-field>
      }
    </div>
    @if (status() === 'error') {
      <pp-notice tone="danger" announce>
        <p class="error">{{ errorText() }}</p>
        <button pp-button size="sm" (click)="retried()">Try again</button>
      </pp-notice>
    } @else if (status() === 'loading' && !loaded()) {
      <pp-progress [label]="loadingWords()" />
    }
    <div
      #items
      class="items"
      tabindex="-1"
      [hidden]="!showItems()"
      [attr.aria-busy]="status() === 'loading' ? 'true' : null"
    >
      <ng-content />
    </div>
    <div #empty class="empty" [hidden]="!showEmpty()">
      <ng-content select="[ppListEmpty]" />
    </div>
    @if (hasPrevious() || hasNext()) {
      <nav class="pager" [attr.aria-label]="label() + ' pages'">
        <button
          pp-button
          size="sm"
          [disabled]="!hasPrevious()"
          [busy]="turning() === 'previous' && status() === 'loading'"
          (click)="turn('previous')"
        >
          Previous
        </button>
        <span class="page">Page {{ page() }}</span>
        <button
          pp-button
          size="sm"
          [disabled]="!hasNext()"
          [busy]="turning() === 'next' && status() === 'loading'"
          (click)="turn('next')"
        >
          Next
        </button>
      </nav>
    }
  `,
  styleUrl: './list.css',
  host: {
    role: 'region',
    tabindex: '-1',
    '[attr.aria-label]': 'label()',
  },
})
export class PpList<K> {
  /** What the list holds, in the reader's words: "Posts". It names the region and its pages. */
  readonly label = input.required<string>();
  readonly status = input<PpListStatus>('ready');
  /** Whether the answer in hand has nothing in it; the projected `ppListEmpty` content shows instead of the rows. */
  readonly empty = input(false);
  readonly errorText = input("This list couldn't be loaded. Check your connection and try again.");
  /** What the first load's progress bar says; "Loading posts" from the label by default. */
  readonly loadingText = input<string>();
  readonly resultsText = input<string>();

  readonly sortOptions = input<readonly PpSortOption<K>[]>([]);
  readonly sortLabel = input('Sort by');
  readonly sort = model<K>();

  /** The page's number, counted by the app from 1, since a cursor carries none. */
  readonly page = input(1);
  readonly hasPrevious = input(false);
  readonly hasNext = input(false);

  readonly pageChange = output<PpPageTurn>();
  readonly retry = output<void>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly items = viewChild.required<ElementRef<HTMLElement>>('items');
  private readonly emptyPart = viewChild.required<ElementRef<HTMLElement>>('empty');
  /** The way the reader last paged, while that page loads, so only the button they pressed shows it working. */
  protected readonly turning = signal<PpPageTurn | null>(null);
  protected readonly loadingWords = computed(
    () => this.loadingText() ?? `Loading ${this.label().toLowerCase()}`,
  );
  private readonly toaster = inject(PpToaster);

  /** Whether any answer has arrived yet: until one has, a load shows progress rather than keeping old rows. */
  protected readonly loaded = linkedSignal<PpListStatus, boolean>({
    source: this.status,
    computation: (status, previous) => status === 'ready' || (previous?.value ?? false),
  });
  protected readonly showItems = computed(
    () => this.loaded() && this.status() !== 'error' && !this.empty(),
  );
  protected readonly showEmpty = computed(
    () => this.loaded() && this.status() !== 'error' && this.empty(),
  );
  /**
   * The chosen sort, or the first when none or an unknown one is chosen, as the select shows it. Its option is marked
   * by attribute as well as property, because a server render writes no property.
   */
  protected readonly currentSort = computed(() => {
    const options = this.sortOptions();
    const sort = this.sort();
    return options.find((option) => Object.is(option.value, sort)) ?? options[0];
  });

  /** Whether the first answer, or first refusal, has come; results that follow it are said aloud. */
  private settled = false;
  private lastStatus: PpListStatus | null = null;

  constructor() {
    effect(() => {
      const status = this.status();
      untracked(() => this.statusChanged(status));
    });
    // Rows or an empty state hidden while focus is inside them would drop it on the page; it stays on the list.
    afterRenderEffect(() => {
      const hidden = [
        this.showItems() ? null : this.items().nativeElement,
        this.showEmpty() ? null : this.emptyPart().nativeElement,
      ];
      const focused = this.host.ownerDocument.activeElement;
      if (hidden.some((part) => part?.contains(focused))) {
        this.host.focus();
      }
    });
  }

  protected sorted(event: Event): void {
    const index = Number((event.target as HTMLSelectElement).value);
    const option = this.sortOptions()[index];
    if (option) {
      this.sort.set(option.value);
    }
  }

  protected turn(direction: PpPageTurn): void {
    // The rows the reader was at the bottom of are about to go; the next page starts at the top of the rows, past the
    // search and filters, or at the list itself when there are no rows to show.
    const items = this.items().nativeElement;
    (items.hidden ? this.host : items).focus();
    this.turning.set(direction);
    this.pageChange.emit(direction);
  }

  /** Try again goes with the error it answers, so focus moves to the list, where the answer will show. */
  protected retried(): void {
    this.host.focus();
    this.retry.emit();
  }

  private statusChanged(status: PpListStatus): void {
    const arrived = status === 'ready' && this.lastStatus !== null && this.lastStatus !== 'ready';
    const text = this.resultsText();
    if (arrived && this.settled && text) {
      this.toaster.announce(text);
    }
    if (status !== 'loading') {
      this.settled = true;
      this.turning.set(null);
    }
    this.lastStatus = status;
  }
}
