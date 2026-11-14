import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  PpButton,
  PpCard,
  PpChipGroup,
  PpChipOption,
  PpEmptyState,
  PpField,
  PpFieldset,
  PpNotice,
  PpPageHeader,
  PpProgress,
  PpSaveStatus,
  PpTextarea,
  PpTextField,
  PpToaster,
} from '@pantrypress/ui';
import { ApiProblem } from '../../core/api/api-problem';
import { SAME_SCREEN_STATE } from '../../core/navigation/same-screen';
import { RecipeDraft, RecipeDrafts } from '../../core/recipes/recipe-drafts';
import {
  RECIPE_NOT_FOUND_CODE,
  RecipeDetail,
  RecipesClient,
  RecipeVocabulary,
} from '../../core/recipes/recipes-client';
import { SessionClient } from '../../core/session/session-client';
import { signInFor } from '../../core/session/session-guards';
import { DashboardSites } from '../dashboard/dashboard-sites';
import { RecipeAutosave } from './recipe-autosave';
import { placeErrors, RecipeFields } from './recipe-form';
import { readMinutes, totalMinutes, writeDuration } from './recipe-minutes';

/** The editor's address for a recipe the server has not made yet. */
export const NEW_RECIPE = 'new';

/**
 * Text limits, shown to the person as the box's own limit. The server's are the rule (`RecipeLimits`, PP4.11); these
 * only keep a box from taking more than it would accept.
 */
export const LIMITS = {
  title: 200,
  summary: 1000,
  yieldUnit: 40,
  additionalTimeLabel: 30,
  videoUrl: 2048,
} as const;

type Loading =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready' }
  | { readonly kind: 'gone' }
  | { readonly kind: 'failed'; readonly problem: ApiProblem };

type Vocabulary =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly value: RecipeVocabulary }
  | { readonly kind: 'failed' };

const time = () =>
  new FormGroup({
    hours: new FormControl('', { nonNullable: true }),
    minutes: new FormControl('', { nonNullable: true }),
  });

/**
 * The recipe editor (PP4.13, RCP-001): one page for one recipe, under `/dashboard/sites/{siteSlug}/recipes/{id}`, or
 * `…/recipes/new` for one not made yet. This slice draws the details, times, and yield; the ingredients and steps
 * (PP4.14) and the previews (PP4.15) join the same page.
 *
 * - There is no Save button: every pause is saved (`RecipeAutosave`, PNFR-007), and the header says where saving stands.
 * - A new recipe is made once it has a title, and its address then becomes its own without moving focus.
 * - What the caller may do is the API's to say (`canEdit`, and its refusals); the screen shows it and disables what
 *   cannot be saved.
 */
@Component({
  selector: 'ppa-recipe-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    PpButton,
    PpCard,
    PpChipGroup,
    PpEmptyState,
    PpField,
    PpFieldset,
    PpNotice,
    PpPageHeader,
    PpProgress,
    PpSaveStatus,
    PpTextarea,
    PpTextField,
    ReactiveFormsModule,
    RouterLink,
  ],
  templateUrl: './recipe-editor.html',
  styleUrl: './recipe-editor.css',
})
export class RecipeEditor {
  private readonly recipes = inject(RecipesClient);
  private readonly drafts = inject(RecipeDrafts);
  private readonly sessions = inject(SessionClient);
  private readonly sites = inject(DashboardSites);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly toaster = inject(PpToaster);

  protected readonly limits = LIMITS;

  /** The three times a recipe can have, in the order the recipe card shows them. */
  protected readonly times = [
    { name: 'prep', legend: 'Prep time' },
    { name: 'cook', legend: 'Cook time' },
    { name: 'additional', legend: 'Additional time' },
  ] as const;

  protected readonly site = computed(() => {
    const current = this.sites.current();
    return current.kind === 'ready' ? current.site : null;
  });

