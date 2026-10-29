import { TestBed } from '@angular/core/testing';
import { ApiClient, ApiReadOptions, ApiWriteOptions } from '../api/api-client';
import { ApiPath } from '../api/api-path';
import { ApiResult } from '../api/api-problem';
import { readSiteDetail, readSitePage, readSiteSummary, SitesClient } from './sites-client';

const SUMMARY = {
  slug: 'apple-pie',
  name: 'Apple Pie Club',
  status: 'live',
  role: 'owner',
  host: 'apple-pie.pantrypress.app',
};

const DETAIL = {
  ...SUMMARY,
  tagline: 'Pies, mostly',
  timeZone: 'America/Chicago',
  locale: 'en-US',
  createdAt: '2026-10-01T12:00:00Z',
};

class FakeApi {
  readonly calls: { method: string; path: ApiPath; options: ApiWriteOptions }[] = [];
  answer: ApiResult<unknown> = { ok: true, status: 200, value: null };
  get = (path: ApiPath, options: ApiReadOptions = {}) => this.take('GET', path, options);
  post = (path: ApiPath, options: ApiWriteOptions = {}) => this.take('POST', path, options);
  private take(method: string, path: ApiPath, options: ApiWriteOptions) {
    this.calls.push({ method, path, options });
    return Promise.resolve(this.answer);
  }
}

describe('SitesClient', () => {
  let api: FakeApi;
  let client: SitesClient;

  beforeEach(() => {
    api = new FakeApi();
    TestBed.configureTestingModule({ providers: [{ provide: ApiClient, useValue: api }] });
    client = TestBed.inject(SitesClient);
  });

  it('lists a page of the caller sites, sending only what was asked', async () => {
    api.answer = { ok: true, status: 200, value: { items: [SUMMARY], nextCursor: 'c2' } };
    const result = await client.list({ limit: 100, cursor: 'c1' });
    expect(api.calls[0]).toMatchObject({
      method: 'GET',
      path: 'sites',
      options: { query: { limit: 100, cursor: 'c1' } },
    });
    expect(result).toEqual({
      ok: true,
      status: 200,
      value: { items: [SUMMARY], nextCursor: 'c2' },
    });
    await client.list();
    expect(api.calls[1].options).toEqual({ query: {} });
  });

  it('reads one site by its slug, as one encoded segment', async () => {
    api.answer = { ok: true, status: 200, value: DETAIL };
    const result = await client.get('apple-pie');
    expect(api.calls[0]).toMatchObject({ method: 'GET', path: 'sites/apple-pie' });
    expect(result.ok && result.value).toEqual(DETAIL);
    await client.get('a/b?c');
    expect(api.calls[1].path).toBe('sites/a%2Fb%3Fc');
  });

  it('accepts an invitation with its token in the body and the link key as Idempotency-Key', async () => {
    api.answer = { ok: true, status: 200, value: SUMMARY };
    const result = await client.acceptInvitation('tok+en', 'invitation-1');
    expect(api.calls[0]).toEqual({
      method: 'POST',
      path: 'site-invitations/accept',
      options: { body: { token: 'tok+en' }, idempotencyKey: 'invitation-1' },
    });
    expect(result.ok && result.value).toEqual(SUMMARY);
  });

  it('passes a refusal through as it came', async () => {
    const refusal: ApiResult<unknown> = {
      ok: false,
      problem: { status: 404, code: 'site-not-found', title: "We couldn't find that site." },
    };
    api.answer = refusal;
    expect(await client.get('nope')).toEqual(refusal);
  });

  it('reads an answer of the wrong shape as unexpected, never as a site', async () => {
    api.answer = { ok: true, status: 200, value: { slug: 'x' } };
    const result = await client.get('x');
    expect(result.ok).toBe(false);
    expect(!result.ok && result.problem.code).toBe('unexpected-error');
  });
});

describe('the site readers', () => {
  it('read every role and status the API spells', () => {
    for (const role of ['owner', 'editor', 'author']) {
      expect(readSiteSummary({ ...SUMMARY, role })?.role).toBe(role);
    }
    for (const status of ['provisioning', 'live', 'suspended', 'read-only', 'offline', 'deleted']) {
      expect(readSiteSummary({ ...SUMMARY, status })?.status).toBe(status);
    }
  });

  it.each([
    { ...SUMMARY, role: 'Owner' },
    { ...SUMMARY, role: 0 },
    { ...SUMMARY, status: 'ReadOnly' },
    { ...SUMMARY, host: undefined },
    null,
    'apple-pie',
  ])('refuse a summary that is not one: %o', (body) => {
    expect(readSiteSummary(body)).toBeNull();
  });

  it('keep no field the API did not send for a summary, and none it should not, like a site id', () => {
    expect(readSiteSummary({ ...SUMMARY, siteId: 'abc' })).toEqual(SUMMARY);
  });

  it('read a detail, and refuse one missing a field', () => {
    expect(readSiteDetail(DETAIL)).toEqual(DETAIL);
    expect(readSiteDetail({ ...DETAIL, timeZone: undefined })).toBeNull();
  });

  it('read a page, its last page included, and refuse one with a bad item or cursor', () => {
    expect(readSitePage({ items: [], nextCursor: null })).toEqual({ items: [], nextCursor: null });
    expect(readSitePage({ items: [SUMMARY] })).toEqual({ items: [SUMMARY], nextCursor: null });
    expect(readSitePage({ items: [SUMMARY, { slug: 1 }], nextCursor: null })).toBeNull();
    expect(readSitePage({ items: [], nextCursor: 4 })).toBeNull();
    expect(readSitePage({ items: 'x' })).toBeNull();
  });
});
