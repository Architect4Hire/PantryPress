import { isPlatformBrowser } from '@angular/common';
import {
  EnvironmentProviders,
  inject,
  Injectable,
  PLATFORM_ID,
  provideAppInitializer,
  signal,
} from '@angular/core';
import { ApiClient } from '../api/api-client';
import { toApiPath } from '../api/api-path';
import { readRuntimeConfig, RUNTIME_CONFIG_DEFAULTS, RuntimeConfig } from './runtime-config';

/**
 * Where the server publishes the document, under `API_BASE`. ApiService answers it from PPL.13 on; until then the
 * gateway forwards the read and the API's 404 leaves the defaults in place.
 */
export const RUNTIME_CONFIG_PATH = toApiPath('client-configuration');

export type RuntimeConfigStatus = 'loading' | 'ready' | 'unavailable';

/**
 * The server's runtime configuration (PP3.7), read once in the browser and never on the server: a prerendered page
 * reads nothing the server publishes (frontend.md), so it is drawn from the defaults and the browser fills in the rest
 * after the first paint. Reading it never holds up the app. When it cannot be read, the defaults stay - the closed or
 * hidden state of every switch - and `status` says `unavailable`; a reload is the retry.
 *
 * Screens draw from `config()` alone. `status` is for diagnostics and tests: it stays `loading` on the server, so a
 * screen that drew a waiting state from it would prerender that state, and then move when the read landed.
 */
@Injectable({ providedIn: 'root' })
export class RuntimeConfigService {
  private readonly api = inject(ApiClient);
  private readonly browser = isPlatformBrowser(inject(PLATFORM_ID));
  private readonly current = signal<RuntimeConfig>(RUNTIME_CONFIG_DEFAULTS);
  private readonly state = signal<RuntimeConfigStatus>('loading');
  private loading: Promise<void> | undefined;

  readonly config = this.current.asReadonly();
  readonly status = this.state.asReadonly();

  /** Starts the one read, and answers when it is over. Later calls share it; on the server it does nothing. */
  load(): Promise<void> {
    if (!this.browser) {
      return Promise.resolve();
    }
    this.loading ??= this.read();
    return this.loading;
  }

  private async read(): Promise<void> {
    const result = await this.api.get<unknown>(RUNTIME_CONFIG_PATH);
    const config = result.ok ? readRuntimeConfig(result.value) : null;
    if (config === null) {
      this.state.set('unavailable');
      return;
    }
    this.current.set(config);
    this.state.set('ready');
  }
}

/** Starts the read as the app starts, without waiting for it: nothing on screen depends on it to appear. */
export function provideRuntimeConfig(): EnvironmentProviders {
  return provideAppInitializer(() => {
    void inject(RuntimeConfigService).load();
  });
}