  protected readonly loading = signal<Loading>({ kind: 'loading' });
  protected readonly vocabulary = signal<Vocabulary>({ kind: 'loading' });
  protected readonly autosave = signal<RecipeAutosave | null>(null);
  /** A new recipe picked up from this device where it was left. */
  protected readonly resumed = signal(false);
  /** An archived recipe being put back. */
  protected readonly restoring = signal(false);
  /** Keeping or giving up edits after the recipe changed somewhere else. */
  protected readonly deciding = signal(false);
  /**
   * Whether "More details" is open. Opened by the person, or by an error inside it; never closed by anything but the
   * person, so it can never shut on someone typing in it.
   */
  protected readonly moreOpen = signal(false);

  protected readonly form = new FormGroup({
    title: new FormControl('', { nonNullable: true }),
    summary: new FormControl('', { nonNullable: true }),
    videoUrl: new FormControl('', { nonNullable: true }),
    courses: new FormControl<readonly string[]>([], { nonNullable: true }),
    cuisines: new FormControl<readonly string[]>([], { nonNullable: true }),
    diets: new FormControl<readonly string[]>([], { nonNullable: true }),
    allergens: new FormControl<readonly string[]>([], { nonNullable: true }),
    keywords: new FormControl('', { nonNullable: true }),
    prep: time(),
    cook: time(),
    additional: time(),
    additionalTimeLabel: new FormControl('', { nonNullable: true }),
    yieldLow: new FormControl('', { nonNullable: true }),
    yieldHigh: new FormControl('', { nonNullable: true }),
    yieldUnit: new FormControl('', { nonNullable: true }),
  });

  /** The form as typed, kept as a signal so the page can read it. */
  protected readonly fields = signal<RecipeFields>(this.form.getRawValue());

  protected readonly base = computed(() => this.autosave()?.base() ?? null);
  protected readonly stop = computed(() => this.autosave()?.stop() ?? null);
  protected readonly isNew = computed(() => this.base() === null);
  protected readonly saveView = computed(
    () => this.autosave()?.view() ?? { state: 'none' as const },
  );
  protected readonly refusal = computed(() => this.autosave()?.refusal() ?? null);
  /** Whether the recipe can be changed here at all. Its words stay readable and selectable either way. */
  protected readonly locked = computed(() => {
    const stop = this.stop();
    return stop === 'read-only' || stop === 'archived' || stop === 'gone';
  });
  protected readonly busy = computed(() => this.autosave()?.busy() ?? false);
  protected readonly hasEdits = computed(() => this.autosave()?.hasEdits() ?? false);
  protected readonly placed = computed(() => placeErrors(this.autosave()?.errors() ?? {}));

  /** The saved title, so the heading never changes under someone typing. */
  protected readonly heading = computed(() => {
    const base = this.base();
    if (this.loading().kind === 'loading') {
      return 'Recipe';
    }
    return base === null ? 'New recipe' : base.title;
  });

  protected readonly hasAdditionalTime = computed(
    () => readMinutes(this.fields().additional).kind === 'minutes',
  );

  protected readonly totalTime = computed(() => {
    const f = this.fields();
    const read = [f.prep, f.cook, f.additional].map((t) => readMinutes(t));
    if (read.some((r) => r.kind === 'unreadable')) {
      return null;
    }
    const total = totalMinutes(read.map((r) => (r.kind === 'minutes' ? r.value : null)));
    return total === null ? null : writeDuration(total);
  });

  protected readonly courseOptions = computed(() => this.options('courses'));
  protected readonly cuisineOptions = computed(() => this.options('cuisines'));
  protected readonly dietOptions = computed(() => this.options('diets'));
  protected readonly allergenOptions = computed(() => this.options('allergens'));

