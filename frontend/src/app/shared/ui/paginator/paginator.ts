import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { Icon } from '../icon/icon';

/** "Showing 11–20 of 42" with previous/next. Page numbers are 1-based. */
@Component({
  selector: 'bm-paginator',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav
      [attr.aria-label]="label()"
      class="flex flex-wrap items-center justify-between gap-3 text-sm text-muted"
    >
      <p aria-live="polite">
        @if (total() === 0) {
          No results
        } @else {
          Showing <span class="text-text">{{ from() }}–{{ to() }}</span> of
          <span class="text-text">{{ total() }}</span>
        }
      </p>
      <div class="flex items-center gap-2">
        <button
          type="button"
          class="flex size-10 items-center justify-center rounded-md border border-border-strong text-text disabled:opacity-40"
          [disabled]="page() <= 1"
          (click)="pageChange.emit(page() - 1)"
        >
          <bm-icon name="chevron-left" [size]="18" label="Previous page" />
        </button>
        <span>Page {{ page() }} of {{ pageCount() }}</span>
        <button
          type="button"
          class="flex size-10 items-center justify-center rounded-md border border-border-strong text-text disabled:opacity-40"
          [disabled]="page() >= pageCount()"
          (click)="pageChange.emit(page() + 1)"
        >
          <bm-icon name="chevron-right" [size]="18" label="Next page" />
        </button>
      </div>
    </nav>
  `,
})
export class Paginator {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly total = input.required<number>();
  readonly label = input('Pagination');
  readonly pageChange = output<number>();

  protected readonly pageCount = computed(() =>
    Math.max(1, Math.ceil(this.total() / this.pageSize())),
  );
  protected readonly from = computed(() =>
    this.total() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1,
  );
  protected readonly to = computed(() => Math.min(this.total(), this.page() * this.pageSize()));
}
