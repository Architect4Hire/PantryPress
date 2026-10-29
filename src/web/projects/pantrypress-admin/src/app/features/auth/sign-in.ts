import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { PpButton, PpField, PpNotice, PpTextField } from '@pantrypress/ui';
import { ApiProblem } from '../../core/api/api-problem';
import { SessionClient, SessionView } from '../../core/session/session-client';
import { dashboardAddress, SignInReason } from '../../core/session/session-guards';
import { AuthFrame } from './auth-frame';
import { feedbackFor, FormFeedback, noFeedback } from './form-feedback';

type Step = 'password' | 'code';
type SignInField = 'email' | 'password' | 'code';

/** Codes that are about the code the person typed, said under the code field rather than above the form. */
const CODE_REFUSALS = new Set(['invalid-code', 'code-already-used']);

/** A second step that can no longer be finished: the half-finished sign-in ran out or is gone, so it starts again. */
const SIGN_IN_EXPIRED = 'sign-in-expired';

const REASONS: Readonly<Record<SignInReason, string>> = {
  'signed-out': "You've been signed out. Sign in again to pick up where you left off.",
  unavailable:
    "We couldn't check whether you're signed in. If you were, signing in again takes you back.",
};

/**
 * Sign in (PP3.8, ACCT-002): email and password, then - for an account with two-step sign-in - a code from the
 * authenticator app or a recovery code. The gateway holds the session; this screen only asks it. On success it goes
 * to the dashboard address the visitor was turned away from (`next`, only ever under /dashboard).
 */
@Component({
  selector: 'ppa-sign-in',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AuthFrame, PpButton, PpField, PpNotice, PpTextField, ReactiveFormsModule, RouterLink],
  templateUrl: './sign-in.html',
  styleUrl: './auth-form.css',
})
export class SignIn {
  private readonly sessions = inject(SessionClient);
  private readonly router = inject(Router);
  private readonly query = inject(ActivatedRoute).snapshot.queryParamMap;
  private readonly frame = viewChild.required(AuthFrame);

  protected readonly step = signal<Step>(
    this.sessions.session()?.stage === 'second-factor' ? 'code' : 'password',
  );
  protected readonly busy = signal(false);
  protected readonly feedback = signal<FormFeedback<SignInField>>(noFeedback());
  protected readonly heading = computed(() =>
    this.step() === 'password' ? 'Sign in' : 'Enter your sign-in code',
  );

  /** Why the visitor was sent here, if a guard said; shown until they try to sign in. */
  protected readonly reason = signal(this.reasonText(this.query.get('reason')));

  protected readonly credentials = new FormGroup({
    email: new FormControl('', { nonNullable: true }),
    password: new FormControl('', { nonNullable: true }),
  });

  protected readonly code = new FormGroup({
    code: new FormControl('', { nonNullable: true }),
  });

  protected async signIn(): Promise<void> {
    if (this.busy()) {
      return;
    }
    const { email, password } = this.credentials.getRawValue();
    const result = await this.run(() => this.sessions.signIn(email, password));
    if (result === null) {
      return;
    }
    if (result.ok) {
      this.credentials.controls.password.reset();
      await this.arrived(result.value);
      return;
    }
    this.credentials.controls.password.reset();
    this.feedback.set(feedbackFor(result.problem, ['email', 'password']));
  }

  protected async completeSecondStep(): Promise<void> {
    if (this.busy()) {
      return;
    }
    const result = await this.run(() =>
      this.sessions.completeSecondStep(this.code.getRawValue().code),
    );
    if (result === null) {
      return;
    }
    this.code.reset();
    if (result.ok) {
      await this.arrived(result.value);
      return;
    }
    this.refusedCode(result.problem);
  }

  /** Leaves a half-finished sign-in: the gateway drops it, and the first step comes back. */
  protected async startAgain(): Promise<void> {
    if (this.busy()) {
      return;
    }
    await this.run(() => this.sessions.signOut());
    this.toPasswordStep(noFeedback());
  }

  private async arrived(view: SessionView): Promise<void> {
    if (view.stage === 'second-factor') {
      this.step.set('code');
      this.frame().focusHeading();
      return;
    }
    await this.router.navigateByUrl(dashboardAddress(this.query.get('next')));
  }

  private refusedCode(problem: ApiProblem): void {
    if (problem.code === SIGN_IN_EXPIRED || problem.status === 401) {
      this.toPasswordStep({ fields: {}, notice: problem.title });
      return;
    }
    this.feedback.set(
      CODE_REFUSALS.has(problem.code)
        ? { fields: { code: problem.title }, notice: null }
        : feedbackFor(problem, ['code']),
    );
  }

  private toPasswordStep(feedback: FormFeedback<SignInField>): void {
    this.step.set('password');
    this.feedback.set(feedback);
    this.frame().focusHeading();
  }

  // One request at a time; the reason a guard gave is spent once the person acts.
  private async run<T>(call: () => Promise<T>): Promise<T | null> {
    this.busy.set(true);
    this.reason.set(null);
    this.feedback.set(noFeedback());
    try {
      return await call();
    } finally {
      this.busy.set(false);
    }
  }

  private reasonText(reason: string | null): string | null {
    return reason === 'signed-out' || reason === 'unavailable' ? REASONS[reason] : null;
  }
}