  constructor() {
    const title = inject(Title);
    effect(() => {
      const site = this.site();
      const heading = this.heading();
      title.setTitle(
        site ? `${heading} - Recipes - ${site.name} - PantryPress` : `${heading} - PantryPress`,
      );
    });

    // Every edit goes to the autosave, which keeps it on this device at once.
    const edits = this.form.valueChanges.subscribe(() => {
      const fields = this.form.getRawValue();
      this.fields.set(fields);
      untracked(this.autosave)?.edit(fields);
    });

    // A screen kept for another recipe's address (Back, Forward) starts again on that recipe; the address a new recipe
    // was just given is its own, and changes nothing.
    const params = this.route.paramMap.subscribe((map) => {
      const id = map.get('recipeId') ?? NEW_RECIPE;
      const current = untracked(this.autosave);
      if (current !== null && current.base()?.id === id) {
        return;
      }
      void this.open(id);
    });

    const window = inject(DOCUMENT).defaultView;
    const online = () => untracked(this.autosave)?.online();
    window?.addEventListener('online', online);

    inject(DestroyRef).onDestroy(() => {
      edits.unsubscribe();
      params.unsubscribe();
      window?.removeEventListener('online', online);
      untracked(this.autosave)?.leave();
    });

    // Saving can stop on any answer; what cannot be saved any more cannot be changed either. Text boxes go read-only
    // (bound in the template), so their words can still be read in full and copied; the chips have no read-only state.
    effect(() => {
      const locked = this.locked();
      untracked(() => this.lock(locked));
    });

    // An error inside "More details" opens it; nothing closes it but the person.
    effect(() => {
      const slots = this.placed().slots;
      if (slots['allergens'] || slots['keywords'] || slots['videoUrl']) {
        untracked(() => this.moreOpen.set(true));
      }
    });

    void this.loadVocabulary();
  }

  /** The error to show under a box, if any. */
  protected error(slot: string): string | undefined {
    return this.placed().slots[slot];
  }

  protected async retryLoad(): Promise<void> {
    await this.open(this.route.snapshot.paramMap.get('recipeId') ?? NEW_RECIPE);
  }

  protected retryVocabulary(): void {
    void this.loadVocabulary();
  }

  /** The disclosure was opened or closed by the person. */
  protected toggled(event: Event): void {
    this.moreOpen.set((event.target as HTMLDetailsElement).open);
  }

  protected startOver(): void {
    const autosave = this.autosave();
    if (autosave === null || autosave.busy()) {
      return;
    }
    const kept = autosave.startOver();
    this.resumed.set(false);
    this.sync();
    this.offerUndo('Started a blank recipe.', kept);
  }

  protected async keepMine(): Promise<void> {
    await this.decide(async (autosave) => {
      await autosave.keepMine();
    });
  }

  protected async useSaved(): Promise<void> {
    await this.decide(async (autosave) => {
      const kept = await autosave.useSaved();
      if (kept !== null) {
        this.offerUndo('Your changes were set aside for the newer copy.', kept);
      }
    });
  }

  /** Lets go of what was typed where it cannot be saved, and shows the recipe as it is. */
  protected discard(): void {
    const kept = this.autosave()?.discard() ?? null;
    this.sync();
    this.offerUndo('What you typed was set aside.', kept);
  }

  /** Every way of letting typed words go can be undone for as long as the toast is up (frontend.md, easy as pie). */
  private offerUndo(message: string, kept: RecipeDraft | null): void {
    if (kept === null) {
      return;
    }
    const autosave = this.autosave();
    this.toaster.show({
      message,
      tone: 'info',
      action: {
        label: 'Undo',
        run: () => {
          if (autosave !== null && autosave === this.autosave()) {
            autosave.bringBack(kept);
            this.resumed.set(false);
            this.sync();
          }
        },
      },
    });
  }

  protected async restore(): Promise<void> {
    const autosave = this.autosave();
    if (autosave === null || this.restoring()) {
      return;
    }
    this.restoring.set(true);
    try {
      await autosave.restore();
    } finally {
      this.restoring.set(false);
    }
    this.sync();
  }

  private async decide(choice: (autosave: RecipeAutosave) => Promise<void>): Promise<void> {
    const autosave = this.autosave();
    if (autosave === null || this.deciding()) {
      return;
    }
    this.deciding.set(true);
    try {
      await choice(autosave);
    } finally {
      this.deciding.set(false);
    }
    this.sync();
  }

