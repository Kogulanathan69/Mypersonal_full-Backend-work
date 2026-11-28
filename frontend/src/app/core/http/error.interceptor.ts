import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { IS_API_REQUEST, SUPPRESS_SESSION_EXPIRY } from '../api/http-context';
import { SessionEventsService } from '../auth/session-events.service';
import { OnlineStatusService } from '../network/online-status.service';
import { CORRELATION_HEADER } from './correlation-id.interceptor';
import { toAppError } from './error-mapping';

/** Maps every API failure to AppError and raises the session-expired event on 401. */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.context.get(IS_API_REQUEST)) return next(req);
  const online = inject(OnlineStatusService);
  const session = inject(SessionEventsService);
  return next(req).pipe(
    catchError((err: unknown) => {
      const appError = toAppError(
        err,
        online.online(),
        req.headers.get(CORRELATION_HEADER) ?? undefined,
      );
      if (appError.kind === 'unauthorized' && !req.context.get(SUPPRESS_SESSION_EXPIRY))
        session.markExpired();
      return throwError(() => appError);
    }),
  );
};
