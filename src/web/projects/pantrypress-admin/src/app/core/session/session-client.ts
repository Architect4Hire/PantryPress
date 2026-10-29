import { inject, Injectable, signal } from '@angular/core';
import { ApiClient } from '../api/api-client';
import { toApiPath } from '../api/api-path';
import { ApiResult, isRecord, UNEXPECTED_CODE, UNEXPECTED_TITLE } from '../api/api-problem';

/** How far along a sign-in is, as the gateway says it (`SessionStage`, kebab-case). */
export type SessionStage = 'complete' | 'second-factor';

/**
 * What the admin app is told about its session (the gateway's `SessionView`): never the security stamp, and never a
 * token (ACCT-002). `second-factor` is a sign-in that still owes a code; only `complete` may act.
 */
export interface SessionView {
  readonly stage: SessionStage;
  readonly accountId: string;
  readonly email: string;
  readonly emailVerified: boolean;
  readonly secondFactorVerified: boolean;
}

/** The gateway's answer to a session that is over (PP1.12): the app says "you were signed out", not just "sign in". */
export const SESSION_ENDED_CODE = 'session-ended';

const SESSION = toApiPath('account/session');
const SECOND_FACTOR = toApiPath('account/session/second-factor');

/**
 * The BFF session (PP3.8): the gateway's own four routes, which hold the cookie and never hand the browser a token
 * (PP1.6, gateway.md). It keeps the last answer in `session`, so a screen the guard let in knows the stage without
 * asking again. The guard asks the gateway each time the dashboard is entered; nothing here decides who is signed in.
 */
@Injectable({ providedIn: 'root' })
export class SessionClient {
  private readonly api = inject(ApiClient);
  private readonly last = signal<SessionView | null>(null);

  /** The last session the gateway reported, or null when it said there is none (or has not been asked). */
  readonly session = this.last.asReadonly();

  /** What the gateway says now. 401 is no session; `session-ended` is one that was ended elsewhere. */
  async current(): Promise<ApiResult<SessionView>> {
    return this.remember(await this.api.get<unknown>(SESSION));
  }

  /** The first step. A `second-factor` answer owes a code; nothing else is sent until it is given. */
  async signIn(email: string, password: string): Promise<ApiResult<SessionView>> {
    return this.remember(await this.api.post<unknown>(SESSION, { body: { email, password } }));
  }

  /** The second step: a code from the authenticator app, or one recovery code. */
  async completeSecondStep(code: string): Promise<ApiResult<SessionView>> {
    return this.remember(await this.api.post<unknown>(SECOND_FACTOR, { body: { code } }));
  }

  /** Ends the session at the API as well as in this browser; the gateway clears its cookies even if the API is down. */
  async signOut(): Promise<ApiResult<void>> {
    const result = await this.api.delete<void>(SESSION);
    this.last.set(null);
    return result;
  }

  private remember(result: ApiResult<unknown>): ApiResult<SessionView> {
    if (!result.ok) {
      if (result.problem.status === 401) {
        this.last.set(null);
      }
      return result;
    }
    const view = readSessionView(result.value);
    if (view === null) {
      return {
        ok: false,
        problem: { status: result.status, code: UNEXPECTED_CODE, title: UNEXPECTED_TITLE },
      };
    }
    this.last.set(view);
    return { ok: true, status: result.status, value: view };
  }
}

/** The gateway's `SessionView`, checked field by field; anything else is not one. */
export function readSessionView(body: unknown): SessionView | null {
  if (
    !isRecord(body) ||
    (body['stage'] !== 'complete' && body['stage'] !== 'second-factor') ||
    typeof body['accountId'] !== 'string' ||
    typeof body['email'] !== 'string' ||
    typeof body['emailVerified'] !== 'boolean' ||
    typeof body['secondFactorVerified'] !== 'boolean'
  ) {
    return null;
  }
  return {
    stage: body['stage'],
    accountId: body['accountId'],
    email: body['email'],
    emailVerified: body['emailVerified'],
    secondFactorVerified: body['secondFactorVerified'],
  };
}
