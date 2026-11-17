import { TestBed } from '@angular/core/testing';
import { DRAFT_PREFIX, RecipeDraft, RecipeDrafts } from './recipe-drafts';

const DRAFT: RecipeDraft = {
  recipeId: 'r1',
  baseVersion: 'v1',
  fields: { title: 'Apple pie' },
  edited: ['title'],
  outstanding: null,
};

describe('recipe drafts on this device (PP4.13, PNFR-007)', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  const drafts = () => TestBed.inject(RecipeDrafts);

  it('keeps a draft in local storage under the account, the site, and the recipe', () => {
    drafts().write('acct-1', 'apple-pie', 'r1', DRAFT);
    expect(localStorage.getItem(`${DRAFT_PREFIX}acct-1:apple-pie:r1`)).toBe(JSON.stringify(DRAFT));
    expect(drafts().read('acct-1', 'apple-pie', 'r1')).toEqual(DRAFT);
  });

  it('never hands one account’s or one site’s draft to another', () => {
    drafts().write('acct-1', 'apple-pie', 'r1', DRAFT);
    expect(drafts().read('acct-2', 'apple-pie', 'r1')).toBeNull();
    expect(drafts().read('acct-1', 'apple-pies', 'r1')).toBeNull();
    expect(drafts().read('acct-1', 'apple-pie', null)).toBeNull();
  });

  it('keeps a new recipe’s draft under “new”, apart from any recipe’s', () => {
    drafts().write('acct-1', 'apple-pie', null, { ...DRAFT, recipeId: null });
    expect(localStorage.getItem(`${DRAFT_PREFIX}acct-1:apple-pie:new`)).not.toBeNull();
    drafts().remove('acct-1', 'apple-pie', null);
    expect(drafts().read('acct-1', 'apple-pie', null)).toBeNull();
  });

  it('reads back nothing from a draft it cannot read', () => {
    const name = `${DRAFT_PREFIX}acct-1:apple-pie:r1`;
    for (const raw of [
      'not json',
      '[]',
      JSON.stringify({ ...DRAFT, edited: [1] }),
      JSON.stringify({ ...DRAFT, fields: 'x' }),
      JSON.stringify({ ...DRAFT, outstanding: { key: '', version: null, content: {} } }),
    ]) {
      localStorage.setItem(name, raw);
      expect(drafts().read('acct-1', 'apple-pie', 'r1')).toBeNull();
    }
  });

  it('forgets every draft on signing out, and nothing else', () => {
    drafts().write('acct-1', 'apple-pie', 'r1', DRAFT);
    drafts().write('acct-2', 'plum-tart', null, DRAFT);
    localStorage.setItem('pp-invitation', 'kept by its own owner');
    drafts().clearAll();
    expect(drafts().read('acct-1', 'apple-pie', 'r1')).toBeNull();
    expect(drafts().read('acct-2', 'plum-tart', null)).toBeNull();
    expect(localStorage.getItem('pp-invitation')).toBe('kept by its own owner');
    // The page is on its way out: a save answered meanwhile writes nothing back.
    drafts().write('acct-1', 'apple-pie', 'r1', DRAFT);
    expect(drafts().read('acct-1', 'apple-pie', 'r1')).toBeNull();
  });

  it('keeps a draft for this page when storage refuses it', () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError');
    });
    try {
      drafts().write('acct-1', 'apple-pie', 'r1', DRAFT);
      expect(drafts().read('acct-1', 'apple-pie', 'r1')).toEqual(DRAFT);
    } finally {
      setItem.mockRestore();
    }
  });
});
