import { Routes } from '@angular/router';

/** Dev-only review pages. Owner: F — Foundation. Not shipped in production builds. */
export const DEV_CHILD_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'ui' },
  {
    path: 'ui',
    title: 'UI gallery (dev)',
    loadComponent: () => import('./ui-gallery/ui-gallery').then((m) => m.UiGallery),
  },
  {
    path: 'layouts',
    title: 'Layout previews (dev)',
    loadComponent: () => import('./layout-preview/layout-index').then((m) => m.LayoutIndex),
  },
  {
    path: 'layouts/public',
    loadComponent: () =>
      import('../layouts/public-layout/public-layout').then((m) => m.PublicLayout),
    children: [
      {
        path: '',
        title: 'Public layout (dev)',
        data: { name: 'Public', padded: true },
        loadComponent: () =>
          import('./layout-preview/preview-content').then((m) => m.PreviewContent),
      },
    ],
  },
  {
    path: 'layouts/auth',
    loadComponent: () => import('../layouts/auth-layout/auth-layout').then((m) => m.AuthLayout),
    children: [
      {
        path: '',
        title: 'Auth layout (dev)',
        loadComponent: () => import('./layout-preview/auth-preview').then((m) => m.AuthPreview),
      },
    ],
  },
  {
    path: 'layouts/customer',
    loadComponent: () =>
      import('../layouts/customer-layout/customer-layout').then((m) => m.CustomerLayout),
    children: [
      {
        path: '',
        title: 'Customer layout (dev)',
        data: { name: 'Customer' },
        loadComponent: () =>
          import('./layout-preview/preview-content').then((m) => m.PreviewContent),
      },
      {
        path: 'wizard',
        title: 'Wizard frame (dev)',
        loadComponent: () => import('./layout-preview/wizard-preview').then((m) => m.WizardPreview),
      },
    ],
  },
  {
    path: 'layouts/admin',
    loadComponent: () => import('../layouts/admin-layout/admin-layout').then((m) => m.AdminLayout),
    children: [
      {
        path: '',
        title: 'Admin layout (dev)',
        data: { name: 'Admin' },
        loadComponent: () =>
          import('./layout-preview/preview-content').then((m) => m.PreviewContent),
      },
    ],
  },
];
