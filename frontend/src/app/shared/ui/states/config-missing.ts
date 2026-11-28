import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { RuntimeConfigService } from '../../../core/config/runtime-config.service';
import { Icon } from '../icon/icon';

/**
 * Shown instead of a feature whose configuration is missing (e.g. no API base URL, no map token).
 * Explains the gap honestly rather than failing silently or showing fake data.
 */
@Component({
  selector: 'bm-config-missing',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <div role="status" class="flex gap-4 rounded-lg border border-warning/60 bg-surface p-5">
      <bm-icon name="settings" [size]="24" class="text-warning" />
      <div class="min-w-0">
        <p class="font-semibold text-text">{{ feature() }} is not configured</p>
        <p class="mt-1 text-sm text-muted">{{ explanation() }}</p>
        @if (showDetails() && config.problems().length) {
          <ul class="mt-2 list-disc pl-5 text-xs text-subtle">
            @for (p of config.problems(); track p) {
              <li>{{ p }}</li>
            }
          </ul>
        }
      </div>
    </div>
  `,
})
export class ConfigMissing {
  readonly feature = input.required<string>();
  readonly explanation = input(
    'An administrator needs to provide the required settings before this can be used.',
  );
  /** Show config.json problems (developer-facing; off by default). */
  readonly showDetails = input(false);
  protected readonly config = inject(RuntimeConfigService);
}
