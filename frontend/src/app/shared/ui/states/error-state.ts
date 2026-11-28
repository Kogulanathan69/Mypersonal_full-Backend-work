import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { AppError, AppErrorKind } from '../../../core/api/app-error';
import { Button } from '../button/button';
import { Icon } from '../icon/icon';
import { IconName } from '../icon/icon-paths';

const TITLES: Partial<Record<AppErrorKind, string>> = {
  offline: 'You are offline',
  network: 'Connection problem',
  timeout: 'This is taking too long',
  forbidden: 'Access denied',
  'not-found': 'Not found',
  conflict: 'This changed elsewhere',
  config: 'Not configured yet',
  contract: 'Unexpected response',
  server: 'Service unavailable',
};

const ICONS: Partial<Record<AppErrorKind, IconName>> = {
  offline: 'wifi-off',
  network: 'wifi-off',
  forbidden: 'lock',
  config: 'settings',
  timeout: 'clock',
};

/**
 * Inline error panel for a failed load. Shows the AppError's user-safe message and its
 * reference id, and offers "Try again" only when the error is retryable.
 */
@Component({
  selector: 'bm-error-state',
  imports: [Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <div
      role="alert"
      class="flex flex-col items-center gap-3 rounded-lg border border-danger/50 bg-surface px-6 py-8 text-center"
    >
      <span
        class="flex size-12 items-center justify-center rounded-pill bg-surface-raised text-danger"
      >
        <bm-icon [name]="icon()" [size]="24" />
      </span>
      <p class="text-base font-semibold text-text">{{ resolvedTitle() }}</p>
      <p class="max-w-md text-sm text-muted">{{ resolvedMessage() }}</p>
      @if (error()?.correlationId; as ref) {
        <p class="text-xs text-subtle">
          Reference: <span class="font-mono">{{ ref }}</span>
        </p>
      }
      @if (canRetry()) {
        <button bmButton variant="secondary" size="sm" type="button" (click)="retry.emit()">
          <bm-icon name="refresh" [size]="16" /> Try again
        </button>
      }
      <ng-content />
    </div>
  `,
})
export class ErrorState {
  readonly error = input<AppError | null>(null);
  readonly title = input('');
  readonly message = input('');
  /** Force-show or hide the retry button; default follows AppError.retryable. */
  readonly retryable = input<boolean | undefined>(undefined);
  readonly retry = output<void>();

  protected readonly resolvedTitle = computed(
    () => this.title() || TITLES[this.error()?.kind ?? 'unknown'] || 'Something went wrong',
  );
  protected readonly resolvedMessage = computed(
    () => this.message() || this.error()?.message || 'Please try again.',
  );
  protected readonly icon = computed<IconName>(
    () => ICONS[this.error()?.kind ?? 'unknown'] ?? 'alert-triangle',
  );
  protected readonly canRetry = computed(
    () => this.retryable() ?? this.error()?.retryable ?? false,
  );
}
