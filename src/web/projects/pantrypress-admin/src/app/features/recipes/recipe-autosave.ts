import { computed, signal } from '@angular/core';
import { PpSaveState } from '@pantrypress/ui';
import { ApiProblem, ApiResult } from '../../core/api/api-problem';
import { OutstandingSave, RecipeDraft, RecipeDrafts } from '../../core/recipes/recipe-drafts';
import {
  RECIPE_ARCHIVED_CODE,
  RECIPE_CHANGED_CODE,
  RECIPE_NOT_FOUND_CODE,
  RECIPE_NOT_YOURS_CODE,
  RecipeDetail,
  RecipesClient,
} from '../../core/recipes/recipes-client';
import {
  changedGroups,
  differsFrom,
  EMPTY_FIELDS,
  FieldErrors,
  fieldErrorsFrom,
  fieldsFrom,
  RECIPE_FIELD_GROUPS,
  RecipeFieldGroup,
  RecipeFields,
  readFields,
  restoreFields,
  withGroups,
} from './recipe-form';

/** How long a pause in typing is before it is saved: inside PNFR-007's two seconds. */
export const SAVE_DELAY_MS = 1500;

/** How long to wait before sending a save that did not get through again, then again; the last repeats. */
export const RETRY_DELAYS_MS = [2000, 4000, 8000, 15000, 30000] as const;

/**
 * Why saving has stopped until the person decides:
 *
 * - `changed`: the recipe changed somewhere else since these edits were made on it;
 * - `archived`: the recipe was archived;
 * - `read-only`: the caller may read the recipe but not change it;
 * - `gone`: there is no such recipe on this site (any more).
 */
export type SaveStop = 'changed' | 'archived' | 'read-only' | 'gone';

/** What the editor shows of saving. `none` shows nothing: a new recipe with no title yet, or one that can't be changed. */
export type SaveView =
  { readonly state: 'none' } | { readonly state: PpSaveState; readonly detail?: string };

export const KEPT_HERE = 'Your changes are kept on this device.';
export const FIX_MARKED = "Fix what's marked below.";
export const CANNOT_SAVE = "It can't be saved to this recipe.";
/** A save kept here this long may have landed beyond the server's memory of its key (24 hours, PP1.9). */
export const STALE_SAVE_MS = 23 * 60 * 60 * 1000;
export const STALE_SAVE =
  "We couldn't confirm your last save. If this recipe is already in your library, open it there instead.";

/** What the autosave needs from outside: the API, the drafts, and the clock. */
export interface AutosaveDeps {
  readonly recipes: Pick<RecipesClient, 'get' | 'create' | 'update' | 'restore'>;
  readonly drafts: Pick<RecipeDrafts, 'read' | 'write' | 'remove'>;
  /** A new `Idempotency-Key`. */
  readonly newKey: () => string;
  /** The session ended: keep everything here and go and sign in. */
  readonly signedOut: (problem: ApiProblem) => void;
  /** The new recipe now exists: give the screen its address. */
  readonly created: (recipe: RecipeDetail) => void;
}

/**
 * The recipe editor's autosave (PNFR-007): every pause in typing is saved within two seconds, and nothing typed is lost
 * to a reload or a lost connection.
 *
 * - Every edit is written to this device at once (`RecipeDrafts`) and cleared only once the server has it.
 * - One save is in flight at a time; what is typed meanwhile goes in the next.
 * - A save sent and never answered may have landed, so it is sent again exactly - the same key, version, and recipe -
 *   until an answer comes (PP1.9), and only then is anything newer sent. It survives a reload in the draft.
 * - Each save gets a new `Idempotency-Key`; a refusal the person fixes is a new save, with a new key.
 * - Each answer's `version` goes with the next save. A recipe changed somewhere else is never overwritten unasked: saving
 *   stops, and the person keeps their edits over the newer copy (`keepMine`) or takes the newer copy (`useSaved`).
 * - A new recipe is made by its first save with a title, under a key kept in its draft, so a reload before the answer
 *   makes the same recipe rather than a second.
 */
export class RecipeAutosave {
  private readonly fieldsState = signal<RecipeFields>(EMPTY_FIELDS);
  private readonly baseState = signal<RecipeDetail | null>(null);
  private readonly stopState = signal<SaveStop | null>(null);
  private readonly localErrors = signal<FieldErrors>({});
  private readonly serverErrors = signal<FieldErrors>({});
  /** A refusal that says nothing about a field, in the API's words. */
  private readonly refusalState = signal<string | null>(null);
  private readonly inFlight = signal(false);
  private readonly waiting = signal(false);
  private readonly retrying = signal(false);

