import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

/**
 * A platform legal page's stand-in (PPL.2) until PPL.19 renders the documents PP19.13 writes. Its heading is the
 * route's `data.title`. Prerendered, like every public path.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'ppa-legal-placeholder',
  template: `
    <main>
      <h1>{{ title }}</h1>
      <p>This page is on its way.</p>
    </main>
  `,
})
export class LegalPlaceholder {
  protected readonly title = inject(ActivatedRoute).snapshot.data['title'] as string;
}
