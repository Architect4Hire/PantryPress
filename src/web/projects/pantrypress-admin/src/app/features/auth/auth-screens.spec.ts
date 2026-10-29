import { Location } from '@angular/common';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import axe from 'axe-core';
import { ApiProblem, ApiResult } from '../../core/api/api-problem';
import { RuntimeConfig, RUNTIME_CONFIG_DEFAULTS } from '../../core/config/runtime-config';
import { RuntimeConfigService } from '../../core/config/runtime-config.service';
import { AccountClient } from '../../core/session/account-client';
import { SessionClient, SessionView } from '../../core/session/session-client';
import { feedbackFor } from './form-feedback';
import { ForgotPassword } from './forgot-password';
import { readAccountLink } from './link-fragment';
import { ResetPassword } from './reset-password';
import { SignIn } from './sign-in';
import { SignUp } from './sign-up';
import { VerifyEmail } from './verify-email';

@Component({ template: '<h1>Dashboard page</h1>' })
class Dashboard {}

const ACCOUNT = '7f1f5c9a-1d2b-4c3d-9e4f-5a6b7c8d9e0f';
const LINK = `account=${ACCOUNT}&token=${encodeURIComponent('tok+en/==')}`;
const PHRASE = 'a long kitchen phrase';
const NEW_PHRASE = 'a brand new kitchen phrase';

const VIEW: SessionView = {
  stage: 'complete',
  accountId: ACCOUNT,
  email: 'cook@example.test',
  emailVerified: true,
  secondFactorVerified: false,
};

const OK: ApiResult<void> = { ok: true, status: 202, value: undefined };

function refused<T>(
  status: number,
  code: string,
  title: string,
  errors?: ApiProblem['errors'],
): ApiResult<T> {
  return { ok: false, problem: { status, code, title, ...(errors ? { errors } : {}) } };
}

/** Records each call and answers from a queue a test fills; an empty queue answers the fallback. */
class Calls<T> {
  readonly made: unknown[][] = [];
  private readonly queue: T[] = [];
  constructor(private readonly fallback: T) {}
  answer(...answers: T[]): void {
    this.queue.push(...answers);
  }
  take(args: unknown[]): Promise<T> {
    this.made.push(args);
    return Promise.resolve(this.queue.shift() ?? this.fallback);
  }
}

class FakeSessions {
  readonly session = signal<SessionView | null>(null);
  readonly signInCalls = new Calls<ApiResult<SessionView>>({ ok: true, status: 200, value: VIEW });
  readonly codeCalls = new Calls<ApiResult<SessionView>>({ ok: true, status: 200, value: VIEW });
  readonly signOutCalls = new Calls<ApiResult<void>>({ ok: true, status: 204, value: undefined });
  signIn(...args: unknown[]) {
    return this.signInCalls.take(args);
  }
  completeSecondStep(...args: unknown[]) {
    return this.codeCalls.take(args);
  }
  signOut() {
    return this.signOutCalls.take([]);
  }
}

class FakeAccounts {
  private readonly calls = new Map<string, Calls<ApiResult<void>>>();
  on(name: string): Calls<ApiResult<void>> {
    let calls = this.calls.get(name);
    if (calls === undefined) {
      calls = new Calls<ApiResult<void>>(OK);
      this.calls.set(name, calls);
    }
    return calls;
  }
  register(...args: unknown[]) {
    return this.on('register').take(args);
  }
  verifyEmail(...args: unknown[]) {
    return this.on('verifyEmail').take(args);
  }
  resendVerification(...args: unknown[]) {
    return this.on('resendVerification').take(args);
  }
  forgotPassword(...args: unknown[]) {
    return this.on('forgot').take(args);
  }
  resetPassword(...args: unknown[]) {
    return this.on('reset').take(args);
  }
}

interface Screen {
  readonly root: () => HTMLElement;
  readonly sessions: FakeSessions;
  readonly accounts: FakeAccounts;
  readonly url: () => string;
  readonly settle: () => Promise<void>;
}

