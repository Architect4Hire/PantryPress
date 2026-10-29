import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { RenderMode } from '@angular/ssr';
import { routes } from './app.routes';
import { serverRoutes } from './app.routes.server';
import { ApiClient } from './core/api/api-client';
import { ApiPath } from './core/api/api-path';
import { ApiResult } from './core/api/api-problem';
import { provideRuntimeConfig, RuntimeConfigService } from './core/config/runtime-config.service';
import { RUNTIME_CONFIG_DEFAULTS } from './core/config/runtime-config';
import { PendingInvitation } from './core/sites/pending-invitation';
import { DEV_ROUTES } from './dev-routes';
import { PUBLIC_PATHS, SIGNED_IN_REDIRECT_PATHS } from './public-paths';

/** The account screens and the dashboard: drawn in the browser, never prerendered (PP3.8). */
const ACCOUNT_SCREENS = ['sign-in', 'sign-up', 'verify-email', 'forgot-password', 'reset-password'];

const SIGNED_IN = {
  stage: 'complete',
  accountId: '7f1f5c9a-1d2b-4c3d-9e4f-5a6b7c8d9e0f',
  email: 'cook@example.test',
  emailVerified: true,
  secondFactorVerified: false,
};

/**
 * Stands in for `ApiClient`, the app's one way to HTTP (a lint rule keeps `@angular/common/http` in `core/api/`), and
 * records every call. The session read answers as `session` says - nobody signed in unless a test says so - so the
 * guards can decide; every other call never answers, since no page here may wait on one.
 */
class RecordingApi {
  readonly calls: string[] = [];
  session: ApiResult<unknown> = {
    ok: false,
    problem: { status: 401, code: 'sign-in-required', title: 'Please sign in to continue.' },
  };
  private record(method: string, path: ApiPath): Promise<ApiResult<unknown>> {
    this.calls.push(`${method} ${path}`);
    if (method === 'GET' && path === 'account/session') {
      return Promise.resolve(this.session);
    }
    // As today's server answers it: nothing published yet, so every switch keeps its closed default.
    if (method === 'GET' && path === 'client-configuration') {
      return Promise.resolve({
        ok: false,
        problem: { status: 404, code: 'not-found', title: "We couldn't find that." },
      });
    }
    return new Promise<never>(() => undefined);
  }
  get = (path: ApiPath) => this.record('GET', path);
  post = (path: ApiPath) => this.record('POST', path);
  put = (path: ApiPath) => this.record('PUT', path);
  patch = (path: ApiPath) => this.record('PATCH', path);
  delete = (path: ApiPath) => this.record('DELETE', path);
}

/** The app's router and start-up work, as `appConfig` gives them, on the platform asked for. */
function setUp(platform: 'browser' | 'server' = 'browser'): RecordingApi {
  const api = new RecordingApi();
  TestBed.configureTestingModule({
    providers: [
      provideRouter(routes),
      provideRuntimeConfig(),
      { provide: ApiClient, useValue: api },
      { provide: PLATFORM_ID, useValue: platform },
    ],
  });
  return api;
}

async function open(url: string) {
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(url);
  const root = harness.routeNativeElement as HTMLElement;
  return {
    harness,
    root,
    h1: () => [...root.querySelectorAll('h1')].map((h) => h.textContent?.trim()),
    router: TestBed.inject(Router),
    url: () => TestBed.inject(Router).url,
  };
}

