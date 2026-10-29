import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { ApiProblem } from '../api/api-problem';
import { JOIN_PATH, PendingInvitation } from '../sites/pending-invitation';
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

  return signInFor(router, state.url, result.ok ? null : result.problem);
};

/**
 * Keeps an invitation link's token (PP3.9) before the dashboard's door can send the visitor to sign in, which carries
 * no fragment on. It sits on a parent of the dashboard's route, because a parent's guards finish before a child's
 * start, while guards on one route run together.
 */
export const keepInvitationLink: CanActivateFn = (_route, state) => {
  if (pathOf(state.url) === JOIN_PATH) {
    inject(PendingInvitation).keep(state.root.fragment);
  }
  return true;
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

/**
 * Where a dashboard screen sends a visitor the gateway turned away, with the reason said on the sign-in screen: the
 * guard's refusals, and a screen's own 401 (`session-ended`) mid-task. `next` keeps the address under /dashboard but
 * never its fragment: a fragment carries an emailed link's secret, and in a query string a reload would send it to the
 * gateway and keep it in history.
 */
export function signInFor(router: Router, url: string, problem: ApiProblem | null): UrlTree {
  const next = dashboardAddress(url.split('#')[0]);
  const reason = signInReason(problem);
  return router.createUrlTree(['/sign-in'], {
    queryParams: {
      ...(next === DASHBOARD ? {} : { next }),
      ...(reason === null ? {} : { reason }),
    },
  });
}

/** Why the gateway turned a visitor away, in the sign-in screen's terms; a plain 401 is no session, and says nothing. */
function signInReason(problem: ApiProblem | null): SignInReason | null {
  if (problem === null) {
    return null;
  }
  if (problem.code === SESSION_ENDED_CODE) {
    return 'signed-out';
  }
  if (problem.status === 401) {
    return null;
  }
  return problem.status === 429 ? 'busy' : 'unavailable';
}

function pathOf(url: string): string {
  return url.split(/[?#]/)[0];
}