/** Opens a screen through the real router, with the clients it talks to standing in for the gateway and the API. */
async function open(
  url: string,
  prepare: (sessions: FakeSessions, accounts: FakeAccounts) => void = () => undefined,
  config: RuntimeConfig = RUNTIME_CONFIG_DEFAULTS,
): Promise<Screen> {
  const sessions = new FakeSessions();
  const accounts = new FakeAccounts();
  prepare(sessions, accounts);
  TestBed.configureTestingModule({
    providers: [
      { provide: SessionClient, useValue: sessions },
      { provide: AccountClient, useValue: accounts },
      { provide: RuntimeConfigService, useValue: { config: signal(config).asReadonly() } },
      provideRouter([
        { path: 'sign-in', component: SignIn },
        { path: 'sign-up', component: SignUp },
        { path: 'verify-email', component: VerifyEmail },
        { path: 'forgot-password', component: ForgotPassword },
        { path: 'reset-password', component: ResetPassword },
        { path: 'dashboard', children: [{ path: '**', component: Dashboard }] },
      ]),
    ],
  });
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(url);
  const settle = async () => {
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
  };
  await settle();
  return {
    root: () => harness.routeNativeElement as HTMLElement,
    sessions,
    accounts,
    url: () => TestBed.inject(Router).url,
    settle,
  };
}

function heading(screen: Screen): string | undefined {
  return screen.root().querySelector('h1')?.textContent?.trim();
}

function control(screen: Screen, label: string): HTMLInputElement {
  const labels = [...screen.root().querySelectorAll('label')];
  const match = labels.find((candidate) => candidate.textContent?.trim().startsWith(label));
  const input = match ? screen.root().querySelector<HTMLInputElement>(`#${match.htmlFor}`) : null;
  if (input === null) {
    throw new Error(`No field labelled '${label}'.`);
  }
  return input;
}

function type(screen: Screen, label: string, value: string): void {
  const input = control(screen, label);
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

async function submit(screen: Screen): Promise<void> {
  screen.root().querySelector('form')?.dispatchEvent(new Event('submit'));
  await screen.settle();
}

async function press(screen: Screen, text: string): Promise<void> {
  const button = [...screen.root().querySelectorAll<HTMLElement>('button, a')].find(
    (candidate) => candidate.textContent?.trim() === text,
  );
  if (button === undefined) {
    throw new Error(`No button '${text}'.`);
  }
  button.click();
  await screen.settle();
}

function notices(screen: Screen): string[] {
  return [...screen.root().querySelectorAll('pp-notice')].map(
    (notice) => notice.querySelector('.content')?.textContent?.replace(/\s+/g, ' ').trim() ?? '',
  );
}

function fieldError(screen: Screen, label: string): string | null {
  const described = control(screen, label).getAttribute('aria-describedby') ?? '';
  const errors = described
    .split(' ')
    .map((id) => (id ? screen.root().querySelector(`#${id}`) : null))
    .filter((element) => element?.closest('pp-field-error'));
  return errors[0]?.textContent?.trim() ?? null;
}

async function axeViolations(screen: Screen): Promise<string[]> {
  const results = await axe.run(screen.root(), {
    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] },
    rules: { 'color-contrast': { enabled: false } },
  });
  return results.violations.map(
    (violation) =>
      `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`,
  );
}

