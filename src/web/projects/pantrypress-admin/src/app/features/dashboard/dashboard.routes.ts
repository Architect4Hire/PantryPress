import { Routes } from '@angular/router';

/**
 * Everything signed in, under `/dashboard` (PP-D19, PPL.2). Rendered in the browser only, never prerendered, and behind
 * the session guard on its parent (PP3.8). Every address under it matches here - an unknown one gets the not-found page
 * as a child - so the guard runs for all of them, and an anonymous visitor is sent to sign in rather than told what
 * exists. PP3.9 adds the shell.
 */
const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'Dashboard - PantryPress',
    loadComponent: () => import('./dashboard-home').then((m) => m.DashboardHome),
  },
  {
    path: '**',
    title: 'Page not found - PantryPress',
    loadComponent: () => import('../not-found/not-found').then((m) => m.NotFound),
  },
];

export default routes;
