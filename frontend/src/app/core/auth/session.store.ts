import { Injectable, computed, signal } from '@angular/core';
import { SessionUser } from './auth-api';

/**
 * The signed-in user, held in memory only. Nothing about the session is written to
 * localStorage or sessionStorage. Populated by the auth flow once the contract is integrated.
 */
@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly _user = signal<SessionUser | null>(null);
  readonly user = this._user.asReadonly();
  readonly isAuthenticated = computed(() => this._user() !== null);

  hasRole(role: string): boolean {
    return this._user()?.roles.includes(role) ?? false;
  }

  hasPermission(permission: string): boolean {
    return this._user()?.permissions.includes(permission) ?? false;
  }

  set(user: SessionUser): void {
    this._user.set(user);
  }

  /** Clears everything about the session (logout, expiry). */
  clear(): void {
    this._user.set(null);
  }
}
