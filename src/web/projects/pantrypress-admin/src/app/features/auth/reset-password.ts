import { Location } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { PpButton, PpField, PpNotice, PpTextField } from '@pantrypress/ui';
import { AccountClient, INVALID_RESET_LINK_CODE } from '../../core/session/account-client';
import { SECOND_FACTOR_REQUIRED_CODE } from '../../core/session/session-client';
import { AuthFrame } from './auth-frame';
import { feedbackFor, FormFeedback, marksFields, noFeedback, refusesLink } from './form-feedback';
import { AccountLink, readAccountLink } from './link-fragment';
import { PendingSignIn } from './pending-sign-in';

type State = 'choosing' | 'changed' | 'maybe-changed' | 'invalid';

const HEADINGS: Readonly<Record<State, string>> = {
  choosing: 'Choose a new password',
  changed: 'Your password is changed',
  'maybe-changed': 'Your password may already be changed',
  invalid: "This link doesn't work",
};

/**
 * The page a password reset email links to (PP3.8, PP1.3): `/reset-password#account=…&token=…`. The link leaves the
 * address bar as the page opens. A link works once, so a used or old one offers a new one rather than a form.
 *
 * One answer is ambiguous, and the API says so (`PasswordController.Reset`): a retry after an answer that never came
 * back is refused as a used link, although the first try may have set the password. So a refusal of the link that
 * follows an unanswered try leads with signing in with the new password, not with asking for another link.
 */
@Component({
  selector: 'ppa-reset-password',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AuthFrame,
    PendingSignIn,
    PpButton,
    PpField,
    PpNotice,
    PpTextField,
    ReactiveFormsModule,
    RouterLink,
  ],
  templateUrl: './reset-password.html',
  styleUrl: './auth-form.css',
})
export class ResetPassword {
  private readonly accounts = inject(AccountClient);
  private readonly frame = viewChild.required(AuthFrame);
  private readonly link: AccountLink | null;

  /** Whether a try on this page got no answer it could read, so the password may have been set by it. */
  private unanswered = false;

  protected readonly state = signal<State>('choosing');
  protected readonly heading = computed(() => HEADINGS[this.state()]);
  protected readonly busy = signal(false);
  protected readonly pending = signal(false);
  protected readonly feedback = signal<FormFeedback<'newPassword'>>(noFeedback());

  protected readonly form = new FormGroup({
    newPassword: new FormControl('', { nonNullable: true }),
  });

  constructor() {
    this.link = readAccountLink(inject(ActivatedRoute).snapshot.fragment);
    const location = inject(Location);
    location.replaceState(location.path(false));
    if (this.link === null) {
      this.state.set('invalid');
    }
  }

  protected async save(): Promise<void> {
    if (this.link === null || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.pending.set(false);
    this.feedback.set(noFeedback());
    try {
      const { newPassword } = this.form.getRawValue();
      const result = await this.accounts.resetPassword(
        this.link.accountId,
        this.link.token,
        newPassword,
      );
      this.form.reset();
      if (result.ok) {
        this.moveTo('changed');
      } else if (result.problem.code === INVALID_RESET_LINK_CODE || refusesLink(result.problem)) {
        this.moveTo(this.unanswered ? 'maybe-changed' : 'invalid');
      } else if (result.problem.code === SECOND_FACTOR_REQUIRED_CODE) {
        this.pending.set(true);
      } else {
        this.unanswered ||= result.problem.status === 0 || result.problem.status >= 500;
        const feedback = feedbackFor(result.problem, ['newPassword']);
        this.feedback.set(feedback);
        if (marksFields(feedback)) {
          this.frame().focusFirstInvalid();
        }
      }
    } finally {
      this.busy.set(false);
    }
  }

  private moveTo(state: State): void {
    this.state.set(state);
    this.frame().focusHeading();
  }
}
