import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { ApiClient } from './api-client';
import { toApiPath } from './api-path';
import { refuseOnServer } from './refuse-on-server';

function setUp(platform: 'browser' | 'server') {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptors([refuseOnServer])),
      provideHttpClientTesting(),
      { provide: PLATFORM_ID, useValue: platform },
    ],
  });
  return { http: TestBed.inject(HttpClient), backend: TestBed.inject(HttpTestingController) };
}

describe('refuseOnServer', () => {
  it('lets a request through in the browser', async () => {
    const { http, backend } = setUp('browser');
    const answer = firstValueFrom(http.get('/api/v1/client-configuration'));
    backend.expectOne('/api/v1/client-configuration').flush({});
    await expect(answer).resolves.toEqual({});
  });

  it('refuses every request while prerendering, before it leaves', async () => {
    const { http, backend } = setUp('server');
    await expect(firstValueFrom(http.get('/api/v1/client-configuration'))).rejects.toThrow(
      'GET /api/v1/client-configuration was asked for while prerendering',
    );
    backend.verify();
  });

  it('is thrown by ApiClient, never drawn as a refusal', async () => {
    setUp('server');
    await expect(TestBed.inject(ApiClient).get(toApiPath('client-configuration'))).rejects.toThrow(
      /while prerendering/,
    );
  });
});
