import { HttpClient, HttpErrorResponse, HttpHeaders, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ANTIFORGERY_FAILED_CODE, API_BASE } from './api-constants';
import { ApiPath } from './api-path';
import { ApiResult, readProblem } from './api-problem';

export type ApiQuery = Readonly<Record<string, string | number | boolean>>;

export interface ApiReadOptions {
  readonly query?: ApiQuery;
}

export interface ApiWriteOptions extends ApiReadOptions {
  readonly body?: unknown;
  /**
   * Sent as `Idempotency-Key` on a route that opts in (PP1.9). The caller makes one key per thing the person did and
   * sends the same one again when it retries that thing, so a retry after a lost answer gets the first answer.
   */
  readonly idempotencyKey?: string;
}

type WriteMethod = 'POST' | 'PUT' | 'PATCH' | 'DELETE';

// api-contract.md: 1 to 128 visible ASCII characters, bare or quoted, as ApiIdempotency.TryParseKey reads it - one
// pair of wrapping quotes comes off, and a quote, backslash, or comma is never part of a key. A key the server would
// refuse is a bug in the caller.
const IDEMPOTENCY_KEY = /^[\x21\x23-\x2b\x2d-\x5b\x5d-\x7e]{1,128}$/;

function isIdempotencyKey(key: string): boolean {
  const bare = key.length >= 2 && key.startsWith('"') && key.endsWith('"') ? key.slice(1, -1) : key;
  return IDEMPOTENCY_KEY.test(bare);
}

/**
 * The admin app's one way to the BFF (PP3.7, frontend.md), and the only class in the app that injects `HttpClient`:
 * a lint rule keeps `@angular/common/http` inside `core/api/`. Feature clients are written on it from the contracts,
 * one typed method per route, and screens use those.
 *
 * - Every call is same-origin under `API_BASE`, so the browser sends the gateway's session cookie and nothing else
 *   holds a token (ACCT-002). It sends no site, and no `X-PP-*` header but the antiforgery token: the gateway strips
 *   the rest and adds the identity itself, and a site comes only from the route's `{siteSlug}` (tenancy.md rule 7).
 * - Writes carry that token, `X-PP-Xsrf`, through Angular's own interceptor, configured by `provideApi()`.
 * - Every answer is an `ApiResult`: a refusal or an outage is a value to show, never a thrown error.
 * - A write the gateway refuses as `antiforgery-failed` is sent once more. The gateway refuses it before forwarding and
 *   mints a fresh pair with the refusal (`gateway.md`), so the retry is safe and a stale token costs no reload. That
 *   rests on the code being the gateway's alone: ApiService declares no `antiforgery-failed`, so nothing that reached
 *   the API can answer it. Nothing else is retried here.
 */
@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);

  get<T>(path: ApiPath, options: ApiReadOptions = {}): Promise<ApiResult<T>> {
    return this.send<T>('GET', path, options);
  }

  post<T>(path: ApiPath, options: ApiWriteOptions = {}): Promise<ApiResult<T>> {
    return this.write<T>('POST', path, options);
  }

  put<T>(path: ApiPath, options: ApiWriteOptions = {}): Promise<ApiResult<T>> {
    return this.write<T>('PUT', path, options);
  }

  patch<T>(path: ApiPath, options: ApiWriteOptions = {}): Promise<ApiResult<T>> {
    return this.write<T>('PATCH', path, options);
  }

  delete<T>(path: ApiPath, options: ApiWriteOptions = {}): Promise<ApiResult<T>> {
    return this.write<T>('DELETE', path, options);
  }

  private async write<T>(
    method: WriteMethod,
    path: ApiPath,
    options: ApiWriteOptions,
  ): Promise<ApiResult<T>> {
    if (options.idempotencyKey !== undefined && !isIdempotencyKey(options.idempotencyKey)) {
      throw new Error(
        'An idempotency key is 1 to 128 visible ASCII characters, bare or quoted, with no quote, backslash, or comma.',
      );
    }

    const first = await this.send<T>(method, path, options);
    if (
      first.ok ||
      first.problem.status !== 400 ||
      first.problem.code !== ANTIFORGERY_FAILED_CODE
    ) {
      return first;
    }
    return this.send<T>(method, path, options);
  }

  private async send<T>(
    method: string,
    path: ApiPath,
    options: ApiWriteOptions,
  ): Promise<ApiResult<T>> {
    let headers = new HttpHeaders({ Accept: 'application/json' });
    if (options.idempotencyKey !== undefined) {
      headers = headers.set('Idempotency-Key', options.idempotencyKey);
    }

    try {
      const response = await firstValueFrom(
        this.http.request<T>(method, `${API_BASE}/${path}`, {
          body: options.body,
          headers,
          params:
            options.query === undefined ? undefined : new HttpParams({ fromObject: options.query }),
          observe: 'response',
          responseType: 'json',
        }),
      );
      // A 204 has no body; its caller asks for `ApiResult<void>`.
      return { ok: true, status: response.status, value: (response.body ?? undefined) as T };
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        return {
          ok: false,
          problem: readProblem(error.status, error.error, error.headers.get('Retry-After')),
        };
      }
      throw error;
    }
  }
}