describe('sign in (PP3.8)', () => {
  it('signs in and goes to the dashboard address the visitor was turned away from', async () => {
    const screen = await open('/sign-in?next=%2Fdashboard%2Fsites');
    expect(heading(screen)).toBe('Sign in');
    expect(control(screen, 'Email address').autocomplete).toBe('username');
    expect(control(screen, 'Password').autocomplete).toBe('current-password');

    type(screen, 'Email address', 'cook@example.test');
    type(screen, 'Password', PHRASE);
    await submit(screen);

    expect(screen.sessions.signInCalls.made).toEqual([['cook@example.test', PHRASE]]);
    expect(screen.url()).toBe('/dashboard/sites');
  });

  it('never follows a next address outside the dashboard', async () => {
    const screen = await open('/sign-in?next=%2F%2Fevil.example');
    await submit(screen);
    expect(screen.url()).toBe('/dashboard');
  });

  it('asks for the code when the account has two-step sign-in, then finishes', async () => {
    const screen = await open('/sign-in', (sessions) =>
      sessions.signInCalls.answer({
        ok: true,
        status: 200,
        value: { ...VIEW, stage: 'second-factor' },
      }),
    );
    type(screen, 'Email address', 'cook@example.test');
    type(screen, 'Password', PHRASE);
    await submit(screen);

    expect(heading(screen)).toBe('Enter your sign-in code');
    expect(control(screen, 'Sign-in code').autocomplete).toBe('one-time-code');
    expect(await axeViolations(screen)).toEqual([]);

    type(screen, 'Sign-in code', '123456');
    await submit(screen);

    expect(screen.sessions.codeCalls.made).toEqual([['123456']]);
    expect(screen.url()).toBe('/dashboard');
  });

  it('opens at the code step when the guard found a half-finished sign-in', async () => {
    const screen = await open('/sign-in', (sessions) =>
      sessions.session.set({ ...VIEW, stage: 'second-factor' }),
    );
    expect(heading(screen)).toBe('Enter your sign-in code');
  });

  it("says a refused sign-in in the server's words, and clears the password", async () => {
    const screen = await open('/sign-in', (sessions) =>
      sessions.signInCalls.answer(
        refused(400, 'invalid-credentials', "That email address and password don't match."),
      ),
    );
    type(screen, 'Email address', 'cook@example.test');
    type(screen, 'Password', 'wrong');
    await submit(screen);

    expect(notices(screen)).toEqual(["Error: That email address and password don't match."]);
    expect(control(screen, 'Password').value).toBe('');
    expect(control(screen, 'Email address').value).toBe('cook@example.test');
    expect(screen.url()).toBe('/sign-in');
  });

  it('puts a field the server named under that field', async () => {
    const screen = await open('/sign-in', (sessions) =>
      sessions.signInCalls.answer(
        refused(400, 'validation-failed', 'Check the highlighted fields.', {
          email: ['Enter an email address.'],
        }),
      ),
    );
    await submit(screen);

    expect(fieldError(screen, 'Email address')).toBe('Error: Enter an email address.');
    expect(notices(screen)).toEqual([]);
    expect(await axeViolations(screen)).toEqual([]);
  });

  it('says how long a lockout lasts, as the server words it', async () => {
    const screen = await open('/sign-in', (sessions) =>
      sessions.signInCalls.answer(
        refused(429, 'too-many-attempts', 'Too many tries. Try again in 15 minutes.'),
      ),
    );
    await submit(screen);
    expect(notices(screen)).toEqual(['Error: Too many tries. Try again in 15 minutes.']);
  });

  it('says a wrong or reused code under the code field', async () => {
    const screen = await open('/sign-in', (sessions) => {
      sessions.session.set({ ...VIEW, stage: 'second-factor' });
      sessions.codeCalls.answer(
        refused(400, 'invalid-code', "That code isn't right."),
        refused(400, 'code-already-used', 'That code was already used.'),
      );
    });
    type(screen, 'Sign-in code', '000000');
    await submit(screen);
    expect(fieldError(screen, 'Sign-in code')).toBe("Error: That code isn't right.");

    type(screen, 'Sign-in code', '123456');
    await submit(screen);
    expect(fieldError(screen, 'Sign-in code')).toBe('Error: That code was already used.');
    expect(heading(screen)).toBe('Enter your sign-in code');
  });

  it('starts again when the sign-in can no longer be finished', async () => {
    const screen = await open('/sign-in', (sessions) => {
      sessions.session.set({ ...VIEW, stage: 'second-factor' });
      sessions.codeCalls.answer(
        refused(401, 'sign-in-expired', 'That took too long. Please sign in again.'),
      );
    });
    type(screen, 'Sign-in code', '123456');
    await submit(screen);

    expect(heading(screen)).toBe('Sign in');
    expect(notices(screen)).toEqual(['Error: That took too long. Please sign in again.']);
  });

  it('lets a person leave the code step for another account', async () => {
    const screen = await open('/sign-in', (sessions) =>
      sessions.session.set({ ...VIEW, stage: 'second-factor' }),
    );
    await press(screen, 'Sign in with a different account');

    expect(screen.sessions.signOutCalls.made).toHaveLength(1);
    expect(heading(screen)).toBe('Sign in');
  });

  it.each([
    ['signed-out', "You've been signed out. Sign in again to pick up where you left off."],
    [
      'unavailable',
      "We couldn't check whether you're signed in. If you were, signing in again takes you back.",
    ],
  ])('says why it was sent here (%s)', async (reason, text) => {
    const screen = await open(`/sign-in?reason=${reason}`);
    expect(notices(screen)).toEqual([`Note: ${text}`]);
  });

  it('ignores a reason it does not know', async () => {
    const screen = await open('/sign-in?reason=%3Cscript%3E');
    expect(notices(screen)).toEqual([]);
  });

  it('passes axe and links to the other account screens', async () => {
    const screen = await open('/sign-in');
    const links = [...screen.root().querySelectorAll('a')].map((a) => a.getAttribute('href'));
    expect(links).toEqual(['/', '/forgot-password', '/sign-up']);
    expect(await axeViolations(screen)).toEqual([]);
  });
});

