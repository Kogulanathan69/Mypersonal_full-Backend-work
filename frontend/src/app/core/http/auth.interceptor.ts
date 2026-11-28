import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { IS_API_REQUEST } from '../api/http-context';
import { AUTH_TOKEN_PROVIDER } from '../auth/auth-token';

/**
 * API requests use the verified bearer-token contract. Browser credentials are deliberately
 * omitted: the backend's CORS policy does not enable credentialed cross-origin requests.
 * Non-API requests (config.json, assets) are never touched.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.context.get(IS_API_REQUEST)) return next(req);
  const token = inject(AUTH_TOKEN_PROVIDER)();
  return next(
    req.clone({
      ...(token ? { setHeaders: { Authorization: `Bearer ${token}` } } : {}),
    }),
  );
};
