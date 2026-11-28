import { authInterceptor } from './auth.interceptor';
import { authRefreshInterceptor } from './auth-refresh.interceptor';
import { baseUrlInterceptor } from './base-url.interceptor';
import { correlationIdInterceptor } from './correlation-id.interceptor';
import { errorInterceptor } from './error.interceptor';
import { loadingInterceptor } from './loading.interceptor';
import { timeoutInterceptor } from './timeout.interceptor';

export * from './error-mapping';
export * from './correlation-id.interceptor';

/**
 * Order matters: the first interceptor is the outermost.
 * loading (counts everything) → correlation-id (so errors can quote it) → error (maps every
 * failure below it) → timeout → base-url (may fail with 'config') → refresh/retry → auth.
 */
export const API_INTERCEPTORS = [
  loadingInterceptor,
  correlationIdInterceptor,
  errorInterceptor,
  timeoutInterceptor,
  baseUrlInterceptor,
  authRefreshInterceptor,
  authInterceptor,
];
