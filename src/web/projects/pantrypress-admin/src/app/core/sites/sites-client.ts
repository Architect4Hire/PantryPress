import { inject, Injectable } from '@angular/core';
import { ApiClient } from '../api/api-client';
import { apiPath, toApiPath } from '../api/api-path';
import { ApiResult, isRecord, UNEXPECTED_CODE, UNEXPECTED_TITLE } from '../api/api-problem';

/** A member's role on one site (SITE-010), as the API spells it (kebab-case). Each covers the ones after it. */
export type SiteRole = 'owner' | 'editor' | 'author';

/** Where a site is in its life (SITE-001), as the API spells it. */
export type SiteStatus =
  'provisioning' | 'live' | 'suspended' | 'read-only' | 'offline' | 'deleted';

/** One of the caller's sites in a list (`SiteSummary`). `host` is where it answers. Never a site id (tenancy.md rule 7). */
export interface SiteSummary {
  readonly slug: string;
  readonly name: string;
  readonly status: SiteStatus;
  readonly role: SiteRole;
  readonly host: string;
}

/** One of the caller's sites, read by its slug (`SiteDetail`). */
export interface SiteDetail extends SiteSummary {
  readonly tagline: string;
  readonly timeZone: string;
  readonly locale: string;
  readonly createdAt: string;
}

/** A page of the caller's sites, ordered by address; `nextCursor` is null on the last. */
export interface SitePage {
  readonly items: readonly SiteSummary[];
  readonly nextCursor: string | null;
}

/** No such site, or not one of the caller's (404): one answer, so a slug tells a stranger nothing (tenancy.md rule 4). */
export const SITE_NOT_FOUND_CODE = 'site-not-found';

/** An invitation link that cannot be used: expired, used, replaced, or not one of ours - one answer (400). */
export const INVITATION_INVALID_CODE = 'invitation-invalid';

/** The caller is signed in with another address than the invited one (403). */
export const INVITATION_OTHER_ADDRESS_CODE = 'invitation-other-address';

/** The caller has the invited address but has not confirmed it (403). */
export const EMAIL_NOT_VERIFIED_CODE = 'email-not-verified';

/** A request the API could not read: for an invitation, no token or one far too long to be one (400). */
export const VALIDATION_FAILED_CODE = 'validation-failed';

/** An `Idempotency-Key` already used for a different request (422); a new key is owed. */
export const IDEMPOTENCY_KEY_REUSED_CODE = 'idempotency-key-reused';

/** The caller already belongs to the invitation's site; nothing changed (409). */
export const ALREADY_MEMBER_CODE = 'already-member';

const ROLES: readonly string[] = ['owner', 'editor', 'author'] satisfies SiteRole[];
const STATUSES: readonly string[] = [
  'provisioning',
  'live',
  'suspended',
  'read-only',
  'offline',
  'deleted',
] satisfies SiteStatus[];

/** The most sites one page may hold (api-contract.md); the switcher asks for that many. */
export const MAX_PAGE = 100;

/**
 * The caller's sites and joining one (PP2.10, PP2.11), on ApiService's admin surface through the gateway. Listing and
 * accepting name no site of the caller's, so they are account-level; reading one names it by its slug, and the site
 * policy decides (auth.md). No call here ever sends a site id: a site comes from the route's slug, or from the
 * invitation's sealed token.
 */
@Injectable({ providedIn: 'root' })
export class SitesClient {
  private readonly api = inject(ApiClient);

  /** A page of the caller's sites; `cursor` is the page before's `nextCursor`, missing for the first. */
  async list(options: { limit?: number; cursor?: string } = {}): Promise<ApiResult<SitePage>> {
    const query = {
      ...(options.limit === undefined ? {} : { limit: options.limit }),
      ...(options.cursor === undefined ? {} : { cursor: options.cursor }),
    };
    return read(await this.api.get<unknown>(toApiPath('sites'), { query }), readSitePage);
  }

  /** One of the caller's sites with their role there; 404 `site-not-found` for any other slug. */
  async get(slug: string): Promise<ApiResult<SiteDetail>> {
    return read(await this.api.get<unknown>(apiPath`sites/${slug}`), readSiteDetail);
  }

  /**
   * Joins the site an invitation is for, with its role. `idempotencyKey` is one per link, so a retry after a lost
   * answer gets the first answer again rather than `already-member` (PP1.9).
   */
  async acceptInvitation(token: string, idempotencyKey: string): Promise<ApiResult<SiteSummary>> {
    return read(
      await this.api.post<unknown>(toApiPath('site-invitations/accept'), {
        body: { token },
        idempotencyKey,
      }),
      readSiteSummary,
    );
  }
}

function read<T>(result: ApiResult<unknown>, reader: (body: unknown) => T | null): ApiResult<T> {
  if (!result.ok) {
    return result;
  }
  const value = reader(result.value);
  return value === null
    ? {
        ok: false,
        problem: { status: result.status, code: UNEXPECTED_CODE, title: UNEXPECTED_TITLE },
      }
    : { ok: true, status: result.status, value };
}

/** The API's `SiteSummary`, checked field by field; anything else is not one. */
export function readSiteSummary(body: unknown): SiteSummary | null {
  if (
    !isRecord(body) ||
    typeof body['slug'] !== 'string' ||
    typeof body['name'] !== 'string' ||
    typeof body['host'] !== 'string' ||
    typeof body['role'] !== 'string' ||
    typeof body['status'] !== 'string' ||
    !ROLES.includes(body['role']) ||
    !STATUSES.includes(body['status'])
  ) {
    return null;
  }
  return {
    slug: body['slug'],
    name: body['name'],
    status: body['status'] as SiteStatus,
    role: body['role'] as SiteRole,
    host: body['host'],
  };
}

/** The API's `SiteDetail`, checked field by field. */
export function readSiteDetail(body: unknown): SiteDetail | null {
  const summary = readSiteSummary(body);
  if (
    summary === null ||
    !isRecord(body) ||
    typeof body['tagline'] !== 'string' ||
    typeof body['timeZone'] !== 'string' ||
    typeof body['locale'] !== 'string' ||
    typeof body['createdAt'] !== 'string'
  ) {
    return null;
  }
  return {
    ...summary,
    tagline: body['tagline'],
    timeZone: body['timeZone'],
    locale: body['locale'],
    createdAt: body['createdAt'],
  };
}

/** The API's `SitePage`: every item a `SiteSummary`, and a string cursor or none. */
export function readSitePage(body: unknown): SitePage | null {
  if (!isRecord(body) || !Array.isArray(body['items'])) {
    return null;
  }
  const cursor = body['nextCursor'];
  if (cursor !== null && cursor !== undefined && typeof cursor !== 'string') {
    return null;
  }
  const items: SiteSummary[] = [];
  for (const item of body['items'] as unknown[]) {
    const summary = readSiteSummary(item);
    if (summary === null) {
      return null;
    }
    items.push(summary);
  }
  return { items, nextCursor: typeof cursor === 'string' ? cursor : null };
}

/** A role in words, as a label beside the site. */
export const ROLE_WORDS: Readonly<Record<SiteRole, string>> = {
  owner: 'Owner',
  editor: 'Editor',
  author: 'Author',
};

/** A status in words, for a pill beside the site. Never "deleted" in practice: a deleted site lists as no site. */
export const STATUS_WORDS: Readonly<Record<SiteStatus, string>> = {
  provisioning: 'Getting ready',
  live: 'Live',
  suspended: 'Suspended',
  'read-only': 'Read only',
  offline: 'Offline',
  deleted: 'Deleted',
};
