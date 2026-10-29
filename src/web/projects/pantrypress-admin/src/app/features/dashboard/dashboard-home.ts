import { ChangeDetectionStrategy, Component } from '@angular/core';

/** The dashboard's stand-in (PPL.2) until PP3.9 builds its shell. */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'ppa-dashboard-home',
  template: `
    <main>
      <h1>Dashboard</h1>
    </main>
  `,
})
export class DashboardHome {}
