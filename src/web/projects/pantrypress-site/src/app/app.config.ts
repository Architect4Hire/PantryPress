import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideClientHydration, withIncrementalHydration } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';

// Incremental hydration (PP-D08): pages stay server HTML; only @defer (hydrate on …) islands hydrate.
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideClientHydration(withIncrementalHydration()),
  ],
};
