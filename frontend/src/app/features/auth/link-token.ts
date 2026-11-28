import { Signal, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

/**
 * Reads a one-time token from `?token=` once, keeps it in memory only, and removes it from the
 * address bar (replaceUrl) so it does not stay in browser history, screenshots or referrers.
 */
export function readLinkToken(): { token: Signal<string | null>; email: string | null } {
  const route = inject(ActivatedRoute);
  const router = inject(Router);
  const params = route.snapshot.queryParamMap;
  const raw = params.get('token');
  const token = signal<string | null>(raw && raw.length <= 4096 ? raw : null);
  if (raw !== null) {
    void router.navigate([], {
      relativeTo: route,
      queryParams: { token: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }
  return { token: token.asReadonly(), email: params.get('email') };
}
