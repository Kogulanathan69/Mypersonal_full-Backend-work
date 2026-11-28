import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type BadgeTone = 'neutral' | 'accent' | 'success' | 'warning' | 'danger' | 'info';

const TONES: Record<BadgeTone, string> = {
  neutral: 'text-muted border-border-strong',
  accent: 'text-accent border-accent',
  success: 'text-success border-success',
  warning: 'text-warning border-warning',
  danger: 'text-danger border-danger',
  info: 'text-info border-info',
};

/** Small label. Tone is never the only signal: the text itself must carry the meaning. */
@Component({
  selector: 'bm-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class]': 'classes()' },
  template: `
    @if (dot()) {
      <span class="size-1.5 rounded-pill bg-current" aria-hidden="true"></span>
    }
    <ng-content />
  `,
})
export class Badge {
  readonly tone = input<BadgeTone>('neutral');
  readonly dot = input(false);
  protected readonly classes = computed(
    () =>
      `inline-flex items-center gap-1.5 rounded-pill border bg-bg-deep/40 px-2.5 py-0.5 text-xs font-medium whitespace-nowrap ${TONES[this.tone()]}`,
  );
}