  private edited = new Set<RecipeFieldGroup>();
  private readonly outstandingState = signal<OutstandingSave | null>(null);
  private timer: ReturnType<typeof setTimeout> | null = null;
  private attempts = 0;
  private again = false;
  private closed = false;
  /** Moves on "start over": an answer to a save sent before it belongs to a recipe the person gave up, and is dropped. */
  private generation = 0;

  private setOutstanding(save: OutstandingSave | null): void {
    this.outstandingState.set(save);
  }

  /** The form as typed. */
  readonly fields = this.fieldsState.asReadonly();
  /** The server's last answer: what is saved. */
  readonly base = this.baseState.asReadonly();
  readonly stop = this.stopState.asReadonly();
  readonly refusal = this.refusalState.asReadonly();
  /** Field errors by the API's paths: this screen's own reading first, then the server's. */
  readonly errors = computed<FieldErrors>(() => ({
    ...this.serverErrors(),
    ...this.localErrors(),
  }));

  /** Whether there are edits here the server does not have. */
  readonly hasEdits = computed(() => {
    const base = this.baseState();
    return (
      this.outstandingState() !== null ||
      (base === null
        ? JSON.stringify(this.fieldsState()) !== JSON.stringify(EMPTY_FIELDS)
        : differsFrom(this.fieldsState(), base))
    );
  });

  /** Whether a save is on its way or waiting to be sent again: a time to make no other choice. */
  readonly busy = computed(() => this.inFlight() || this.retrying());

  readonly view = computed<SaveView>(() => {
    const stop = this.stopState();
    if (stop !== null) {
      // Saving has stopped: only edits made here are news. Where the person can still save them, they are kept for
      // that; where they cannot (no right to, no such recipe), they are still on the screen to copy.
      if (!this.hasEdits()) {
        return { state: 'none' };
      }
      return stop === 'read-only' || stop === 'gone'
        ? { state: 'failed', detail: CANNOT_SAVE }
        : { state: 'failed', detail: KEPT_HERE };
    }
    const base = this.baseState();
    if (this.inFlight()) {
      return { state: 'saving' };
    }
    if (this.retrying()) {
      return { state: 'retrying', detail: KEPT_HERE };
    }
    if (this.refusalState() !== null || Object.keys(this.errors()).length > 0) {
      return { state: 'failed', detail: FIX_MARKED };
    }
    if (
      base === null &&
      this.outstandingState() === null &&
      this.fieldsState().title.trim() === ''
    ) {
      return { state: 'none' };
    }
    if (this.waiting() || base === null || differsFrom(this.fieldsState(), base)) {
      return { state: 'pending' };
    }
    return { state: 'saved' };
  });

  constructor(
    private readonly deps: AutosaveDeps,
    private readonly account: string,
    private readonly slug: string,
  ) {}

  /**
   * Starts on a new recipe: a draft started here before is picked up where it was left (and says so: true), with any
   * create still waiting for its answer.
   */
  startNew(): boolean {
    const draft = this.deps.drafts.read(this.account, this.slug, null);
    const fields = draft === null ? null : restoreFields(draft.fields);
    if (draft === null || fields === null) {
      return false;
    }
    this.fieldsState.set(fields);
    this.edited = new Set(groupsOf(draft.edited));
    this.setOutstanding(this.fresh(draft.outstanding));
    this.schedule(0);
    return true;
  }

  /**
   * Forgets the draft of a new recipe and starts with an empty form. A create already on its way is let go: its answer is
   * dropped, so nothing typed from here on is saved over the recipe it may make. Answers what was let go, for an undo.
   */
  startOver(): RecipeDraft | null {
    const kept = this.snapshot();
    this.generation += 1;
    this.clearTimer();
    this.deps.drafts.remove(this.account, this.slug, null);
    this.setOutstanding(null);
    this.edited.clear();
    this.attempts = 0;
    this.again = false;
    this.retrying.set(false);
    this.waiting.set(false);
    this.inFlight.set(false);
    this.localErrors.set({});
    this.serverErrors.set({});
    this.refusalState.set(null);
    this.fieldsState.set(EMPTY_FIELDS);
    return kept;
  }

  /** Puts back what `startOver` or `useSaved` let go: the undo of either. */
  bringBack(kept: RecipeDraft): void {
    const fields = restoreFields(kept.fields);
    if (fields === null) {
      return;
    }
    this.fieldsState.set(fields);
    this.edited = new Set(groupsOf(kept.edited));
    this.setOutstanding(this.fresh(kept.outstanding));
    this.keep();
    const stop = this.stopState();
    if (stop === 'read-only' || stop === 'gone') {
      // Back on the screen to copy; still nothing here can save them.
      return;
    }
    if (this.baseState() === null) {
      this.schedule(0);
    } else {
      // The newer copy is on screen: the choice between it and these edits is the person's again.
      this.stopState.set('changed');
    }
  }

