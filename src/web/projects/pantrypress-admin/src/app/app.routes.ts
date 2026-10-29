import { Routes } from '@angular/router';
import { DEV_ROUTES } from './dev-routes';

// The development-only routes come first, so a not-found route added after them (PPL.2) never shadows one.
export const routes: Routes = [...DEV_ROUTES];
