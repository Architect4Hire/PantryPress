import { DOCUMENT } from '@angular/common';
import { inject, Injectable, signal } from '@angular/core';

/** Where an invitation's email links (PP2.11, PP2.14): `/dashboard/join#token=…`. */
export const JOIN_PATH = '/dashboard/join';

/**
 * An invitation link waiting to be accepted, and the `Idempotency-Key` its acceptance is sent with (PP1.9): the same key
 * for every retry of a lost answer, a new one after a refusal the person has since fixed.
 */
export interface InvitationLink {
  readonly token: string;
  readonly key: string;
}

const STORAGE_KEY = 'pp-invitation';

/**
 * The invitation link a visitor arrived with, kept until it is accepted or refused for good (PP3.9). The token comes
 * only in the link's fragment, which a browser never sends anywhere, and is kept in this browser's local storage
 * rather than any address, so signing in - or signing up and confirming the address, whose link usually opens a new
 * tab - never puts it in a query string, history, or a server's logs. On its own it joins no one: the API also wants
 * the invited address, signed in and confirmed.
 *
 * It is forgotten when it is accepted, refused for good, replaced by another link, or when this browser signs out from
 * the account menu. The app sets no lifetime of its own: whether the link still works is the API's to say. Where
 * storage cannot be used (a private window that refuses it, the build's prerender), it is kept for this page only,
 * which still carries it through a sign-in in the same tab.
 */
@Injectable({ providedIn: 'root' })
export class PendingInvitation {
  private readonly storage = storageOf(inject(DOCUMENT));
  private readonly held = signal<InvitationLink | null>(this.stored());

  /** The link waiting, or null. */
  readonly link = this.held.asReadonly();

  /**
   * Keeps the token in a link's fragment, as the router hands it over (decoded once, as the email escaped it). A new
   * token replaces the one held and gets a key of its own; the same token again keeps its key, so a link opened twice
   * is still one acceptance. A fragment with no token changes nothing. Says whether it held a token.
   */
  keep(fragment: string | null | undefined): boolean {
    const token = readInvitationToken(fragment);
    if (token === null) {
      return false;
    }
    if (this.held()?.token !== token) {
      this.save({ token, key: newKey() });
    }
    return true;
  }

  /**
   * Gives the link held a new key. The API keeps a refusal against the key it was sent with, so after one the person
   * can fix - confirming the address, signing in with the invited one - the next try is a new request.
   */
  renewKey(): void {
    const link = this.held();
    if (link !== null) {
      this.save({ token: link.token, key: newKey() });
    }
  }

  /** Forgets the link. */
  clear(): void {
    this.held.set(null);
    try {
      this.storage?.removeItem(STORAGE_KEY);
    } catch {
      // Nothing was kept there to forget.
    }
  }

  private save(link: InvitationLink): void {
    this.held.set(link);
    try {
      this.storage?.setItem(STORAGE_KEY, JSON.stringify(link));
    } catch {
      // Kept for this page only.
    }
  }

  private stored(): InvitationLink | null {
    try {
      const raw = this.storage?.getItem(STORAGE_KEY);
      const value: unknown = raw ? JSON.parse(raw) : null;
      return typeof value === 'object' &&
        value !== null &&
        'token' in value &&
        'key' in value &&
        typeof value.token === 'string' &&
        typeof value.key === 'string' &&
        value.token !== '' &&
        value.key !== ''
        ? { token: value.token, key: value.key }
        : null;
    } catch {
      return null;
    }
  }
}

/**
 * The token in `token=…`, split by hand rather than with `URLSearchParams`, which would decode it again and read every
 * `+` as a space (as `link-fragment.ts` explains). An empty token is no token.
 */
export function readInvitationToken(fragment: string | null | undefined): string | null {
  for (const part of (fragment ?? '').split('&')) {
    if (part.startsWith('token=') && part.length > 'token='.length) {
      return part.slice('token='.length);
    }
  }
  return null;
}

function storageOf(document: Document): Storage | null {
  try {
    return document.defaultView?.localStorage ?? null;
  } catch {
    return null;
  }
}

// One key per link, so a retry after a lost answer is the same request (api-contract.md: 1 to 128 visible characters).
function newKey(): string {
  return `invitation-${crypto.randomUUID()}`;
}