describe('the apex route table (PPL.2)', () => {
  describe('in the browser', () => {
    beforeEach(() => setUp());
    afterEach(() => localStorage.clear());

    it('renders the landing page at /, with one main and one h1', async () => {
      const page = await open('/');
      expect(page.h1()).toEqual(['PantryPress']);
      expect(page.root.querySelectorAll('main')).toHaveLength(1);
      expect(page.root.tagName.toLowerCase()).toBe('ppa-landing');
      expect(document.title).toBe('PantryPress');
    });

    it('renders the same landing page at /pricing, asked to open at its pricing section', async () => {
      const page = await open('/pricing');
      expect(page.root.tagName.toLowerCase()).toBe('ppa-landing');
      expect(page.h1()).toEqual(['PantryPress']);
      expect(page.router.routerState.snapshot.root.firstChild?.data).toMatchObject({
        title: 'Pricing',
        scrollTo: 'pricing',
      });
      expect(document.title).toBe('Pricing - PantryPress');
    });

    it.each([
      ['terms', 'Terms of service'],
      ['privacy', 'Privacy policy'],
      ['acceptable-use', 'Acceptable use policy'],
      ['dmca', 'Copyright and DMCA'],
    ])('renders the legal placeholder at /%s', async (path, heading) => {
      const page = await open(`/${path}`);
      expect(page.root.tagName.toLowerCase()).toBe('ppa-legal-placeholder');
      expect(page.h1()).toEqual([heading]);
      expect(document.title).toBe(`${heading} - PantryPress`);
    });

    it.each([
      ['sign-in', 'ppa-sign-in', 'Sign in - PantryPress'],
      ['sign-up', 'ppa-sign-up', 'Create your account - PantryPress'],
      ['verify-email', 'ppa-verify-email', 'Confirm your email address - PantryPress'],
      ['forgot-password', 'ppa-forgot-password', 'Reset your password - PantryPress'],
      ['reset-password', 'ppa-reset-password', 'Choose a new password - PantryPress'],
    ])('renders the account screen at /%s (PP3.8)', async (path, tag, title) => {
      const page = await open(`/${path}`);
      expect(page.root.tagName.toLowerCase()).toBe(tag);
      expect(page.h1()).toHaveLength(1);
      expect(page.url()).toBe(`/${path}`);
      expect(document.title).toBe(title);
    });

    it('sends an anonymous visitor at the dashboard to sign in, keeping where they were going', async () => {
      const page = await open('/dashboard/sites');
      expect(page.root.tagName.toLowerCase()).toBe('ppa-sign-in');
      expect(page.url()).toBe('/sign-in?next=%2Fdashboard%2Fsites');
    });

    it('renders the dashboard in its shell for a complete session', async () => {
      const api = TestBed.inject(ApiClient) as unknown as RecordingApi;
      api.session = { ok: true, status: 200, value: SIGNED_IN };
      const page = await open('/dashboard');
      expect(page.root.tagName.toLowerCase()).toBe('ppa-dashboard-shell');
      expect(page.root.querySelectorAll('main')).toHaveLength(1);
      expect(page.root.querySelector('main ppa-dashboard-home')).not.toBeNull();
      expect(page.h1()).toEqual(['Your sites']);
      expect(page.url()).toBe('/dashboard');
      expect(document.title).toBe('Your sites - PantryPress');
    });

    it("renders the dashboard's own not-found page, in the shell, for a complete session", async () => {
      const api = TestBed.inject(ApiClient) as unknown as RecordingApi;
      api.session = { ok: true, status: 200, value: SIGNED_IN };
      const page = await open('/dashboard/x');
      expect(page.root.querySelector('main ppa-dashboard-not-found')).not.toBeNull();
      expect(page.h1()).toEqual(['Page not found']);
      expect(page.url()).toBe('/dashboard/x');
      expect(document.title).toBe('Page not found - PantryPress');
    });

    it('keeps an invitation link through the way to sign in, and never puts its token in an address (PP3.9)', async () => {
      const page = await open('/dashboard/join#token=sealed+token/value=');
      expect(page.root.tagName.toLowerCase()).toBe('ppa-sign-in');
      expect(page.url()).toBe('/sign-in?next=%2Fdashboard%2Fjoin');
      expect(TestBed.inject(PendingInvitation).link()?.token).toBe('sealed+token/value=');
    });

    it('drops any fragment from the address sign-in goes on to', async () => {
      const page = await open('/dashboard/sites/my-blog#secret');
      expect(page.url()).toBe('/sign-in?next=%2Fdashboard%2Fsites%2Fmy-blog');
    });

    it.each(['/nope', '/pricing/x', '/terms/x', '/sign-in/x'])(
      'renders the not-found page at %s, at the address asked for',
      async (url) => {
        const page = await open(url);
        expect(page.root.tagName.toLowerCase()).toBe('ppa-not-found');
        expect(page.h1()).toEqual(['Page not found']);
        expect(page.url()).toBe(url);
        expect(document.title).toBe('Page not found - PantryPress');
      },
    );

    it('links the not-found page home', async () => {
      const page = await open('/nope');
      expect(page.root.querySelector('a')?.getAttribute('href')).toBe('/');
    });

    it.each(PUBLIC_PATHS)('names a route for the public path /%s', async (path) => {
      const page = await open(`/${path}`);
      expect(page.root.tagName.toLowerCase()).not.toBe('ppa-not-found');
    });
  });

  describe('as the build prerenders it', () => {
    // The control: in the browser the same start-up reads the runtime configuration, so the recorder sees calls.
    it('sees the browser read the runtime configuration, after the page is drawn', async () => {
      const api = setUp('browser');
      const page = await open('/');
      expect(page.h1()).toEqual(['PantryPress']);
      expect(api.calls).toEqual(['GET client-configuration']);
    });

    it.each(PUBLIC_PATHS)(
      'renders /%s with no API call and nothing the server publishes',
      async (path) => {
        const api = setUp('server');
        const page = await open(`/${path}`);
        expect(page.h1()).toHaveLength(1);
        expect(api.calls).toEqual([]);
        const config = TestBed.inject(RuntimeConfigService);
        expect(config.config()).toEqual(RUNTIME_CONFIG_DEFAULTS);
        expect(config.status()).toBe('loading');
      },
    );
  });
});

