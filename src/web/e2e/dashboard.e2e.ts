import AxeBuilder from '@axe-core/playwright';
import { expect, Locator, Page, Route, test } from '@playwright/test';

/*
 * The dashboard's shell and joining a site in a browser (PP3.9), with the gateway and the API stood in for at the
 * network: switching sites and signing out by keyboard alone, an invitation link opened while signed out and accepted
 * after signing in, and axe with contrast on in both modes. Which site a caller may have is the API's to decide and is
 * proven in its tests; here only what the app does with each answer is.
 */

const WCAG = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];
const MODES = ['light', 'dark'] as const;
const VIEW = {
  accountId: '7f1f5c9a-1d2b-4c3d-9e4f-5a6b7c8d9e0f',
  email: 'cook@example.test',
  emailVerified: true,
  secondFactorVerified: false,
};
const APPLE = {
  slug: 'apple-pie',
  name: 'Apple Pie Club',
  status: 'live',
  role: 'owner',
  host: 'apple-pie.pantrypress.app',
};
const PLUM = {
  slug: 'plum-tart',
  name: 'Plum Tart Society',
  status: 'read-only',
  role: 'editor',
  host: 'plum-tart.pantrypress.app',
};
const detail = (site: typeof APPLE) => ({
  ...site,
  tagline: '',
  timeZone: 'America/Chicago',
  locale: 'en-US',
  createdAt: '2026-10-01T12:00:00Z',
});
const TOKEN = 'sealed+token/value==';

interface Api {
  /** A site name longer than any screen is wide, for reflow. */
  longName: boolean;
  signedIn: boolean;
  joined: boolean;
  acceptAnswer: 'ok' | 'other-address' | 'email-not-verified' | 'invalid';
  readonly requests: string[];
  readonly accepts: { body: unknown; key: string | null }[];
}

/** Answers the admin surface the way the gateway and ApiService would for one browser. */
async function standIn(page: Page, api: Partial<Api> = {}): Promise<Api> {
  const state: Api = {
    longName: false,
    signedIn: true,
    joined: false,
    acceptAnswer: 'ok',
    requests: [],
    accepts: [],
    ...api,
  };
  const json = (route: Route, status: number, body?: unknown) =>
    route.fulfill({
      status,
      contentType: status >= 400 ? 'application/problem+json' : 'application/json',
      body: body === undefined ? '' : JSON.stringify(body),
    });
  const problem = (route: Route, status: number, code: string, title: string) =>
    json(route, status, { status, code, title });
  await page.route('**/api/v1/**', async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname.replace('/api/v1/', '');
    state.requests.push(`${request.method()} ${url.pathname}${url.search}`);
    const apple = state.longName
      ? { ...APPLE, name: 'The Very Long Name Of A Kitchen Where Apple Pies Are Baked Daily' }
      : APPLE;
    const mine = state.joined ? [apple, PLUM] : [apple];

    if (request.method() === 'GET' && path === 'account/session') {
      return state.signedIn
        ? json(route, 200, { ...VIEW, stage: 'complete' })
        : problem(route, 401, 'sign-in-required', 'Please sign in to continue.');
    }
    if (request.method() === 'POST' && path === 'account/session') {
      state.signedIn = true;
      return json(route, 200, { ...VIEW, stage: 'complete' });
    }
    if (request.method() === 'DELETE' && path === 'account/session') {
      state.signedIn = false;
      return route.fulfill({ status: 204 });
    }
    if (request.method() === 'GET' && path === 'sites') {
      return json(route, 200, { items: mine, nextCursor: null });
    }
    if (request.method() === 'GET' && path.startsWith('sites/')) {
      const site = mine.find((candidate) => `sites/${candidate.slug}` === path);
      return site
        ? json(route, 200, detail(site))
        : problem(route, 404, 'site-not-found', "We couldn't find that site.");
    }
    if (request.method() === 'POST' && path === 'site-invitations/accept') {
      state.accepts.push({
        body: JSON.parse(request.postData() ?? 'null'),
        key: request.headers()['idempotency-key'] ?? null,
      });
      switch (state.acceptAnswer) {
        case 'other-address':
          return problem(
            route,
            403,
            'invitation-other-address',
            'This invitation was sent to a different email address. Sign in with that address to accept it.',
          );
        case 'email-not-verified':
          return problem(
            route,
            403,
            'email-not-verified',
            'Confirm your email address first. Check your inbox for our email.',
          );
        case 'invalid':
          return problem(
            route,
            400,
            'invitation-invalid',
            "This invitation has expired or was already used. Ask the site's owner to invite you again.",
          );
        default:
          state.joined = true;
          return json(route, 200, PLUM);
      }
    }
    return problem(route, 404, 'not-found', "We couldn't find that.");
  });
  return state;
}

/**
 * Moves to a link with Tab. WebKit, like Safari by default, leaves links out of Tab's order, and the Windows build
 * Playwright runs takes no Option-Tab either, so there the link is focused directly; Chromium and Firefox prove the order.
 */
async function tabTo(page: Page, browserName: string, link: Locator, presses: number) {
  if (browserName === 'webkit') {
    await link.focus();
    return;
  }
  for (let i = 0; i < presses; i += 1) {
    await page.keyboard.press('Tab');
  }
}

