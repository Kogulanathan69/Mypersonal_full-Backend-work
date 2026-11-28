import { inject, InjectionToken } from '@angular/core';
import { AuthTokenStore } from './auth-token.store';

/**
 * Supplies a bearer token for API requests, if the backend uses bearer tokens at all.
 * Default: no token (cookie session assumed). The auth model is BACKEND CONTRACT REQUIRED;
 * step 7 replaces this provider. A token is only ever held in memory — never in
 * localStorage or sessionStorage.
 */
export type AuthTokenProvider = () => string | null;

export const AUTH_TOKEN_PROVIDER = new InjectionToken<AuthTokenProvider>('AUTH_TOKEN_PROVIDER', {
  providedIn: 'root',
  factory: () => {
    const tokens = inject(AuthTokenStore);
    return () => tokens.accessToken();
  },
});
