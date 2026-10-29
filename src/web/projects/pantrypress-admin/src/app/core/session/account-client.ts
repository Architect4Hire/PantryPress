import { inject, Injectable } from '@angular/core';
import { ApiClient } from '../api/api-client';
import { toApiPath } from '../api/api-path';
import { ApiResult } from '../api/api-problem';

/** The answer to a write that changed an account in a way another request did at the same time (409). */
export const CHANGED_ELSEWHERE_CODE = 'changed-elsewhere';

/** A confirmation link that is wrong, used, or too old (400). */
export const INVALID_VERIFICATION_LINK_CODE = 'invalid-verification-link';

/** A password reset link that is wrong, used, or too old (400). */
export const INVALID_RESET_LINK_CODE = 'invalid-reset-link';

/**
 * The anonymous account routes on ApiService's admin surface (PP1.2, PP1.3), one method each. Each answers the same
 * way whether or not the address has an account, so nothing here can tell a caller who is registered.
 */
@Injectable({ providedIn: 'root' })
export class AccountClient {
  private readonly api = inject(ApiClient);

  /** Creates an account and emails a confirmation link: 202 whether or not the address was free. */
  register(email: string, password: string): Promise<ApiResult<void>> {
    return this.api.post<void>(toApiPath('account/register'), { body: { email, password } });
  }

  /** Confirms the address with the account id and token from the emailed link: 204. */
  verifyEmail(accountId: string, token: string): Promise<ApiResult<void>> {
    return this.api.post<void>(toApiPath('account/email/verify'), { body: { accountId, token } });
  }

  /** Emails a new confirmation link if the address has an unconfirmed account: always 202. */
  resendVerification(email: string): Promise<ApiResult<void>> {
    return this.api.post<void>(toApiPath('account/email/resend'), { body: { email } });
  }

  /** Emails a password reset link if the address has an account: always 202. */
  forgotPassword(email: string): Promise<ApiResult<void>> {
    return this.api.post<void>(toApiPath('account/password/forgot'), { body: { email } });
  }

  /** Sets a new password with the account id and token from the reset link: 204. A link works once. */
  resetPassword(accountId: string, token: string, newPassword: string): Promise<ApiResult<void>> {
    return this.api.post<void>(toApiPath('account/password/reset'), {
      body: { accountId, token, newPassword },
    });
  }
}
