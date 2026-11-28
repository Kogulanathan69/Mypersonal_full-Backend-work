import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  input,
  model,
  viewChildren,
} from '@angular/core';

export interface TabItem {
  readonly id: string;
  readonly label: string;
  readonly count?: number;
}

/**
 * WAI-ARIA tablist with roving focus (Left/Right/Home/End). The panel content is rendered
 * by the parent; give the panel `id="{idPrefix}-panel-{tabId}"` and role="tabpanel".
 */
@Component({
  selector: 'bm-tabs',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      role="tablist"
      [attr.aria-label]="label()"
      class="flex gap-1 overflow-x-auto border-b border-border"
    >
      @for (tab of tabs(); track tab.id; let i = $index) {
        <button
          #tabButton
          type="button"
          role="tab"
          [id]="idPrefix() + '-tab-' + tab.id"
          [attr.aria-selected]="tab.id === selected()"
          [attr.aria-controls]="idPrefix() + '-panel-' + tab.id"
          [attr.tabindex]="tab.id === selected() ? 0 : -1"
          (click)="select(tab.id)"
          (keydown)="onKey($event, i)"
          class="-mb-px flex items-center gap-2 border-b-2 px-4 py-2.5 text-sm whitespace-nowrap transition-colors"
          [class]="
            tab.id === selected()
              ? 'border-accent font-semibold text-text'
              : 'border-transparent text-muted hover:text-text'
          "
        >
          {{ tab.label }}
          @if (tab.count !== undefined) {
            <span class="rounded-pill bg-surface-raised px-2 text-xs text-muted">{{
              tab.count
            }}</span>
          }
        </button>
      }
    </div>
  `,
})
export class Tabs {
  readonly tabs = input.required<readonly TabItem[]>();
  readonly label = input.required<string>();
  readonly idPrefix = input('bm-tabs');
  readonly selected = model<string>('');

  private readonly buttons = viewChildren<ElementRef<HTMLButtonElement>>('tabButton');

  protected select(id: string): void {
    this.selected.set(id);
  }

  protected onKey(event: KeyboardEvent, index: number): void {
    const count = this.tabs().length;
    const target = {
      ArrowRight: (index + 1) % count,
      ArrowLeft: (index - 1 + count) % count,
      Home: 0,
      End: count - 1,
    }[event.key];
    if (target === undefined) return;
    event.preventDefault();
    const tab = this.tabs()[target];
    if (!tab) return;
    this.select(tab.id);
    this.buttons()[target]?.nativeElement.focus();
  }
}
