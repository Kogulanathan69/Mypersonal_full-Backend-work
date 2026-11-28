import { HttpErrorResponse } from '@angular/common/http';
import { AppError, AppErrorKind, isAppError } from '../api/app-error';
import { parseProblemDetails, toControlName } from '../api/problem-details';
import { USER_MESSAGES } from '../api/user-messages';
import { CORRELATION_HEADER } from './correlation-id.interceptor';

function kindForStatus(status: number, online: boolean): AppErrorKind {
  if (status === 0) return online ? 'network' : 'offline';
  if (status === 400 || status === 422) return 'validation';
  if (status === 401) return 'unauthorized';
  if (status === 403) return 'forbidden';
  if (status === 404) return 'not-found';
  if (status === 409 || status === 412) return 'conflict';
  if (status === 429) return 'rate-limited';
  if (status >= 500) return 'server';
  return 'unknown';
}

/** Converts anything thrown during a request into an AppError with a user-safe message. */
export function toAppError(error: unknown, online = true, requestCorrelationId?: string): AppError {
  if (isAppError(error)) return error;

  if (error instanceof HttpErrorResponse) {
    const kind = kindForStatus(error.status, online);
    const problem = parseProblemDetails(error.error);
    const fieldErrors: Record<string, readonly string[]> = {};
    for (const [key, messages] of Object.entries(problem?.errors ?? {})) {
      fieldErrors[toControlName(key)] = messages;
    }
    const correlationId =
      error.headers?.get(CORRELATION_HEADER) ??
      problem?.traceId ??
      requestCorrelationId ??
      undefined;
    return new AppError({
      kind,
      status: error.status,
      // ProblemDetails.title is written for end users by ASP.NET; raw bodies are not shown.
      message: kind === 'validation' && problem?.title ? problem.title : USER_MESSAGES[kind],
      fieldErrors,
      correlationId,
      detail: `HTTP ${error.status} ${error.statusText || ''}`.trim(),
    });
  }

  return new AppError({
    kind: 'unknown',
    message: USER_MESSAGES.unknown,
    detail: error instanceof Error ? error.name : typeof error,
  });
}
