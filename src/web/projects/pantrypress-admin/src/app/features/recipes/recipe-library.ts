import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import {
  PpButton,
  PpEmptyState,
  PpField,
  PpList,
  PpListStatus,
  PpNotice,
  PpPageHeader,
  PpPageTurn,
  PpSearchField,
  PpSelect,
  PpStatusPill,
  PpTab,
  PpTabs,
  PpToaster,
} from '@pantrypress/ui';
import { ApiProblem } from '../../core/api/api-problem';
import { RecipesClient, RecipeSummary, RecipeVocabulary } from '../../core/recipes/recipes-client';
import { signInFor } from '../../core/session/session-guards';
import { DashboardSites } from '../dashboard/dashboard-sites';

/** Which recipes the library shows: in the library, or archived. */
export type LibraryTab = 'library' | 'archived';

/** What the address asks the list for (PP4.12b). Empty means any. */
export interface LibraryFilters {
  readonly tab: LibraryTab;
  readonly search: string;
  readonly course: string;
  readonly cuisine: string;
  readonly diet: string;
}

type Vocabulary =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly value: RecipeVocabulary }
  | { readonly kind: 'failed' };

type FilterName = 'course' | 'cuisine' | 'diet';

/** How many recipes one page shows. */
export const LIBRARY_PAGE = 25;

/** The address's query, read the one way the screen reads it. Anything else in it is ignored. */
export function readFilters(params: ParamMap): LibraryFilters {
  return {
    tab: params.get('archived') === 'true' ? 'archived' : 'library',
    search: params.get('q') ?? '',
    course: params.get('course') ?? '',
    cuisine: params.get('cuisine') ?? '',
    diet: params.get('diet') ?? '',
  };
}

function sameFilters(a: LibraryFilters, b: LibraryFilters): boolean {
  return (
    a.tab === b.tab &&
    a.search === b.search &&
    a.course === b.course &&
    a.cuisine === b.cuisine &&
    a.diet === b.diet
  );
}

/**
 * A site's recipe library (PP4.12b, RCP-001): every recipe of the site, most recently edited first, a page at a time,
 * searched by title and filtered by course, cuisine, and diet on the server, with an Archived tab beside it.
 *
 * - The address holds the search, the filters, and the tab (`?q=&course=&cuisine=&diet=&archived=true`), so Back and a
 *   reload keep them; the page is not in it, since a cursor lives only as long as the list it came from.
 * - Who may archive or restore a recipe is the API's to decide (`canEdit` says so, and a refusal is shown as it is
 *   worded); a recipe the caller may only read says "View only".
 * - The site comes from the route alone, through the site area, which draws this only once the API has answered for it.
 */
@Component({
  selector: 'ppa-recipe-library',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet,
    PpButton,
    PpEmptyState,
    PpField,
    PpList,
    PpNotice,
    PpPageHeader,
    PpSearchField,
    PpSelect,
    PpStatusPill,
    PpTab,
    PpTabs,
  ],
  templateUrl: './recipe-library.html',
  styleUrl: './recipe-library.css',
})
export class RecipeLibrary {
  private readonly recipes = inject(RecipesClient);
  private readonly sites = inject(DashboardSites);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toaster = inject(PpToaster);
  private readonly listRegion = viewChild(PpList, { read: ElementRef });

  protected readonly site = computed(() => {
    const current = this.sites.current();
    return current.kind === 'ready' ? current.site : null;
  });

  protected readonly filters = signal<LibraryFilters>(
    readFilters(this.route.snapshot.queryParamMap),
  );
  protected readonly searchText = signal(this.filters().search);
  protected readonly vocabulary = signal<Vocabulary>({ kind: 'loading' });

  protected readonly status = signal<PpListStatus>('loading');
  protected readonly items = signal<readonly RecipeSummary[]>([]);
  protected readonly nextCursor = signal<string | null>(null);
  /** The cursor each page was asked with; the first page has none. */
  private readonly cursors = signal<readonly (string | null)[]>([null]);
  protected readonly page = computed(() => this.cursors().length);
  /** The field errors of a search or filter the API refused, in its words; null when it took them. */
  protected readonly refusal = signal<readonly string[] | null>(null);
  /** The recipe being archived or restored, if any. */
  protected readonly working = signal<string | null>(null);

