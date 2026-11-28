import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type CardVariant = 'default' | 'highlight' | 'flat';

/**
 * Surface container. `highlight` adds the gold outline used for the selected option in the
 * reference screens. Optional heading is rendered as the given heading level for a correct outline.
 */
@Component({
  selector: 'bm-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class]': 'hostClass()' },
  template: `
    @if (heading() || subheading()) {
      <header class="mb-4 flex items-start justify-between gap-4">
        <div class="min-w-0">
          @if (heading()) {
            <div role="heading" [attr.aria-level]="level()" class="text-lg font-semibold text-text">
              {{ heading() }}
            </div>
          }
          @if (subheading()) {
            <p class="mt-1 text-sm text-muted">{{ subheading() }}</p>
          }
        </div>
        <ng-content select="[cardActions]" />
      </header>
    }
    <ng-content />
  `,
})
export class Card {
  readonly heading = input('');
  readonly subheading = input('');
  readonly level = input<2 | 3 | 4>(2);
  readonly variant = input<CardVariant>('default');
  readonly padding = input<'sm' | 'md' | 'lg'>('md');

  protected readonly hostClass = computed(() => {
    const pad = { sm: 'p-4', md: 'p-5 sm:p-6', lg: 'p-6 sm:p-8' }[this.padding()];
    const look = {
      default: 'surface-card',
      highlight: 'surface-card border-accent shadow-glow',
      flat: 'block rounded-lg border border-border bg-surface-raised',
    }[this.variant()];
    return `block ${look} ${pad}`;
  });
}
