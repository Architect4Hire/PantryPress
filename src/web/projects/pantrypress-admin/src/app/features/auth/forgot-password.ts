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
import { AccountClient } from '../../core/session/account-client';
import { AuthFrame } from './auth-frame';
import { feedbackFor, FormFeedback, noFeedback } from './form-feedback';

/**
 * Ask for a password reset link (PP3.8, PP1.3). The answer is the same whether or not the address has an account, so
 * the screen never says which; it says what happens if one does.
 */
@Component({
  selector: 'ppa-forgot-password',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AuthFrame, PpButton, PpField, PpNotice, PpTextField, ReactiveFormsModule, RouterLink],
  templateUrl: './forgot-password.html',
  styleUrl: './auth-form.css',
})
export class ForgotPassword {
  private readonly accounts = inject(AccountClient);
  private readonly frame = viewChild.required(AuthFrame);

  protected readonly sentTo = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly feedback = signal<FormFeedback<'email'>>(noFeedback());
  protected readonly heading = computed(() =>
    this.sentTo() === null ? 'Reset your password' : 'Check your email',
  );

  protected readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true }),
  });

  protected async send(): Promise<void> {
    if (this.busy()) {
      return;
    }
    const { email } = this.form.getRawValue();
    this.busy.set(true);
    this.feedback.set(noFeedback());
    try {
      const result = await this.accounts.forgotPassword(email);
      if (result.ok) {
        this.sentTo.set(email);
        this.frame().focusHeading();
      } else {
        this.feedback.set(feedbackFor(result.problem, ['email']));
      }
    } finally {
      this.busy.set(false);
    }
  }
}
