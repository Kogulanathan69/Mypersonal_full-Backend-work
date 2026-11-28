import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { TimeoutError, catchError, throwError, timeout } from 'rxjs';
import { AppError } from '../api/app-error';
import { IS_API_REQUEST, REQUEST_TIMEOUT_MS } from '../api/http-context';
import { USER_MESSAGES } from '../api/user-messages';
import { RuntimeConfigService } from '../config/runtime-config.service';

export const timeoutInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.context.get(IS_API_REQUEST)) return next(req);
  const perRequest = req.context.get(REQUEST_TIMEOUT_MS);
  const ms =
    perRequest > 0 ? perRequest : inject(RuntimeConfigService).config().api.requestTimeoutMs;
  return next(req).pipe(
    timeout(ms),
    catchError((err: unknown) =>
      throwError(() =>
        err instanceof TimeoutError
          ? new AppError({
              kind: 'timeout',
              message: USER_MESSAGES.timeout,
              detail: `No response after ${ms} ms`,
            })
          : err,
      ),
    ),
  );
};
