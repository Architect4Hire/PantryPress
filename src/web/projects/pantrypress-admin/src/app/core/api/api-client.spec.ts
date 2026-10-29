import { HttpRequest } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiClient } from './api-client';
import { XSRF_COOKIE_NAME, XSRF_HEADER_NAME } from './api-constants';
import { apiPath, toApiPath } from './api-path';
import { ApiResult } from './api-problem';
import { provideApi } from './provide-api';

describe('ApiClient', () => {
  let client: ApiClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideApi(), provideHttpClientTesting()] });
    client = TestBed.inject(ApiClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    backend.verify();
    document.cookie = `${XSRF_COOKIE_NAME}=; Path=/; Secure; Expires=Thu, 01 Jan 1970 00:00:00 GMT`;
  });

  // A retry is sent after the first answer's promise settles, so the next request is looked for after a turn.
  async function nextRequest(): Promise<TestRequest> {
    await new Promise((resolve) => setTimeout(resolve));
    return backend.expectOne(() => true);
  }

  function problem(code: string, title = 'A plain title.'): object {
    return { type: 'about:blank', title, status: 400, code, traceId: '00-abc-def-01' };
  }

  describe('paths', () => {
    it('sends every call under /api/v1 with a JSON Accept', async () => {
      const answer = client.get<{ name: string }>(toApiPath('sites'));
      const request = backend.expectOne('/api/v1/sites');
      expect(request.request.method).toBe('GET');
      expect(request.request.headers.get('Accept')).toBe('application/json');
      request.flush({ name: 'Ada' });

      expect(await answer).toEqual({ ok: true, status: 200, value: { name: 'Ada' } });
    });

    it('sends a query as parameters, never in the path', async () => {
      const answer = client.get(toApiPath('sites'), { query: { limit: 20, cursor: 'a b' } });
      const request = backend.expectOne((r) => r.url === '/api/v1/sites');
      expect(request.request.urlWithParams).toBe('/api/v1/sites?limit=20&cursor=a%20b');
      request.flush([]);
      await answer;
    });

    it('answers a 204 with no value', async () => {
      const answer = client.delete<void>(toApiPath('account/session'));
      backend
        .expectOne('/api/v1/account/session')
        .flush(null, { status: 204, statusText: 'No Content' });

      expect(await answer).toEqual({ ok: true, status: 204, value: undefined });
    });
  });

  describe('what it sends', () => {
    it('sends no X-PP header but the write token, and no cookie header of its own', async () => {
      document.cookie = `${XSRF_COOKIE_NAME}=token-1; Path=/; Secure`;
      const read = client.get(toApiPath('sites'));
      const write = client.post(toApiPath('sites'), {
        body: { slug: 'ada' },
        idempotencyKey: 'k-1',
      });
      const requests = backend.match(() => true);
      expect(requests).toHaveLength(2);
      const own = requests.map((request) =>
        request.request.headers
          .keys()
          .map((name) => name.toLowerCase())
          .filter((name) => name.startsWith('x-pp-') || name === 'cookie'),
      );
      expect(own).toEqual([[], [XSRF_HEADER_NAME.toLowerCase()]]);
      requests.forEach((request) => request.flush({}));
      await Promise.all([read, write]);
    });

    it('sends the JSON body of a write', async () => {
      const answer = client.put(apiPath`sites/${'ada'}`, { body: { name: 'Ada' } });
      const request = backend.expectOne('/api/v1/sites/ada');
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ name: 'Ada' });
      request.flush({});
      await answer;
    });

    it('sends Idempotency-Key only when the caller gives one', async () => {
      const keyed = client.post(toApiPath('sites'), { idempotencyKey: '"quoted-key"' });
      const plain = client.patch(toApiPath('sites/ada'));
      const [first, second] = backend.match(() => true);
      expect(first.request.headers.get('Idempotency-Key')).toBe('"quoted-key"');
      expect(second.request.headers.has('Idempotency-Key')).toBe(false);
      first.flush({});
      second.flush({});
      await Promise.all([keyed, plain]);
    });

    it('refuses a key the server would refuse, before sending anything', async () => {
      // As ApiIdempotency.TryParseKey refuses them.
      const refused = [
        '',
        '"',
        '""',
        '"abc',
        'a"b',
        'a\\b',
        'a,b',
        'x'.repeat(129),
        `"${'x'.repeat(129)}"`,
        'has space',
        'naïve',
      ];
      for (const key of refused) {
        await expect(client.post(toApiPath('sites'), { idempotencyKey: key }), key).rejects.toThrow(
          /idempotency key/,
        );
      }
      // Nothing went out for those (afterEach verifies); the longest keys the server takes do.
      for (const key of ['x'.repeat(128), `"${'x'.repeat(128)}"`, '"a"', 'a']) {
        const answer = client.post(toApiPath('sites'), { idempotencyKey: key });
        backend.expectOne('/api/v1/sites').flush({});
        expect((await answer).ok, key).toBe(true);
      }
    });
  });

  describe('antiforgery', () => {
    it("echoes the gateway's request token in X-PP-Xsrf on a write", async () => {
      document.cookie = `${XSRF_COOKIE_NAME}=token-1; Path=/; Secure`;
      const answer = client.post(toApiPath('sites'), { body: {} });
      const request = backend.expectOne('/api/v1/sites');
      expect(request.request.headers.get(XSRF_HEADER_NAME)).toBe('token-1');
      request.flush({});
      await answer;
    });

    it('sends no token on a read', async () => {
      document.cookie = `${XSRF_COOKIE_NAME}=token-1; Path=/; Secure`;
      const answer = client.get(toApiPath('sites'));
      const request = backend.expectOne('/api/v1/sites');
      expect(request.request.headers.has(XSRF_HEADER_NAME)).toBe(false);
      request.flush([]);
      await answer;
    });

    it('sends a write refused as antiforgery-failed once more, with the fresh token', async () => {
      document.cookie = `${XSRF_COOKIE_NAME}=stale; Path=/; Secure`;
      const answer = client.post(toApiPath('sites'), {
        body: { slug: 'ada' },
        idempotencyKey: 'k-1',
      });
      const first = backend.expectOne('/api/v1/sites');
      expect(first.request.headers.get(XSRF_HEADER_NAME)).toBe('stale');
      // The gateway mints a fresh pair with its refusal.
      document.cookie = `${XSRF_COOKIE_NAME}=fresh; Path=/; Secure`;
      first.flush(problem('antiforgery-failed'), { status: 400, statusText: 'Bad Request' });

      const second = await nextRequest();
      expect(sent(second.request)).toEqual(sent(first.request));
      expect(second.request.headers.get(XSRF_HEADER_NAME)).toBe('fresh');
      second.flush({ slug: 'ada' }, { status: 201, statusText: 'Created' });

      expect(await answer).toEqual({ ok: true, status: 201, value: { slug: 'ada' } });
    });

    it('gives up after the one retry', async () => {
      const answer = client.delete(toApiPath('account/session'));
      backend.expectOne('/api/v1/account/session').flush(problem('antiforgery-failed'), badRequest);
      (await nextRequest()).flush(problem('antiforgery-failed'), badRequest);

      expect(await failure(answer)).toMatchObject({ status: 400, code: 'antiforgery-failed' });
    });

    it('never retries a read, another 400, or the same code at another status', async () => {
      const read = client.get(toApiPath('sites'));
      backend.expectOne('/api/v1/sites').flush(problem('antiforgery-failed'), badRequest);
      const other = client.post(toApiPath('sites'));
      backend.expectOne('/api/v1/sites').flush(problem('slug'), badRequest);
      const forbidden = client.post(toApiPath('sites'));
      backend
        .expectOne('/api/v1/sites')
        .flush(problem('antiforgery-failed'), { status: 403, statusText: 'Forbidden' });

      await Promise.all([read, other, forbidden]);
      // A retry would go out after a turn; afterEach's verify then finds it unanswered.
      await new Promise((resolve) => setTimeout(resolve));
    });
  });

  describe('failures are values', () => {
    it("reads a problem document's code, title, field errors, Retry-After, and trace id", async () => {
      const answer = client.post(toApiPath('account/sign-up'));
      backend.expectOne('/api/v1/account/sign-up').flush(
        {
          ...problem('validation-failed', 'Check the highlighted fields.'),
          status: 429,
          errors: { email: ['Enter an email address.'], ignored: [1] },
        },
        { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '42' } },
      );

      expect(await failure(answer)).toEqual({
        status: 429,
        code: 'validation-failed',
        title: 'Check the highlighted fields.',
        errors: { email: ['Enter an email address.'] },
        retryAfter: 42,
        traceId: '00-abc-def-01',
      });
    });

    it("takes the status from the answer, not the body's claim", async () => {
      const answer = client.get(toApiPath('sites/ada'));
      backend
        .expectOne('/api/v1/sites/ada')
        .flush({ ...problem('site-not-found'), status: 200 }, notFound);

      expect(await failure(answer)).toMatchObject({ status: 404, code: 'site-not-found' });
    });

    it('reads an answer that is not a problem document as unexpected-error', async () => {
      const answer = client.get(toApiPath('sites'));
      backend.expectOne('/api/v1/sites').flush('<html>Bad gateway</html>', {
        status: 502,
        statusText: 'Bad Gateway',
        headers: { 'Retry-After': 'soon' },
      });

      expect(await failure(answer)).toEqual({
        status: 502,
        code: 'unexpected-error',
        title: 'Something went wrong on our side. Please try again in a moment.',
      });
    });

    it('keeps the code and trace id of a problem that came without a title', async () => {
      const answer = client.post(toApiPath('sites'));
      backend
        .expectOne('/api/v1/sites')
        .flush(
          { status: 409, code: 'slug-unavailable', traceId: 't-1' },
          { status: 409, statusText: 'Conflict' },
        );
      expect(await failure(answer)).toEqual({
        status: 409,
        code: 'slug-unavailable',
        title: "That didn't work. Please try again.",
        traceId: 't-1',
      });

      const fault = client.get(toApiPath('sites'));
      backend
        .expectOne('/api/v1/sites')
        .flush({ status: 503, traceId: 't-2' }, { status: 503, statusText: 'Down' });
      expect(await failure(fault)).toEqual({
        status: 503,
        code: 'unexpected-error',
        title: 'Something went wrong on our side. Please try again in a moment.',
        traceId: 't-2',
      });
    });

    it('reads an empty errors object as a plain refusal', async () => {
      const answer = client.post(toApiPath('sites'));
      backend
        .expectOne('/api/v1/sites')
        .flush({ ...problem('bad-request'), errors: {} }, badRequest);

      expect(await failure(answer)).not.toHaveProperty('errors');
    });

    it('reads no answer at all as unreachable', async () => {
      const answer = client.get(toApiPath('sites'));
      backend.expectOne('/api/v1/sites').error(new ProgressEvent('error'));

      expect(await failure(answer)).toEqual({
        status: 0,
        code: 'unreachable',
        title: "We couldn't reach PantryPress. Check your connection and try again.",
      });
    });
  });
});

