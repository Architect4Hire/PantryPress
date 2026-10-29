import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { PpCard, PpPageHeader, PpStatusPill } from '@pantrypress/ui';
import { ROLE_WORDS, STATUS_WORDS } from '../../core/sites/sites-client';
import { DashboardSites } from './dashboard-sites';
import { statusTone } from './site-status';

/**
 * A site's own page in the dashboard (PP3.9): its name, address, the caller's role, and its status. PP10.2 makes it the
 * site's overview. It is drawn only once the site area has the site.
 */
@Component({
  selector: 'ppa-site-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpCard, PpPageHeader, PpStatusPill],
  template: `
    @if (site(); as site) {
      <pp-page-header [heading]="site.name" [description]="site.tagline || undefined" />
      <pp-card>
        <h2 class="heading">About this site</h2>
        <dl class="facts">
          <div>
            <dt>Address</dt>
            <dd>{{ site.host }}</dd>
          </div>
          <div>
            <dt>Your role</dt>
            <dd>{{ roles[site.role] }}</dd>
          </div>
          <div>
            <dt>Status</dt>
            <dd>
              <pp-status-pill [tone]="tone(site.status)">{{
                statuses[site.status]
              }}</pp-status-pill>
            </dd>
          </div>
        </dl>
      </pp-card>
    }
  `,
  styles: `
    .heading {
      margin: 0 0 var(--space-4);
      font-size: var(--text-4);
    }
    .facts {
      display: grid;
      gap: var(--space-3);
      margin: 0;
    }
    .facts div {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-1) var(--space-3);
      align-items: center;
    }
    dt {
      min-inline-size: 7rem;
      color: var(--muted);
    }
    dd {
      margin: 0;
      overflow-wrap: anywhere;
    }
  `,
})
export class SiteHome {
  private readonly sites = inject(DashboardSites);
  protected readonly roles = ROLE_WORDS;
  protected readonly statuses = STATUS_WORDS;
  protected readonly tone = statusTone;
  protected readonly site = computed(() => {
    const current = this.sites.current();
    return current.kind === 'ready' ? current.site : null;
  });

  constructor() {
    // The route can only say "Your site"; once the site is known, the tab says which.
    const title = inject(Title);
    effect(() => {
      const site = this.site();
      if (site) {
        title.setTitle(`${site.name} - PantryPress`);
      }
    });
  }
}
