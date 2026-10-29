import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { SESSION_ENDED_CODE, SessionClient } from './session-client';

/** Where a signed-in visitor belongs, and the only prefix a `next` address may have. */
export const DASHBOARD = '/dashboard';

/** Why the sign-in screen was sent to, said on it in words. */
export type SignInReason = 'signed-out' | 'busy' | 'unavailable';

/**
 * The dashboard's door (PP3.8, ADR 0019). It asks the gateway each time the dashboard is entered, because the cookie
 * alone is the gateway's to read: the gateway's own redirect at '/' trusts it, and this is where the API's word is
 * heard. Every refusal goes to /sign-in - never '/' or '/pricing', which the gateway would send straight back here
 * while the cookie lasts - so an API that is down cannot make the two redirects chase each other.
 */
export const signedInGuard: CanActivateFn = async (_route, state) => {
  const router = inject(Router);
  const result = await inject(SessionClient).current();
  if (result.ok && result.value.stage === 'complete') {
    return true;
  }

  const reason: SignInReason | null = result.ok
    ? null
    : result.problem.code === SESSION_ENDED_CODE
      ? 'signed-out'
      : result.problem.status === 401
        ? null
        : result.problem.status === 429
          ? 'busy'
          : 'unavailable';
  return signInFor(router, state.url, reason);
};

/**
 * Sign-in and sign-up are for someone who is not signed in: a complete session goes to the dashboard. When the
 * gateway cannot say, the screen is shown - a person can always try to sign in.
 */
export const signedOutGuard: CanActivateFn = async () => {
  const router = inject(Router);
  const result = await inject(SessionClient).current();
  return result.ok && result.value.stage === 'complete' ? router.parseUrl(DASHBOARD) : true;
};

/**
 * Where to go after signing in: the dashboard address the visitor was turned away from, or the dashboard itself. Only
 * an address under /dashboard is taken, so a crafted link cannot send a person anywhere else once they sign in.
 */
export function dashboardAddress(next: string | null | undefined): string {
  if (
    typeof next !== 'string' ||
    !/^\/dashboard(?:[/?#]|$)/.test(next) ||
    /\/\/|\\|\/\.\.?(?:[/?#]|$)/.test(next)
  ) {
    return DASHBOARD;
  }
  return next;
}

function signInFor(router: Router, url: string, reason: SignInReason | null): UrlTree {
  const next = dashboardAddress(url);
  return router.createUrlTree(['/sign-in'], {
    queryParams: {
      ...(next === DASHBOARD ? {} : { next }),
      ...(reason === null ? {} : { reason }),
    },
  });
}
