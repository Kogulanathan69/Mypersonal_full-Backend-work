import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, of, switchMap, tap, throwError } from 'rxjs';
import * as z from 'zod/mini';
import { ApiClient } from '../api/api-client';
import { AppError } from '../api/app-error';
import {
  AuthApi,
  RegisterInput,
  ResetPasswordInput,
  SessionUser,
  SignInInput,
} from './auth-api';
import { AUTH_CONTRACTS } from './auth.contracts';
import { AuthTokenStore } from './auth-token.store';
import { SessionStore } from './session.store';

const userSchema = z.object({
  id: z.string(),
  email: z.string(),
  isActive: z.boolean(),
  createdAtUtc: z.string(),
});

const loginSchema = z.object({
  accessToken: z.string(),
  expiresAtUtc: z.string(),
  refreshToken: z.optional(z.string()),
  refreshTokenExpiresAtUtc: z.optional(z.string()),
  tokenType: z.string(),
  user: userSchema,
});

const profileSchema = z.object({
  id: z.string(),
  email: z.string(),
  fullName: z.nullable(z.unknown()),
  phoneNumber: z.nullable(z.unknown()),
  isActive: z.boolean(),
  createdAtUtc: z.string(),
});

const registrationSchema = userSchema;
const forgotSchema = z.object({ message: z.string() });
const emptySchema = z.null();

@Injectable({ providedIn: 'root' })
export class LiveAuthApi implements AuthApi {
  private readonly api = inject(ApiClient);
  private readonly tokens = inject(AuthTokenStore);
  private readonly session = inject(SessionStore);

  signIn(input: SignInInput): Observable<SessionUser> {
    return this.api
      .post(
        'api/v1/auth/login',
        { email: input.email.trim(), password: input.password },
        loginSchema,
        { contractId: AUTH_CONTRACTS.signIn.id, suppressSessionExpiry: true },
      )
      .pipe(
        tap((response) =>
          this.tokens.set(response.accessToken, response.refreshToken ?? null),
        ),
        switchMap((response) =>
          this.loadProfile().pipe(
            catchError(() => of(this.toSessionUser(response.user.id, response.user.email, null))),
          ),
        ),
      );
  }

  register(input: RegisterInput): Observable<void> {
    return this.api
      .post(
        'api/v1/auth/register',
        { email: input.email.trim(), password: input.password },
        registrationSchema,
        { contractId: AUTH_CONTRACTS.register.id, suppressSessionExpiry: true },
      )
      .pipe(map(() => undefined));
  }

  verifyEmail(): Observable<void> {
    return this.unavailableVerification();
  }

  resendVerification(): Observable<void> {
    return this.unavailableVerification();
  }

  forgotPassword(email: string): Observable<void> {
    return this.api
      .post(
        'api/v1/auth/forgot-password',
        { email: email.trim() },
        forgotSchema,
        { contractId: AUTH_CONTRACTS.forgotPassword.id, suppressSessionExpiry: true },
      )
      .pipe(map(() => undefined));
  }

  resetPassword(input: ResetPasswordInput): Observable<void> {
    return this.api
      .post(
        'api/v1/auth/reset-password',
        {
          email: input.email.trim(),
          token: input.token,
          newPassword: input.password,
          confirmPassword: input.password,
        },
        emptySchema,
        { contractId: AUTH_CONTRACTS.resetPassword.id, suppressSessionExpiry: true },
      )
      .pipe(map(() => undefined));
  }

  signOut(): Observable<void> {
    const refreshToken = this.tokens.refreshToken();
    if (!refreshToken) {
      this.clearSession();
      return of(undefined);
    }
    return this.api
      .post('api/v1/auth/logout', { refreshToken }, emptySchema, {
        contractId: AUTH_CONTRACTS.signOut.id,
        suppressSessionExpiry: true,
      })
      .pipe(
        map(() => undefined),
        tap({ finalize: () => this.clearSession() }),
      );
  }

  currentUser(): Observable<SessionUser | null> {
    if (!this.tokens.accessToken()) return of(null);
    return this.loadProfile().pipe(
      catchError(() => {
        this.clearSession();
        return of(null);
      }),
    );
  }

  private loadProfile(): Observable<SessionUser> {
    return this.api
      .get('api/v1/users/me', profileSchema, { contractId: AUTH_CONTRACTS.currentUser.id })
      .pipe(
        map((profile) =>
          this.toSessionUser(
            profile.id,
            profile.email,
            typeof profile.fullName === 'string' ? profile.fullName : null,
          ),
        ),
      );
  }

  private toSessionUser(id: string, email: string, fullName: string | null): SessionUser {
    return {
      id,
      email,
      displayName: fullName?.trim() || email,
      roles: [],
      permissions: [],
    };
  }

  private unavailableVerification(): Observable<never> {
    return throwError(
      () =>
        new AppError({
          kind: 'config',
          message:
            'Email verification is available after sign-in, but this link flow is not supported by the current server contract.',
          detail: 'The verified backend requires an authenticated code-based verification request.',
        }),
    );
  }

  private clearSession(): void {
    this.tokens.clear();
    this.session.clear();
  }
}