describe('sign up (PP3.8)', () => {
  it('says when sign-up opens while it is closed, and offers no form', async () => {
    const screen = await open('/sign-up');
    expect(heading(screen)).toBe('Sign-up opens at launch');
    expect(screen.root().querySelector('form')).toBeNull();
    expect(screen.root().querySelector('a[href="/sign-in"]')).not.toBeNull();
    expect(await axeViolations(screen)).toEqual([]);
  });

  it('registers and then says to check the inbox, with a way to send the link again', async () => {
    const screen = await open('/sign-up', () => undefined, { publicSignUp: true });
    expect(heading(screen)).toBe('Create your account');
    expect(control(screen, 'Password').autocomplete).toBe('new-password');
    expect(await axeViolations(screen)).toEqual([]);

    type(screen, 'Email address', 'cook@example.test');
    type(screen, 'Password', PHRASE);
    await submit(screen);

    expect(screen.accounts.on('register').made).toEqual([['cook@example.test', PHRASE]]);
    expect(heading(screen)).toBe('Check your email');
    expect(screen.root().textContent).toContain('cook@example.test');

    await press(screen, 'Send the link again');
    expect(screen.accounts.on('resendVerification').made).toEqual([['cook@example.test']]);
    expect(notices(screen)).toEqual(['Success: We sent another link.']);
    expect(await axeViolations(screen)).toEqual([]);
  });

  it("puts the server's field messages under their fields", async () => {
    const screen = await open(
      '/sign-up',
      (_sessions, accounts) =>
        accounts.on('register').answer(
          refused(400, 'validation-failed', 'Check the highlighted fields.', {
            password: ['Use at least 12 characters.'],
          }),
        ),
      { publicSignUp: true },
    );
    await submit(screen);

    expect(fieldError(screen, 'Password')).toBe('Error: Use at least 12 characters.');
    expect(heading(screen)).toBe('Create your account');
  });

  it("says the server's refusal when it closes sign-up under an open form", async () => {
    const screen = await open(
      '/sign-up',
      (_sessions, accounts) =>
        accounts.on('register').answer(refused(403, 'sign-up-closed', 'Sign-up opens at launch.')),
      { publicSignUp: true },
    );
    await submit(screen);
    expect(notices(screen)).toEqual(['Error: Sign-up opens at launch.']);
  });
});

