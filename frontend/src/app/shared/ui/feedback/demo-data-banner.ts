import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { BUILD_ENV } from '../../../core/config/build-env.token';
import { Icon } from '../icon/icon';

/**
 * Permanent banner in the mock build: every number and name on screen is demo data,
 * not a BuildMind result. Never rendered in production or development builds.
 */
@Component({
  selector: 'bm-demo-data-banner',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (env.useMocks) {
      <div
        role="note"
        aria-label="Demo data notice"
        data-testid="demo-data-banner"
        class="flex items-center justify-center gap-2 border-b border-info/50 bg-bg-deep px-4 py-1.5 text-xs font-medium text-info"
      >
        <bm-icon name="info" [size]="14" />
        Demo data — not connected to BuildMind servers. Nothing shown here is a real result.
      </div>
    }
  `,
})
export class DemoDataBanner {
  protected readonly env = inject(BUILD_ENV);
}
