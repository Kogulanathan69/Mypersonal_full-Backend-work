import { HttpContextToken } from '@angular/common/http';

/** Do not show the global loading bar for this request (polling, background refresh). */
export const SKIP_GLOBAL_LOADING = new HttpContextToken<boolean>(() => false);

/** Per-request timeout in ms. 0 = use runtime config default. */
export const REQUEST_TIMEOUT_MS = new HttpContextToken<number>(() => 0);

/**
 * Do not raise the session-expired dialog on 401 for this request. Used by sign-in, where 401
 * means "wrong email or password", not "your session ended".
 */
export const SUPPRESS_SESSION_EXPIRY = new HttpContextToken<boolean>(() => false);

/** Marks a request as a BuildMind API call (set by ApiClient). Other requests are left untouched. */
export const IS_API_REQUEST = new HttpContextToken<boolean>(() => false);
