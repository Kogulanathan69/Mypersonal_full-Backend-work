import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ToastKind, ToastService } from '../../../core/feedback/toast.service';
import { Icon } from '../icon/icon';
import { IconName } from '../icon/icon-paths';

const ICON: Record<ToastKind, IconName> = {
  success: 'check-circle',
  info: 'info',
  warning: 'alert-triangle',
  error: 'alert-circle',
};
const TONE: Record<ToastKind, string> = {
  success: 'text-success',
  info: 'text-info',
  warning: 'text-warning',
  error: 'text-danger',
};

/** Renders ToastService toasts and the live regions that announce them to screen readers. */
@Component({
  selector: 'bm-toast-host',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="sr-only" aria-live="polite" aria-atomic="true">
      @if (toasts.announcement(); as a) {
        @if (a.politeness === 'polite') {
          <span>{{ a.text }}</span>
        }
      }
    </div>
    <div class="sr-only" aria-live="assertive" aria-atomic="true">
      @if (toasts.announcement(); as a) {
        @if (a.politeness === 'assertive') {
          <span>{{ a.text }}</span>
        }
      }
    </div>
    <section
      aria-label="Notifications"
      class="pointer-events-none fixed inset-x-3 bottom-3 z-50 flex flex-col items-stretch gap-2 sm:inset-x-auto sm:right-4 sm:bottom-4 sm:w-96"
    >
      @for (toast of toasts.toasts(); track toast.id) {
        <div
          class="pointer-events-auto flex items-start gap-3 rounded-lg border border-border bg-surface-raised p-4 shadow-card"
        >
          <bm-icon [name]="icon[toast.kind]" [size]="20" [class]="tone[toast.kind]" />
          <div class="min-w-0 flex-1">
            <p class="text-sm font-semibold text-text">{{ toast.title }}</p>
            @if (toast.message) {
              <p class="mt-0.5 text-sm text-muted">{{ toast.message }}</p>
            }
          </div>
          <button
            type="button"
            class="-m-1 rounded-md p-1 text-subtle hover:text-text"
            (click)="toasts.dismiss(toast.id)"
          >
            <bm-icon name="x" [size]="16" label="Dismiss notification" />
          </button>
        </div>
      }
    </section>
  `,
})
export class ToastHost {
  protected readonly toasts = inject(ToastService);
  protected readonly icon = ICON;
  protected readonly tone = TONE;
}
