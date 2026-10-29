import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  ElementRef,
  inject,
} from '@angular/core';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, RouterLink, RouterOutlet } from '@angular/router';
import { PpButton, PpNotice, PpPageHeader, PpProgress } from '@pantrypress/ui';
import { DashboardSites } from './dashboard-sites';

/**
 * Everything under `/dashboard/sites/{siteSlug}` (PP3.9). It hands the route's slug to `DashboardSites` and draws the
 * site's screens only once the API has answered for it, so a screen never shows a site the caller may not have. No such
 * site and one the caller is not part of are one answer (tenancy.md rule 4), and the words say neither.
 */
@Component({
  selector: 'ppa-site-area',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpButton, PpNotice, PpPageHeader, PpProgress, RouterLink, RouterOutlet],
  template: `
    @let state = sites.current();
    @switch (state.kind) {
      @case ('ready') {
        <router-outlet />
      }
      @case ('not-found') {
        <pp-page-header
          heading="We couldn't find that site"
          description="Check the address, or choose one of your sites."
        />
        <a pp-button variant="primary" routerLink="/dashboard">Go to your sites</a>
      }
      @case ('failed') {
        <pp-page-header heading="We couldn't open this site" />
        <pp-notice tone="danger" announce>
          <p class="text">{{ state.problem.title }}</p>
          <button pp-button size="sm" (click)="retry()">Try again</button>
        </pp-notice>
      }
      @default {
        <pp-progress label="Opening your site" />
      }
    }
  `,
  styles: `
    .text {
      margin: 0 0 var(--space-3);
    }
  `,
})
export class SiteArea {
  protected readonly sites = inject(DashboardSites);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  constructor() {
    // The route can only say "Your site"; a refusal says what the page is instead.
    const title = inject(Title);
    effect(() => {
      const kind = this.sites.current().kind;
      if (kind === 'not-found') {
        title.setTitle('Site not found - PantryPress');
      } else if (kind === 'failed') {
        title.setTitle("Site couldn't be opened - PantryPress");
      }
    });
    const subscription = inject(ActivatedRoute).paramMap.subscribe((params) =>
      this.sites.select(params.get('siteSlug')),
    );
    inject(DestroyRef).onDestroy(() => {
      subscription.unsubscribe();
      this.sites.select(null);
    });
  }

  /** Try again takes its own button away, so focus waits on the main area and the shell moves it on to the heading. */
  protected retry(): void {
    this.host.closest<HTMLElement>('main')?.focus();
    this.sites.focusParked = true;
    this.sites.reload();
  }
}