  private async open(id: string): Promise<void> {
    untracked(this.autosave)?.leave();
    const site = this.site();
    if (site === null) {
      return;
    }
    const account = this.sessions.session()?.accountId;
    if (!account) {
      // Drafts are filed by account; with none known, nothing may be kept or read.
      await this.signIn({ status: 401, code: 'session-ended', title: 'Sign in again.' });
      return;
    }
    const autosave = new RecipeAutosave(
      {
        recipes: this.recipes,
        drafts: this.drafts,
        newKey: () => `recipe-save-${crypto.randomUUID()}`,
        signedOut: (problem) => void this.signIn(problem),
        created: (recipe) => this.created(recipe),
      },
      account,
      site.slug,
    );
    this.resumed.set(false);

    if (id === NEW_RECIPE) {
      // Picked up where it was left. Offered as a choice only while nothing is sent by itself: a kept draft with a title
      // is made at once, and an offer to start over would vanish under the person.
      this.resumed.set(autosave.startNew() && autosave.fields().title.trim() === '');
      this.autosave.set(autosave);
      this.loading.set({ kind: 'ready' });
      this.sync();
      return;
    }

    this.autosave.set(null);
    this.loading.set({ kind: 'loading' });
    const result = await this.recipes.get(site.slug, id);
    if (!result.ok) {
      if (result.problem.status === 401) {
        await this.signIn(result.problem);
      } else if (
        result.problem.code === RECIPE_NOT_FOUND_CODE ||
        result.problem.code === 'not-found' ||
        result.problem.code === 'site-not-found'
      ) {
        // No such recipe here: anything kept for it can never be saved or shown again.
        this.drafts.remove(account, site.slug, id);
        this.loading.set({ kind: 'gone' });
      } else {
        this.loading.set({ kind: 'failed', problem: result.problem });
      }
      return;
    }
    autosave.startWith(result.value);
    this.autosave.set(autosave);
    this.loading.set({ kind: 'ready' });
    this.sync();
  }

  /** The new recipe exists: its address becomes its own, without moving the person's focus. */
  private created(recipe: RecipeDetail): void {
    this.resumed.set(false);
    const slug = this.site()?.slug;
    if (slug === undefined) {
      return;
    }
    void this.router.navigate(['/dashboard/sites', slug, 'recipes', recipe.id], {
      replaceUrl: true,
      state: SAME_SCREEN_STATE,
    });
  }

  /** Puts the autosave's form in the boxes - after a load or a decision - without counting it as an edit. */
  private sync(): void {
    const autosave = this.autosave();
    if (autosave === null) {
      return;
    }
    const fields = autosave.fields();
    this.form.setValue(
      {
        ...fields,
        prep: { ...fields.prep },
        cook: { ...fields.cook },
        additional: { ...fields.additional },
      },
      { emitEvent: false },
    );
    this.fields.set(fields);
    this.lock(this.locked());
  }

  /** The chips cannot be read-only, so they alone are disabled while the recipe cannot be changed. */
  private lock(locked: boolean): void {
    const chips = [
      this.form.controls.courses,
      this.form.controls.cuisines,
      this.form.controls.diets,
      this.form.controls.allergens,
    ];
    for (const chip of chips) {
      if (locked && chip.enabled) {
        chip.disable({ emitEvent: false });
      } else if (!locked && chip.disabled) {
        chip.enable({ emitEvent: false });
      }
    }
  }

  private async loadVocabulary(): Promise<void> {
    const site = this.site();
    if (site === null) {
      return;
    }
    this.vocabulary.set({ kind: 'loading' });
    const result = await this.recipes.vocabulary(site.slug);
    if (result.ok) {
      this.vocabulary.set({ kind: 'ready', value: result.value });
    } else if (result.problem.status === 401) {
      await this.signIn(result.problem);
    } else {
      this.vocabulary.set({ kind: 'failed' });
    }
  }

  private options(list: keyof RecipeVocabulary): readonly PpChipOption<string>[] {
    const vocabulary = this.vocabulary();
    return vocabulary.kind === 'ready'
      ? vocabulary.value[list].map((word) => ({ value: word.code, label: word.name }))
      : [];
  }

  private async signIn(problem: ApiProblem): Promise<void> {
    await this.router.navigateByUrl(signInFor(this.router, this.router.url, problem));
  }

  /** The site's recipe library. */
  protected readonly library = computed(() => [
    '/dashboard/sites',
    this.site()?.slug ?? '',
    'recipes',
  ]);
}
