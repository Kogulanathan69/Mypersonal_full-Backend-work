import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  Directive,
  TemplateRef,
  computed,
  contentChildren,
  inject,
  input,
  model,
} from '@angular/core';
import { Icon } from '../icon/icon';

export interface TableColumn {
  readonly key: string;
  readonly header: string;
  readonly align?: 'start' | 'end';
  readonly sortable?: boolean;
  /** Hidden in the mobile card view (keep the most important 3–4 columns visible). */
  readonly hideOnMobile?: boolean;
}

export interface SortState {
  readonly key: string;
  readonly direction: 'asc' | 'desc';
}

/** Custom cell template: `<ng-template bmCell="status" let-row>…</ng-template>` */
@Directive({ selector: 'ng-template[bmCell]' })
export class BmCell {
  readonly bmCell = input.required<string>();
  readonly template = inject(TemplateRef<{ $implicit: unknown }>);
}

/**
 * Accessible table with a caption and sortable headers (aria-sort). Below 768px each row becomes
 * a card. Sorting and paging are done by the parent/server; the table only emits the sort state.
 */
@Component({
  selector: 'bm-data-table',
  imports: [NgTemplateOutlet, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <div class="hidden overflow-x-auto rounded-lg border border-border md:block">
      <table class="w-full border-collapse text-sm">
        <caption class="sr-only">
          {{
            caption()
          }}
        </caption>
        <thead class="bg-surface-raised text-left text-xs tracking-wide text-muted uppercase">
          <tr>
            @for (col of columns(); track col.key) {
              <th
                scope="col"
                class="px-4 py-3 font-medium"
                [class.text-right]="col.align === 'end'"
                [attr.aria-sort]="ariaSort(col)"
              >
                @if (col.sortable) {
                  <button
                    type="button"
                    class="inline-flex items-center gap-1 uppercase hover:text-text"
                    (click)="toggleSort(col.key)"
                  >
                    {{ col.header }}
                    <bm-icon [name]="sortIcon(col)" [size]="14" />
                  </button>
                } @else {
                  {{ col.header }}
                }
              </th>
            }
          </tr>
        </thead>
        <tbody>
          @for (row of rows(); track trackRow(row, $index)) {
            <tr class="border-t border-border hover:bg-surface-raised/50">
              @for (col of columns(); track col.key) {
                <td class="px-4 py-3 text-text" [class.text-right]="col.align === 'end'">
                  <ng-container
                    *ngTemplateOutlet="
                      cellTemplate(col.key) ?? plain;
                      context: { $implicit: row, key: col.key }
                    "
                  />
                </td>
              }
            </tr>
          }
        </tbody>
      </table>
    </div>

    <ul class="flex flex-col gap-3 md:hidden" [attr.aria-label]="caption()">
      @for (row of rows(); track trackRow(row, $index)) {
        <li class="rounded-lg border border-border bg-surface p-4">
          <dl class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-sm">
            @for (col of mobileColumns(); track col.key) {
              <dt class="text-muted">{{ col.header }}</dt>
              <dd class="text-text">
                <ng-container
                  *ngTemplateOutlet="
                    cellTemplate(col.key) ?? plain;
                    context: { $implicit: row, key: col.key }
                  "
                />
              </dd>
            }
          </dl>
        </li>
      }
    </ul>

    <ng-template #plain let-row let-key="key">{{ valueOf(row, key) }}</ng-template>
  `,
})
export class DataTable<T extends object> {
  readonly caption = input.required<string>();
  readonly columns = input.required<readonly TableColumn[]>();
  readonly rows = input.required<readonly T[]>();
  readonly rowKey = input<keyof T | null>(null);
  readonly sort = model<SortState | null>(null);

  private readonly cells = contentChildren(BmCell);
  protected readonly mobileColumns = computed(() => this.columns().filter((c) => !c.hideOnMobile));

  protected cellTemplate(key: string): TemplateRef<unknown> | null {
    return this.cells().find((c) => c.bmCell() === key)?.template ?? null;
  }

  protected valueOf(row: T, key: string): unknown {
    return (row as Record<string, unknown>)[key] ?? '—';
  }

  protected trackRow(row: T, index: number): unknown {
    const key = this.rowKey();
    return key ? row[key] : index;
  }

  protected toggleSort(key: string): void {
    const s = this.sort();
    this.sort.set({ key, direction: s?.key === key && s.direction === 'asc' ? 'desc' : 'asc' });
  }

  protected ariaSort(col: TableColumn): 'ascending' | 'descending' | 'none' | null {
    if (!col.sortable) return null;
    const s = this.sort();
    return s?.key !== col.key ? 'none' : s.direction === 'asc' ? 'ascending' : 'descending';
  }

  protected sortIcon(col: TableColumn): 'chevron-up' | 'chevron-down' {
    const s = this.sort();
    return s?.key === col.key && s.direction === 'desc' ? 'chevron-down' : 'chevron-up';
  }
}
