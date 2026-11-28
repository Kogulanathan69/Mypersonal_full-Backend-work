import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { LoadingService } from '../../../core/feedback/loading.service';

/** Thin gold bar at the top of the viewport while requests are in flight. */
@Component({
  selector: 'bm-global-progress',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loading.isLoading()) {
      <div
        role="progressbar"
        aria-label="Loading"
        aria-valuetext="Loading"
        class="fixed inset-x-0 top-0 z-50 h-0.5 overflow-hidden bg-surface-raised"
      >
        <div
          class="h-full w-1/3 animate-[bm-indeterminate_1.1s_ease-in-out_infinite] bg-accent"
        ></div>
      </div>
    }
  `,
  styles: `
    @keyframes bm-indeterminate {
      from {
        transform: translateX(-100%);
      }
      to {
        transform: translateX(300%);
      }
    }
  `,
})
export class GlobalProgress {
  protected readonly loading = inject(LoadingService);
}
