import { Observable, throwError } from 'rxjs';
import { AppError } from './app-error';
import { listContracts } from './contract-status';

/**
 * For operations whose backend contract is not yet known: fails immediately with a truthful,
 * user-safe "not connected yet" error instead of guessing an endpoint or faking success.
 */
export function blockedOperation<T>(contractId: string, feature: string): Observable<T> {
  const contract = listContracts().find((c) => c.id === contractId);
  if (!contract) throw new Error(`Undeclared contract "${contractId}".`);
  return throwError(
    () =>
      new AppError({
        kind: 'config',
        message: `${feature} is not connected yet. The BuildMind server interface for it has not been confirmed.`,
        detail: `Contract "${contractId}" is ${contract.status}: ${contract.notes ?? 'no verified endpoint'}.`,
      }),
  );
}
