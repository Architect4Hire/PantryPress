import { RenderMode, ServerRoute } from '@angular/ssr';
import { PUBLIC_PATHS } from './public-paths';

/**
 * How each route is built (PP-D19, PPL.2). The build's `outputMode` is `static`, so nothing here runs at request
 * time: the public paths are prerendered into documents of their own, and every other route - `/dashboard` and the
 * sign-in screens above all - is the browser-rendered `index.csr.html`. No route renders on a server.
 */
export const serverRoutes: ServerRoute[] = [
  ...PUBLIC_PATHS.map((path): ServerRoute => ({ path, renderMode: RenderMode.Prerender })),
  { path: '**', renderMode: RenderMode.Client },
];
