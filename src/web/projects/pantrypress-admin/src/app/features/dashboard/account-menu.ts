import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { PpDropdown, PpDropdownItem, PpToaster } from '@pantrypress/ui';
import { DocumentLoad } from '../../core/navigation/document-load';
import { SessionClient } from '../../core/session/session-client';
import { PendingInvitation } from '../../core/sites/pending-invitation';

/**
 * The signed-in person's menu (PP3.9): who is signed in, and signing out. Signing out ends the session at the API as
 * well as in this browser (`SessionClient.signOut`), forgets any invitation waiting here, and loads the home page as a
 * new document, so nothing of the session stays in memory. A refusal keeps the session and says why in a toast, since
 * the menu that was pressed has closed.
 */
@Component({
  selector: 'ppa-account-menu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpDropdown, PpDropdownItem],
  template: `
    <pp-dropdown label="Account" align="end" [caption]="caption()">
      <button pp-dropdown-item (click)="signOut()">Sign out</button>
    </pp-dropdown>
  `,
})
export class AccountMenu {
  private readonly sessions = inject(SessionClient);
  private readonly invitation = inject(PendingInvitation);
  private readonly documents = inject(DocumentLoad);
  private readonly toaster = inject(PpToaster);
  private readonly busy = signal(false);

  protected readonly caption = computed(() => {
    const email = this.sessions.session()?.email;
    return email ? `Signed in as ${email}` : undefined;
  });

  protected async signOut(): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    // The menu closed when it was pressed, so the wait is said aloud.
    this.toaster.announce('Signing you out…');
    try {
      const result = await this.sessions.signOut();
      if (result.ok) {
        this.invitation.clear();
        this.documents.to('/');
        return;
      }
      this.toaster.show({ tone: 'danger', message: result.problem.title });
    } finally {
      this.busy.set(false);
    }
  }
}
