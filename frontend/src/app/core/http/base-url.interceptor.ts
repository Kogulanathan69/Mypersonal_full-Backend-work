import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { throwError } from 'rxjs';
import { AppError } from '../api/app-error';
import { IS_API_REQUEST } from '../api/http-context';
import { USER_MESSAGES } from '../api/user-messages';
import { RuntimeConfigService } from '../config/runtime-config.service';

/** Resolves relative API paths against the runtime-configured base URL. */
export const baseUrlInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.context.get(IS_API_REQUEST)) return next(req);
  const base = inject(RuntimeConfigService).config().api.mainBaseUrl;
  if (!base) {
    return throwError(
      () =>
        new AppError({
          kind: 'config',
          message: USER_MESSAGES.config,
          detail: 'api.mainBaseUrl is empty in config.json',
        }),
    );
  }
  const url = `${base.replace(/\/+$/, '')}/${req.url.replace(/^\/+/, '')}`;
  return next(req.clone({ url }));
};
