import { Injectable, signal } from '@angular/core';

/**
 * Raised when the backend says the session is no longer valid (HTTP 401).
 * The app shell listens and shows the session-expired dialog.
 * The real session store and sign-in flow arrive in Phase 01 step 7.
 */
@Injectable({ providedIn: 'root' })
export class SessionEventsService {
  private readonly _expired = signal(false);
  readonly expired = this._expired.asReadonly();

  markExpired(): void {
    this._expired.set(true);
  }

  acknowledge(): void {
    this._expired.set(false);
  }
}
