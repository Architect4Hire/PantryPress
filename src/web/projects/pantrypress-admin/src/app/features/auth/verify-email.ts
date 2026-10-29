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
import { PpButton, PpField, PpNotice, PpProgress, PpTextField } from '@pantrypress/ui';
import { AccountClient, INVALID_VERIFICATION_LINK_CODE } from '../../core/session/account-client';
import { AuthFrame } from './auth-frame';
import { feedbackFor, FormFeedback, noFeedback } from './form-feedback';
import { AccountLink, readAccountLink } from './link-fragment';

type State = 'checking' | 'confirmed' | 'invalid' | 'failed';

const HEADINGS: Readonly<Record<State, string>> = {
  checking: 'Confirming your email address',
  confirmed: 'Your email address is confirmed',
  invalid: "This link doesn't work",
  failed: "We couldn't confirm your address",
};

/**
 * The page a confirmation email links to (PP3.8, ACCT-001): `/verify-email#account=…&token=…`. It confirms the address
 * as it opens, signed in or not - sending a signed-in visitor on first would lose the link - and takes the link out of
 * the address bar so it is not kept in history. A link that does not work offers a new one; a confirmation that failed
 * for another reason offers to try again.
 */
@Component({
  selector: 'ppa-verify-email',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AuthFrame,
    PpButton,
    PpField,
    PpNotice,
    PpProgress,
    PpTextField,
    ReactiveFormsModule,
    RouterLink,
  ],
  templateUrl: './verify-email.html',
  styleUrl: './auth-form.css',
})
export class VerifyEmail {
  private readonly accounts = inject(AccountClient);
  private readonly frame = viewChild.required(AuthFrame);
  private readonly link: AccountLink | null;

  protected readonly state = signal<State>('checking');
  protected readonly heading = computed(() => HEADINGS[this.state()]);
  protected readonly failure = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly feedback = signal<FormFeedback<'email'>>(noFeedback());
  protected readonly resent = signal(false);

  protected readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true }),
  });

  constructor() {
    this.link = readAccountLink(inject(ActivatedRoute).snapshot.fragment);
    const location = inject(Location);
    location.replaceState(location.path(false));
    if (this.link === null) {
      this.state.set('invalid');
      return;
    }
    void this.confirm();
  }

  protected async confirm(): Promise<void> {
    if (this.link === null || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.failure.set(null);
    try {
      const result = await this.accounts.verifyEmail(this.link.accountId, this.link.token);
      if (result.ok) {
        this.moveTo('confirmed');
      } else if (result.problem.code === INVALID_VERIFICATION_LINK_CODE) {
        this.moveTo('invalid');
      } else {
        this.failure.set(result.problem.title);
        this.moveTo('failed');
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async resend(): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.resent.set(false);
    this.feedback.set(noFeedback());
    try {
      const result = await this.accounts.resendVerification(this.form.getRawValue().email);
      if (result.ok) {
        this.resent.set(true);
      } else {
        this.feedback.set(feedbackFor(result.problem, ['email']));
      }
    } finally {
      this.busy.set(false);
    }
  }

  private moveTo(state: State): void {
    const moved = this.state() !== state;
    this.state.set(state);
    if (moved) {
      this.frame().focusHeading();
    }
  }
}
