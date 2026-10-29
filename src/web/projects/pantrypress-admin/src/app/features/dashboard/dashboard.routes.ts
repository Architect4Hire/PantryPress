import { Routes } from '@angular/router';

/**
 * Everything signed in, under `/dashboard` (PP-D19, PPL.2). Rendered in the browser only, never prerendered, and behind
 * the session guard on its parent (PP3.8). Every address under it matches here - an unknown one gets the dashboard's
 * not-found page - so the guard runs for all of them, and an anonymous visitor is sent to sign in rather than told what
 * exists.
 *
 * The shell (PP3.9) holds every screen. A site's screens are under `sites/{siteSlug}`, as the API's are, so a slug never
 * sits beside a word of the dashboard's own (`join`, `account`) where a site could take the same word.
 */
const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./dashboard-shell').then((m) => m.DashboardShell),
    children: [
      {
        path: '',
        pathMatch: 'full',
        title: 'Your sites - PantryPress',
        loadComponent: () => import('./dashboard-home').then((m) => m.DashboardHome),
      },
      {
        path: 'join',
        title: 'Join a site - PantryPress',
        loadComponent: () => import('./join-site').then((m) => m.JoinSite),
      },
      {
        path: 'sites/:siteSlug',
        loadComponent: () => import('./site-area').then((m) => m.SiteArea),
        children: [
          {
            path: '',
            pathMatch: 'full',
            title: 'Your site - PantryPress',
            loadComponent: () => import('./site-home').then((m) => m.SiteHome),
          },
          {
            path: '**',
            title: 'Page not found - PantryPress',
            loadComponent: () => import('./dashboard-not-found').then((m) => m.DashboardNotFound),
          },
        ],
      },
      {
        path: '**',
        title: 'Page not found - PantryPress',
        loadComponent: () => import('./dashboard-not-found').then((m) => m.DashboardNotFound),
      },
    ],
  },
];

export default routes;
