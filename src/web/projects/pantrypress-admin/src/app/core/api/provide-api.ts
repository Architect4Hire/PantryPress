import { provideHttpClient, withFetch, withXsrfConfiguration } from '@angular/common/http';
import { EnvironmentProviders } from '@angular/core';
import { XSRF_COOKIE_NAME, XSRF_HEADER_NAME } from './api-constants';

/**
 * The HTTP stack under `ApiClient` (PP3.7): the browser's `fetch`, and Angular's antiforgery interceptor reading the
 * gateway's request token from `__Host-pp-xsrf` and sending it in `X-PP-Xsrf` on every write (PP1.7). The interceptor
 * adds it only to same-origin writes, which every call under `API_BASE` is.
 */
export function provideApi(): EnvironmentProviders {
  return provideHttpClient(
    withFetch(),
    withXsrfConfiguration({ cookieName: XSRF_COOKIE_NAME, headerName: XSRF_HEADER_NAME }),
  );
}
