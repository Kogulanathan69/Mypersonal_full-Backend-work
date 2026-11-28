import { HttpBackend, HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, finalize, map, shareReplay, tap, throwError } from 'rxjs';
import * as z from 'zod/mini';
import { AuthTokenStore } from './auth-token.store';
import { SessionStore } from './session.store';

const refreshSchema = z.object({
  accessToken: z.string(),
  tokenType: z.string(),
  expiresAtUtc: z.string(),
  refreshToken: z.string(),
  refreshTokenExpiresAtUtc: z.string(),
  user: z.object({
    id: z.string(),
    email: z.string(),
    isActive: z.boolean(),
    createdAtUtc: z.string(),
  }),
});

/** Coordinates one refresh request for any number of simultaneous 401 responses. */
@Injectable({ providedIn: 'root' })
export class AuthRefreshService {
  private readonly rawHttp = new HttpClient(inject(HttpBackend));
  private readonly tokens = inject(AuthTokenStore);
  private readonly session = inject(SessionStore);
  private inFlight: Observable<string> | null = null;

  refresh(requestUrl: string): Observable<string> {
    if (this.inFlight) return this.inFlight;
    const refreshToken = this.tokens.refreshToken();
    const marker = '/api/v1/';
    const markerIndex = requestUrl.indexOf(marker);
    if (!refreshToken || markerIndex < 0) {
      return throwError(() => new Error('No renewable BuildMind session is available.'));
    }

    const refreshUrl = `${requestUrl.slice(0, markerIndex)}${marker}auth/refresh`;
    this.inFlight = this.rawHttp
      .post<unknown>(refreshUrl, { refreshToken })
      .pipe(
        map((value) => {
          const parsed = z.safeParse(refreshSchema, value);
          if (!parsed.success) throw new Error('The refresh response did not match its contract.');
          return parsed.data;
        }),
        tap((response) => this.tokens.set(response.accessToken, response.refreshToken)),
        map((response) => response.accessToken),
        finalize(() => {
          this.inFlight = null;
        }),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
    return this.inFlight;
  }

  clear(): void {
    this.tokens.clear();
    this.session.clear();
  }
}
