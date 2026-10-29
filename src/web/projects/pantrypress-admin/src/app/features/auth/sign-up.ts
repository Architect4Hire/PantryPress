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
import { RuntimeConfigService } from '../../core/config/runtime-config.service';
import { AccountClient } from '../../core/session/account-client';
import { AuthFrame } from './auth-frame';
import { feedbackFor, FormFeedback, noFeedback } from './form-feedback';

type SignUpField = 'email' | 'password';

/**
 * Create an account (PP3.8, ACCT-001). While sign-up is closed (BIZ-009; the server's `publicSignUp`, PPL.13) the
 * screen says when it opens and offers sign-in instead of a form the API would refuse: the server is the gate, and this
 * is courtesy. Open, it registers and then says to check the inbox, with a way to send the link again. The answer is
 * the same whether or not the address already had an account, and so is this screen.
 */
@Component({
  selector: 'ppa-sign-up',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AuthFrame, PpButton, PpField, PpNotice, PpTextField, ReactiveFormsModule, RouterLink],
  templateUrl: './sign-up.html',
  styleUrl: './auth-form.css',
})
export class SignUp {
  private readonly accounts = inject(AccountClient);
  private readonly frame = viewChild.required(AuthFrame);

  private readonly config = inject(RuntimeConfigService).config;

  protected readonly open = computed(() => this.config().publicSignUp);
  protected readonly sentTo = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly feedback = signal<FormFeedback<SignUpField>>(noFeedback());
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
    if (this.busy()) {
      return;
    }
    const { email, password } = this.form.getRawValue();
    this.busy.set(true);
    this.feedback.set(noFeedback());
    try {
      const result = await this.accounts.register(email, password);
      if (result.ok) {
        this.form.controls.password.reset();
        this.sentTo.set(email);
        this.frame().focusHeading();
        return;
      }
      this.feedback.set(feedbackFor(result.problem, ['email', 'password']));
    } finally {
      this.busy.set(false);
    }
  }

  protected async resend(): Promise<void> {
    const email = this.sentTo();
    if (email === null || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.resent.set(false);
    this.feedback.set(noFeedback());
    try {
      const result = await this.accounts.resendVerification(email);
      if (result.ok) {
        this.resent.set(true);
      } else {
        this.feedback.set({ fields: {}, notice: result.problem.title });
      }
    } finally {
      this.busy.set(false);
    }
  }
}