  /** Lets go of edits that cannot be saved here (no right to, no such recipe) and shows the recipe as it is. */
  discard(): RecipeDraft | null {
    const kept = this.snapshot();
    const base = this.baseState();
    this.setOutstanding(null);
    this.edited.clear();
    this.localErrors.set({});
    this.serverErrors.set({});
    this.refusalState.set(null);
    this.deps.drafts.remove(this.account, this.slug, base?.id ?? null);
    this.fieldsState.set(base === null ? EMPTY_FIELDS : fieldsFrom(base));
    return kept;
  }

  /**
   * Starts on a recipe the server answered for. Edits kept on this device are put back: sent at once when they were made
   * on this very version (or a save of them is still waiting for its answer), and otherwise held while the person decides,
   * since the recipe changed since. Says whether there were any.
   */
  startWith(recipe: RecipeDetail): boolean {
    this.baseState.set(recipe);
    this.fieldsState.set(fieldsFrom(recipe));
    const draft = this.deps.drafts.read(this.account, this.slug, recipe.id);
    const fields = draft === null ? null : restoreFields(draft.fields);
    if (draft === null || fields === null) {
      if (!recipe.canEdit) {
        this.stopState.set('read-only');
      } else if (recipe.archivedAt !== null) {
        this.stopState.set('archived');
      }
      return false;
    }
    this.fieldsState.set(fields);
    this.edited = new Set(groupsOf(draft.edited));
    if (!recipe.canEdit) {
      // Edits this account can no longer save are shown, to copy, until the person lets them go (`discard`).
      this.stopState.set('read-only');
      return true;
    }
    this.setOutstanding(this.fresh(draft.outstanding));
    if (recipe.archivedAt !== null && this.outstandingState() === null) {
      this.stopState.set('archived');
    } else if (this.outstandingState() === null && draft.baseVersion !== recipe.version) {
      this.stopState.set('changed');
    } else {
      this.schedule(0);
    }
    return true;
  }

  /** The person changed the form. */
  edit(fields: RecipeFields): void {
    const before = this.fieldsState();
    for (const group of changedGroups(before, fields)) {
      this.edited.add(group);
    }
    this.fieldsState.set(fields);
    this.keep();
    if (this.stopState() === null) {
      this.schedule(SAVE_DELAY_MS);
    }
  }

  /** Saves now whatever is waiting: leaving the screen, or the connection is back. */
  saveNow(): void {
    if (this.stopState() === null && (this.timer !== null || this.retrying() || this.again)) {
      this.schedule(0);
    }
  }

  /** The connection came back: a save waiting to be sent again goes now. */
  online(): void {
    if (this.retrying()) {
      this.schedule(0);
    }
  }

  /** Keeps the person's edits over the newest copy of the recipe, and saves them. */
  async keepMine(): Promise<void> {
    const latest = await this.reread();
    if (latest === null) {
      return;
    }
    this.stopState.set(null);
    this.fieldsState.set(withGroups(fieldsFrom(latest), this.fieldsState(), this.edited));
    this.keep();
    this.schedule(0);
  }

  /** Gives up the edits made here and shows the newest copy. Answers what was let go, for an undo. */
  async useSaved(): Promise<RecipeDraft | null> {
    const kept = this.snapshot();
    const latest = await this.reread();
    if (latest === null) {
      return null;
    }
    this.finishUseSaved(latest);
    return kept;
  }

  private finishUseSaved(latest: RecipeDetail): void {
    this.stopState.set(null);
    this.fieldsState.set(fieldsFrom(latest));
    this.edited.clear();
    this.setOutstanding(null);
    this.localErrors.set({});
    this.serverErrors.set({});
    this.deps.drafts.remove(this.account, this.slug, latest.id);
  }

  /** Puts an archived recipe back in the library, then saves the edits kept here over it. */
  async restore(): Promise<boolean> {
    const base = this.baseState();
    if (base === null) {
      return false;
    }
    const result = await this.deps.recipes.restore(this.slug, base.id, this.deps.newKey());
    if (!result.ok) {
      this.refused(result.problem);
      return false;
    }
    await this.keepMine();
    return true;
  }

