import { Injectable, signal } from '@angular/core';

/**
 * Ephemeral authentication credentials. Tokens deliberately live only in memory: a reload
 * requires a new sign-in, and neither credential is exposed to localStorage/sessionStorage.
 */
@Injectable({ providedIn: 'root' })
export class AuthTokenStore {
  private readonly _accessToken = signal<string | null>(null);
  private readonly _refreshToken = signal<string | null>(null);

  readonly accessToken = this._accessToken.asReadonly();
  readonly refreshToken = this._refreshToken.asReadonly();

  set(accessToken: string, refreshToken: string | null): void {
    this._accessToken.set(accessToken);
    this._refreshToken.set(refreshToken);
  }

  clear(): void {
    this._accessToken.set(null);
    this._refreshToken.set(null);
  }
}
