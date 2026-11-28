import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { Icon } from '../icon/icon';
import { IconName } from '../icon/icon-paths';

/** Shown when a list or area has no data yet. Project actions (e.g. "Create project") as content. */
@Component({
  selector: 'bm-empty-state',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <div
      class="flex flex-col items-center gap-3 rounded-lg border border-dashed border-border-strong px-6 py-10 text-center"
    >
      <span
        class="flex size-12 items-center justify-center rounded-pill bg-surface-raised text-accent"
      >
        <bm-icon [name]="icon()" [size]="24" />
      </span>
      <p class="text-base font-semibold text-text">{{ title() }}</p>
      @if (message()) {
        <p class="max-w-md text-sm text-muted">{{ message() }}</p>
      }
      <div class="mt-2 flex flex-wrap justify-center gap-3"><ng-content /></div>
    </div>
  `,
})
export class EmptyState {
  readonly title = input.required<string>();
  readonly message = input('');
  readonly icon = input<IconName>('folder');
}
