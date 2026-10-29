import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PpButton, PpPageHeader } from '@pantrypress/ui';

/**
 * The dashboard's not-found page (PP3.9), inside the shell, for any address under `/dashboard` the routes do not name.
 * Its way on is a router link: `/dashboard` is the app's own, never one the gateway decides.
 */
@Component({
  selector: 'ppa-dashboard-not-found',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpButton, PpPageHeader, RouterLink],
  template: `
    <pp-page-header
      heading="Page not found"
      description="There's nothing at this address in your dashboard."
    />
    <a pp-button variant="primary" routerLink="/dashboard">Go to your sites</a>
  `,
})
export class DashboardNotFound {}
