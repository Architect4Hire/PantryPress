import AxeBuilder from '@axe-core/playwright';
import { expect, Page, Route, test } from '@playwright/test';

/*
 * The account screens in a browser (PP3.8), with the gateway and the API stood in for at the network: the guards, both
 * sign-in steps, an emailed link, and axe with contrast on in both modes. The gateway's own behaviour - the cookie, the
 * redirect at '/' - is proven in its tests; here only what the app does with each answer is.
 */

const WCAG = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];
const ACCOUNT = '7f1f5c9a-1d2b-4c3d-9e4f-5a6b7c8d9e0f';
const VIEW = {
  accountId: ACCOUNT,
  email: 'cook@example.test',
  emailVerified: true,
  secondFactorVerified: false,
};

interface Api {
  stage: 'none' | 'second-factor' | 'complete';
  twoStep: boolean;
  readonly bodies: Record<string, unknown[]>;
}

/** Answers the admin surface the way the gateway and ApiService would for one browser. */
async function standIn(page: Page, api: Partial<Api> = {}): Promise<Api> {
  const state: Api = { stage: 'none', twoStep: false, bodies: {}, ...api };
  const json = (route: Route, status: number, body?: unknown) =>
    route.fulfill({
      status,
      contentType: status >= 400 ? 'application/problem+json' : 'application/json',
      body: body === undefined ? '' : JSON.stringify(body),
    });
  await page.route('**/api/v1/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname.replace('/api/v1/', '');
    const body: unknown = request.postData() ? JSON.parse(request.postData()!) : undefined;
    (state.bodies[`${request.method()} ${path}`] ??= []).push(body);

    switch (`${request.method()} ${path}`) {
      case 'GET account/session':
        return state.stage === 'none'
          ? json(route, 401, {
              status: 401,
              code: 'sign-in-required',
              title: 'Please sign in to continue.',
            })
          : json(route, 200, { ...VIEW, stage: state.stage });
      case 'POST account/session':
        state.stage = state.twoStep ? 'second-factor' : 'complete';
        return json(route, 200, { ...VIEW, stage: state.stage });
      case 'POST account/session/second-factor':
        state.stage = 'complete';
        return json(route, 200, { ...VIEW, stage: 'complete', secondFactorVerified: true });
      case 'POST account/email/verify':
        return route.fulfill({ status: 204 });
      default:
        return json(route, 404, {
          status: 404,
          code: 'not-found',
          title: "We couldn't find that.",
        });
    }
  });
  return state;
}

async function violations(page: Page): Promise<string[]> {
  const results = await new AxeBuilder({ page }).withTags(WCAG).analyze();
  return results.violations.map(
    (violation) =>
      `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
  );
}

test('an anonymous visitor at the dashboard signs in and arrives where they were going', async ({
  page,
}) => {
  const api = await standIn(page);

  await page.goto('/dashboard');
  await expect(page).toHaveURL(/\/sign-in$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Sign in' })).toBeFocused();

  await page.getByLabel('Email address').fill('cook@example.test');
  await page.getByLabel('Password').fill('a long kitchen phrase');
  await page.getByRole('button', { name: 'Sign in' }).click();

  await expect(page).toHaveURL(/\/dashboard$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Dashboard' })).toBeVisible();
  expect(api.bodies['POST account/session']).toEqual([
    { email: 'cook@example.test', password: 'a long kitchen phrase' },
  ]);
});

test('an account with two-step sign-in is asked for its code, by keyboard alone', async ({
  page,
}) => {
  const api = await standIn(page, { twoStep: true });

  await page.goto('/sign-in');
  await page.keyboard.press('Tab');
  await page.keyboard.type('cook@example.test');
  await page.keyboard.press('Tab');
  await page.keyboard.type('a long kitchen phrase');
  await page.keyboard.press('Enter');

  await expect(
    page.getByRole('heading', { level: 1, name: 'Enter your sign-in code' }),
  ).toBeFocused();
  await page.keyboard.press('Tab');
  await page.keyboard.type('123456');
  await page.keyboard.press('Enter');

  await expect(page).toHaveURL(/\/dashboard$/);
  expect(api.bodies['POST account/session/second-factor']).toEqual([{ code: '123456' }]);
});

test('a signed-in visitor at sign-in goes to the dashboard', async ({ page }) => {
  await standIn(page, { stage: 'complete' });
  await page.goto('/sign-in');
  await expect(page).toHaveURL(/\/dashboard$/);
});

test('a confirmation link confirms, keeps its token whole, and leaves the address bar', async ({
  page,
}) => {
  const api = await standIn(page);
  const token = 'CfDJ8+abc/def==';

  await page.goto(`/verify-email#account=${ACCOUNT}&token=${encodeURIComponent(token)}`);

  await expect(
    page.getByRole('heading', { level: 1, name: 'Your email address is confirmed' }),
  ).toBeVisible();
  expect(api.bodies['POST account/email/verify']).toEqual([{ accountId: ACCOUNT, token }]);
  expect(new URL(page.url()).hash).toBe('');
});

test.describe('axe, contrast included', () => {
  for (const scheme of ['light', 'dark'] as const) {
    for (const path of [
      '/sign-in',
      '/sign-in?reason=signed-out',
      '/sign-up',
      '/forgot-password',
      '/reset-password',
      '/verify-email',
    ]) {
      test(`${path} passes in ${scheme} mode`, async ({ page }) => {
        await standIn(page);
        await page.emulateMedia({ colorScheme: scheme });
        await page.goto(path);
        await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
        expect(await violations(page)).toEqual([]);
      });
    }
  }
});
