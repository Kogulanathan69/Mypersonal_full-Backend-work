import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { OnlineStatusService } from '../../../core/network/online-status.service';
import { Icon } from '../icon/icon';

@Component({
  selector: 'bm-offline-banner',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div aria-live="polite">
      @if (!status.online()) {
        <div
          class="flex items-center justify-center gap-2 bg-warning px-4 py-2 text-sm font-medium text-on-accent"
        >
          <bm-icon name="wifi-off" [size]="16" />
          You are offline. Changes will not be saved until your connection returns.
        </div>
      }
    </div>
  `,
})
export class OfflineBanner {
  protected readonly status = inject(OnlineStatusService);
}
