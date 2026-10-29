import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  PpButton,
  PpEmptyState,
  PpList,
  PpListStatus,
  PpNotice,
  PpPageHeader,
  PpPageTurn,
  PpStatusPill,
} from '@pantrypress/ui';
import { signInFor } from '../../core/session/session-guards';
import { PendingInvitation } from '../../core/sites/pending-invitation';
import { ROLE_WORDS, SiteSummary, SitesClient, STATUS_WORDS } from '../../core/sites/sites-client';
import { statusTone } from './site-status';

/** How many sites a page of the list holds. */
const PAGE_SIZE = 20;

/**
 * Where the dashboard opens (PP3.9): the caller's sites, a page at a time, each leading to its own dashboard. An
 * invitation waiting in this browser - one opened before signing up, say, and confirmed in another tab - is offered
 * first, since accepting it is what brings a new member their site.
 */
@Component({
  selector: 'ppa-dashboard-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpButton, PpEmptyState, PpList, PpNotice, PpPageHeader, PpStatusPill, RouterLink],
  template: `
    <pp-page-header heading="Your sites" description="Choose a site to work on." />
    @if (invitation()) {
      <pp-notice class="invitation" tone="info" heading="You have an invitation waiting">
        <p class="text">Someone has invited you to help run their site.</p>
        <a pp-button variant="primary" size="sm" routerLink="/dashboard/join">
          Accept the invitation
        </a>
      </pp-notice>
    }
    <pp-list
      label="Sites"
      loadingText="Loading your sites"
      errorText="Your sites couldn't be loaded. Check your connection and try again."
      [status]="status()"
      [empty]="empty()"
      [page]="page()"
      [hasPrevious]="page() > 1"
      [hasNext]="next() !== null"
      [resultsText]="resultsText()"
      (pageChange)="turn($event)"
      (retry)="retry()"
    >
      <ul class="sites">
        @for (site of items(); track site.slug) {
          <li class="site">
            <a class="name" [routerLink]="['/dashboard/sites', site.slug]">{{ site.name }}</a>
            <span class="host">{{ site.host }}</span>
            <span class="meta">
              {{ roles[site.role] }}
              <pp-status-pill [tone]="tone(site.status)">{{
                statuses[site.status]
              }}</pp-status-pill>
            </span>
          </li>
        }
      </ul>
      <pp-empty-state
        ppListEmpty
        heading="No sites yet"
        description="Starting a site of your own from here is coming soon. If someone invites you to help run theirs, open the link in their email and it appears here."
      />
    </pp-list>
  `,
  styleUrl: './dashboard-home.css',
})
export class DashboardHome {
  private readonly sites = inject(SitesClient);
  private readonly router = inject(Router);
  protected readonly invitation = inject(PendingInvitation).link;
  protected readonly roles = ROLE_WORDS;
  protected readonly statuses = STATUS_WORDS;
  protected readonly tone = statusTone;

  protected readonly status = signal<PpListStatus>('loading');
  protected readonly items = signal<readonly SiteSummary[]>([]);
  protected readonly page = signal(1);
  protected readonly next = signal<string | null>(null);
  protected readonly empty = computed(
    () => this.status() === 'ready' && this.page() === 1 && this.items().length === 0,
  );
  protected readonly resultsText = computed(() => `Page ${this.page()} of your sites`);

  // The cursor each page was asked with; the first page has none.
  private cursors: (string | undefined)[] = [undefined];
  // The page last asked for, which Try again asks for again: a failed turn leaves `page` where it was.
  private asked = 1;

  constructor() {
    void this.load(1);
  }

  protected turn(turn: PpPageTurn): void {
    const page = this.page() + (turn === 'next' ? 1 : -1);
    if (turn === 'next') {
      this.cursors[page - 1] = this.next() ?? undefined;
    }
    void this.load(page);
  }

  protected retry(): void {
    void this.load(this.asked);
  }

  private async load(page: number): Promise<void> {
    this.asked = page;
    this.status.set('loading');
    const result = await this.sites.list({ limit: PAGE_SIZE, cursor: this.cursors[page - 1] });
    if (result.ok) {
      this.cursors = this.cursors.slice(0, page);
      this.items.set(result.value.items);
      this.next.set(result.value.nextCursor);
      this.page.set(page);
      this.status.set('ready');
    } else if (result.problem.status === 401) {
      await this.router.navigateByUrl(signInFor(this.router, '/dashboard', result.problem));
    } else if (result.problem.status === 400 && page > 1) {
      // A cursor the API no longer takes: start again from the first page rather than ask with it again.
      this.cursors = [undefined];
      void this.load(1);
    } else {
      this.status.set('error');
    }
  }
}
