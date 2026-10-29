/**
 * What the admin app knows about the BFF before it asks anything (PP3.7). The app and the gateway share one origin
 * (PP-D19), so none of this differs between environments and none of it is runtime configuration: it is the contract
 * `gateway.md` writes down, kept in one place.
 */

/** The admin surface, as the gateway forwards it to ApiService (`api-contract.md`). Every call is under it. */
export const API_BASE = '/api/v1';

/**
 * The antiforgery pair (PP1.7, `gateway.md`): the gateway sets the request token in this cookie, the one cookie the
 * admin app reads, and expects it back in this header on every `POST`, `PUT`, `PATCH`, and `DELETE`.
 */
export const XSRF_COOKIE_NAME = '__Host-pp-xsrf';
export const XSRF_HEADER_NAME = 'X-PP-Xsrf';

/** The gateway's refusal of a write whose token was missing or stale. It mints a fresh pair with it. */
export const ANTIFORGERY_FAILED_CODE = 'antiforgery-failed';
