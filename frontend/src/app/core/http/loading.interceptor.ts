import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { finalize } from 'rxjs';
import { SKIP_GLOBAL_LOADING } from '../api/http-context';
import { LoadingService } from '../feedback/loading.service';

export const loadingInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.context.get(SKIP_GLOBAL_LOADING)) return next(req);
  const loading = inject(LoadingService);
  loading.begin();
  return next(req).pipe(finalize(() => loading.end()));
};
