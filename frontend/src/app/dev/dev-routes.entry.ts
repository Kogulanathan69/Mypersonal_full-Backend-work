import { Routes } from '@angular/router';

/**
 * Dev review routes (UI gallery, layout previews) for development and mock builds.
 * The production build swaps this file for dev-routes.entry.prod.ts (angular.json
 * fileReplacements), so none of this code ships to users.
 */
export const DEV_ROUTES: Routes = [
  {
    path: 'dev',
    loadChildren: () => import('./dev.routes').then((m) => m.DEV_CHILD_ROUTES),
  },
];
