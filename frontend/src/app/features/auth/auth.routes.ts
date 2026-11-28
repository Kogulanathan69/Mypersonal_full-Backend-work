import { Routes } from '@angular/router';
import { guestGuard } from '../../core/auth/auth.guards';

/** Authentication pages inside the auth layout. Owner: F — Foundation. */
export const AUTH_ROUTES: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'sign-in' },
  {
    path: 'sign-in',
    title: 'Sign in',
    canMatch: [guestGuard],
    loadComponent: () => import('./sign-in').then((m) => m.SignIn),
  },
  {
    path: 'register',
    title: 'Create account',
    canMatch: [guestGuard],
    loadComponent: () => import('./register').then((m) => m.Register),
  },
  {
    path: 'verify',
    title: 'Verify email',
    loadComponent: () => import('./verify-email').then((m) => m.VerifyEmail),
  },
  {
    path: 'forgot-password',
    title: 'Reset password',
    loadComponent: () => import('./forgot-password').then((m) => m.ForgotPassword),
  },
  {
    path: 'reset-password',
    title: 'Choose a new password',
    loadComponent: () => import('./reset-password').then((m) => m.ResetPassword),
  },
];
