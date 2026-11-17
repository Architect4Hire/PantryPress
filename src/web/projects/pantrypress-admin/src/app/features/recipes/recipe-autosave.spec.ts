import { ApiResult } from '../../core/api/api-problem';
import { RecipeDraft } from '../../core/recipes/recipe-drafts';
import { RecipeContent, RecipeDetail } from '../../core/recipes/recipes-client';
import {
  AutosaveDeps,
  CANNOT_SAVE,
  FIX_MARKED,
  KEPT_HERE,
  RecipeAutosave,
  RETRY_DELAYS_MS,
  SAVE_DELAY_MS,
  STALE_SAVE,
} from './recipe-autosave';
import { EMPTY_FIELDS, fieldsFrom, RecipeFields } from './recipe-form';
import { recipeDetail } from './recipe-fixtures.spec-helper';

type Answer = ApiResult<RecipeDetail>;

const ok = (value: RecipeDetail): Answer => ({ ok: true, status: 200, value });
const problem = (
  status: number,
  code: string,
  extra: Partial<{ errors: Record<string, string[]>; retryAfter: number }> = {},
): Answer => ({ ok: false, problem: { status, code, title: `Refused: ${code}`, ...extra } });

interface Sent {
  readonly kind: 'create' | 'update';
  readonly version: string | null;
  readonly content: RecipeContent;
  readonly key: string;
}

/** The API and this device, stood in for: answers come from a queue, or echo the save as the server would. */
class World {
  readonly sent: Sent[] = [];
  readonly answers: (Answer | ((sent: Sent) => Answer))[] = [];
  readonly gets: Answer[] = [];
  readonly store = new Map<string, RecipeDraft>();
  readonly signedOut: unknown[] = [];
  readonly created: RecipeDetail[] = [];
  /** Answers held back until released, to type while a save is on its way. */
  hold = false;
  private release: (() => void)[] = [];
  private keys = 0;
  private versions = 1;

  readonly deps: AutosaveDeps = {
    recipes: {
      get: () => Promise.resolve(this.gets.shift() ?? problem(500, 'unexpected-error')),
      create: (_slug, content, key) => this.answer({ kind: 'create', version: null, content, key }),
      update: (_slug, _id, version, content, key) =>
        this.answer({ kind: 'update', version, content, key }),
      restore: () => Promise.resolve({ ok: true, status: 204, value: undefined }),
    },
    drafts: {
      read: (_a, _s, id) => this.store.get(id ?? 'new') ?? null,
      write: (_a, _s, id, draft) => void this.store.set(id ?? 'new', draft),
      remove: (_a, _s, id) => void this.store.delete(id ?? 'new'),
    },
    newKey: () => `key-${++this.keys}`,
    signedOut: (p) => void this.signedOut.push(p),
    created: (r) => void this.created.push(r),
  };

  /** The server's answer to a save it took: the recipe as sent, under a new version. */
  echo(sent: Sent): RecipeDetail {
    const base = recipeDetail();
    return {
      ...base,
      ...sent.content,
      id: base.id,
      version: `v${++this.versions}`,
      ingredientGroups: base.ingredientGroups,
      stepGroups: base.stepGroups,
    } as RecipeDetail;
  }

  releaseAll(): void {
    const waiting = this.release;
    this.release = [];
    waiting.forEach((go) => go());
  }

  private async answer(sent: Sent): Promise<Answer> {
    this.sent.push(sent);
    if (this.hold) {
      await new Promise<void>((go) => this.release.push(go));
    }
    const next = this.answers.shift();
    if (next === undefined) {
      return ok(this.echo(sent));
    }
    return typeof next === 'function' ? next(sent) : next;
  }
}

async function pass(ms: number): Promise<void> {
  await vi.advanceTimersByTimeAsync(ms);
}

