import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import {
  NavigationCancel,
  NavigationEnd,
  NavigationError,
  NavigationStart,
  Router,
  RouterOutlet,
} from '@angular/router';
import { PpProgress, PpToastRegion } from '@pantrypress/ui';

/**
 * The app's shell: the router outlet, the one toast region (PP3.8), and a progress bar while the router waits - for
 * the gateway's word on the session before the dashboard opens (PP3.9), or a screen's code - so a slow first load is
 * never a blank page. The bar is drawn only once the app has rendered in the browser, so a prerendered page hydrates
 * exactly as it was rendered, and it fades in after a moment (in CSS, with no timer), so a quick move shows nothing. It
 * sits in the page's flow above the outlet, never fixed, so it can never cover the control that has focus (2.4.11).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpProgress, PpToastRegion, RouterOutlet],
  selector: 'ppa-root',
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly drawn = signal(false);
  private readonly pending = signal(false);
  protected readonly waiting = computed(() => this.drawn() && this.pending());

  constructor() {
    afterNextRender(() => this.drawn.set(true));
    const subscription = inject(Router).events.subscribe((event) => {
      if (event instanceof NavigationStart) {
        this.pending.set(true);
      } else if (
        event instanceof NavigationEnd ||
        event instanceof NavigationCancel ||
        event instanceof NavigationError
      ) {
        this.pending.set(false);
      }
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }
}
