/**
 * One error type for the whole app. Every HTTP failure, timeout, contract mismatch or
 * configuration gap becomes an AppError, so screens handle errors in one consistent way.
 * `message` is always safe to show to users: it never contains tokens, stack traces or raw server text.
 */
export type AppErrorKind =
  | 'network' // no connection / server unreachable
  | 'offline' // browser reports offline
  | 'timeout'
  | 'unauthorized' // 401: session missing or expired
  | 'forbidden' // 403
  | 'not-found' // 404
  | 'conflict' // 409 / 412: stale revision or duplicate action
  | 'validation' // 400 / 422 with field errors
  | 'rate-limited' // 429
  | 'server' // 5xx
  | 'contract' // response did not match the agreed DTO schema
  | 'config' // required runtime configuration missing
  | 'unknown';

export interface AppErrorInit {
  kind: AppErrorKind;
  message: string;
  status?: number;
  correlationId?: string;
  /** Field-level messages keyed by form control name (from ProblemDetails `errors`). */
  fieldErrors?: Readonly<Record<string, readonly string[]>>;
  /** Developer detail for logs only (already redacted). Never shown to users. */
  detail?: string;
}

export class AppError extends Error {
  readonly kind: AppErrorKind;
  readonly status?: number;
  readonly correlationId?: string;
  readonly fieldErrors: Readonly<Record<string, readonly string[]>>;
  readonly detail?: string;

  constructor(init: AppErrorInit) {
    super(init.message);
    this.name = 'AppError';
    this.kind = init.kind;
    this.status = init.status;
    this.correlationId = init.correlationId;
    this.fieldErrors = init.fieldErrors ?? {};
    this.detail = init.detail;
  }

  /** Safe to retry automatically or via a "Try again" button. */
  get retryable(): boolean {
    return ['network', 'offline', 'timeout', 'rate-limited', 'server'].includes(this.kind);
  }
}

export const isAppError = (value: unknown): value is AppError => value instanceof AppError;