describe('the recipe autosave (PP4.13, PNFR-007)', () => {
  let world: World;
  const make = () => new RecipeAutosave(world.deps, 'acct-1', 'apple-pie');
  const edit = (autosave: RecipeAutosave, change: Partial<RecipeFields>) =>
    autosave.edit({ ...autosave.fields(), ...change });

  beforeEach(() => {
    vi.useFakeTimers();
    world = new World();
  });
  afterEach(() => vi.useRealTimers());

  describe('a recipe the server has', () => {
    it('saves a pause in typing within two seconds, once, with the version it read', async () => {
      const autosave = make();
      autosave.startWith(recipeDetail());
      expect(autosave.view()).toEqual({ state: 'saved' });

      edit(autosave, { title: 'A' });
      edit(autosave, { title: 'Ap' });
      edit(autosave, { title: 'Apple tart' });
      expect(autosave.view()).toEqual({ state: 'pending' });
      expect(world.store.get(recipeDetail().id)?.fields['title']).toBe('Apple tart');
      await pass(SAVE_DELAY_MS - 1);
      expect(world.sent).toHaveLength(0);
      await pass(1);

      expect(world.sent).toHaveLength(1);
      expect(SAVE_DELAY_MS).toBeLessThanOrEqual(2000);
      expect(world.sent[0]).toMatchObject({
        kind: 'update',
        version: 'AAAAAAAAB9E=',
        key: 'key-1',
      });
      expect(world.sent[0].content.title).toBe('Apple tart');
      expect(autosave.view()).toEqual({ state: 'saved' });
      expect(autosave.base()?.version).toBe('v2');
      // Saved: nothing is left on this device.
      expect(world.store.size).toBe(0);
    });

    it('sends the next save with the version the last answer gave, under a new key', async () => {
      const autosave = make();
      autosave.startWith(recipeDetail());
      edit(autosave, { title: 'One' });
      await pass(SAVE_DELAY_MS);
      edit(autosave, { title: 'Two' });
      await pass(SAVE_DELAY_MS);
      expect(world.sent.map((s) => [s.version, s.key])).toEqual([
        ['AAAAAAAAB9E=', 'key-1'],
        ['v2', 'key-2'],
      ]);
    });

    it('saves nothing when the form says the same recipe another way', async () => {
      const autosave = make();
      autosave.startWith(recipeDetail());
      edit(autosave, { yieldLow: '8.0' });
      await pass(SAVE_DELAY_MS);
      expect(world.sent).toHaveLength(0);
      expect(autosave.view()).toEqual({ state: 'saved' });
    });

    it('keeps one save in flight, and sends what was typed meanwhile next', async () => {
      const autosave = make();
      autosave.startWith(recipeDetail());
      world.hold = true;
      edit(autosave, { title: 'First' });
      await pass(SAVE_DELAY_MS);
      expect(autosave.view()).toEqual({ state: 'saving' });
      edit(autosave, { summary: 'Typed while saving' });
      await pass(SAVE_DELAY_MS);
      expect(world.sent).toHaveLength(1);
      world.hold = false;
      world.releaseAll();
      await pass(0);
      await pass(0);
      expect(world.sent).toHaveLength(2);
      expect(world.sent[1].version).toBe('v2');
      expect(world.sent[1].content).toMatchObject({
        title: 'First',
        summary: 'Typed while saving',
      });
      expect(autosave.view()).toEqual({ state: 'saved' });
    });

    it('sends a save that got no answer again exactly - key, version, and recipe - before anything newer', async () => {
      const autosave = make();
      autosave.startWith(recipeDetail());
      world.answers.push(problem(0, 'unreachable'), problem(502, 'bad-gateway'));
      edit(autosave, { title: 'Lost' });
      await pass(SAVE_DELAY_MS);
      expect(autosave.view()).toEqual({ state: 'retrying', detail: KEPT_HERE });
      // Typing meanwhile is kept, but never sent ahead of the save that may have landed.
      edit(autosave, { summary: 'Later' });
      await pass(SAVE_DELAY_MS);
      await pass(RETRY_DELAYS_MS[0]);
      await pass(RETRY_DELAYS_MS[1]);
      expect(world.sent.map((s) => [s.key, s.version, s.content.title, s.content.summary])).toEqual(
        [
          ['key-1', 'AAAAAAAAB9E=', 'Lost', 'A deep-dish pie.'],
          ['key-1', 'AAAAAAAAB9E=', 'Lost', 'A deep-dish pie.'],
          ['key-1', 'AAAAAAAAB9E=', 'Lost', 'A deep-dish pie.'],
          ['key-2', 'v2', 'Lost', 'Later'],
        ],
      );
      expect(autosave.view()).toEqual({ state: 'saved' });
    });

    it('backs off between tries, waits for Retry-After, and tries at once when the connection is back', async () => {
      const autosave = make();
      autosave.startWith(recipeDetail());
      world.answers.push(
        problem(503, 'unexpected-error'),
        problem(429, 'rate-limited', { retryAfter: 20 }),
        problem(0, 'unreachable'),
      );
      edit(autosave, { title: 'X' });
      await pass(SAVE_DELAY_MS);
      await pass(RETRY_DELAYS_MS[0]);
      expect(world.sent).toHaveLength(2);
      await pass(19_999);
      expect(world.sent).toHaveLength(2);
      await pass(1);
      expect(world.sent).toHaveLength(3);
      autosave.online();
      await pass(0);
      expect(world.sent).toHaveLength(4);
      expect(new Set(world.sent.map((s) => s.key))).toEqual(new Set(['key-1']));
    });

    it('keeps a save still waiting for its answer through a reload, and sends it first', async () => {
      const first = make();
      first.startWith(recipeDetail());
      // Lost on the way, and again as the tab closed (leave() sends what is waiting).
      world.answers.push(problem(0, 'unreachable'), problem(0, 'unreachable'));
      edit(first, { title: 'Before the reload' });
      await pass(SAVE_DELAY_MS);
      first.leave();

      const again = make();
      expect(again.startWith(recipeDetail())).toBe(true);
      expect(again.fields().title).toBe('Before the reload');
      await pass(0);
      expect(world.sent.map((s) => s.key)).toEqual(['key-1', 'key-1', 'key-1']);
      expect(again.view()).toEqual({ state: 'saved' });
    });

    it('puts edits kept on this device back, and saves them when they were made on this version', async () => {
      const first = make();
      first.startWith(recipeDetail());
      edit(first, { summary: 'Typed, then the tab closed' });
      first.leave();
      world.sent.length = 0;
      // leave() sends at once what was waiting; say that never got out.
      world.store.set(recipeDetail().id, {
        recipeId: recipeDetail().id,
        baseVersion: recipeDetail().version,
        fields: { ...fieldsFrom(recipeDetail()), summary: 'Typed, then the tab closed' },
        edited: ['summary'],
        outstanding: null,
      });
      const again = make();
      again.startWith(recipeDetail());
      await pass(0);
      expect(world.sent).toHaveLength(1);
      expect(world.sent[0].content.summary).toBe('Typed, then the tab closed');
    });

    it('stops, keeping the edits, when they were made on an older version', async () => {
      world.store.set(recipeDetail().id, {
        recipeId: recipeDetail().id,
        baseVersion: 'older',
        fields: { ...fieldsFrom(recipeDetail()), title: 'Mine' },
        edited: ['title'],
        outstanding: null,
      });
      const autosave = make();
      autosave.startWith(recipeDetail());
      await pass(SAVE_DELAY_MS);
      expect(world.sent).toHaveLength(0);
      expect(autosave.stop()).toBe('changed');
      expect(autosave.view()).toEqual({ state: 'failed', detail: KEPT_HERE });
      expect(autosave.fields().title).toBe('Mine');
    });

    it('shows a refusal the person can fix at its fields, and saves the fix under a new key', async () => {
      const autosave = make();
      autosave.startWith(recipeDetail());
      world.answers.push(
        problem(400, 'validation-failed', {
          errors: {
            'content.yield.high': ['Make the high end of the range more than the low end.'],
          },
        }),
      );
      edit(autosave, { yieldHigh: '2' });
      await pass(SAVE_DELAY_MS);
      expect(autosave.view()).toEqual({ state: 'failed', detail: FIX_MARKED });
      expect(autosave.errors()).toEqual({
        'yield.high': ['Make the high end of the range more than the low end.'],
      });
      // Nothing is sent again until the person changes something.
      await pass(60_000);
      expect(world.sent).toHaveLength(1);
      edit(autosave, { yieldHigh: '10' });
      await pass(SAVE_DELAY_MS);
      expect(world.sent.map((s) => s.key)).toEqual(['key-1', 'key-2']);
      expect(autosave.errors()).toEqual({});
      expect(autosave.view()).toEqual({ state: 'saved' });
    });

    it('marks what it cannot read itself, and sends nothing until it can', async () => {
      const autosave = make();
      autosave.startWith(recipeDetail());
      edit(autosave, { yieldLow: 'eight' });
      await pass(SAVE_DELAY_MS);
      expect(world.sent).toHaveLength(0);
      expect(Object.keys(autosave.errors())).toEqual(['yield.low']);
      expect(autosave.view()).toEqual({ state: 'failed', detail: FIX_MARKED });
      expect(world.store.get(recipeDetail().id)?.fields['yieldLow']).toBe('eight');
    });

    it('stops on a recipe changed elsewhere; keeping mine lays my edited groups over the newer copy', async () => {
      const autosave = make();
      autosave.startWith(recipeDetail());
      world.answers.push(problem(409, 'recipe-changed'));
      edit(autosave, { title: 'Mine' });
      await pass(SAVE_DELAY_MS);
      expect(autosave.stop()).toBe('changed');
      // Typing goes on, kept here, but nothing is sent.
      edit(autosave, { yieldUnit: 'slices' });
      await pass(SAVE_DELAY_MS);
      expect(world.sent).toHaveLength(1);

      world.gets.push(
        ok(recipeDetail({ version: 'theirs', summary: 'Their summary', title: 'Theirs' })),
      );
      await autosave.keepMine();
      await pass(0);
      expect(autosave.stop()).toBeNull();
      expect(world.sent[1]).toMatchObject({ version: 'theirs', key: 'key-2' });
      expect(world.sent[1].content).toMatchObject({
        title: 'Mine',
        summary: 'Their summary',
        yieldUnit: 'slices',
      });
    });

    it('takes the newer copy and forgets the edits when the person chooses it', async () => {
      const autosave = make();
      autosave.startWith(recipeDetail());
      world.answers.push(problem(409, 'recipe-changed'));
      edit(autosave, { title: 'Mine' });
      await pass(SAVE_DELAY_MS);
      world.gets.push(ok(recipeDetail({ version: 'theirs', title: 'Theirs' })));
      await autosave.useSaved();
      expect(autosave.fields().title).toBe('Theirs');
      expect(autosave.view()).toEqual({ state: 'saved' });
      expect(world.store.size).toBe(0);
    });

    it.each([
      [problem(409, 'recipe-archived'), 'archived'],
      [problem(403, 'recipe-not-yours'), 'read-only'],
      [problem(404, 'recipe-not-found'), 'gone'],
    ] as const)('stops on %j', async (answer, stop) => {
      const autosave = make();
      autosave.startWith(recipeDetail());
      world.answers.push(answer);
      edit(autosave, { title: 'X' });
      await pass(SAVE_DELAY_MS);
      expect(autosave.stop()).toBe(stop);
      expect(world.store.get(recipeDetail().id)?.fields['title']).toBe('X');
    });

    it('restores an archived recipe and then saves the edits kept here', async () => {
      world.store.set(recipeDetail().id, {
        recipeId: recipeDetail().id,
        baseVersion: recipeDetail().version,
        fields: { ...fieldsFrom(recipeDetail()), title: 'Kept' },
        edited: ['title'],
        outstanding: null,
      });
      const autosave = make();
      autosave.startWith(recipeDetail({ archivedAt: '2026-11-14T19:00:00Z' }));
      expect(autosave.stop()).toBe('archived');
      world.gets.push(ok(recipeDetail({ version: 'restored' })));
      await autosave.restore();
      await pass(0);
      expect(autosave.stop()).toBeNull();
      expect(world.sent[0]).toMatchObject({ version: 'restored' });
      expect(world.sent[0].content.title).toBe('Kept');
    });

    it('is read-only for a recipe the caller may not change, with nothing to say about saving', () => {
      const autosave = make();
      autosave.startWith(recipeDetail({ canEdit: false }));
      expect(autosave.stop()).toBe('read-only');
      expect(autosave.view()).toEqual({ state: 'none' });
    });

    it('shows edits kept here that can no longer be saved, to copy, until the person lets them go (with undo)', async () => {
      world.store.set(recipeDetail().id, {
        recipeId: recipeDetail().id,
        baseVersion: recipeDetail().version,
        fields: { ...fieldsFrom(recipeDetail()), title: 'Typed before my role changed' },
        edited: ['title'],
        outstanding: null,
      });
      const autosave = make();
      expect(autosave.startWith(recipeDetail({ canEdit: false }))).toBe(true);
      expect(autosave.stop()).toBe('read-only');
      expect(autosave.fields().title).toBe('Typed before my role changed');
      expect(autosave.view()).toEqual({ state: 'failed', detail: CANNOT_SAVE });
      await pass(SAVE_DELAY_MS);
      expect(world.sent).toHaveLength(0);

      const kept = autosave.discard();
      expect(autosave.fields().title).toBe('Apple pie');
      expect(world.store.size).toBe(0);
      autosave.bringBack(kept!);
      expect(autosave.fields().title).toBe('Typed before my role changed');
      expect(autosave.stop()).toBe('read-only');
      await pass(SAVE_DELAY_MS);
      expect(world.sent).toHaveLength(0);
    });

    it('says a refusal mid-edit cannot be saved, and keeps the words on screen', async () => {
      const autosave = make();
      autosave.startWith(recipeDetail());
      world.answers.push(problem(403, 'recipe-not-yours'));
      edit(autosave, { title: 'Mine now' });
      await pass(SAVE_DELAY_MS);
      expect(autosave.stop()).toBe('read-only');
      expect(autosave.view()).toEqual({ state: 'failed', detail: CANNOT_SAVE });
      expect(autosave.fields().title).toBe('Mine now');
    });

    it('remembers as edited what the server did not get, even when an older save was the one resent', async () => {
      const autosave = make();
      autosave.startWith(recipeDetail());
      world.answers.push(problem(0, 'unreachable'));
      edit(autosave, { summary: 'First' });
      await pass(SAVE_DELAY_MS);
      // Typed while the first save waits to go again.
      edit(autosave, { title: 'Typed during the retry' });
      world.answers.push(ok(world.echo(world.sent[0])), problem(409, 'recipe-changed'));
      await pass(RETRY_DELAYS_MS[0]);
      await pass(0);
      expect(autosave.stop()).toBe('changed');

      world.gets.push(ok(recipeDetail({ version: 'theirs', title: 'Their title' })));
      await autosave.keepMine();
      expect(autosave.fields().title).toBe('Typed during the retry');
    });

    it('waits and tries again on 408, which the server never keeps', async () => {
      const autosave = make();
      autosave.startWith(recipeDetail());
      world.answers.push(problem(408, 'request-timeout'));
      edit(autosave, { title: 'Slow' });
      await pass(SAVE_DELAY_MS);
      expect(autosave.view()).toEqual({ state: 'retrying', detail: KEPT_HERE });
      await pass(RETRY_DELAYS_MS[0]);
      expect(world.sent.map((s) => s.key)).toEqual(['key-1', 'key-1']);
    });

    it('reads a refusal naming only parts the recipe no longer has as a change made elsewhere', async () => {
      const autosave = make();
      autosave.startWith(recipeDetail());
      world.answers.push(
        problem(400, 'validation-failed', {
          errors: {
            'content.ingredientGroups[0].lines[0].id': ['This part is not in the recipe any more.'],
          },
        }),
      );
      edit(autosave, { title: 'X' });
      await pass(SAVE_DELAY_MS);
      expect(autosave.stop()).toBe('changed');
      expect(autosave.errors()).toEqual({});
    });

    it('lets go of a kept save too old for the server to remember, and says so', async () => {
      const dayAgo = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString();
      world.store.set(recipeDetail().id, {
        recipeId: recipeDetail().id,
        baseVersion: recipeDetail().version,
        fields: { ...fieldsFrom(recipeDetail()), title: 'Old' },
        edited: ['title'],
        outstanding: {
          key: 'old-key',
          version: recipeDetail().version,
          content: {} as never,
          sentAt: dayAgo,
        },
      });
      const autosave = make();
      autosave.startWith(recipeDetail());
      expect(autosave.refusal()).toBe(STALE_SAVE);
      await pass(0);
      expect(world.sent.map((s) => s.key)).not.toContain('old-key');
    });

    it('keeps everything here and asks for sign-in when the session has ended', async () => {
      const autosave = make();
      autosave.startWith(recipeDetail());
      world.answers.push(problem(401, 'session-ended'));
      edit(autosave, { title: 'Signed out' });
      await pass(SAVE_DELAY_MS);
      expect(world.signedOut).toHaveLength(1);
      expect(world.store.get(recipeDetail().id)?.outstanding?.key).toBe('key-1');
      await pass(60_000);
      expect(world.sent).toHaveLength(1);
    });

    it('sends what is waiting at once when the screen goes, and nothing after', async () => {
      const autosave = make();
      autosave.startWith(recipeDetail());
      edit(autosave, { title: 'Leaving' });
      autosave.leave();
      await pass(0);
      expect(world.sent).toHaveLength(1);
      await pass(60_000);
      expect(world.sent).toHaveLength(1);
    });
  });

  describe('a new recipe', () => {
    it('saves nothing until there is a title, then makes the recipe once', async () => {
      const autosave = make();
      expect(autosave.startNew()).toBe(false);
      edit(autosave, { summary: 'No title yet' });
      await pass(SAVE_DELAY_MS);
      expect(world.sent).toHaveLength(0);
      expect(autosave.view()).toEqual({ state: 'none' });
      expect(world.store.get('new')?.fields['summary']).toBe('No title yet');

      edit(autosave, { title: 'Plum tart' });
      await pass(SAVE_DELAY_MS);
      expect(world.sent).toEqual([
        expect.objectContaining({ kind: 'create', version: null, key: 'key-1' }),
      ]);
      expect(world.sent[0].content.ingredientGroups).toEqual([]);
      expect(world.created).toHaveLength(1);
      expect(autosave.base()?.id).toBe(recipeDetail().id);
      expect(world.store.has('new')).toBe(false);

      edit(autosave, { summary: 'Now an update' });
      await pass(SAVE_DELAY_MS);
      expect(world.sent[1]).toMatchObject({ kind: 'update', version: 'v2' });
    });

    it('makes the same recipe, never a second, when the create is sent again after a reload', async () => {
      const first = make();
      first.startNew();
      world.answers.push(problem(0, 'unreachable'), problem(0, 'unreachable'));
      edit(first, { title: 'Plum tart' });
      await pass(SAVE_DELAY_MS);
      first.leave();

      const again = make();
      expect(again.startNew()).toBe(true);
      await pass(0);
      expect(world.sent.map((s) => [s.kind, s.key])).toEqual([
        ['create', 'key-1'],
        ['create', 'key-1'],
        ['create', 'key-1'],
      ]);
      expect(world.created).toHaveLength(1);
    });

    it('starts over on request, forgetting the draft', () => {
      world.store.set('new', {
        recipeId: null,
        baseVersion: null,
        fields: { ...EMPTY_FIELDS, title: 'Old start' },
        edited: ['title'],
        outstanding: null,
      });
      const autosave = make();
      expect(autosave.startNew()).toBe(true);
      const kept = autosave.startOver();
      expect(autosave.fields()).toEqual(EMPTY_FIELDS);
      expect(world.store.has('new')).toBe(false);
      // Undo puts it all back, and it is kept here again.
      autosave.bringBack(kept!);
      expect(autosave.fields().title).toBe('Old start');
      expect(world.store.get('new')?.fields['title']).toBe('Old start');
    });

    it('drops the answer to a create sent before starting over, so nothing is saved over that recipe', async () => {
      const autosave = make();
      autosave.startNew();
      world.hold = true;
      edit(autosave, { title: 'Abandoned', summary: 'Gone' });
      await pass(SAVE_DELAY_MS);
      expect(autosave.busy()).toBe(true);
      autosave.startOver();
      expect(autosave.view()).toEqual({ state: 'none' });
      world.hold = false;
      world.releaseAll();
      await pass(0);
      expect(world.created).toHaveLength(0);
      expect(autosave.base()).toBeNull();

      edit(autosave, { title: 'Fresh' });
      await pass(SAVE_DELAY_MS);
      expect(world.sent.map((s) => [s.kind, s.content.title])).toEqual([
        ['create', 'Abandoned'],
        ['create', 'Fresh'],
      ]);
    });

    it('never moves a person who has left the screen to the recipe a late create made', async () => {
      const autosave = make();
      autosave.startNew();
      world.hold = true;
      edit(autosave, { title: 'Left behind' });
      await pass(SAVE_DELAY_MS);
      autosave.leave();
      world.hold = false;
      world.releaseAll();
      await pass(0);
      expect(world.created).toHaveLength(0);
      // Saved: nothing is left for the next visit.
      expect(world.store.has('new')).toBe(false);
    });
  });
});
