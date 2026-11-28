import { Injectable, computed, signal } from '@angular/core';
import { Observable, defer, finalize } from 'rxjs';

/**
 * Counts in-flight work that should show the global progress bar.
 * Screens use their own skeletons for their own data; this is for "something is happening".
 */
@Injectable({ providedIn: 'root' })
export class LoadingService {
  private readonly pending = signal(0);
  readonly isLoading = computed(() => this.pending() > 0);
  readonly pendingCount = this.pending.asReadonly();

  begin(): void {
    this.pending.update((n) => n + 1);
  }

  end(): void {
    this.pending.update((n) => Math.max(0, n - 1));
  }

  /** Wraps an observable so the bar shows from subscribe until complete, error or unsubscribe. */
  track<T>(source: Observable<T>): Observable<T> {
    return defer(() => {
      this.begin();
      return source.pipe(finalize(() => this.end()));
    });
  }
}
