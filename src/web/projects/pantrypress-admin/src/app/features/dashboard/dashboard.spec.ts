import { Location } from '@angular/common';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { PpToaster } from '@pantrypress/ui';
import axe from 'axe-core';
import { ApiResult } from '../../core/api/api-problem';
import { DocumentLoad } from '../../core/navigation/document-load';
import { AccountClient } from '../../core/session/account-client';
import { SessionClient, SessionView } from '../../core/session/session-client';
import { PendingInvitation } from '../../core/sites/pending-invitation';
import { SiteDetail, SitePage, SitesClient, SiteSummary } from '../../core/sites/sites-client';
import dashboardRoutes from './dashboard.routes';

@Component({ template: '<h1>Sign in</h1>' })
class SignInStandIn {}

const VIEW: SessionView = {
  stage: 'complete',
  accountId: '7f1f5c9a-1d2b-4c3d-9e4f-5a6b7c8d9e0f',
  email: 'cook@example.test',
  emailVerified: true,
  secondFactorVerified: false,
};

const APPLE: SiteDetail = {
  slug: 'apple-pie',
  name: 'Apple Pie Club',
  tagline: 'Pies, mostly',
  status: 'live',
  role: 'owner',
  host: 'apple-pie.pantrypress.app',
  timeZone: 'America/Chicago',
  locale: 'en-US',
  createdAt: '2026-10-01T12:00:00Z',
};

const PLUM: SiteDetail = {
  ...APPLE,
  slug: 'plum-tart',
  name: 'Plum Tart Society',
  tagline: '',
  status: 'read-only',
  role: 'author',
  host: 'plum-tart.pantrypress.app',
};

const summary = ({ slug, name, status, role, host }: SiteDetail): SiteSummary => ({
  slug,
  name,
  status,
  role,
  host,
});

const PAGE: SitePage = { items: [summary(APPLE), summary(PLUM)], nextCursor: null };

function refused<T>(status: number, code: string, title: string): ApiResult<T> {
  return { ok: false, problem: { status, code, title } };
}

/** Records each call and answers from a queue a test fills; an empty queue answers the fallback. */
class Calls<T> {
  readonly made: unknown[][] = [];
  private readonly queue: (T | 'never')[] = [];
  constructor(private readonly fallback: (args: unknown[]) => T) {}
  answer(...answers: (T | 'never')[]): void {
    this.queue.push(...answers);
  }
  take(args: unknown[]): Promise<T> {
    this.made.push(args);
    const next = this.queue.shift();
    if (next === 'never') {
      return new Promise<T>(() => undefined);
    }
    return Promise.resolve(next ?? this.fallback(args));
  }
}

class FakeSites {
  readonly listCalls = new Calls<ApiResult<SitePage>>(() => ({
    ok: true,
    status: 200,
    value: PAGE,
  }));
  readonly getCalls = new Calls<ApiResult<SiteDetail>>(([slug]) => {
    const site = [APPLE, PLUM].find((candidate) => candidate.slug === slug);
    return site
      ? { ok: true, status: 200, value: site }
      : refused(404, 'site-not-found', "We couldn't find that site.");
  });
  readonly acceptCalls = new Calls<ApiResult<SiteSummary>>(() => ({
    ok: true,
    status: 200,
    value: summary(PLUM),
  }));
  list(...args: unknown[]) {
    return this.listCalls.take(args);
  }
  get(...args: unknown[]) {
    return this.getCalls.take(args);
  }
  acceptInvitation(...args: unknown[]) {
    return this.acceptCalls.take(args);
  }
}

class FakeSessions {
  readonly session = signal<SessionView | null>(VIEW);
  readonly signOutCalls = new Calls<ApiResult<void>>(() => ({
    ok: true,
    status: 204,
    value: undefined,
  }));
  signOut() {
    return this.signOutCalls.take([]);
  }
}

class FakeAccounts {
  readonly resendCalls = new Calls<ApiResult<void>>(() => ({
    ok: true,
    status: 202,
    value: undefined,
  }));
  resendVerification(...args: unknown[]) {
    return this.resendCalls.take(args);
  }
}

class FakeDocuments {
  readonly loaded: string[] = [];
  to(path: string): void {
    this.loaded.push(path);
  }
}

interface World {
  sites: FakeSites;
  sessions: FakeSessions;
  accounts: FakeAccounts;
  documents: FakeDocuments;
}

