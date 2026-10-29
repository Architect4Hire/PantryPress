/**
 * The apex's public paths (PP-D19, PPL.2): the routes prerendered at build into static documents that read complete
 * without JavaScript. Everything else - the sign-in screens and all of `/dashboard` - is rendered in the browser.
 *
 * This is the one list. The server routes (`app.routes.server.ts`) prerender exactly these, and the gateway's own list
 * (PPL.3) is tested against this file read as text, so keep it one array literal of single-quoted paths and nothing
 * computed, in router form (no leading slash; `''` is `/`). A new top-level public path joins here (frontend.md).
 */
export const PUBLIC_PATHS = ['', 'pricing', 'terms', 'privacy', 'acceptable-use', 'dmca'] as const;

export type PublicPath = (typeof PUBLIC_PATHS)[number];

/**
 * The public paths that send a signed-in visitor to `/dashboard` instead (PPL-001, ADR 0019): the landing page and its
 * pricing address, never the legal pages, which a signed-in owner must still be able to read. The gateway answers the
 * redirect from its own cookie (PPL.3), and its configured list is tested against this one read as text, so it too
 * stays one literal array.
 */
export const SIGNED_IN_REDIRECT_PATHS = ['', 'pricing'] as const satisfies readonly PublicPath[];