async function violations(page: Page): Promise<string[]> {
  const results = await new AxeBuilder({ page }).withTags(WCAG).analyze();
  return results.violations.map(
    (violation) =>
      `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
  );
}

test('switches sites by keyboard alone, keeping the shell, and the address names the site', async ({
  page,
  browserName,
}) => {
  await standIn(page, { joined: true });
  await page.goto('/dashboard/sites/apple-pie');
  await expect(page.getByRole('heading', { level: 1, name: 'Apple Pie Club' })).toBeVisible();

  const switcher = page.getByRole('button', { name: 'Site Apple Pie Club' });
  await switcher.focus();
  await page.keyboard.press('Enter');
  await expect(switcher).toHaveAttribute('aria-expanded', 'true');
  const plum = page.getByRole('link', { name: 'Plum Tart Society' });
  await tabTo(page, browserName, plum, 2);
  await expect(plum).toBeFocused();
  await page.keyboard.press('Enter');

  await expect(page).toHaveURL(/\/dashboard\/sites\/plum-tart$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Plum Tart Society' })).toBeFocused();
  await expect(page.getByRole('button', { name: 'Site Plum Tart Society' })).toBeVisible();
  await expect(
    page.getByRole('navigation', { name: 'Dashboard' }).getByRole('link', { name: 'Overview' }),
  ).toHaveAttribute('aria-current', 'page');
});

test('the skip link moves focus to the screen', async ({ page, browserName }) => {
  await standIn(page);
  await page.goto('/dashboard');
  await expect(page.getByRole('heading', { level: 1, name: 'Your sites' })).toBeVisible();
  const skip = page.getByRole('link', { name: 'Skip to main content' });
  await tabTo(page, browserName, skip, 1);
  await expect(skip).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { level: 1, name: 'Your sites' })).toBeFocused();
  await expect(page).toHaveURL(/\/dashboard$/);
});

test('signs out from the account menu and lands on the home page', async ({ page }) => {
  const api = await standIn(page);
  await page.goto('/dashboard');
  const menu = page.getByRole('button', { name: 'Account' });
  await menu.click();
  await expect(page.getByText('Signed in as cook@example.test')).toBeVisible();
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/$/);
  expect(api.requests).toContain('DELETE /api/v1/account/session');
});

test('an invitation opened while signed out is accepted after signing in, and its token is never in an address', async ({
  page,
}) => {
  const api = await standIn(page, { signedIn: false });
  await page.goto(`/dashboard/join#token=${encodeURIComponent(TOKEN)}`);

  await expect(page).toHaveURL(/\/sign-in\?next=%2Fdashboard%2Fjoin$/);
  await expect(
    page.getByText('To accept your invitation, sign in with the email address it was sent to.'),
  ).toBeVisible();
  await page.getByLabel('Email address').fill('cook@example.test');
  await page.getByLabel('Password').fill('a long kitchen phrase');
  await page.getByRole('button', { name: 'Sign in' }).click();

  await expect(page).toHaveURL(/\/dashboard\/sites\/plum-tart$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Plum Tart Society' })).toBeVisible();
  await expect(
    page
      .getByRole('region', { name: 'Notifications' })
      .getByText("You've joined Plum Tart Society as an editor."),
  ).toBeVisible();
  expect(api.accepts).toHaveLength(1);
  expect(api.accepts[0].body).toEqual({ token: TOKEN });
  expect(api.accepts[0].key).toMatch(/^invitation-/);
  // Neither the API nor any address the app moved to saw the token outside the one body that carries it.
  expect(api.requests.some((request) => request.includes('token'))).toBe(false);
  expect(await page.evaluate(() => localStorage.getItem('pp-invitation'))).toBeNull();
});

test('reflows at 320 pixels wide with a long site name and both menus open, never scrolling sideways', async ({
  page,
}) => {
  await page.setViewportSize({ width: 320, height: 640 });
  await standIn(page, { longName: true, joined: true });
  await page.goto('/dashboard/sites/apple-pie');
  await expect(page.locator('ppa-site-home h1')).toBeVisible();
  const scrollsSideways = () =>
    page.evaluate(
      () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
    );
  expect(await scrollsSideways()).toBe(false);
  await page.getByRole('button', { name: /^Site / }).click();
  expect(await scrollsSideways()).toBe(false);
  // The open panel lies over the account button at this width, as a panel does, so it is closed first.
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: 'Account' }).click();
  expect(await scrollsSideways()).toBe(false);
  expect(await violations(page)).toEqual([]);
});

for (const mode of MODES) {
  test.describe(`axe, contrast included, in ${mode} mode`, () => {
    test.beforeEach(async ({ page }) => {
      await page.emulateMedia({ colorScheme: mode });
    });

    test('over your sites, and a site with both menus open', async ({ page }) => {
      await standIn(page, { joined: true });
      await page.goto('/dashboard');
      await expect(page.getByRole('link', { name: 'Plum Tart Society' })).toBeVisible();
      expect(await violations(page)).toEqual([]);

      await page.goto('/dashboard/sites/apple-pie');
      await expect(page.getByRole('heading', { level: 1, name: 'Apple Pie Club' })).toBeVisible();
      await page.getByRole('button', { name: 'Site Apple Pie Club' }).click();
      expect(await violations(page)).toEqual([]);
      await page.getByRole('button', { name: 'Account' }).click();
      expect(await violations(page)).toEqual([]);
    });

    test('over a site that is not found', async ({ page }) => {
      await standIn(page);
      await page.goto('/dashboard/sites/someone-else');
      await expect(
        page.getByRole('heading', { level: 1, name: "We couldn't find that site" }),
      ).toBeVisible();
      expect(await violations(page)).toEqual([]);
    });

    for (const answer of ['other-address', 'email-not-verified', 'invalid'] as const) {
      test(`over the join screen when the API answers ${answer}`, async ({ page }) => {
        await standIn(page, { acceptAnswer: answer });
        await page.goto(`/dashboard/join#token=${encodeURIComponent(TOKEN)}`);
        await expect(page.locator('ppa-join-site h1')).not.toHaveText('Joining the site');
        await expect(page).toHaveURL(/\/dashboard\/join$/);
        expect(await violations(page)).toEqual([]);
      });
    }
  });
}
