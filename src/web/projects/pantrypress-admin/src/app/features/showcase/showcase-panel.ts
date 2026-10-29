import { computed, DestroyRef, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl } from '@angular/forms';
import type {
  PpChipOption,
  PpListStatus,
  PpPageTurn,
  PpRadioOption,
  PpSortOption,
  PpUploadItem,
} from '@pantrypress/ui';

/** The two modes the showcase draws every primitive in, side by side. */
export type ShowcaseMode = 'light' | 'dark';

type Course = 'breakfast' | 'dinner' | 'dessert';
type Diet = 'vegetarian' | 'vegan' | 'gluten-free';
type Sort = 'newest' | 'title';
type ListShape = 'rows' | 'empty' | 'error';

interface Recipe {
  readonly title: string;
  readonly course: Course;
  readonly published: boolean;
  readonly added: number;
}

const RECIPES: readonly Recipe[] = [
  { title: 'Apple pie', course: 'dessert', published: true, added: 12 },
  { title: 'Banana bread', course: 'breakfast', published: true, added: 11 },
  { title: 'Chicken pot pie', course: 'dinner', published: false, added: 10 },
  { title: 'Dutch baby', course: 'breakfast', published: true, added: 9 },
  { title: 'Eggplant parmesan', course: 'dinner', published: true, added: 8 },
  { title: 'Fig tart', course: 'dessert', published: false, added: 7 },
  { title: 'Granola', course: 'breakfast', published: true, added: 6 },
  { title: 'Hand pies', course: 'dessert', published: true, added: 5 },
  { title: 'Irish stew', course: 'dinner', published: true, added: 4 },
  { title: 'Jam roly-poly', course: 'dessert', published: false, added: 3 },
  { title: 'Kedgeree', course: 'breakfast', published: true, added: 2 },
  { title: 'Lemon tart', course: 'dessert', published: true, added: 1 },
];

const PAGE_SIZE = 4;

/** How long the showcase pretends a request takes, so a list or an upload shows its working state. */
export const SHOWCASE_WAIT_MS = 600;

/**
 * One mode's copy of the showcase's live state (PP3.6). Each panel has its own, so opening, choosing, or paging in the
 * light panel leaves the dark one as it was. It stands in for the app's API: requests "answer" after a short wait, the
 * one place the showcase waits on time. Made in a component's field initializer, so it may inject.
 */
export class ShowcasePanel {
  readonly label: string;

  // Actions and inputs (PP3.3).
  readonly frequencies: readonly PpRadioOption<string>[] = [
    { value: 'weekly', label: 'Weekly', hint: 'Every Monday morning.' },
    { value: 'monthly', label: 'Monthly' },
    { value: 'never', label: 'Never' },
  ];
  readonly frequency = new FormControl<string | null>('weekly');
  readonly frequencyRequired = new FormControl<string | null>(null);
  readonly frequencyDisabled = new FormControl<string | null>({ value: 'monthly', disabled: true });

  // Data (PP3.5).
  readonly courses: readonly PpChipOption<Course>[] = [
    { value: 'breakfast', label: 'Breakfast' },
    { value: 'dinner', label: 'Dinner' },
    { value: 'dessert', label: 'Dessert' },
  ];
  readonly diets: readonly PpChipOption<Diet>[] = [
    { value: 'vegetarian', label: 'Vegetarian' },
    { value: 'vegan', label: 'Vegan' },
    { value: 'gluten-free', label: 'Gluten-free' },
  ];
  readonly periods: readonly PpChipOption<string>[] = [
    { value: 'week', label: 'This week' },
    { value: 'month', label: 'This month' },
    { value: 'year', label: 'This year' },
  ];
  readonly sorts: readonly PpSortOption<Sort>[] = [
    { value: 'newest', label: 'Newest first' },
    { value: 'title', label: 'Title' },
  ];
  readonly shapes: readonly PpRadioOption<ListShape>[] = [
    { value: 'rows', label: 'With recipes' },
    { value: 'empty', label: 'Empty' },
    { value: 'error', label: "Couldn't load" },
  ];
  readonly courseFilter = new FormControl<readonly Course[]>([], { nonNullable: true });
  readonly courseChips = new FormControl<readonly Course[]>(['dinner'], { nonNullable: true });
  readonly periodChip = new FormControl<string | null>('month');
  readonly disabledChips = new FormControl<readonly Diet[]>(
    { value: ['vegetarian'], disabled: true },
    { nonNullable: true },
  );
  readonly shape = new FormControl<ListShape>('rows', { nonNullable: true });

  readonly listStatus = signal<PpListStatus>('ready');
  readonly sort = signal<Sort>('newest');
  readonly page = signal(1);
  /** The words in the search field as typed; `query` is what the last search asked for. */
  readonly searchText = signal('');
  private readonly query = signal('');
  private readonly chosenCourses = signal<readonly Course[]>([]);
  private readonly chosenShape = toSignal(this.shape.valueChanges, {
    initialValue: this.shape.value,
  });

