import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ApiClient } from '../api/api-client';
import { ApiPath } from '../api/api-path';
import { ApiResult } from '../api/api-problem';
import { readRuntimeConfig, RUNTIME_CONFIG_DEFAULTS } from './runtime-config';
import { RUNTIME_CONFIG_PATH, RuntimeConfigService } from './runtime-config.service';

// The service is tested over a stand-in ApiClient: how a call reaches the BFF is ApiClient's own spec.
class FakeApi {
  readonly reads: ApiPath[] = [];
  private answer: (value: ApiResult<unknown>) => void = () => undefined;

  get<T>(path: ApiPath): Promise<ApiResult<T>> {
    this.reads.push(path);
    return new Promise((resolve) => (this.answer = resolve as (value: ApiResult<unknown>) => void));
  }

  answers(result: ApiResult<unknown>): void {
    this.answer(result);
  }
}

function setUp(platform: 'browser' | 'server') {
  const api = new FakeApi();
  TestBed.configureTestingModule({
    providers: [
      { provide: ApiClient, useValue: api },
      { provide: PLATFORM_ID, useValue: platform },
    ],
  });
  return { api, service: TestBed.inject(RuntimeConfigService) };
}

describe('RuntimeConfigService', () => {
  it('holds the defaults while the read is out, then the read', async () => {
    const { api, service } = setUp('browser');
    const loaded = service.load();
    expect(service.status()).toBe('loading');
    expect(service.config()).toEqual(RUNTIME_CONFIG_DEFAULTS);

    api.answers({ ok: true, status: 200, value: {} });
    await loaded;

    expect(api.reads).toEqual([RUNTIME_CONFIG_PATH]);
    expect(RUNTIME_CONFIG_PATH).toBe('client-configuration');
    expect(service.status()).toBe('ready');
    expect(service.config()).toEqual(RUNTIME_CONFIG_DEFAULTS);
  });

  it('keeps the defaults when the server does not publish it yet (404) or cannot be reached', async () => {
    for (const problem of [
      { status: 404, code: 'not-found', title: "We couldn't find that." },
      { status: 0, code: 'unreachable', title: "We couldn't reach PantryPress." },
    ]) {
      TestBed.resetTestingModule();
      const { api, service } = setUp('browser');
      const loaded = service.load();
      api.answers({ ok: false, problem });
      await loaded;

      expect(service.status()).toBe('unavailable');
      expect(service.config()).toEqual(RUNTIME_CONFIG_DEFAULTS);
    }
  });

  it('keeps the defaults when the answer is not a JSON object', async () => {
    const { api, service } = setUp('browser');
    const loaded = service.load();
    api.answers({ ok: true, status: 200, value: ['not', 'an', 'object'] });
    await loaded;

    expect(service.status()).toBe('unavailable');
  });

  it('reads once however many ask', async () => {
    const { api, service } = setUp('browser');
    const first = service.load();
    const second = service.load();
    expect(second).toBe(first);
    api.answers({ ok: true, status: 200, value: {} });
    await Promise.all([first, second, service.load()]);

    expect(api.reads).toHaveLength(1);
  });

  it('never reads on the server, so a prerendered page is drawn from the defaults', async () => {
    const { api, service } = setUp('server');
    await service.load();

    expect(api.reads).toEqual([]);
    expect(service.status()).toBe('loading');
    expect(service.config()).toEqual(RUNTIME_CONFIG_DEFAULTS);
  });
});

describe('readRuntimeConfig', () => {
  it('keeps only the fields the app knows', () => {
    expect(readRuntimeConfig({ somethingNew: true, publicSignUp: 'yes' })).toEqual(
      RUNTIME_CONFIG_DEFAULTS,
    );
  });

  it('reads nothing from a body that is not an object', () => {
    for (const body of [null, undefined, 'text', 42, [], true]) {
      expect(readRuntimeConfig(body)).toBeNull();
    }
  });
});
