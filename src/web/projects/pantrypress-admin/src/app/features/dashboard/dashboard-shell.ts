import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injector,
  viewChild,
} from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AccountMenu } from './account-menu';
import { DashboardSites } from './dashboard-sites';
import { SITE_SECTIONS } from './site-sections';
import { SiteSwitcher } from './site-switcher';

/**
 * The dashboard's frame (PP3.9, PP-D19): a skip link, a header with the product name, the site switcher, and the
 * account menu, the navigation, and the one `<main>` the screens draw into. Each screen brings the page's one `h1`.
 * The site comes from the route alone (`DashboardSites`).
 *
 * - After each move inside the dashboard, focus goes to the new screen's heading, so a screen reader hears where it
 *   is; the first screen a document opens on keeps the browser's own start. A site's screen has no heading until the
 *   API has answered for the site, so focus waits on the main area and goes on to the heading once it is drawn.
 * - Nothing in it is fixed or sticky, so nothing can cover a focused control (2.4.11) and no scroll padding is owed.
 * - It draws no toast region: the app's one is in `app.html` (PP3.8).
 */
@Component({
  selector: 'ppa-dashboard-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AccountMenu, RouterLink, RouterLinkActive, RouterOutlet, SiteSwitcher],
  providers: [DashboardSites],
  templateUrl: './dashboard-shell.html',
  styleUrl: './dashboard-shell.css',
})
export class DashboardShell {
  protected readonly sites = inject(DashboardSites);
  protected readonly sections = SITE_SECTIONS;
  protected readonly site = computed(() => {
    const current = this.sites.current();
    return current.kind === 'ready' ? current.site : null;
  });

  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');
  private readonly injector = inject(Injector);

  constructor() {
    void this.sites.refreshMine();
    const router = inject(Router);
    const subscription = router.events.subscribe((event) => {
      // Navigation 1 is the document's own first; every later one is a move the reader made.
      if (event instanceof NavigationEnd && event.id > 1) {
        afterNextRender(() => this.focusScreen(), { injector: this.injector });
      }
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
    effect(() => {
      if (this.sites.current().kind !== 'loading' && this.sites.focusParked) {
        afterNextRender(
          () => {
            if (
              this.sites.focusParked &&
              this.main().nativeElement.ownerDocument.activeElement === this.main().nativeElement
            ) {
              this.focusScreen();
            }
          },
          { injector: this.injector },
        );
      }
    });
  }

  /** The skip link moves focus without touching the address, which would be another navigation. */
  protected skip(event: Event): void {
    event.preventDefault();
    this.focusScreen();
  }

  private focusScreen(): void {
    const main = this.main().nativeElement;
    const heading = main.querySelector<HTMLElement>('h1');
    this.sites.focusParked = heading === null;
    if (heading) {
      heading.tabIndex = -1;
      heading.focus();
    } else {
      main.focus();
    }
  }
}