describe('the server routes (PPL.2)', () => {
  const prerendered = serverRoutes
    .filter((route) => route.renderMode === RenderMode.Prerender)
    .map((route) => route.path);

  it('prerender exactly the public paths', () => {
    expect(prerendered).toEqual([...PUBLIC_PATHS]);
  });

  it('render every other route in the browser, and none on a server', () => {
    const rest = serverRoutes.filter((route) => route.renderMode !== RenderMode.Prerender);
    expect(rest).toEqual([{ path: '**', renderMode: RenderMode.Client }]);
    expect(serverRoutes.some((route) => route.renderMode === RenderMode.Server)).toBe(false);
  });

  it('never prerender the dashboard or a sign-in screen', () => {
    for (const path of prerendered) {
      expect(path === 'dashboard' || path.startsWith('dashboard/'), path).toBe(false);
      expect(ACCOUNT_SCREENS, path).not.toContain(path);
    }
  });

  it('cover nothing the route table does not name', () => {
    const named = new Set(routes.map((route) => route.path));
    for (const path of prerendered) {
      expect(named.has(path), path).toBe(true);
    }
  });

  // The other direction: a public page added to the route table but not to PUBLIC_PATHS would be drawn in the
  // browser, with nothing to read without JavaScript (frontend.md's closed list).
  it('leave no other top-level page out of the public paths', () => {
    const browserOnly = new Set([
      'dashboard',
      '**',
      ...ACCOUNT_SCREENS,
      ...DEV_ROUTES.map((r) => r.path),
    ]);
    const unlisted = routes
      .map((route) => route.path ?? '')
      .filter(
        (path) => !browserOnly.has(path) && !(PUBLIC_PATHS as readonly string[]).includes(path),
      );
    expect(unlisted).toEqual([]);
  });
});

describe('the signed-in redirect paths (PPL-001)', () => {
  it('are the landing page and its pricing address, and never a legal page', () => {
    expect([...SIGNED_IN_REDIRECT_PATHS]).toEqual(['', 'pricing']);
  });
});
