import { Routes } from '@angular/router';

/**
 * The development build's stand-in for `dev-routes.ts` (PP3.6): the `@pantrypress/ui` showcase, every primitive in
 * every state and both modes. It is lazy, so even a development build loads it only when asked.
 */
export const DEV_ROUTES: Routes = [
  {
    path: 'showcase',
    title: 'Component showcase - PantryPress',
    loadComponent: () => import('./features/showcase/showcase').then((m) => m.Showcase),
  },
];
