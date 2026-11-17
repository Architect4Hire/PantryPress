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
            // No route title: the router would put it back on every search, filter, or tab change, dropping the site's
            // name; the screen sets its own (PP4.12b).
            path: 'recipes',
            loadComponent: () => import('../recipes/recipe-library').then((m) => m.RecipeLibrary),
          },
          {
            // `new` for a recipe not made yet, or the recipe's id (PP4.13). One route for both, so the screen stays the
            // same one when a new recipe is given its address. No route title: the screen sets its own, with the recipe's.
            path: 'recipes/:recipeId',
            loadComponent: () => import('../recipes/recipe-editor').then((m) => m.RecipeEditor),
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
