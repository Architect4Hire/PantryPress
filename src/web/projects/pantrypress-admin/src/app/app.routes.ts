import { Routes } from '@angular/router';
import { signedInGuard, signedOutGuard } from './core/session/session-guards';
import { DEV_ROUTES } from './dev-routes';
import { Landing } from './features/landing/landing';

const notFound = () => import('./features/not-found/not-found').then((m) => m.NotFound);
const legal = () => import('./features/legal/legal-placeholder').then((m) => m.LegalPlaceholder);

/**
 * The apex's route table (PP-D19, PPL.2). The public paths are `PUBLIC_PATHS` and are prerendered; every other route
 * is rendered in the browser (`app.routes.server.ts`). The development-only routes come first, so the not-found route
 * at the end never shadows one.
 */
export const routes: Routes = [
  ...DEV_ROUTES,
  // The landing page is the one eager route: it holds the LCP element, and a lazy chunk would only delay it.
  { path: '', pathMatch: 'full', title: 'PantryPress', component: Landing },
  {
    path: 'pricing',
    title: 'Pricing - PantryPress',
    component: Landing,
    data: { title: 'Pricing', scrollTo: 'pricing' },
  },
  // Placeholders until PPL.19 renders the documents PP19.13 writes.
  {
    path: 'terms',
    title: 'Terms of service - PantryPress',
    loadComponent: legal,
    data: { title: 'Terms of service' },
  },
  {
    path: 'privacy',
    title: 'Privacy policy - PantryPress',
    loadComponent: legal,
    data: { title: 'Privacy policy' },
  },
  {
    path: 'acceptable-use',
    title: 'Acceptable use policy - PantryPress',
    loadComponent: legal,
    data: { title: 'Acceptable use policy' },
  },
  {
    path: 'dmca',
    title: 'Copyright and DMCA - PantryPress',
    loadComponent: legal,
    data: { title: 'Copyright and DMCA' },
  },
  // The account screens (PP3.8), drawn in the browser. Sign-in and sign-up are for someone signed out; the two pages
  // an emailed link opens work either way, since sending a signed-in visitor on first would lose the link.
  {
    path: 'sign-in',
    title: 'Sign in - PantryPress',
    canActivate: [signedOutGuard],
    loadComponent: () => import('./features/auth/sign-in').then((m) => m.SignIn),
  },
  {
    path: 'sign-up',
    title: 'Create your account - PantryPress',
    canActivate: [signedOutGuard],
    loadComponent: () => import('./features/auth/sign-up').then((m) => m.SignUp),
  },
  {
    path: 'verify-email',
    title: 'Confirm your email address - PantryPress',
    loadComponent: () => import('./features/auth/verify-email').then((m) => m.VerifyEmail),
  },
  {
    path: 'forgot-password',
    title: 'Reset your password - PantryPress',
    loadComponent: () => import('./features/auth/forgot-password').then((m) => m.ForgotPassword),
  },
  {
    path: 'reset-password',
    title: 'Choose a new password - PantryPress',
    loadComponent: () => import('./features/auth/reset-password').then((m) => m.ResetPassword),
  },
  // Everything signed in, behind the gateway's word on the session (PP3.8). PP3.9 adds the shell.
  {
    path: 'dashboard',
    canActivate: [signedInGuard],
    loadChildren: () => import('./features/dashboard/dashboard.routes'),
  },
  { path: '**', title: 'Page not found - PantryPress', loadComponent: notFound },
];
