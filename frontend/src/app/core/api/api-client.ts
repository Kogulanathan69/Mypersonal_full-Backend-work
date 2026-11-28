import { HttpClient, HttpContext, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, throwError } from 'rxjs';
import * as z from 'zod/mini';
import { BUILD_ENV } from '../config/build-env.token';
import { AppError } from './app-error';
import { listContracts } from './contract-status';
import {
  IS_API_REQUEST,
  REQUEST_TIMEOUT_MS,
  SKIP_GLOBAL_LOADING,
  SUPPRESS_SESSION_EXPIRY,
} from './http-context';
import { USER_MESSAGES } from './user-messages';

export interface ApiRequestOptions {
  /** Id of a contract declared with declareContract(). Required: no undeclared calls. */
  readonly contractId: string;
  readonly params?: Readonly<Record<string, string | number | boolean>>;
  /** Sent as Idempotency-Key for actions that must not run twice (e.g. generate, confirm). */
  readonly idempotencyKey?: string;
  /** Sent as If-Match for optimistic concurrency on plan revisions. */
  readonly ifMatch?: string;
  readonly skipGlobalLoading?: boolean;
  /** For sign-in style calls where 401 means bad credentials, not an expired session. */
  readonly suppressSessionExpiry?: boolean;
  readonly timeoutMs?: number;
}

type Method = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';

/**
 * The only way feature code talks to the BuildMind backend.
 *  - Paths are relative ("projects/42"); the base URL comes from runtime config (base-url interceptor).
 *  - Every call names a declared contract; unverified contracts only run in the mock build.
 *  - Every response is validated with a zod schema; a mismatch becomes AppError('contract').
 */
@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);
  private readonly env = inject(BUILD_ENV);

  get<T>(path: string, schema: z.ZodMiniType<T>, options: ApiRequestOptions): Observable<T> {
    return this.request('GET', path, schema, undefined, options);
  }

  getBlob(path: string, options: ApiRequestOptions): Observable<Blob> {
    const gate = this.checkContract(options.contractId);
    if (gate) return throwError(() => gate);
    let params = new HttpParams();
    for (const [key, value] of Object.entries(options.params ?? {})) params = params.set(key, String(value));
    const context = new HttpContext()
      .set(IS_API_REQUEST, true)
      .set(SKIP_GLOBAL_LOADING, options.skipGlobalLoading ?? false)
      .set(SUPPRESS_SESSION_EXPIRY, options.suppressSessionExpiry ?? false)
      .set(REQUEST_TIMEOUT_MS, options.timeoutMs ?? 0);
    return this.http.get(path.replace(/^\/+/, ''), { params, context, responseType: 'blob' });
  }

  post<T>(
    path: string,
    body: unknown,
    schema: z.ZodMiniType<T>,
    options: ApiRequestOptions,
  ): Observable<T> {
    return this.request('POST', path, schema, body, options);
  }

  put<T>(
    path: string,
    body: unknown,
    schema: z.ZodMiniType<T>,
    options: ApiRequestOptions,
  ): Observable<T> {
    return this.request('PUT', path, schema, body, options);
  }

  patch<T>(
    path: string,
    body: unknown,
    schema: z.ZodMiniType<T>,
    options: ApiRequestOptions,
  ): Observable<T> {
    return this.request('PATCH', path, schema, body, options);
  }

  delete<T>(path: string, schema: z.ZodMiniType<T>, options: ApiRequestOptions): Observable<T> {
    return this.request('DELETE', path, schema, undefined, options);
  }

  private request<T>(
    method: Method,
    path: string,
    schema: z.ZodMiniType<T>,
    body: unknown,
    options: ApiRequestOptions,
  ): Observable<T> {
    const gate = this.checkContract(options.contractId);
    if (gate) return throwError(() => gate);

    let headers = new HttpHeaders();
    if (options.idempotencyKey) headers = headers.set('Idempotency-Key', options.idempotencyKey);
    if (options.ifMatch) headers = headers.set('If-Match', options.ifMatch);

    let params = new HttpParams();
    for (const [key, value] of Object.entries(options.params ?? {})) {
      params = params.set(key, String(value));
    }

    const context = new HttpContext()
      .set(IS_API_REQUEST, true)
      .set(SKIP_GLOBAL_LOADING, options.skipGlobalLoading ?? false)
      .set(SUPPRESS_SESSION_EXPIRY, options.suppressSessionExpiry ?? false)
      .set(REQUEST_TIMEOUT_MS, options.timeoutMs ?? 0);

    return this.http
      .request<unknown>(method, path.replace(/^\/+/, ''), { body, headers, params, context })
      .pipe(map((raw) => this.validate(raw, schema, options.contractId)));
  }

  private checkContract(contractId: string): AppError | null {
    const contract = listContracts().find((c) => c.id === contractId);
    if (!contract) {
      // Programming error: fail loudly in every build.
      throw new Error(
        `API call uses undeclared contract "${contractId}". Declare it in contract-status first.`,
      );
    }
    if (contract.status === 'VERIFIED') return null;
    if (contract.status === 'MOCK' && this.env.useMocks) return null;
    return new AppError({
      kind: 'config',
      message: USER_MESSAGES.config,
      detail: `Contract "${contractId}" is ${contract.status}; not available in the ${this.env.name} build.`,
    });
  }

  private validate<T>(raw: unknown, schema: z.ZodMiniType<T>, contractId: string): T {
    const result = z.safeParse(schema, raw);
    if (result.success) return result.data;
    // Log paths and messages only — never response values, which may contain personal data.
    const issues = result.error.issues.map((i) => `${i.path.join('.') || '(root)'}: ${i.message}`);
    throw new AppError({
      kind: 'contract',
      message: USER_MESSAGES.contract,
      detail: `Response for "${contractId}" did not match its schema: ${issues.join('; ')}`,
    });
  }
}
