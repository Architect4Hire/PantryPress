import { inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ApiProblem } from '../../core/api/api-problem';
import { signInFor } from '../../core/session/session-guards';
import { MAX_PAGE, SiteDetail, SitePage, SitesClient } from '../../core/sites/sites-client';

/** The site the dashboard is working on: the route's `siteSlug`, as the API answered for it. */
export type CurrentSite =
  | { readonly kind: 'none' }
  | { readonly kind: 'loading'; readonly slug: string }
  | { readonly kind: 'ready'; readonly slug: string; readonly site: SiteDetail }
  | { readonly kind: 'not-found'; readonly slug: string }
  | { readonly kind: 'failed'; readonly slug: string; readonly problem: ApiProblem };

/** The first page of the caller's sites, for the switcher. */
export type MySites =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly page: SitePage }
  | { readonly kind: 'failed'; readonly problem: ApiProblem };

/**
 * What the dashboard's shell knows about sites (PP3.9), provided by the shell so it lives as long as the dashboard is
 * open. The site comes only from the route: the site area hands this its `siteSlug`, and the API decides whether the
 * caller may have it (auth.md) - no such site and no membership are one answer, `not-found`. Nothing here chooses a
 * site any other way, and nothing is sent but the slug (tenancy.md rule 7).
 *
 * A 401 on the way is a session that ended since the dashboard's door let the visitor in, and goes to sign in.
 */
@Injectable()
export class DashboardSites {
  private readonly sites = inject(SitesClient);
  private readonly router = inject(Router);
  private readonly currentState = signal<CurrentSite>({ kind: 'none' });
  private readonly mineState = signal<MySites>({ kind: 'loading' });
  private asked = 0;

  /**
   * Focus waits on the shell's main area for a site screen that is still loading; the shell moves it on to the heading
   * once the site has been answered for (PP3.9).
   */
  focusParked = false;

  readonly current = this.currentState.asReadonly();
  readonly mine = this.mineState.asReadonly();

  /** Works on the site with this slug, or on none. The same slug again asks nothing, unless it failed. */
  select(slug: string | null): void {
    const now = this.currentState();
    if (slug === null) {
      this.asked += 1;
      this.currentState.set({ kind: 'none' });
      return;
    }
    if (now.kind !== 'none' && now.kind !== 'failed' && now.slug === slug) {
      return;
    }
    void this.load(slug);
  }

  /** Asks for the current site again, after a failure. */
  reload(): void {
    const now = this.currentState();
    if (now.kind !== 'none') {
      void this.load(now.slug);
    }
  }

  /** Reads the caller's first page of sites again: after joining one, or after a failure. */
  async refreshMine(): Promise<void> {
    const result = await this.sites.list({ limit: MAX_PAGE });
    if (result.ok) {
      this.mineState.set({ kind: 'ready', page: result.value });
    } else if (!(await this.signedOut(result.problem, '/dashboard'))) {
      this.mineState.set({ kind: 'failed', problem: result.problem });
    }
  }

  private async load(slug: string): Promise<void> {
    const ask = ++this.asked;
    this.currentState.set({ kind: 'loading', slug });
    const result = await this.sites.get(slug);
    // A later choice wins: an answer for a site the reader has moved on from is dropped.
    if (ask !== this.asked) {
      return;
    }
    if (result.ok) {
      this.currentState.set({ kind: 'ready', slug, site: result.value });
    } else if (result.problem.status === 404) {
      this.currentState.set({ kind: 'not-found', slug });
    } else if (!(await this.signedOut(result.problem, this.siteAddress(slug)))) {
      this.currentState.set({ kind: 'failed', slug, problem: result.problem });
    }
  }

  // The slug is the route's, so it is written into an address the router's way, one encoded segment.
  private siteAddress(slug: string): string {
    return this.router.serializeUrl(this.router.createUrlTree(['/dashboard/sites', slug]));
  }

  private async signedOut(problem: ApiProblem, next: string): Promise<boolean> {
    if (problem.status !== 401) {
      return false;
    }
    await this.router.navigateByUrl(signInFor(this.router, next, problem));
    return true;
  }
}
