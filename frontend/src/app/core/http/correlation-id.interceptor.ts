import { HttpInterceptorFn } from '@angular/common/http';
import { IS_API_REQUEST } from '../api/http-context';

/**
 * Header name is an ASSUMPTION pending backend confirmation (contract register: cross-cutting).
 * The id lets support staff match a user-reported error to server logs.
 */
export const CORRELATION_HEADER = 'X-Correlation-ID';

export const createCorrelationId = (): string =>
  globalThis.crypto?.randomUUID?.() ??
  `bm-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;

export const correlationIdInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.context.get(IS_API_REQUEST) || req.headers.has(CORRELATION_HEADER)) return next(req);
  return next(req.clone({ setHeaders: { [CORRELATION_HEADER]: createCorrelationId() } }));
};
