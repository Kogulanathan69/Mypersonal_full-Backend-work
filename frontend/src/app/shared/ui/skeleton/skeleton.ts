import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * Placeholder blocks while data loads. Hidden from assistive technology; put aria-busy="true"
 * on the region being loaded so screen readers know content is coming.
 */
@Component({
  selector: 'bm-skeleton',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'aria-hidden': 'true', class: 'block' },
  template: `
    <div class="flex flex-col gap-2.5">
      @for (w of widths(); track $index) {
        <div
          class="animate-pulse bg-surface-raised"
          [class]="shape() === 'circle' ? 'rounded-pill' : 'rounded-md'"
          [style.height]="height()"
          [style.width]="shape() === 'circle' ? height() : w"
        ></div>
      }
    </div>
  `,
})
export class Skeleton {
  readonly lines = input(3);
  readonly height = input('0.875rem');
  readonly shape = input<'line' | 'block' | 'circle'>('line');
  protected readonly widths = computed(() =>
    Array.from({ length: this.lines() }, (_, i) =>
      this.shape() === 'block'
        ? '100%'
        : i === this.lines() - 1 && this.lines() > 1
          ? '60%'
          : '100%',
    ),
  );
}