describe('API paths', () => {
  it('accepts a path under the admin surface', () => {
    expect(toApiPath('sites/ada/members')).toBe('sites/ada/members');
    expect(toApiPath('slug-availability')).toBe('slug-availability');
  });

  it('refuses anything that could leave its route', () => {
    for (const path of [
      '',
      '/sites',
      'sites/',
      'sites//ada',
      '../internal/v1/sessions/current',
      'sites/./ada',
      'sites/%2e%2e/%2E%2E/internal/v1/sessions/current',
      'sites/.%2e/ada',
      'sites/%2e./ada',
      'sites/%2e/ada',
      'sites?limit=1',
      'sites#top',
      'https://example.com/sites',
      'sites\\ada',
    ]) {
      expect(() => toApiPath(path), path).toThrow(/Not a path/);
    }
  });

  it('encodes each value as exactly one segment', () => {
    expect(apiPath`sites/${'ada'}/members/${42}`).toBe('sites/ada/members/42');
    expect(apiPath`sites/${'a/b?c#d'}`).toBe('sites/a%2Fb%3Fc%23d');
    expect(apiPath`sites/${"it's (mine)!*"}`).toBe('sites/it%27s%20%28mine%29%21%2A');
  });

  it('refuses a value that would leave or climb out of its segment', () => {
    for (const value of ['', '.', '..']) {
      expect(() => apiPath`sites/${value}/members`, value).toThrow(/Not a path segment/);
    }
  });
});

const badRequest = { status: 400, statusText: 'Bad Request' };
const notFound = { status: 404, statusText: 'Not Found' };

async function failure<T>(answer: Promise<ApiResult<T>>) {
  const result = await answer;
  if (result.ok) {
    throw new Error(`Expected a failure, got ${result.status}.`);
  }
  return result.problem;
}

// What a request carries, minus the token the retry is meant to change.
function sent(request: HttpRequest<unknown>) {
  const headers = Object.fromEntries(
    request.headers
      .keys()
      .filter((name) => name !== XSRF_HEADER_NAME)
      .map((name) => [name, request.headers.getAll(name)]),
  );
  return { method: request.method, url: request.urlWithParams, body: request.body, headers };
}
