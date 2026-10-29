import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { ApiResult } from '../api/api-problem';
import { SessionClient, SessionView } from './session-client';
import { dashboardAddress, signedInGuard, signedOutGuard } from './session-guards';

@Component({ template: 'page' })
class Page {}

const VIEW: SessionView = {
  stage: 'complete',
  accountId: '7f1f5c9a-1d2b-4c3d-9e4f-5a6b7c8d9e0f',
  email: 'cook@example.test',
  emailVerified: true,
  secondFactorVerified: false,
};

function refused(status: number, code: string): ApiResult<SessionView> {
  return { ok: false, problem: { status, code, title: 'A plain title.' } };
}

/** The gateway's answer to `GET /session`, as a test sets it. */
class FakeSessions {
  answer: ApiResult<SessionView> = refused(401, 'sign-in-required');
  calls = 0;
  current(): Promise<ApiResult<SessionView>> {
    this.calls++;
    return Promise.resolve(this.answer);
  }
}

async function visit(url: string, answer: ApiResult<SessionView>) {
  const sessions = new FakeSessions();
  sessions.answer = answer;
  TestBed.configureTestingModule({
    providers: [
      { provide: SessionClient, useValue: sessions },
      provideRouter([
        { path: '', component: Page },
        { path: 'pricing', component: Page },
        { path: 'sign-in', component: Page, canActivate: [signedOutGuard] },
        { path: 'sign-up', component: Page, canActivate: [signedOutGuard] },
        {
          path: 'dashboard',
          canActivate: [signedInGuard],
          children: [{ path: '**', component: Page }],
        },
      ]),
    ],
  });
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(url);
  return { url: TestBed.inject(Router).url, sessions };
}

describe('signedInGuard', () => {
  it('lets a complete session into the dashboard', async () => {
    const { url } = await visit('/dashboard/sites', { ok: true, status: 200, value: VIEW });
    expect(url).toBe('/dashboard/sites');
  });

  it('sends nobody signed in to sign in, keeping the dashboard address they wanted', async () => {
    const { url } = await visit(
      '/dashboard/sites/my-blog?tab=posts',
      refused(401, 'sign-in-required'),
    );
    expect(url).toBe('/sign-in?next=%2Fdashboard%2Fsites%2Fmy-blog%3Ftab%3Dposts');
  });

  it('sends the dashboard itself to sign in with no next address', async () => {
    const { url } = await visit('/dashboard', refused(401, 'sign-in-required'));
    expect(url).toBe('/sign-in');
  });

  it('says a session ended elsewhere was signed out', async () => {
    const { url } = await visit('/dashboard', refused(401, 'session-ended'));
    expect(url).toBe('/sign-in?reason=signed-out');
  });

  it.each([
    [502, 'unexpected-error'],
    [504, 'unexpected-error'],
    [0, 'unreachable'],
  ])(
    'sends a session it could not check (%i) to sign in, never to a page the gateway redirects',
    async (status, code) => {
      const { url } = await visit('/dashboard/x', refused(status, code));
      expect(url).toBe('/sign-in?next=%2Fdashboard%2Fx&reason=unavailable');
      expect(url.startsWith('/pricing')).toBe(false);
      expect(url).not.toBe('/');
    },
  );

  it('says the service is busy when the session check was turned away for too many requests', async () => {
    const { url } = await visit('/dashboard', refused(429, 'too-many-requests'));
    expect(url).toBe('/sign-in?reason=busy');
  });

  it('sends a half-finished sign-in back to finish it', async () => {
    const { url } = await visit('/dashboard', {
      ok: true,
      status: 200,
      value: { ...VIEW, stage: 'second-factor' },
    });
    expect(url).toBe('/sign-in');
  });
});

describe('signedOutGuard', () => {
  it.each(['/sign-in', '/sign-up'])(
    'sends a complete session at %s to the dashboard',
    async (path) => {
      const { url } = await visit(path, { ok: true, status: 200, value: VIEW });
      expect(url).toBe('/dashboard');
    },
  );

  it.each([
    refused(401, 'sign-in-required'),
    refused(401, 'session-ended'),
    refused(502, 'unexpected-error'),
    { ok: true as const, status: 200, value: { ...VIEW, stage: 'second-factor' as const } },
  ])('shows the screen to anyone else, the API being down included', async (answer) => {
    const { url } = await visit('/sign-in', answer);
    expect(url).toBe('/sign-in');
  });
});

describe('dashboardAddress', () => {
  it.each([
    ['/dashboard', '/dashboard'],
    ['/dashboard/sites/my-blog', '/dashboard/sites/my-blog'],
    ['/dashboard?tab=posts', '/dashboard?tab=posts'],
    ['/dashboard#top', '/dashboard#top'],
  ])('takes %s', (next, expected) => expect(dashboardAddress(next)).toBe(expected));

  it.each([
    null,
    undefined,
    '',
    '/',
    '/pricing',
    '/sign-in',
    'https://evil.example/dashboard',
    '//evil.example/dashboard',
    '/dashboard//evil.example',
    '/dashboardx',
    '/dashboard/../pricing',
    '/dashboard/..',
    '/dashboard\\evil',
    'dashboard',
  ])('turns %s into the dashboard itself', (next) => {
    expect(dashboardAddress(next)).toBe('/dashboard');
  });
});
