import { isPlatformBrowser } from '@angular/common';
import { HttpInterceptorFn } from '@angular/common/http';
import { inject, PLATFORM_ID } from '@angular/core';
import { throwError } from 'rxjs';

/**
 * No request leaves a prerender (PPL.2, frontend.md): a prerendered page reads no session, account, site, or price, so
 * a call made while the build renders one is a bug. It is refused with a plain error, never an `HttpErrorResponse`, so
 * `ApiClient` throws it rather than turning it into a refusal a screen would quietly draw into the static page.
 */
export const refuseOnServer: HttpInterceptorFn = (request, next) =>
  isPlatformBrowser(inject(PLATFORM_ID))
    ? next(request)
    : throwError(
        () =>
          new Error(
            `${request.method} ${request.url} was asked for while prerendering; a prerendered page makes no call.`,
          ),
      );
