import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideClientHydration } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { provideApi } from './core/api/provide-api';
import { provideRuntimeConfig } from './core/config/runtime-config.service';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    // A prerendered page is taken over where it stands rather than drawn again (PPL.2): the LCP element stays put. A
    // browser-rendered route starts from `index.csr.html`, which says there is nothing to hydrate. No event replay: it
    // writes an inline script into each document, which the gateway's policy blocks and refuses to serve (PPL.3).
    provideClientHydration(),
    provideApi(),
    provideRuntimeConfig(),
  ],
};