describe('verify email (PP3.8)', () => {
  it('confirms the address as it opens, and takes the link out of the address bar', async () => {
    const screen = await open(`/verify-email#${LINK}`, (_sessions, accounts) =>
      accounts.on('verifyEmail').answer({ ok: true, status: 204, value: undefined }),
    );

    expect(screen.accounts.on('verifyEmail').made).toEqual([[ACCOUNT, 'tok+en/==']]);
    expect(heading(screen)).toBe('Your email address is confirmed');
    expect(TestBed.inject(Location).path(true)).toBe('/verify-email');
    expect(screen.root().querySelector('a[href="/dashboard"]')?.textContent?.trim()).toBe(
      'Go to your dashboard',
    );
    expect(await axeViolations(screen)).toEqual([]);
  });

  it('offers a new link when this one does not work', async () => {
    const screen = await open(`/verify-email#${LINK}`, (_sessions, accounts) =>
      accounts
        .on('verifyEmail')
        .answer(refused(400, 'invalid-verification-link', 'That link has expired.')),
    );
    expect(heading(screen)).toBe("This link doesn't work");

    type(screen, 'Email address', 'cook@example.test');
    await submit(screen);

    expect(screen.accounts.on('resendVerification').made).toEqual([['cook@example.test']]);
    expect(notices(screen)).toEqual([
      "Success: If that address has an account waiting to be confirmed, we've sent it a new link.",
    ]);
    expect(await axeViolations(screen)).toEqual([]);
  });

  it.each([undefined, 'account=nope&token=x', `account=${ACCOUNT}`, 'token=x'])(
    'says a missing or broken link does not work, without asking the server (%s)',
    async (fragment) => {
      const screen = await open(
        fragment === undefined ? '/verify-email' : `/verify-email#${fragment}`,
      );
      expect(heading(screen)).toBe("This link doesn't work");
      expect(screen.accounts.on('verifyEmail').made).toEqual([]);
    },
  );

  it('offers to try again when the confirmation failed for another reason', async () => {
    const screen = await open(`/verify-email#${LINK}`, (_sessions, accounts) =>
      accounts
        .on('verifyEmail')
        .answer(
          refused(
            409,
            'changed-elsewhere',
            'Your account was changed elsewhere. Please try again.',
          ),
          { ok: true, status: 204, value: undefined },
        ),
    );
    expect(heading(screen)).toBe("We couldn't confirm your address");
    expect(notices(screen)).toEqual([
      'Error: Your account was changed elsewhere. Please try again.',
    ]);

    await press(screen, 'Try again');

    expect(screen.accounts.on('verifyEmail').made).toHaveLength(2);
    expect(heading(screen)).toBe('Your email address is confirmed');
  });
});

describe('forgot password (PP3.8)', () => {
  it('sends a link and says what happens without saying whether the address has an account', async () => {
    const screen = await open('/forgot-password');
    expect(heading(screen)).toBe('Reset your password');
    expect(await axeViolations(screen)).toEqual([]);

    type(screen, 'Email address', 'cook@example.test');
    await submit(screen);

    expect(screen.accounts.on('forgot').made).toEqual([['cook@example.test']]);
    expect(heading(screen)).toBe('Check your email');
    expect(screen.root().textContent?.replace(/\s+/g, ' ')).toContain(
      "If an account uses cook@example.test, we've sent it a link",
    );
  });

  it('puts a field the server named under that field', async () => {
    const screen = await open('/forgot-password', (_sessions, accounts) =>
      accounts.on('forgot').answer(
        refused(400, 'validation-failed', 'Check the field.', {
          email: ['Enter an email address.'],
        }),
      ),
    );
    await submit(screen);
    expect(fieldError(screen, 'Email address')).toBe('Error: Enter an email address.');
  });
});

