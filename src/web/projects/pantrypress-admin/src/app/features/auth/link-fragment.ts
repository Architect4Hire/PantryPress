/** What an emailed account link carries after its `#` (PP1.2, PP1.3): never in the path or query, so never sent anywhere. */
export interface AccountLink {
  readonly accountId: string;
  readonly token: string;
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Reads `account=…&token=…` from a link's fragment as the router hands it over - already decoded once, as
 * `AccountEmails` escaped it. It is split by hand rather than with `URLSearchParams`, which would decode again and read
 * every `+` in a base64 token as a space. A token holds no `&` (base64 and base64url do not), so the split is exact.
 * Anything else - a missing part, an account that is not an id, an empty token - is no link at all.
 */
export function readAccountLink(fragment: string | null | undefined): AccountLink | null {
  if (!fragment) {
    return null;
  }
  const parts = new Map<string, string>();
  for (const part of fragment.split('&')) {
    const equals = part.indexOf('=');
    if (equals > 0) {
      parts.set(part.slice(0, equals), part.slice(equals + 1));
    }
  }
  const accountId = parts.get('account');
  const token = parts.get('token');
  return accountId !== undefined && GUID.test(accountId) && token ? { accountId, token } : null;
}