  private readonly matches = computed(() => {
    const words = this.query().toLowerCase();
    const courses = this.chosenCourses();
    const found = RECIPES.filter(
      (recipe) =>
        recipe.title.toLowerCase().includes(words) &&
        (courses.length === 0 || courses.includes(recipe.course)),
    );
    return this.sort() === 'title'
      ? [...found].sort((a, b) => a.title.localeCompare(b.title))
      : [...found].sort((a, b) => b.added - a.added);
  });
  readonly rows = computed(() =>
    this.matches().slice((this.page() - 1) * PAGE_SIZE, this.page() * PAGE_SIZE),
  );
  /** Only a list showing rows has pages: the empty and error states have nothing to page through. */
  private readonly paged = computed(() => this.chosenShape() === 'rows');
  readonly hasPrevious = computed(() => this.paged() && this.page() > 1);
  readonly hasNext = computed(
    () => this.paged() && this.page() * PAGE_SIZE < this.matches().length,
  );
  /** Whether the reader has narrowed the list, so its empty state can offer to widen it again. */
  readonly narrowed = computed(() => this.query() !== '' || this.chosenCourses().length > 0);
  readonly listEmpty = computed(
    () => this.chosenShape() === 'empty' || this.matches().length === 0,
  );
  readonly shownStatus = computed<PpListStatus>(() =>
    this.chosenShape() === 'error' ? 'error' : this.listStatus(),
  );
  readonly resultsText = computed(() => {
    const count = this.matches().length;
    return `Page ${this.page()}: ${count} ${count === 1 ? 'recipe' : 'recipes'}`;
  });

  readonly uploads = signal<readonly PpUploadItem[]>([
    { id: 'waiting', name: 'apple-pie.jpg', status: 'waiting' },
    { id: 'uploading', name: 'banana-bread.jpg', status: 'uploading', progress: 60 },
    { id: 'done', name: 'dutch-baby.png', status: 'done' },
    {
      id: 'failed',
      name: 'fig-tart.jpg',
      status: 'failed',
      error: 'The connection dropped before this photo was sent. Try again.',
    },
  ]);

  // Layout and feedback (PP3.4).
  readonly tab = signal<string | undefined>('published');
  readonly noticeShown = signal(true);
  /** The tip is there at load, so only its return is announced (a notice that appears after load). */
  readonly tipReturned = signal(false);

  private readonly timers = new Set<ReturnType<typeof setTimeout>>();
  private chosen = 0;

  constructor(readonly mode: ShowcaseMode) {
    this.label = mode === 'light' ? 'Light' : 'Dark';
    this.shape.valueChanges.subscribe(() => this.page.set(1));
    this.courseFilter.valueChanges.subscribe((courses) =>
      this.reload(() => {
        this.chosenCourses.set(courses);
        this.page.set(1);
      }),
    );
    inject(DestroyRef).onDestroy(() => this.timers.forEach((timer) => clearTimeout(timer)));
  }

  search(words: string): void {
    this.reload(() => {
      this.query.set(words);
      this.page.set(1);
    });
  }

  sorted(sort: Sort | undefined): void {
    this.reload(() => {
      this.sort.set(sort ?? 'newest');
      this.page.set(1);
    });
  }

  turn(direction: PpPageTurn): void {
    this.reload(() => this.page.update((page) => page + (direction === 'next' ? 1 : -1)));
  }

  /** The empty state's next step: every recipe again, with the search and filter cleared. */
  showEverything(): void {
    this.shape.setValue('rows');
    this.searchText.set('');
    this.reload(() => {
      this.query.set('');
      this.chosenCourses.set([]);
      this.page.set(1);
    });
    this.courseFilter.setValue([], { emitEvent: false });
  }

  retry(): void {
    this.shape.setValue('rows');
    this.reload(() => undefined);
  }

  chooseFiles(files: readonly File[]): void {
    const added = files.map((file) => ({
      id: `chosen-${++this.chosen}`,
      name: file.name,
      status: 'uploading' as const,
      progress: 30,
    }));
    this.uploads.update((items) => [...items, ...added]);
    this.later(() => added.forEach((item) => this.setUpload(item.id, { status: 'done' })));
  }

  retryUpload(id: string): void {
    this.setUpload(id, { status: 'uploading', progress: 30, error: undefined });
    this.later(() => this.setUpload(id, { status: 'done' }));
  }

  removeUpload(id: string): void {
    this.uploads.update((items) => items.filter((item) => item.id !== id));
  }

  /** A new page of results "arrives" after the wait, so the list shows its busy rows first. */
  private reload(arrive: () => void): void {
    this.listStatus.set('loading');
    this.later(() => {
      arrive();
      this.listStatus.set('ready');
    });
  }

  private setUpload(id: string, change: Partial<PpUploadItem>): void {
    this.uploads.update((items) =>
      items.map((item) => (item.id === id ? { ...item, ...change } : item)),
    );
  }

  private later(run: () => void): void {
    const timer = setTimeout(() => {
      this.timers.delete(timer);
      run();
    }, SHOWCASE_WAIT_MS);
    this.timers.add(timer);
  }
}
