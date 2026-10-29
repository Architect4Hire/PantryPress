import { ChangeDetectionStrategy, Component } from '@angular/core';

/**
 * The landing page at `/` and `/pricing` (PP-D19, PPL.2): an empty shell until PPL.6 onward builds its sections.
 * Prerendered, so it reads no session, account, site, or price, and makes no call while it is built (frontend.md).
 * `/pricing` is this page with `data.scrollTo`, which PPL.16 acts on.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'ppa-landing',
  template: `
    <main>
      <h1>PantryPress</h1>
    </main>
  `,
})
export class Landing {}
