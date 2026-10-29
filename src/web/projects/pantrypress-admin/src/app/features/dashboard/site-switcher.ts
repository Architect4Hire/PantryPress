import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { PpDropdown, PpDropdownItem } from '@pantrypress/ui';
import { filter, map } from 'rxjs';
import { SiteSummary } from '../../core/sites/sites-client';
import { DashboardSites } from './dashboard-sites';

/**
 * Chooses which site the dashboard works on (PP3.9, PP-D19) by going to its address: the site is the route's, never a
 * setting kept anywhere else. Moving to another site keeps the part of the dashboard the reader is in. Each choice is a
 * link, so nothing moves until the reader picks one (3.2.2), and the list ends with every site, which also pages
 * through more than the first page holds.
 */
@Component({
  selector: 'ppa-site-switcher',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpDropdown, PpDropdownItem, RouterLink],
  template: `
    <pp-dropdown prefix="Site" [label]="label()" [caption]="caption() ?? undefined">
      @for (site of items(); track site.slug) {
        <a
          pp-dropdown-item
          [routerLink]="linkFor(site)"
          [attr.aria-current]="site.slug === currentSlug() ? 'true' : null"
        >
          {{ site.name }}
        </a>
      }
      <a pp-dropdown-item routerLink="/dashboard">All your sites</a>
    </pp-dropdown>
  `,
  // In the header's row it gives way to a long site name, cut short with an ellipsis, rather than widen the page.
  styles: `
    :host {
      display: block;
      min-inline-size: 0;
      max-inline-size: 100%;
    }
  `,
})
export class SiteSwitcher {
  private readonly sites = inject(DashboardSites);
  private readonly router = inject(Router);
  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected readonly items = computed(() => {
    const mine = this.sites.mine();
    return mine.kind === 'ready' ? mine.page.items : [];
  });

  protected readonly currentSlug = computed(() => {
    const current = this.sites.current();
    return current.kind === 'none' ? null : current.slug;
  });

  protected readonly label = computed(() => {
    const current = this.sites.current();
    if (current.kind === 'ready') {
      return current.site.name;
    }
    const listed = this.items().find((site) => site.slug === this.currentSlug());
    return listed?.name ?? 'Choose a site';
  });

  protected readonly caption = computed(() => {
    const mine = this.sites.mine();
    if (mine.kind === 'loading') {
      return 'Loading your sites…';
    }
    if (mine.kind === 'failed') {
      return "We couldn't load your sites here. Open all your sites to try again.";
    }
    return mine.page.items.length === 0 ? "You aren't part of any sites yet." : null;
  });

  // The path after `/dashboard/sites/{slug}`, kept when moving to another site.
  private readonly section = computed(() => {
    const segments =
      this.router.parseUrl(this.url()).root.children['primary']?.segments.map((s) => s.path) ?? [];
    return segments[0] === 'dashboard' && segments[1] === 'sites' ? segments.slice(3) : [];
  });

  protected linkFor(site: SiteSummary): readonly string[] {
    return ['/dashboard/sites', site.slug, ...this.section()];
  }
}