  /**
   * The screen is going: whatever is waiting is sent now, and no timer runs after this. A save already on its way
   * finishes; anything it cannot send stays in the draft for the next visit.
   */
  leave(): void {
    const waiting = this.timer !== null;
    this.clearTimer();
    this.closed = true;
    if (waiting && this.stopState() === null) {
      void this.flush();
    }
  }

  // ---- Saving ----

  private schedule(delay: number): void {
    if (this.closed) {
      return;
    }
    this.clearTimer();
    this.waiting.set(true);
    this.timer = setTimeout(() => {
      this.timer = null;
      void this.flush();
    }, delay);
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }

  private async flush(): Promise<void> {
    if (this.inFlight()) {
      this.again = true;
      return;
    }
    this.again = false;
    this.waiting.set(false);
    if (this.stopState() !== null) {
      return;
    }
    if (this.outstandingState() === null && !this.prepare()) {
      return;
    }
    await this.send(this.outstandingState() as OutstandingSave);
  }

  /** Makes the next save from the form, if there is one to make. */
  private prepare(): boolean {
    const base = this.baseState();
    const fields = this.fieldsState();
    if (base === null && fields.title.trim() === '') {
      // A recipe is made with its title (PP4.11); until there is one, the form is kept here only.
      return false;
    }
    const read = readFields(fields, base);
    if (!read.ok) {
      this.localErrors.set(read.errors);
      return false;
    }
    this.localErrors.set({});
    if (base !== null && !differsFrom(fields, base)) {
      this.deps.drafts.remove(this.account, this.slug, base.id);
      return false;
    }
    this.setOutstanding({
      key: this.deps.newKey(),
      version: base?.version ?? null,
      content: read.content,
      sentAt: new Date(Date.now()).toISOString(),
    });
    this.keep();
    return true;
  }

  private async send(save: OutstandingSave): Promise<void> {
    const generation = this.generation;
    const base = this.baseState();
    this.inFlight.set(true);
    let result: ApiResult<RecipeDetail>;
    try {
      result =
        base === null
          ? await this.deps.recipes.create(this.slug, save.content, save.key)
          : await this.deps.recipes.update(
              this.slug,
              base.id,
              save.version ?? '',
              save.content,
              save.key,
            );
    } finally {
      if (generation === this.generation) {
        this.inFlight.set(false);
      }
    }

    if (generation !== this.generation) {
      // The person started over while this was on its way: whatever it made is not this screen's recipe.
      return;
    }
    if (result.ok) {
      this.saved(result.value);
    } else {
      this.failed(result.problem);
    }
    if (this.again && this.stopState() === null && !this.retrying()) {
      this.schedule(0);
    }
  }

  private saved(recipe: RecipeDetail): void {
    const first = this.baseState() === null;
    // A screen that has gone owns its drafts only while no newer screen has written over them.
    const mine = (id: string | null) => !this.closed || this.ownsDraft(id);
    const ownedNew = first && mine(null);
    this.setOutstanding(null);
    this.attempts = 0;
    this.retrying.set(false);
    this.serverErrors.set({});
    this.refusalState.set(null);
    this.baseState.set(recipe);
    // What the server's answer does not hold yet is still to be saved - typed while the save was on its way, or before a
    // reload resent an older one; the rest is the server's now.
    this.edited = new Set(
      RECIPE_FIELD_GROUPS.filter((group) =>
        differsFrom(withGroups(fieldsFrom(recipe), this.fieldsState(), [group]), recipe),
      ),
    );
    if (first) {
      if (ownedNew) {
        this.deps.drafts.remove(this.account, this.slug, null);
      }
      if (!this.closed) {
        this.deps.created(recipe);
      }
    }
    if (differsFrom(this.fieldsState(), recipe)) {
      if (!this.closed || ownedNew) {
        this.keep();
      }
      this.again = true;
    } else if (mine(recipe.id)) {
      this.deps.drafts.remove(this.account, this.slug, recipe.id);
    }
  }

  /** Whether the draft filed here is still this screen's form, not a newer screen's. */
  private ownsDraft(id: string | null): boolean {
    const draft = this.deps.drafts.read(this.account, this.slug, id);
    return draft !== null && JSON.stringify(draft.fields) === JSON.stringify(this.fieldsState());
  }

  /** What this screen holds, as a draft: what an undo puts back. */
  private snapshot(): RecipeDraft | null {
    if (!this.hasEdits()) {
      return null;
    }
    const base = this.baseState();
    return {
      recipeId: base?.id ?? null,
      baseVersion: base?.version ?? null,
      fields: this.fieldsState() as unknown as Readonly<Record<string, unknown>>,
      edited: [...this.edited],
      outstanding: this.outstandingState(),
    };
  }