/** Opens the dashboard through the real router and its real routes, with the clients standing in for the API. */
async function open(url: string, prepare: (world: World) => void = () => undefined) {
  const world: World = {
    sites: new FakeSites(),
    sessions: new FakeSessions(),
    accounts: new FakeAccounts(),
    documents: new FakeDocuments(),
  };
  prepare(world);
  TestBed.configureTestingModule({
    providers: [
      { provide: SitesClient, useValue: world.sites },
      { provide: SessionClient, useValue: world.sessions },
      { provide: AccountClient, useValue: world.accounts },
      { provide: DocumentLoad, useValue: world.documents },
      provideRouter([
        { path: 'sign-in', component: SignInStandIn },
        { path: 'dashboard', children: dashboardRoutes },
      ]),
    ],
  });
  const toaster = TestBed.inject(PpToaster);
  const toasts: string[] = [];
  vi.spyOn(toaster, 'show').mockImplementation((options) => {
    toasts.push(`${options.tone}: ${options.message}`);
    return toasts.length;
  });
  const harness = await RouterTestingHarness.create();
  const settle = async () => {
    for (let i = 0; i < 3; i += 1) {
      await harness.fixture.whenStable();
      harness.fixture.detectChanges();
    }
  };
  const router = TestBed.inject(Router);
  const go = async (to: string) => {
    await router.navigateByUrl(to);
    await settle();
  };
  await go(url);
  const root = () => harness.routeNativeElement as HTMLElement;
  return {
    ...world,
    root,
    toasts,
    settle,
    go,
    url: () => router.url,
    h1: () => [...root().querySelectorAll('h1')].map((h) => h.textContent?.trim()),
    button: (name: string) => {
      const match = [...root().querySelectorAll<HTMLButtonElement>('button')].find(
        (candidate) =>
          (candidate.getAttribute('aria-label') ?? candidate.textContent?.trim()) === name,
      );
      if (!match) {
        throw new Error(`No button named '${name}'.`);
      }
      return match;
    },
    links: (within: string) =>
      [...root().querySelectorAll<HTMLAnchorElement>(`${within} a`)].map((a) => ({
        text: a.textContent?.trim(),
        href: a.getAttribute('href'),
        current: a.getAttribute('aria-current'),
      })),
    invitation: () => TestBed.inject(PendingInvitation).link(),
  };
}

type Page = Awaited<ReturnType<typeof open>>;

