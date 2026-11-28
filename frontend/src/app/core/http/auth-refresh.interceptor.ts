import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { IS_API_REQUEST, SUPPRESS_SESSION_EXPIRY } from '../api/http-context';
import { AuthRefreshService } from '../auth/auth-refresh.service';

/** Retries one failed authenticated request after a single-flight refresh-token rotation. */
export const authRefreshInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.context.get(IS_API_REQUEST) || req.context.get(SUPPRESS_SESSION_EXPIRY)) return next(req);
  const refresh = inject(AuthRefreshService);
  return next(req).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }
      return refresh.refresh(req.url).pipe(
        switchMap((accessToken) =>
          next(req.clone({ setHeaders: { Authorization: `Bearer ${accessToken}` } })),
        ),
        catchError(() => {
          refresh.clear();
          return throwError(() => error);
        }),
      );
    }),
  );
};
