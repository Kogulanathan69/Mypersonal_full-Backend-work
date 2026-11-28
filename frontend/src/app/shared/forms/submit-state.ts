import { Signal, signal } from '@angular/core';
import { Observable, finalize, take } from 'rxjs';
import { AppError } from '../../core/api/app-error';
import { toAppError } from '../../core/http/error-mapping';

export interface SubmitState {
  readonly pending: Signal<boolean>;
  readonly error: Signal<AppError | null>;
  /** Runs the action unless one is already pending, so double clicks send one request. */
  run<T>(action: () => Observable<T>, onSuccess?: (value: T) => void): boolean;
  reset(): void;
}

export function createSubmitState(): SubmitState {
  const pending = signal(false);
  const error = signal<AppError | null>(null);
  return {
    pending: pending.asReadonly(),
    error: error.asReadonly(),
    run<T>(action: () => Observable<T>, onSuccess?: (value: T) => void): boolean {
      if (pending()) return false;
      pending.set(true);
      error.set(null);
      action()
        .pipe(
          take(1),
          finalize(() => pending.set(false)),
        )
        .subscribe({
          next: (value) => onSuccess?.(value),
          error: (e: unknown) => error.set(toAppError(e)),
        });
      return true;
    },
    reset(): void {
      error.set(null);
    },
  };
}