describe('reset password (PP3.8)', () => {
  it('sets the new password with the link, which leaves the address bar', async () => {
    const screen = await open(`/reset-password#${LINK}`);
    expect(TestBed.inject(Location).path(true)).toBe('/reset-password');
    expect(heading(screen)).toBe('Choose a new password');
    expect(control(screen, 'New password').autocomplete).toBe('new-password');
    expect(await axeViolations(screen)).toEqual([]);

    type(screen, 'New password', NEW_PHRASE);
    await submit(screen);

    expect(screen.accounts.on('reset').made).toEqual([[ACCOUNT, 'tok+en/==', NEW_PHRASE]]);
    expect(heading(screen)).toBe('Your password is changed');
    expect(screen.root().querySelector('a[href="/sign-in"]')).not.toBeNull();
  });

  it('offers a new link when the link is used or old', async () => {
    const screen = await open(`/reset-password#${LINK}`, (_sessions, accounts) =>
      accounts.on('reset').answer(refused(400, 'invalid-reset-link', 'That link has expired.')),
    );
    type(screen, 'New password', NEW_PHRASE);
    await submit(screen);

    expect(heading(screen)).toBe("This link doesn't work");
    expect(screen.root().querySelector('a[href="/forgot-password"]')).not.toBeNull();
    expect(await axeViolations(screen)).toEqual([]);
  });

  it('says a refused password under its field, and keeps the form', async () => {
    const screen = await open(`/reset-password#${LINK}`, (_sessions, accounts) =>
      accounts.on('reset').answer(
        refused(400, 'validation-failed', 'Check the field.', {
          newPassword: ['Use at least 12 characters.'],
        }),
      ),
    );
    type(screen, 'New password', 'short');
    await submit(screen);

    expect(fieldError(screen, 'New password')).toBe('Error: Use at least 12 characters.');
    expect(heading(screen)).toBe('Choose a new password');
  });

  it('says a missing link does not work without asking the server', async () => {
    const screen = await open('/reset-password');
    expect(heading(screen)).toBe("This link doesn't work");
    expect(screen.root().querySelector('form')).toBeNull();
  });
});

describe('feedbackFor', () => {
  const problem = (errors?: ApiProblem['errors']): ApiProblem => ({
    status: 400,
    code: 'validation-failed',
    title: 'Check the highlighted fields.',
    ...(errors ? { errors } : {}),
  });

  it('places each named field and shows no notice when every one was placed', () => {
    expect(feedbackFor(problem({ email: ['A', 'B'] }), ['email', 'password'])).toEqual({
      fields: { email: 'A' },
      notice: null,
    });
  });

  it('shows the title when the server named nothing, or a field the form does not have', () => {
    expect(feedbackFor(problem(), ['email'])).toEqual({
      fields: {},
      notice: 'Check the highlighted fields.',
    });
    expect(feedbackFor(problem({ email: ['A'], plan: ['B'] }), ['email'])).toEqual({
      fields: { email: 'A' },
      notice: 'Check the highlighted fields.',
    });
  });
});

describe('readAccountLink', () => {
  it('reads the account and token as the router hands the fragment over, decoded once', () => {
    expect(readAccountLink(`account=${ACCOUNT}&token=tok+en/==`)).toEqual({
      accountId: ACCOUNT,
      token: 'tok+en/==',
    });
  });

  it('keeps a plus sign in a token, never reading it as a space', () => {
    expect(readAccountLink(`token=a+b+c&account=${ACCOUNT}`)?.token).toBe('a+b+c');
  });

  it.each([null, '', 'account=&token=x', `account=${ACCOUNT}&token=`, 'account=1&token=x', 'x'])(
    'reads no link from %s',
    (fragment) => expect(readAccountLink(fragment)).toBeNull(),
  );
});
