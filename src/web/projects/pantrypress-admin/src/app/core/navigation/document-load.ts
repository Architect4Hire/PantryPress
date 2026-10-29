import { DOCUMENT } from '@angular/common';
import { inject, Injectable } from '@angular/core';

/**
 * A whole-document load, for an address the router must not draw itself: `/` and `/pricing`, which the gateway decides
 * for a signed-in visitor (frontend.md, PPL-001), and the page a sign-out ends on, so nothing of the session stays in
 * memory. A service, so a spec can see where it went without leaving the page.
 */
@Injectable({ providedIn: 'root' })
export class DocumentLoad {
  private readonly document = inject(DOCUMENT);

  to(path: string): void {
    this.document.location.assign(path);
  }
}
