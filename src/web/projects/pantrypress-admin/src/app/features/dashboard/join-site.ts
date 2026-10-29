import { Location } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  Injector,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { PpButton, PpNotice, PpPageHeader, PpProgress, PpToaster } from '@pantrypress/ui';
import { AccountClient } from '../../core/session/account-client';
import { SessionClient } from '../../core/session/session-client';
import { signInFor } from '../../core/session/session-guards';
import { JOIN_PATH, PendingInvitation } from '../../core/sites/pending-invitation';
import {
  ALREADY_MEMBER_CODE,
  IDEMPOTENCY_KEY_REUSED_CODE,
  EMAIL_NOT_VERIFIED_CODE,
  INVITATION_INVALID_CODE,
  INVITATION_OTHER_ADDRESS_CODE,
  ROLE_WORDS,
  SitesClient,
  VALIDATION_FAILED_CODE,
} from '../../core/sites/sites-client';
import { DashboardSites } from './dashboard-sites';

type State = 'joining' | 'invalid' | 'already-member' | 'other-address' | 'unconfirmed' | 'failed';

const HEADINGS: Readonly<Record<State, string>> = {
  joining: 'Joining the site',
  invalid: "This invitation doesn't work",
  'already-member': "You're already a member of this site",
  'other-address': 'This invitation is for another email address',
  unconfirmed: 'Confirm your email address first',
  failed: "We couldn't accept the invitation",
};

/**
 * The page an invitation's email links to (PP2.11, PP2.14, PP3.9): `/dashboard/join#token=…`, behind the dashboard's
 * sign-in. The token is kept before the sign-in screen is shown (`keepInvitationLink`), so it comes through signing in
 * or signing up; here it leaves the address bar, and the invitation is accepted as the page opens, as a confirmation
 * link is (`/verify-email`). Nothing can be shown first: the token is sealed, and only the API can say which site it
 * names. A refusal the person can fix keeps the link for another try; one that can never work forgets it.
 */
@Component({
  selector: 'ppa-join-site',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PpButton, PpNotice, PpPageHeader, PpProgress, RouterLink],
  templateUrl: './join-site.html',
  styleUrl: './join-site.css',
})
export class JoinSite {
  private readonly sites = inject(SitesClient);
  private readonly dashboard = inject(DashboardSites);
  private readonly invitation = inject(PendingInvitation);
  private readonly sessions = inject(SessionClient);
  private readonly accounts = inject(AccountClient);
  private readonly toaster = inject(PpToaster);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly injector = inject(Injector);

  protected readonly state = signal<State>('joining');
  protected readonly heading = computed(() => HEADINGS[this.state()]);
  /** Which press is being worked on; one at a time, and only its button shows it. */
  protected readonly busy = signal<'accept' | 'sign-out' | 'resend' | null>(null);
  /** The API's own words for the refusal on screen, where they say more than the heading. */
  protected readonly detail = signal<string | null>(null);
  /** What came of the last thing pressed beside the refusal: a resend, a sign-out that was refused. */
  protected readonly note = signal<{ tone: 'success' | 'danger'; text: string } | null>(null);
  protected readonly email = computed(() => this.sessions.session()?.email ?? null);

  constructor() {
    // The guard kept it already; a link opened again while signed in is kept here as well.
    this.invitation.keep(inject(ActivatedRoute).snapshot.fragment);
    const location = inject(Location);
    location.replaceState(location.path(false));
    if (this.invitation.link() === null) {
      this.state.set('invalid');
      return;
    }
    void this.accept();
  }

  protected async accept(): Promise<void> {
    const link = this.invitation.link();
    if (link === null || this.busy() !== null) {
      return;
    }
    this.busy.set('accept');
    this.note.set(null);
    try {
      const result = await this.sites.acceptInvitation(link.token, link.key);
      if (result.ok) {
        this.invitation.clear();
        const site = result.value;
        this.toaster.show({
          tone: 'success',
          message: `You've joined ${site.name} as an ${ROLE_WORDS[site.role].toLowerCase()}.`,
        });
        await this.router.navigate(['/dashboard/sites', site.slug]);
        void this.dashboard.refreshMine();
        return;
      }
      const problem = result.problem;
      // The API stores every answer under 500 but a 401, 408, or 429 against its key (PP1.9), so a refusal the person
      // can fix - an address to confirm, another account to sign in with - is followed by a new key: the same one would
      // be answered with that refusal again for a day. A key the API says was used for something else gets one too.
      if (problem.status === 401) {
        await this.router.navigateByUrl(signInFor(this.router, JOIN_PATH, problem));
      } else if (problem.code === INVITATION_OTHER_ADDRESS_CODE) {
        this.invitation.renewKey();
        this.show('other-address', problem.title);
      } else if (problem.code === EMAIL_NOT_VERIFIED_CODE) {
        this.invitation.renewKey();
        this.show('unconfirmed', problem.title);
      } else if (problem.code === ALREADY_MEMBER_CODE) {
        this.invitation.clear();
        this.show('already-member', null);
      } else if (
        problem.code === INVITATION_INVALID_CODE ||
        problem.code === VALIDATION_FAILED_CODE
      ) {
        // No try can make either work. Only the first has words about invitations; the other's are about a form this
        // screen does not have.
        this.invitation.clear();
        this.show('invalid', problem.code === INVITATION_INVALID_CODE ? problem.title : null);
      } else {
        if (problem.code === IDEMPOTENCY_KEY_REUSED_CODE) {
          this.invitation.renewKey();
        }
        // Anything else may pass: a busy minute, a first try still running (both say to wait), an outage, or a refusal
        // at the gateway. The link is kept, and Try again sends it again.
        this.show('failed', problem.title);
      }
    } finally {
      this.busy.set(null);
    }
  }

  /** Signs out, keeping the invitation, so the invited address can sign in and accept it. */
  protected async signInAgain(): Promise<void> {
    if (this.busy() !== null) {
      return;
    }
    this.busy.set('sign-out');
    this.note.set(null);
    try {
      const result = await this.sessions.signOut();
      if (result.ok) {
        await this.router.navigateByUrl(signInFor(this.router, JOIN_PATH, null));
      } else {
        this.note.set({ tone: 'danger', text: result.problem.title });
      }
    } finally {
      this.busy.set(null);
    }
  }

  /** Sends the confirmation email again, to the address signed in. */
  protected async resend(): Promise<void> {
    const email = this.email();
    if (email === null || this.busy() !== null) {
      return;
    }
    this.busy.set('resend');
    this.note.set(null);
    try {
      const result = await this.accounts.resendVerification(email);
      this.note.set(
        result.ok
          ? {
              tone: 'success',
              text: 'If your address still needs confirming, a new email is on its way.',
            }
          : { tone: 'danger', text: result.problem.title },
      );
    } finally {
      this.busy.set(null);
    }
  }

  private show(state: State, detail: string | null): void {
    const moved = this.state() !== state;
    this.state.set(state);
    this.detail.set(detail);
    if (moved) {
      afterNextRender(() => this.focusHeading(), { injector: this.injector });
    } else {
      // The same refusal again changes nothing on screen, so it is said aloud.
      this.toaster.announce(detail ?? HEADINGS[state]);
    }
  }

  // The heading names the new state, so a screen reader hears what happened.
  private focusHeading(): void {
    const heading = this.host.querySelector<HTMLElement>('h1');
    if (heading) {
      heading.tabIndex = -1;
      heading.focus();
    }
  }
}