  /**
   * A waiting save kept here, unless it is so old the server may have forgotten its key: sent again then, a create that
   * landed would make a second recipe. Such a save is let go, and the person is told.
   */
  private fresh(save: OutstandingSave | null): OutstandingSave | null {
    if (save === null) {
      return null;
    }
    const sentAt = Date.parse(save.sentAt ?? '');
    if (Number.isNaN(sentAt) || Date.now() - sentAt > STALE_SAVE_MS) {
      this.refusalState.set(STALE_SAVE);
      return null;
    }
    return save;
  }

  private failed(problem: ApiProblem): void {
    if (problem.status === 401) {
      // Everything stays here, the waiting save included, for when the person is back.
      this.clearTimer();
      this.deps.signedOut(problem);
      return;
    }
    if (
      problem.status === 0 ||
      problem.status >= 500 ||
      problem.status === 408 ||
      problem.status === 429 ||
      (problem.status === 409 && problem.code === 'idempotency-key-in-use')
    ) {
      // It may have landed: the same save goes again, and nothing newer goes before it.
      const delay =
        problem.retryAfter !== undefined
          ? problem.retryAfter * 1000
          : RETRY_DELAYS_MS[Math.min(this.attempts, RETRY_DELAYS_MS.length - 1)];
      this.attempts += 1;
      this.retrying.set(true);
      this.schedule(delay);
      return;
    }
    // Any other answer is final for this save: it was refused, and the server keeps that refusal against its key.
    this.setOutstanding(null);
    this.attempts = 0;
    this.retrying.set(false);
    this.keep();
    this.refused(problem);
  }

  private refused(problem: ApiProblem): void {
    if (problem.status === 401) {
      this.deps.signedOut(problem);
      return;
    }
    if (problem.status === 400 && onlyMissingParts(problem.errors)) {
      // The server checks part ids before the version (recipes.md, PP4.11): a part another save removed means the recipe
      // changed somewhere else, and is the person's choice like any other change.
      this.stopState.set('changed');
      return;
    }
    switch (problem.code) {
      case RECIPE_CHANGED_CODE:
        this.stopState.set('changed');
        return;
      case RECIPE_ARCHIVED_CODE:
        this.stopState.set('archived');
        return;
      case RECIPE_NOT_YOURS_CODE:
      case 'site-role-required':
        this.stopState.set('read-only');
        return;
      case RECIPE_NOT_FOUND_CODE:
      case 'not-found':
      case 'site-not-found':
        this.stopState.set('gone');
        return;
    }
    if (problem.errors !== undefined && Object.keys(problem.errors).length > 0) {
      this.serverErrors.set(fieldErrorsFrom(problem.errors));
      this.refusalState.set(null);
    } else {
      this.refusalState.set(problem.title);
    }
  }

  /** The newest copy of the recipe, or null when it could not be read (and the screen says why). */
  private async reread(): Promise<RecipeDetail | null> {
    const base = this.baseState();
    if (base === null) {
      return null;
    }
    const result = await this.deps.recipes.get(this.slug, base.id);
    if (!result.ok) {
      this.refused(result.problem);
      return null;
    }
    this.baseState.set(result.value);
    this.serverErrors.set({});
    this.refusalState.set(null);
    if (!result.value.canEdit) {
      this.stopState.set('read-only');
      return null;
    }
    if (result.value.archivedAt !== null) {
      this.stopState.set('archived');
      return null;
    }
    return result.value;
  }

  /** Writes the form, what was edited, and any waiting save to this device. */
  private keep(): void {
    const base = this.baseState();
    const draft: RecipeDraft = {
      recipeId: base?.id ?? null,
      baseVersion: base?.version ?? null,
      fields: this.fieldsState() as unknown as Readonly<Record<string, unknown>>,
      edited: [...this.edited],
      outstanding: this.outstandingState(),
    };
    this.deps.drafts.write(this.account, this.slug, base?.id ?? null, draft);
  }
}

/** A refusal whose every error is at a part's id: parts this save named that the recipe no longer has. */
function onlyMissingParts(errors: ApiProblem['errors']): boolean {
  const paths = Object.keys(errors ?? {});
  return (
    paths.length > 0 &&
    paths.every((path) =>
      /^(content\.)?(ingredientGroups|stepGroups)\[\d+\](\.(lines|steps)\[\d+\])?\.id$/.test(path),
    )
  );
}

function groupsOf(names: readonly string[]): RecipeFieldGroup[] {
  return names.filter((name): name is RecipeFieldGroup =>
    (RECIPE_FIELD_GROUPS as readonly string[]).includes(name),
  );
}