async function violations(page: Page): Promise<string[]> {
  const results = await axe.run(page.root(), {
    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] },
    rules: { 'color-contrast': { enabled: false } },
  });
  return results.violations.map(
    (violation) =>
      `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
  );
}

afterEach(() => {
  localStorage.clear();
  vi.restoreAllMocks();
});

describe('the dashboard shell (PP3.9)', () => {
  it('draws a skip link, a banner, the navigation, and the one main, with the screen bringing the one h1', async () => {
    const page = await open('/dashboard');
    const root = page.root();
    expect(root.tagName.toLowerCase()).toBe('ppa-dashboard-shell');
    expect(root.querySelector('a.skip')?.getAttribute('href')).toBe('#main');
    expect(root.querySelectorAll('header')).toHaveLength(1);
    expect(root.querySelector('nav')?.getAttribute('aria-label')).toBe('Dashboard');
    expect(root.querySelectorAll('main#main')).toHaveLength(1);
    expect(page.h1()).toEqual(['Your sites']);
    expect(root.querySelectorAll('pp-toast-region')).toHaveLength(0);
  });

  it('moves focus to the screen with the skip link, without leaving the address', async () => {
    const page = await open('/dashboard');
    page.root().querySelector<HTMLAnchorElement>('a.skip')!.click();
    expect(document.activeElement?.textContent?.trim()).toBe('Your sites');
    expect(page.url()).toBe('/dashboard');
  });

  it("moves focus to the new screen's heading after each move inside the dashboard", async () => {
    const page = await open('/dashboard');
    await page.go('/dashboard/sites/apple-pie');
    expect(document.activeElement?.tagName).toBe('H1');
    expect(document.activeElement?.textContent?.trim()).toBe('Apple Pie Club');
  });

  it("lists the site's parts once the site is known, and marks where the reader is", async () => {
    const page = await open('/dashboard');
    expect(page.links('nav')).toEqual([
      { text: 'Your sites', href: '/dashboard', current: 'page' },
    ]);
    await page.go('/dashboard/sites/apple-pie');
    expect(page.root().querySelector('nav .group')?.textContent?.trim()).toBe('Apple Pie Club');
    expect(page.links('nav')).toEqual([
      { text: 'Your sites', href: '/dashboard', current: null },
      { text: 'Overview', href: '/dashboard/sites/apple-pie', current: 'page' },
    ]);
  });

  it('has no accessibility violations', async () => {
    const page = await open('/dashboard/sites/apple-pie');
    expect(await violations(page)).toEqual([]);
  });
});

describe('the site switcher (PP3.9)', () => {
  it("names the current site, links every one of the caller's sites, and ends with all of them", async () => {
    const page = await open('/dashboard/sites/apple-pie');
    expect(page.button('Site Apple Pie Club')).toBeTruthy();
    expect(page.links('ppa-site-switcher')).toEqual([
      { text: 'Apple Pie Club', href: '/dashboard/sites/apple-pie', current: 'true' },
      { text: 'Plum Tart Society', href: '/dashboard/sites/plum-tart', current: null },
      { text: 'All your sites', href: '/dashboard', current: null },
    ]);
    expect(page.sites.listCalls.made).toEqual([[{ limit: 100 }]]);
  });

  it('keeps the part of the dashboard the reader is in when moving to another site', async () => {
    const page = await open('/dashboard/sites/apple-pie/posts');
    const plum = page.links('ppa-site-switcher').find((link) => link.text === 'Plum Tart Society');
    expect(plum?.href).toBe('/dashboard/sites/plum-tart/posts');
  });

  it('sets the site by going to its address', async () => {
    const page = await open('/dashboard/sites/apple-pie');
    page.button('Site Apple Pie Club').click();
    await page.settle();
    page.root().querySelector<HTMLAnchorElement>('ppa-site-switcher a[href$="plum-tart"]')!.click();
    await page.settle();
    expect(page.url()).toBe('/dashboard/sites/plum-tart');
    expect(page.h1()).toEqual(['Plum Tart Society']);
    expect(page.button('Site Plum Tart Society')).toBeTruthy();
  });

  it('says when no site is chosen, when the list could not be read, and when there is none', async () => {
    let page = await open('/dashboard', ({ sites }) =>
      sites.listCalls.answer(refused(503, 'unexpected-error', 'Something went wrong.')),
    );
    expect(page.button('Site Choose a site')).toBeTruthy();
    expect(page.root().querySelector('ppa-site-switcher .caption')?.textContent).toContain(
      "couldn't load your sites",
    );
    TestBed.resetTestingModule();
    page = await open('/dashboard', ({ sites }) =>
      sites.listCalls.answer({ ok: true, status: 200, value: { items: [], nextCursor: null } }),
    );
    expect(page.root().querySelector('ppa-site-switcher .caption')?.textContent).toBe(
      "You aren't part of any sites yet.",
    );
  });
});

describe('the account menu (PP3.9)', () => {
  it('says who is signed in', async () => {
    const page = await open('/dashboard');
    expect(page.root().querySelector('ppa-account-menu .caption')?.textContent).toBe(
      'Signed in as cook@example.test',
    );
  });

  it('signs out at the API, forgets a waiting invitation, and loads the home page as a new document', async () => {
    const page = await open('/dashboard');
    TestBed.inject(PendingInvitation).keep('token=abc');
    const announce = vi.spyOn(TestBed.inject(PpToaster), 'announce');
    page.button('Sign out').click();
    await page.settle();
    expect(announce).toHaveBeenCalledWith('Signing you out…');
    expect(page.sessions.signOutCalls.made).toHaveLength(1);
    expect(page.documents.loaded).toEqual(['/']);
    expect(page.invitation()).toBeNull();
  });

  it('keeps the session and says why when signing out is refused', async () => {
    const page = await open('/dashboard', ({ sessions }) =>
      sessions.signOutCalls.answer(
        refused(429, 'too-many-requests', 'Too many tries. Wait a minute, then try again.'),
      ),
    );
    page.button('Sign out').click();
    await page.settle();
    expect(page.documents.loaded).toEqual([]);
    expect(page.toasts).toEqual(['danger: Too many tries. Wait a minute, then try again.']);
  });
});

describe('the site area (PP3.9)', () => {
  it('shows progress while the API answers for the site, and none of its screens', async () => {
    const page = await open('/dashboard/sites/apple-pie', ({ sites }) =>
      sites.getCalls.answer('never'),
    );
    expect(page.root().querySelector('main pp-progress')?.textContent).toContain(
      'Opening your site',
    );
    expect(page.root().querySelector('ppa-site-home')).toBeNull();
  });

  it("asks for the route's site by its slug alone, and draws its page", async () => {
    const page = await open('/dashboard/sites/apple-pie');
    expect(page.sites.getCalls.made).toEqual([['apple-pie']]);
    expect(page.h1()).toEqual(['Apple Pie Club']);
    expect(page.root().querySelector('ppa-site-home')?.textContent).toContain(
      'apple-pie.pantrypress.app',
    );
    expect(document.title).toBe('Apple Pie Club - PantryPress');
  });

  it("answers a site that is not the caller's like one that does not exist, saying neither", async () => {
    const page = await open('/dashboard/sites/someone-else');
    expect(page.h1()).toEqual(["We couldn't find that site"]);
    expect(document.title).toBe('Site not found - PantryPress');
    expect(page.root().querySelector('main')?.textContent).not.toContain('someone-else');
    expect(page.links('main')).toEqual([
      { text: 'Go to your sites', href: '/dashboard', current: null },
    ]);
    expect(await violations(page)).toEqual([]);
  });

  it('offers to try again after a failure, and opens the site when it works', async () => {
    const page = await open('/dashboard/sites/apple-pie', ({ sites }) =>
      sites.getCalls.answer(refused(503, 'unexpected-error', 'Something went wrong on our side.')),
    );
    expect(page.h1()).toEqual(["We couldn't open this site"]);
    expect(page.root().querySelector('main')?.textContent).toContain(
      'Something went wrong on our side.',
    );
    expect(await violations(page)).toEqual([]);
    page.button('Try again').focus();
    page.button('Try again').click();
    await page.settle();
    expect(page.h1()).toEqual(['Apple Pie Club']);
    // Try again took its own button away; focus waited on the main area and went on to the heading.
    expect(document.activeElement?.textContent?.trim()).toBe('Apple Pie Club');
    expect(document.title).toBe('Apple Pie Club - PantryPress');
  });

  it('sends a session that ended to sign in, coming back to the site', async () => {
    const page = await open('/dashboard/sites/apple-pie', ({ sites }) =>
      sites.getCalls.answer(refused(401, 'session-ended', 'Please sign in again.')),
    );
    expect(page.url()).toBe('/sign-in?next=%2Fdashboard%2Fsites%2Fapple-pie&reason=signed-out');
  });

  it('draws the dashboard not-found page for an unknown part of a site', async () => {
    const page = await open('/dashboard/sites/apple-pie/nope');
    expect(page.h1()).toEqual(['Page not found']);
  });
});

describe('your sites (PP3.9)', () => {
  it('lists the caller sites, each leading to its own dashboard', async () => {
    const page = await open('/dashboard');
    const rows = [...page.root().querySelectorAll('ppa-dashboard-home li')].map((row) =>
      ['.name', '.host', '.meta'].map((part) =>
        row.querySelector(part)?.textContent?.replace(/\s+/g, ' ').trim(),
      ),
    );
    expect(rows).toEqual([
      ['Apple Pie Club', 'apple-pie.pantrypress.app', 'Owner Live'],
      ['Plum Tart Society', 'plum-tart.pantrypress.app', 'Author Read only'],
    ]);
    expect(page.links('ppa-dashboard-home .sites').map((link) => link.href)).toEqual([
      '/dashboard/sites/apple-pie',
      '/dashboard/sites/plum-tart',
    ]);
    expect(page.sites.listCalls.made).toContainEqual([{ limit: 20, cursor: undefined }]);
    expect(await violations(page)).toEqual([]);
  });

  it('pages by cursor, forward and back', async () => {
    const page = await open('/dashboard', ({ sites }) => {
      // The shell's switcher reads first, then the list.
      sites.listCalls.answer(
        { ok: true, status: 200, value: PAGE },
        { ok: true, status: 200, value: { items: [summary(APPLE)], nextCursor: 'c2' } },
        { ok: true, status: 200, value: { items: [summary(PLUM)], nextCursor: null } },
        { ok: true, status: 200, value: { items: [summary(APPLE)], nextCursor: 'c2' } },
      );
    });
    page.button('Next').click();
    await page.settle();
    expect(page.root().querySelector('.sites')?.textContent).toContain('Plum Tart Society');
    page.button('Previous').click();
    await page.settle();
    expect(page.sites.listCalls.made.slice(1)).toEqual([
      [{ limit: 20, cursor: undefined }],
      [{ limit: 20, cursor: 'c2' }],
      [{ limit: 20, cursor: undefined }],
    ]);
  });

  it('says there are no sites yet, and how one arrives', async () => {
    const page = await open('/dashboard', ({ sites }) => {
      const none: ApiResult<SitePage> = {
        ok: true,
        status: 200,
        value: { items: [], nextCursor: null },
      };
      sites.listCalls.answer(none, none);
    });
    const empty = page.root().querySelector('pp-empty-state');
    expect(empty?.textContent).toContain('No sites yet');
    expect(empty?.textContent).toContain('invites you');
    expect(await violations(page)).toEqual([]);
  });

  it('offers to try again when the list cannot be read', async () => {
    const page = await open('/dashboard', ({ sites }) => {
      const failure = refused<SitePage>(503, 'unexpected-error', 'Something went wrong.');
      sites.listCalls.answer(failure, failure);
    });
    expect(page.root().querySelector('ppa-dashboard-home')?.textContent).toContain(
      "Your sites couldn't be loaded.",
    );
    page.button('Try again').click();
    await page.settle();
    expect(page.root().querySelector('.sites')?.textContent).toContain('Apple Pie Club');
  });

  it('asks again for the page that failed, not the one shown before it', async () => {
    const page = await open('/dashboard', ({ sites }) => {
      sites.listCalls.answer(
        { ok: true, status: 200, value: PAGE },
        { ok: true, status: 200, value: { items: [summary(APPLE)], nextCursor: 'c2' } },
        refused(503, 'unexpected-error', 'Something went wrong.'),
      );
    });
    page.button('Next').click();
    await page.settle();
    page.button('Try again').click();
    await page.settle();
    expect(page.sites.listCalls.made.at(-1)).toEqual([{ limit: 20, cursor: 'c2' }]);
  });

  it('starts again from the first page when the API no longer takes a cursor', async () => {
    const page = await open('/dashboard', ({ sites }) => {
      sites.listCalls.answer(
        { ok: true, status: 200, value: PAGE },
        { ok: true, status: 200, value: { items: [summary(APPLE)], nextCursor: 'stale' } },
        refused(400, 'validation-failed', 'Check the highlighted fields.'),
      );
    });
    page.button('Next').click();
    await page.settle();
    expect(page.sites.listCalls.made.at(-1)).toEqual([{ limit: 20, cursor: undefined }]);
    expect(page.root().querySelector('.sites')?.textContent).toContain('Apple Pie Club');
  });

  it('offers an invitation waiting in this browser first', async () => {
    localStorage.setItem('pp-invitation', JSON.stringify({ token: 'abc', key: 'invitation-1' }));
    const page = await open('/dashboard');
    const notice = page.root().querySelector('ppa-dashboard-home pp-notice');
    expect(notice?.textContent).toContain('You have an invitation waiting');
    expect(page.links('ppa-dashboard-home pp-notice')).toEqual([
      { text: 'Accept the invitation', href: '/dashboard/join', current: null },
    ]);
  });
});

describe('joining a site from an invitation (PP3.9)', () => {
  const JOIN = '/dashboard/join#token=sealed+token/value=';

  it('accepts as the page opens, with the token and the link key, and goes to the site joined', async () => {
    const page = await open(JOIN);
    const [token, key] = page.sites.acceptCalls.made[0] as [string, string];
    expect(token).toBe('sealed+token/value=');
    expect(key).toMatch(/^invitation-/);
    expect(page.url()).toBe('/dashboard/sites/plum-tart');
    expect(page.h1()).toEqual(['Plum Tart Society']);
    expect(page.toasts).toEqual(["success: You've joined Plum Tart Society as an author."]);
    expect(page.invitation()).toBeNull();
    // The switcher reads the caller's sites again, so the new one is in it.
    expect(page.sites.listCalls.made.length).toBeGreaterThanOrEqual(2);
  });

  it('takes the token out of the address bar', async () => {
    const page = await open(JOIN, ({ sites }) => sites.acceptCalls.answer('never'));
    expect(TestBed.inject(Location).path(true)).toBe('/dashboard/join');
    expect(page.h1()).toEqual(['Joining the site']);
  });

  it('asks nothing for a link with no token, and says it does not work', async () => {
    const page = await open('/dashboard/join');
    expect(page.sites.acceptCalls.made).toEqual([]);
    expect(page.h1()).toEqual(["This invitation doesn't work"]);
    expect(await violations(page)).toEqual([]);
  });

  it('forgets a link the API refuses for good, in its words', async () => {
    const title =
      'This invitation has expired or was already used. Ask the site owner to invite you again.';
    const page = await open(JOIN, ({ sites }) =>
      sites.acceptCalls.answer(refused(400, 'invitation-invalid', title)),
    );
    expect(page.h1()).toEqual(["This invitation doesn't work"]);
    expect(page.root().querySelector('main')?.textContent).toContain(title);
    expect(page.invitation()).toBeNull();
    expect(document.activeElement?.textContent?.trim()).toBe("This invitation doesn't work");
  });

  it("forgets a link the API could not read, in the screen's own words", async () => {
    const page = await open(JOIN, ({ sites }) =>
      sites.acceptCalls.answer(refused(400, 'validation-failed', 'Check the highlighted fields.')),
    );
    expect(page.h1()).toEqual(["This invitation doesn't work"]);
    expect(page.root().querySelector('main')?.textContent).not.toContain('highlighted');
    expect(page.invitation()).toBeNull();
  });

  it('forgets the link of a site the caller already belongs to', async () => {
    const page = await open(JOIN, ({ sites }) =>
      sites.acceptCalls.answer(
        refused(409, 'already-member', "You're already a member of this site."),
      ),
    );
    expect(page.h1()).toEqual(["You're already a member of this site"]);
    expect(page.invitation()).toBeNull();
    expect(await violations(page)).toEqual([]);
  });

  it('keeps the link for the invited address, and signs out to let it sign in', async () => {
    const page = await open(JOIN, ({ sites }) =>
      sites.acceptCalls.answer(
        refused(
          403,
          'invitation-other-address',
          'This invitation was sent to a different email address. Sign in with that address to accept it.',
        ),
      ),
    );
    expect(page.h1()).toEqual(['This invitation is for another email address']);
    expect(page.root().querySelector('main')?.textContent).toContain(
      "You're signed in as cook@example.test.",
    );
    expect(await violations(page)).toEqual([]);
    page.button('Sign in with another account').click();
    await page.settle();
    expect(page.sessions.signOutCalls.made).toHaveLength(1);
    expect(page.url()).toBe('/sign-in?next=%2Fdashboard%2Fjoin');
    expect(page.invitation()?.token).toBe('sealed+token/value=');
  });

  it('says why when signing out to switch accounts is refused', async () => {
    const page = await open(JOIN, ({ sites, sessions }) => {
      sites.acceptCalls.answer(refused(403, 'invitation-other-address', 'Another address.'));
      sessions.signOutCalls.answer(refused(429, 'too-many-requests', 'Too many tries.'));
    });
    page.button('Sign in with another account').click();
    await page.settle();
    expect(TestBed.inject(Location).path()).toBe('/dashboard/join');
    expect(page.root().querySelector('.note')?.textContent).toContain('Too many tries.');
  });

  it('keeps the link while the address is unconfirmed, resends the email, and tries again with a new key', async () => {
    const page = await open(JOIN, ({ sites }) =>
      sites.acceptCalls.answer(
        refused(
          403,
          'email-not-verified',
          'Confirm your email address first. Check your inbox for our email.',
        ),
      ),
    );
    expect(page.h1()).toEqual(['Confirm your email address first']);
    expect(page.invitation()).not.toBeNull();
    page.button('Send the email again').click();
    await page.settle();
    expect(page.accounts.resendCalls.made).toEqual([['cook@example.test']]);
    expect(page.root().querySelector('.note')?.textContent).toContain(
      'If your address still needs confirming',
    );
    expect(await violations(page)).toEqual([]);
    page.button('Try again').click();
    await page.settle();
    const [first, second] = page.sites.acceptCalls.made as [string, string][];
    // The API keeps the 403 against the first key, so the try after confirming is a new request for the same link.
    expect(second[0]).toBe(first[0]);
    expect(second[1]).not.toBe(first[1]);
    expect(page.url()).toBe('/dashboard/sites/plum-tart');
  });

  it('keeps the link after a failure that may pass, and retries with the same key', async () => {
    const page = await open(JOIN, ({ sites }) =>
      sites.acceptCalls.answer(
        refused(503, 'unexpected-error', 'Something went wrong on our side.'),
      ),
    );
    expect(page.h1()).toEqual(["We couldn't accept the invitation"]);
    expect(page.root().querySelector('main')?.textContent).toContain(
      'Something went wrong on our side.',
    );
    expect(page.invitation()).not.toBeNull();
    expect(await violations(page)).toEqual([]);
    page.button('Try again').click();
    await page.settle();
    const [first, second] = page.sites.acceptCalls.made as [string, string][];
    expect(second).toEqual(first);
  });

  it('gives the link a new key after the invited address is asked for, so signing in with it is a new try', async () => {
    const page = await open(JOIN, ({ sites }) =>
      sites.acceptCalls.answer(refused(403, 'invitation-other-address', 'Another address.')),
    );
    const [[, key]] = page.sites.acceptCalls.made as [string, string][];
    expect(page.invitation()?.key).not.toBe(key);
  });

  it('keeps the link through a refusal at the gateway, which may pass', async () => {
    const page = await open(JOIN, ({ sites }) =>
      sites.acceptCalls.answer(
        refused(400, 'antiforgery-failed', 'Your session needs refreshing. Please try again.'),
      ),
    );
    expect(page.h1()).toEqual(["We couldn't accept the invitation"]);
    expect(page.invitation()?.token).toBe('sealed+token/value=');
  });

  it('takes a new key when the API says the key was used for something else, so Try again can work', async () => {
    const page = await open(JOIN, ({ sites }) =>
      sites.acceptCalls.answer(
        refused(
          422,
          'idempotency-key-reused',
          'That request key was already used for something else.',
        ),
      ),
    );
    expect(page.h1()).toEqual(["We couldn't accept the invitation"]);
    page.button('Try again').click();
    await page.settle();
    const [first, second] = page.sites.acceptCalls.made as [string, string][];
    expect(second[1]).not.toBe(first[1]);
    expect(page.url()).toBe('/dashboard/sites/plum-tart');
  });

  it('keeps the same key while the first try is still running', async () => {
    const page = await open(JOIN, ({ sites }) =>
      sites.acceptCalls.answer(
        refused(
          409,
          'idempotency-key-in-use',
          'Your first request is still going. Wait a moment, then try again.',
        ),
      ),
    );
    expect(page.root().querySelector('main')?.textContent).toContain('still going');
    page.button('Try again').click();
    await page.settle();
    const [first, second] = page.sites.acceptCalls.made as [string, string][];
    expect(second).toEqual(first);
  });

  it('says a repeated refusal aloud, since nothing on screen changes', async () => {
    const page = await open(JOIN, ({ sites }) => {
      const failure = refused<SiteSummary>(
        503,
        'unexpected-error',
        'Something went wrong on our side.',
      );
      sites.acceptCalls.answer(failure, failure);
    });
    const announce = vi.spyOn(TestBed.inject(PpToaster), 'announce');
    page.button('Try again').click();
    await page.settle();
    expect(announce).toHaveBeenCalledWith('Something went wrong on our side.');
  });

  it('sends a session that ended to sign in, keeping the link', async () => {
    const page = await open(JOIN, ({ sites }) =>
      sites.acceptCalls.answer(refused(401, 'session-ended', 'Please sign in again.')),
    );
    expect(page.url()).toBe('/sign-in?next=%2Fdashboard%2Fjoin&reason=signed-out');
    expect(page.invitation()?.token).toBe('sealed+token/value=');
  });

  it('accepts a link kept from before signing in, with the key it was kept with', async () => {
    localStorage.setItem('pp-invitation', JSON.stringify({ token: 'kept', key: 'invitation-1' }));
    const page = await open('/dashboard/join');
    expect(page.sites.acceptCalls.made).toEqual([['kept', 'invitation-1']]);
  });
});
