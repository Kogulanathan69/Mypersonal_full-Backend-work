import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Icon } from '../icon/icon';
import { IconName } from '../icon/icon-paths';

export type FormAlertTone = 'error' | 'warning' | 'info' | 'success';

const TONE: Record<FormAlertTone, { icon: IconName; cls: string }> = {
  error: { icon: 'alert-circle', cls: 'border-danger/60 text-danger' },
  warning: { icon: 'alert-triangle', cls: 'border-warning/60 text-warning' },
  info: { icon: 'info', cls: 'border-info/60 text-info' },
  success: { icon: 'check-circle', cls: 'border-success/60 text-success' },
};

/**
 * Message block at the top of a form. Errors use role="alert" so they are announced when they
 * appear; other tones use role="status".
 */
@Component({
  selector: 'bm-form-alert',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <div
      [attr.role]="tone() === 'error' ? 'alert' : 'status'"
      class="flex gap-3 rounded-lg border bg-surface-raised p-4"
      [class]="meta().cls"
    >
      <bm-icon [name]="meta().icon" [size]="20" class="mt-0.5" />
      <div class="min-w-0 text-sm">
        @if (heading()) {
          <p class="font-semibold text-text">{{ heading() }}</p>
        }
        <p class="text-muted" [class.mt-1]="heading()">{{ message() }}</p>
        @if (reference()) {
          <p class="mt-1 text-xs text-subtle">
            Reference: <span class="font-mono">{{ reference() }}</span>
          </p>
        }
        <ng-content />
      </div>
    </div>
  `,
})
export class FormAlert {
  readonly tone = input<FormAlertTone>('error');
  readonly heading = input('');
  readonly message = input.required<string>();
  readonly reference = input<string | undefined>(undefined);
  protected readonly meta = computed(() => TONE[this.tone()]);
}
