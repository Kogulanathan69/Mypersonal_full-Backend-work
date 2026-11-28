import { InjectionToken, inject } from '@angular/core';
import { SessionStore } from './session.store';

/**
 * Permission names are proposals until the backend role/claim contract is verified.
 * Front-end checks only hide or disable UI; the backend must enforce every permission.
 */
export type Permission = string;

export type PermissionChecker = (permission: Permission) => boolean;

/**
 * Checks permissions issued by the backend for the signed-in user (deny when signed out).
 * Permission names come from the verified backend permission matrix; none are hard-coded.
 */
export const PERMISSION_CHECKER = new InjectionToken<PermissionChecker>('PERMISSION_CHECKER', {
  providedIn: 'root',
  factory: () => {
    const session = inject(SessionStore);
    return (permission: Permission) => session.hasPermission(permission);
  },
});
