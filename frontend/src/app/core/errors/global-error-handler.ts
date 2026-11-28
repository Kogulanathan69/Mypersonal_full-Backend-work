import { ErrorHandler, Injectable, inject } from '@angular/core';
import { isAppError } from '../api/app-error';
import { ToastService } from '../feedback/toast.service';
import { toAppError } from '../http/error-mapping';
import { redact } from './redact';

/**
 * Catches errors nothing else handled. Logs a redacted summary (no tokens, emails or
 * response bodies) and shows one user-safe toast with the correlation id when there is one.
 */
@Injectable()
export class GlobalErrorHandler implements ErrorHandler {
  private readonly toasts = inject(ToastService);

  handleError(error: unknown): void {
    const original = unwrap(error);
    const appError = toAppError(original);
    const summary = [
      `[BuildMind] ${appError.kind}`,
      appError.status ? `status=${appError.status}` : '',
      appError.correlationId ? `correlationId=${appError.correlationId}` : '',
      appError.detail ?? '',
      !isAppError(original) && original instanceof Error
        ? `${original.name}: ${original.message}`
        : '',
    ]
      .filter(Boolean)
      .join(' | ');
    console.error(redact(summary));

    // 401 is handled by the session-expired dialog; do not double-notify.
    if (appError.kind === 'unauthorized') return;
    const reference = appError.correlationId ? `Reference: ${appError.correlationId}` : undefined;
    this.toasts.error(appError.message, reference);
  }
}

/** Angular wraps some errors (e.g. in promises); take the original out. */
function unwrap(error: unknown): unknown {
  if (error && typeof error === 'object' && 'rejection' in error) {
    return (error as { rejection: unknown }).rejection;
  }
  return error;
}
