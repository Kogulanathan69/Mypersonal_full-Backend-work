import { inject } from '@angular/core';
import { CanMatchFn, Router, UrlSegment } from '@angular/router';
import { PERMISSION_CHECKER } from './permission';
import { SessionStore } from './session.store';

/** Signed-in users only; others go to sign-in with a safe return URL. Frontend guards are UX only. */
export const authGuard: CanMatchFn = (_route, segments: UrlSegment[]) => {
  const session = inject(SessionStore);
  if (session.isAuthenticated()) return true;
  const returnUrl = '/' + segments.map((s) => s.path).join('/');
  return inject(Router).createUrlTree(['/auth/sign-in'], { queryParams: { returnUrl } });
};

/** Sign-in and registration pages are for signed-out visitors. */
export const guestGuard: CanMatchFn = () => {
  const session = inject(SessionStore);
  return session.isAuthenticated() ? inject(Router).createUrlTree(['/']) : true;
};

/**
 * Requires a permission issued by the backend. Permission names are taken from the verified
 * permission matrix when it is supplied; none are invented here.
 */
export function permissionGuard(permission: string): CanMatchFn {
  return () => {
    const allowed = inject(PERMISSION_CHECKER)(permission);
    return allowed ? true : inject(Router).createUrlTree(['/forbidden']);
  };
}
