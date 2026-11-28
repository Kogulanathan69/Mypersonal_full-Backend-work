import { Injectable, InjectionToken, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { blockedOperation } from '../api/blocked-operation';
import { AUTH_CONTRACTS } from './auth.contracts';

/**
 * Inputs the screens collect. These are UI models, not backend DTOs: the real request shapes
 * are mapped inside the API implementation once the contract is verified.
 */
export interface SignInInput {
  readonly email: string;
  readonly password: string;
}

export interface RegisterInput {
  readonly fullName: string;
  readonly email: string;
  readonly phone: string | null;
  readonly password: string;
  readonly acceptedTerms: boolean;
}

export interface ResetPasswordInput {
  readonly email: string;
  readonly token: string;
  readonly password: string;
}

/** Minimal signed-in user view used by the shell. Fields come from the verified contract later. */
export interface SessionUser {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  /** Role identifiers exactly as issued by the backend. Never hard-coded in the frontend. */
  readonly roles: readonly string[];
  readonly permissions: readonly string[];
}

export interface AuthApi {
  signIn(input: SignInInput): Observable<SessionUser>;
  register(input: RegisterInput): Observable<void>;
  verifyEmail(token: string): Observable<void>;
  resendVerification(email: string): Observable<void>;
  forgotPassword(email: string): Observable<void>;
  resetPassword(input: ResetPasswordInput): Observable<void>;
  signOut(): Observable<void>;
  currentUser(): Observable<SessionUser | null>;
}

/**
 * Default implementation until the backend contract is supplied: every call fails with a
 * truthful "not connected yet" error. It never sends a request to a guessed URL.
 */
@Injectable({ providedIn: 'root' })
export class UnavailableAuthApi implements AuthApi {
  signIn(): Observable<SessionUser> {
    return blockedOperation(AUTH_CONTRACTS.signIn.id, 'Signing in');
  }
  register(): Observable<void> {
    return blockedOperation(AUTH_CONTRACTS.register.id, 'Account registration');
  }
  verifyEmail(): Observable<void> {
    return blockedOperation(AUTH_CONTRACTS.verifyEmail.id, 'Email verification');
  }
  resendVerification(): Observable<void> {
    return blockedOperation(
      AUTH_CONTRACTS.resendVerification.id,
      'Resending the verification email',
    );
  }
  forgotPassword(): Observable<void> {
    return blockedOperation(AUTH_CONTRACTS.forgotPassword.id, 'Password recovery');
  }
  resetPassword(): Observable<void> {
    return blockedOperation(AUTH_CONTRACTS.resetPassword.id, 'Password reset');
  }
  signOut(): Observable<void> {
    return blockedOperation(AUTH_CONTRACTS.signOut.id, 'Signing out');
  }
  currentUser(): Observable<SessionUser | null> {
    return blockedOperation(AUTH_CONTRACTS.currentUser.id, 'Loading your account');
  }
}

export const AUTH_API = new InjectionToken<AuthApi>('AUTH_API', {
  providedIn: 'root',
  factory: () => inject(UnavailableAuthApi),
});
