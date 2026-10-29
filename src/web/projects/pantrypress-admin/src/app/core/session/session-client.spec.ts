import { TestBed } from '@angular/core/testing';
import { ApiClient, ApiWriteOptions } from '../api/api-client';
import { ApiPath } from '../api/api-path';
import { ApiResult } from '../api/api-problem';
import { AccountClient } from './account-client';
import { readSessionView, SessionClient } from './session-client';

// The clients are tested over a stand-in ApiClient: how a call reaches the BFF - the base path, the antiforgery
// token, problem reading - is ApiClient's own spec, and a lint rule keeps HTTP testing inside core/api/.

const VIEW = {
  stage: 'complete',
  accountId: '7f1f5c9a-1d2b-4c3d-9e4f-5a6b7c8d9e0f',
  email: 'cook@example.test',
  emailVerified: true,
  secondFactorVerified: false,
};

class RecordingApi {
  readonly calls: { method: string; path: ApiPath; body?: unknown }[] = [];
  private readonly answers: ApiResult<unknown>[] = [];

  answer(...answers: ApiResult<unknown>[]): void {
    this.answers.push(...answers);
  }

  get = (path: ApiPath) => this.record('GET', path);
  post = (path: ApiPath, options: ApiWriteOptions = {}) => this.record('POST', path, options.body);
  delete = (path: ApiPath) => this.record('DELETE', path);

  private record(method: string, path: ApiPath, body?: unknown): Promise<ApiResult<unknown>> {
    this.calls.push(body === undefined ? { method, path } : { method, path, body });
    return Promise.resolve(this.answers.shift() ?? { ok: true, status: 200, value: undefined });
  }
}

function refused(status: number, code: string): ApiResult<unknown> {
  return { ok: false, problem: { status, code, title: 'A plain title.' } };
}

function setUp() {
  const api = new RecordingApi();
  TestBed.configureTestingModule({ providers: [{ provide: ApiClient, useValue: api }] });
  return { api, sessions: TestBed.inject(SessionClient), accounts: TestBed.inject(AccountClient) };
}

describe('SessionClient', () => {
  it('reads the current session from the gateway and remembers it', async () => {
    const { api, sessions } = setUp();
    api.answer({ ok: true, status: 200, value: VIEW });

    expect(await sessions.current()).toEqual({ ok: true, status: 200, value: VIEW });
    expect(api.calls).toEqual([{ method: 'GET', path: 'account/session' }]);
    expect(sessions.session()).toEqual(VIEW);
  });

  it('forgets the session when the gateway says there is none', async () => {
    const { api, sessions } = setUp();
    api.answer({ ok: true, status: 200, value: VIEW }, refused(401, 'session-ended'));
    await sessions.current();

    const result = await sessions.current();

    expect(result.ok ? null : result.problem.code).toBe('session-ended');
    expect(sessions.session()).toBeNull();
  });

  it('keeps what it knew when the gateway cannot be asked', async () => {
    const { api, sessions } = setUp();
    api.answer({ ok: true, status: 200, value: VIEW }, refused(502, 'unexpected-error'));
    await sessions.current();
    await sessions.current();

    expect(sessions.session()).toEqual(VIEW);
  });

  it('treats an answer that is not a session view as a failure, never as signed in', async () => {
    const { api, sessions } = setUp();
    api.answer({ ok: true, status: 200, value: { ...VIEW, stage: 'nearly' } });

    const result = await sessions.current();

    expect(result.ok ? null : result.problem.code).toBe('unexpected-error');
    expect(sessions.session()).toBeNull();
  });

  it('signs in with the email and password, and nothing else', async () => {
    const { api, sessions } = setUp();
    api.answer({ ok: true, status: 200, value: { ...VIEW, stage: 'second-factor' } });

    const result = await sessions.signIn('cook@example.test', 'a long kitchen phrase');

    expect(api.calls).toEqual([
      {
        method: 'POST',
        path: 'account/session',
        body: { email: 'cook@example.test', password: 'a long kitchen phrase' },
      },
    ]);
    expect(result.ok && result.value.stage).toBe('second-factor');
    expect(sessions.session()?.stage).toBe('second-factor');
  });

  it('sends the second step to its own route', async () => {
    const { api, sessions } = setUp();
    api.answer({ ok: true, status: 200, value: VIEW });

    expect((await sessions.completeSecondStep('123456')).ok).toBe(true);
    expect(api.calls).toEqual([
      { method: 'POST', path: 'account/session/second-factor', body: { code: '123456' } },
    ]);
  });

  it('signs out at the gateway and forgets the session once the gateway says it is gone', async () => {
    const { api, sessions } = setUp();
    api.answer({ ok: true, status: 200, value: VIEW }, { ok: true, status: 204, value: undefined });
    await sessions.current();

    expect((await sessions.signOut()).ok).toBe(true);
    expect(api.calls.at(-1)).toEqual({ method: 'DELETE', path: 'account/session' });
    expect(sessions.session()).toBeNull();
  });

  it('keeps the session when the gateway refused the sign-out before it ran, and says why', async () => {
    const { api, sessions } = setUp();
    api.answer({ ok: true, status: 200, value: VIEW }, refused(429, 'too-many-requests'));
    await sessions.current();

    const result = await sessions.signOut();

    expect(result.ok ? null : result.problem.code).toBe('too-many-requests');
    expect(sessions.session()).toEqual(VIEW);
  });

  it('forgets a half-finished sign-in the gateway has dropped as expired', async () => {
    const { api, sessions } = setUp();
    api.answer(
      { ok: true, status: 200, value: { ...VIEW, stage: 'second-factor' } },
      refused(400, 'sign-in-expired'),
    );
    await sessions.current();

    await sessions.completeSecondStep('123456');

    expect(sessions.session()).toBeNull();
  });
});

describe('AccountClient', () => {
  it.each([
    [
      (a: AccountClient) => a.register('a@b.test', 'p'),
      'account/register',
      { email: 'a@b.test', password: 'p' },
    ],
    [
      (a: AccountClient) => a.verifyEmail('id', 'tok+en/=='),
      'account/email/verify',
      { accountId: 'id', token: 'tok+en/==' },
    ],
    [
      (a: AccountClient) => a.resendVerification('a@b.test'),
      'account/email/resend',
      { email: 'a@b.test' },
    ],
    [
      (a: AccountClient) => a.forgotPassword('a@b.test'),
      'account/password/forgot',
      { email: 'a@b.test' },
    ],
    [
      (a: AccountClient) => a.resetPassword('id', 'tok', 'new one'),
      'account/password/reset',
      { accountId: 'id', token: 'tok', newPassword: 'new one' },
    ],
  ])('posts its body to %s', async (call, path, body) => {
    const { api, accounts } = setUp();

    expect((await call(accounts)).ok).toBe(true);
    expect(api.calls).toEqual([{ method: 'POST', path, body }]);
  });
});

describe('readSessionView', () => {
  it('takes the gateway view and nothing less', () => {
    expect(readSessionView(VIEW)).toEqual(VIEW);
    for (const broken of [
      null,
      'complete',
      { ...VIEW, stage: 'done' },
      { ...VIEW, accountId: 7 },
      { ...VIEW, email: undefined },
      { ...VIEW, emailVerified: 'yes' },
      { ...VIEW, secondFactorVerified: undefined },
    ]) {
      expect(readSessionView(broken)).toBeNull();
    }
  });

  it('keeps no field the gateway did not promise', () => {
    expect(readSessionView({ ...VIEW, securityStamp: 'never' })).toEqual(VIEW);
  });
});
