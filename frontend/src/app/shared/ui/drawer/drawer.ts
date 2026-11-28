import { CdkTrapFocus } from '@angular/cdk/a11y';
import { ChangeDetectionStrategy, Component, effect, inject, input, model } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { Icon } from '../icon/icon';

/**
 * Slide-in panel for mobile navigation and editor tool panels.
 * Traps focus while open, closes on Escape or backdrop click, and returns focus to the opener.
 */
@Component({
  selector: 'bm-drawer',
  imports: [CdkTrapFocus, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'open() && close()' },
  template: `
    @if (open()) {
      <div class="fixed inset-0 z-40 bg-bg-deep/70" aria-hidden="true" (click)="close()"></div>
      <div
        role="dialog"
        aria-modal="true"
        [attr.aria-label]="label()"
        cdkTrapFocus
        [cdkTrapFocusAutoCapture]="true"
        class="fixed inset-y-0 z-50 flex w-[min(86vw,20rem)] flex-col border-border bg-surface shadow-card"
        [class]="side() === 'left' ? 'left-0 border-r' : 'right-0 border-l'"
      >
        <div class="flex items-center justify-between border-b border-border px-4 py-3">
          <span class="font-semibold text-text">{{ label() }}</span>
          <button type="button" class="rounded-md p-2 text-muted hover:text-text" (click)="close()">
            <bm-icon name="x" [size]="20" label="Close {{ label() }}" />
          </button>
        </div>
        <div class="min-h-0 flex-1 overflow-y-auto p-4"><ng-content /></div>
      </div>
    }
  `,
})
export class Drawer {
  readonly open = model(false);
  readonly label = input.required<string>();
  readonly side = input<'left' | 'right'>('left');

  private readonly document = inject(DOCUMENT);
  private opener: HTMLElement | null = null;

  constructor() {
    effect(() => {
      if (this.open()) {
        this.opener = this.document.activeElement as HTMLElement | null;
        this.document.body.style.overflow = 'hidden';
      } else {
        this.document.body.style.overflow = '';
        this.opener?.focus?.();
        this.opener = null;
      }
    });
  }

  close(): void {
    this.open.set(false);
  }
}
