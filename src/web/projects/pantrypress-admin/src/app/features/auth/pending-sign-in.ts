import { ChangeDetectionStrategy, Component, inject, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { PpButton, PpNotice } from '@pantrypress/ui';
import { SessionClient } from '../../core/session/session-client';

/**
 * What an account screen shows when the gateway refuses it because this browser is part-way through a sign-in
 * (`second-factor-required`, PP3.8): finish it, or drop it and carry on here. Dropping it is a sign-out of the
 * half-finished session; `cleared` says it is gone, so the screen can be used again.
 */
@Component({
  selector: 'ppa-pending-sign-in',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpButton, PpNotice, RouterLink],
  template: `
    <pp-notice tone="warning" heading="You're part way through signing in" announce>
      <p class="text">
        Finish signing in with your code, or start again to carry on here instead.
        @if (failure(); as failure) {
          {{ failure }}
        }
      </p>
      <p class="actions">
        <a pp-button variant="primary" size="sm" routerLink="/sign-in">Finish signing in</a>
        <button
          pp-button
          variant="secondary"
          size="sm"
          type="button"
          [busy]="busy()"
          (click)="drop()"
        >
          Start again
        </button>
      </p>
    </pp-notice>
  `,
  styles: `
    .text {
      margin: 0;
    }
    .actions {
      margin: var(--space-3) 0 0;
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
  `,
})
export class PendingSignIn {
  private readonly sessions = inject(SessionClient);

  readonly cleared = output<void>();
  protected readonly busy = signal(false);
  protected readonly failure = signal<string | null>(null);

  protected async drop(): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.failure.set(null);
    try {
      const result = await this.sessions.signOut();
      if (result.ok) {
        this.cleared.emit();
      } else {
        this.failure.set(result.problem.title);
      }
    } finally {
      this.busy.set(false);
    }
  }
}
