import { Routes } from '@angular/router';
import { DEV_ROUTES } from './dev/dev-routes.entry';
import { authGuard } from './core/auth/auth.guards';

/**
 * Root routes. Owner: F — Foundation.
 * Public pages render inside the public layout. Customer (/app), Admin (/admin), Staff and
 * Architect branches are registered once their backend contracts are available.
 */
export const routes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./layouts/public-layout/public-layout').then((m) => m.PublicLayout),
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () => import('./features/public/home/home-page').then((m) => m.HomePage),
      },
      {
        path: 'about',
        title: 'About',
        loadComponent: () => import('./features/public/pages/about-page').then((m) => m.AboutPage),
      },
      {
        path: 'contact',
        title: 'Help and contact',
        loadComponent: () =>
          import('./features/public/pages/contact-page').then((m) => m.ContactPage),
      },
      {
        path: 'privacy',
        title: 'Privacy information',
        loadComponent: () =>
          import('./features/public/pages/policy-pages').then((m) => m.PrivacyPage),
      },
      {
        path: 'terms',
        title: 'Terms of use',
        loadComponent: () =>
          import('./features/public/pages/policy-pages').then((m) => m.TermsPage),
      },
    ],
  },
  {
    path: 'auth',
    loadComponent: () => import('./layouts/auth-layout/auth-layout').then((m) => m.AuthLayout),
    loadChildren: () => import('./features/auth/auth.routes').then((m) => m.AUTH_ROUTES),
  },
  {
    path: 'app',
    canMatch: [authGuard],
    loadComponent: () =>
      import('./features/customer/customer-shell').then((m) => m.CustomerShell),
    loadChildren: () =>
      import('./features/customer/customer.routes').then((m) => m.CUSTOMER_ROUTES),
  },
  {
    path: 'architect',
    canMatch: [authGuard],
    loadComponent: () => import('./features/architect/architect-shell').then((m) => m.ArchitectShell),
    loadChildren: () => import('./features/architect/architect.routes').then((m) => m.ARCHITECT_ROUTES),
  },
  {
    path: 'staff',
    canMatch: [authGuard],
    loadComponent: () => import('./features/staff/staff-shell').then((m) => m.StaffShell),
    loadChildren: () => import('./features/staff/staff.routes').then((m) => m.STAFF_ROUTES),
  },
  {
    path: 'admin',
    canMatch: [authGuard],
    loadComponent: () => import('./features/admin/admin-shell').then((m) => m.AdminShell),
    loadChildren: () => import('./features/admin/admin.routes').then((m) => m.ADMIN_ROUTES),
  },
  {
    path: 'forbidden',
    title: 'Access denied',
    loadComponent: () => import('./features/system/status-pages').then((m) => m.Forbidden),
  },
  {
    path: 'session-ended',
    title: 'Session ended',
    loadComponent: () => import('./features/system/status-pages').then((m) => m.SessionEnded),
  },
  ...DEV_ROUTES,
  {
    path: '**',
    title: 'Page not found',
    loadComponent: () => import('./features/system/not-found/not-found').then((m) => m.NotFound),
  },
];
