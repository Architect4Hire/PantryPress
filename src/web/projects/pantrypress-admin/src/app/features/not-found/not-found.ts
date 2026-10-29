import { ChangeDetectionStrategy, Component } from '@angular/core';

/**
 * The apex's not-found page (PPL.2), drawn in the browser for any path the route table does not name. Its link home is
 * a document load, never a `routerLink`: the gateway decides `/` for a signed-in visitor (frontend.md).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'ppa-not-found',
  template: `
    <main>
      <h1>Page not found</h1>
      <p>There's nothing at this address. <a href="/">Go to the home page</a></p>
    </main>
  `,
})
export class NotFound {}
