import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { PpButton, PpField, PpNotice, PpTextField } from '@pantrypress/ui';
import { ApiResult } from '../../core/api/api-problem';
import { RuntimeConfigService } from '../../core/config/runtime-config.service';
import { AccountClient } from '../../core/session/account-client';
import { SECOND_FACTOR_REQUIRED_CODE } from '../../core/session/session-client';
import { AuthFrame } from './auth-frame';
import { feedbackFor, FormFeedback, marksFields, noFeedback } from './form-feedback';
import { PendingSignIn } from './pending-sign-in';

type SignUpField = 'email' | 'password';

/**
 * Create an account (PP3.8, ACCT-001). While sign-up is closed (BIZ-009; the server's `publicSignUp`, PPL.13) the
 * screen says when it opens and offers sign-in instead of a form the API would refuse: the server is the gate, and this
 * is courtesy. The route waits for that switch to be read, so the screen never shows one state and then the other.
 * Open, it registers and then says to check the inbox. The server sends nothing to an address that already has an
 * account, and answers the same either way, so the screen says what happens if the address is new, never that a link
 * was sent.
 */
@Component({
  selector: 'ppa-sign-up',
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
  templateUrl: './sign-up.html',
  styleUrl: './auth-form.css',
})
export class SignUp {
  private readonly accounts = inject(AccountClient);
  private readonly config = inject(RuntimeConfigService).config;
  private readonly frame = viewChild.required(AuthFrame);

  protected readonly open = computed(() => this.config().publicSignUp);
  protected readonly sentTo = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly feedback = signal<FormFeedback<SignUpField>>(noFeedback());
  protected readonly pending = signal(false);
  protected readonly resent = signal(false);
  protected readonly heading = computed(() =>
    !this.open()
      ? 'Sign-up opens at launch'
      : this.sentTo() === null
        ? 'Create your account'
        : 'Check your email',
  );

  protected readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true }),
    password: new FormControl('', { nonNullable: true }),
  });

  protected async register(): Promise<void> {
    const { email, password } = this.form.getRawValue();
    const result = await this.run(() => this.accounts.register(email, password));
    if (result === null) {
      return;
    }
    if (result.ok) {
      this.form.controls.password.reset();
      this.sentTo.set(email);
      this.frame().focusHeading();
      return;
    }
    this.refused(result, ['email', 'password']);
  }

  protected async resend(): Promise<void> {
    const email = this.sentTo();
    if (email === null) {
      return;
    }
    const result = await this.run(() => this.accounts.resendVerification(email));
    if (result === null) {
      return;
    }
    if (result.ok) {
      this.resent.set(true);
    } else {
      this.refused(result, []);
    }
  }

  /** Back to the form with the address kept, for a mistyped one. */
  protected useAnotherAddress(): void {
    this.sentTo.set(null);
    this.resent.set(false);
    this.feedback.set(noFeedback());
    this.frame().focusHeading();
  }

  private refused(result: ApiResult<void> & { ok: false }, fields: readonly SignUpField[]): void {
    if (result.problem.code === SECOND_FACTOR_REQUIRED_CODE) {
      this.pending.set(true);
      return;
    }
    const feedback = feedbackFor(result.problem, fields);
    this.feedback.set(feedback);
    if (marksFields(feedback)) {
      this.frame().focusFirstInvalid();
    }
  }

  private async run(call: () => Promise<ApiResult<void>>): Promise<ApiResult<void> | null> {
    if (this.busy()) {
      return null;
    }
    this.busy.set(true);
    this.pending.set(false);
    this.resent.set(false);
    this.feedback.set(noFeedback());
    try {
      return await call();
    } finally {
      this.busy.set(false);
    }
  }
}
