import { RenderMode, ServerRoute } from '@angular/ssr';

// Every public route renders per request (PP-D02): the page depends on the request's Host, so nothing is prerendered
// at build time.
export const serverRoutes: ServerRoute[] = [
  {
    path: '**',
    renderMode: RenderMode.Server,
  },
];
