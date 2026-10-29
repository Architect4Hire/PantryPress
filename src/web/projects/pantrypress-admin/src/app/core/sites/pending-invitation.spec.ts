import { TestBed } from '@angular/core/testing';
import { PendingInvitation, readInvitationToken } from './pending-invitation';

describe('readInvitationToken', () => {
  it('reads the token as the router hands it over, without decoding it again', () => {
    expect(readInvitationToken('token=ab+c/d==')).toBe('ab+c/d==');
    expect(readInvitationToken('x=1&token=abc')).toBe('abc');
  });

  it.each([null, undefined, '', 'token=', 'tok=abc', 'main'])(
    'finds no token in %s',
    (fragment) => {
      expect(readInvitationToken(fragment)).toBeNull();
    },
  );
});

describe('PendingInvitation', () => {
  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  const store = () => TestBed.inject(PendingInvitation);

  it('keeps a token with a key of its own, in this browser', () => {
    expect(store().keep('token=abc')).toBe(true);
    const link = store().link()!;
    expect(link.token).toBe('abc');
    expect(link.key).toMatch(/^invitation-[0-9a-f-]{36}$/);
    expect(JSON.parse(localStorage.getItem('pp-invitation')!)).toEqual(link);
  });

  it('finds it again from another page of the app, as a new tab would', () => {
    store().keep('token=abc');
    const kept = store().link();
    TestBed.resetTestingModule();
    expect(TestBed.inject(PendingInvitation).link()).toEqual(kept);
  });

  it('keeps the key for the same link opened twice, so it is one acceptance', () => {
    store().keep('token=abc');
    const key = store().link()!.key;
    store().keep('token=abc');
    expect(store().link()!.key).toBe(key);
  });

  it('replaces the link held with a new one, and gives it a new key', () => {
    store().keep('token=abc');
    const first = store().link()!;
    store().keep('token=xyz');
    expect(store().link()!.token).toBe('xyz');
    expect(store().link()!.key).not.toBe(first.key);
  });

  it('changes nothing for a fragment with no token', () => {
    store().keep('token=abc');
    expect(store().keep('main')).toBe(false);
    expect(store().link()!.token).toBe('abc');
  });

  it('forgets the link', () => {
    store().keep('token=abc');
    store().clear();
    expect(store().link()).toBeNull();
    expect(localStorage.getItem('pp-invitation')).toBeNull();
  });

  it.each(['not json', '{"token":"abc"}', '{"token":"","key":"k"}', '[1]'])(
    'reads what it did not write (%s) as nothing',
    (raw) => {
      localStorage.setItem('pp-invitation', raw);
      expect(store().link()).toBeNull();
    },
  );

  it('keeps the link for this page when storage refuses it', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('Refused');
    });
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('Refused');
    });
    expect(store().keep('token=abc')).toBe(true);
    expect(store().link()!.token).toBe('abc');
  });
});