  protected readonly narrowed = computed(() => {
    const f = this.filters();
    return f.search.trim() !== '' || f.course !== '' || f.cuisine !== '' || f.diet !== '';
  });

  /**
   * A reload after archiving or restoring says nothing of its own: the toast says what happened, and the one announcer
   * keeps only the latest words, so a count would talk over it and its Undo (4.1.3).
   */
  private readonly quiet = signal(false);

  protected readonly resultsText = computed(() => {
    if (this.status() !== 'ready' || this.refusal() !== null || this.quiet()) {
      return undefined;
    }
    const count = this.items().length;
    if (count === 0) {
      return this.narrowed() ? 'No recipes match' : 'No recipes';
    }
    const words = `${count} ${count === 1 ? 'recipe' : 'recipes'}`;
    return this.page() > 1 ? `Page ${this.page()}, ${words}` : words;
  });

  private asked = 0;

  constructor() {
    const title = inject(Title);
    effect(() => {
      const site = this.site();
      title.setTitle(site ? `Recipes - ${site.name} - PantryPress` : 'Recipes - PantryPress');
    });

    const subscription = this.route.queryParamMap.subscribe((params) => {
      const next = readFilters(params);
      const changed = !sameFilters(next, untracked(this.filters));
      this.filters.set(next);
      this.searchText.set(next.search);
      if (changed || untracked(this.status) === 'loading') {
        void this.load([null]);
      }
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());

    void this.loadVocabulary();
  }

  /** Try again on the notice, which goes away with its button: focus waits on the list first. */
  protected retryVocabulary(): void {
    this.focusList();
    void this.loadVocabulary();
  }

  /** The filters' words: asked once, and again from the notice after a failure. */
  async loadVocabulary(): Promise<void> {
    const slug = this.slug();
    if (slug === null) {
      return;
    }
    this.vocabulary.set({ kind: 'loading' });
    const result = await this.recipes.vocabulary(slug);
    if (result.ok) {
      this.vocabulary.set({ kind: 'ready', value: result.value });
    } else if (!(await this.signedOut(result.problem))) {
      this.vocabulary.set({ kind: 'failed' });
    }
  }

  protected search(text: string): void {
    void this.navigate({ q: text.trim() === '' ? null : text });
  }

  protected choose(name: FilterName, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    void this.navigate({ [name]: value === '' ? null : value });
  }

  protected chooseTab(key: string | undefined): void {
    void this.navigate({ archived: key === 'archived' ? 'true' : null });
  }

  /**
   * Clears the search and filters, keeping the tab. The button that asked goes away with them, so focus waits on the list.
   */
  protected clear(): void {
    this.focusList();
    void this.navigate({ q: null, course: null, cuisine: null, diet: null });
  }

  protected turn(turn: PpPageTurn): void {
    const cursors = this.cursors();
    if (turn === 'next') {
      const next = this.nextCursor();
      if (next !== null) {
        void this.load([...cursors, next]);
      }
    } else if (cursors.length > 1) {
      void this.load(cursors.slice(0, -1));
    }
  }

  /** Asks for the page in view again. */
  protected reload(): void {
    void this.load(this.cursors());
  }

  protected archive(recipe: RecipeSummary): Promise<void> {
    return this.change(recipe, 'archive');
  }

  protected restore(recipe: RecipeSummary): Promise<void> {
    return this.change(recipe, 'restore');
  }

  /** When a recipe was last edited, in the site's own language and time zone. */
  protected edited(at: string): string {
    const site = this.site();
    try {
      return new Intl.DateTimeFormat(site?.locale || undefined, {
        dateStyle: 'medium',
        timeZone: site?.timeZone || undefined,
      }).format(new Date(at));
    } catch {
      return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(new Date(at));
    }
  }

  private async change(recipe: RecipeSummary, action: 'archive' | 'restore'): Promise<void> {
    const slug = this.slug();
    if (slug === null || this.working() !== null) {
      return;
    }
    this.working.set(recipe.id);
    // One key per press: a retry of this press would send it again, and a new press is a new key.
    const key = `recipe-${action}-${crypto.randomUUID()}`;
    const result =
      action === 'archive'
        ? await this.recipes.archive(slug, recipe.id, key)
        : await this.recipes.restore(slug, recipe.id, key);
    this.working.set(null);

    if (!result.ok) {
      if (!(await this.signedOut(result.problem))) {
        this.toaster.show({ message: result.problem.title, tone: 'danger' });
        await this.load(this.cursors(), true);
      }
      return;
    }

    this.toaster.show(
      action === 'archive'
        ? {
            message: `“${recipe.title}” moved to Archived.`,
            tone: 'success',
            action: { label: 'Undo', run: () => void this.undo(slug, recipe, 'restore') },
          }
        : { message: `“${recipe.title}” is back in your library.`, tone: 'success' },
    );
    // The row has gone from this list; focus waits on the list, never on a button that is no longer there.
    this.focusList();
    await this.load(this.cursors(), true);
  }

  private async undo(slug: string, recipe: RecipeSummary, action: 'restore'): Promise<void> {
    const result = await this.recipes.restore(
      slug,
      recipe.id,
      `recipe-${action}-${crypto.randomUUID()}`,
    );
    if (!result.ok) {
      if (!(await this.signedOut(result.problem))) {
        this.toaster.show({ message: result.problem.title, tone: 'danger' });
      }
      return;
    }
    this.toaster.show({ message: `“${recipe.title}” is back in your library.`, tone: 'success' });
    await this.load(this.cursors(), true);
  }

  private async load(cursors: readonly (string | null)[], quiet = false): Promise<void> {
    const slug = this.slug();
    if (slug === null) {
      return;
    }
    const ask = ++this.asked;
    this.quiet.set(quiet);
    const filters = this.filters();
    const cursor = cursors[cursors.length - 1];
    this.status.set('loading');
    const result = await this.recipes.list(slug, {
      limit: LIBRARY_PAGE,
      ...(cursor === null ? {} : { cursor }),
      ...(filters.tab === 'archived' ? { archived: true } : {}),
      search: filters.search,
      course: filters.course,
      cuisine: filters.cuisine,
      diet: filters.diet,
    });
    // A later ask wins: an answer for a list the reader has moved on from is dropped.
    if (ask !== this.asked) {
      return;
    }
    if (result.ok) {
      // A page past the first that has emptied - its last recipe archived or restored - steps back a page, so the
      // screen never says the whole list is empty beside a Previous button.
      if (result.value.items.length === 0 && cursors.length > 1) {
        await this.load(cursors.slice(0, -1), quiet);
        return;
      }
      this.refusal.set(null);
      this.items.set(result.value.items);
      this.nextCursor.set(result.value.nextCursor);
      this.cursors.set(cursors);
      this.status.set('ready');
    } else if (result.problem.status === 400 && result.problem.errors !== undefined) {
      // A search or filter the API would not take - an address edited by hand, or a search too long - says why, in its
      // own words, and offers to clear them. A cursor it refused means the list moved on: start again from page one.
      if (cursor !== null && Object.keys(result.problem.errors).includes('cursor')) {
        await this.load([null]);
        return;
      }
      this.refusal.set(Object.values(result.problem.errors).flat());
      this.items.set([]);
      this.nextCursor.set(null);
      this.cursors.set([null]);
      this.status.set('ready');
    } else if (!(await this.signedOut(result.problem))) {
      this.status.set('error');
    }
  }

  private navigate(changes: Record<string, string | null>): Promise<boolean> {
    return this.router.navigate([], {
      relativeTo: this.route,
      queryParams: changes,
      queryParamsHandling: 'merge',
    });
  }

  private focusList(): void {
    this.listRegion()?.nativeElement.focus();
  }

  private slug(): string | null {
    return this.site()?.slug ?? null;
  }

  private async signedOut(problem: ApiProblem): Promise<boolean> {
    if (problem.status !== 401) {
      return false;
    }
    await this.router.navigateByUrl(signInFor(this.router, this.router.url, problem));
    return true;
  }
}
