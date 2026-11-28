import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Button } from '../button/button';
import { DialogShell } from './dialog-shell';

export interface ConfirmOptions {
  readonly title: string;
  readonly message: string;
  readonly confirmLabel?: string;
  readonly cancelLabel?: string;
  /** 'danger' styles the confirm button red, uses role=alertdialog and focuses Cancel first. */
  readonly tone?: 'default' | 'danger';
  /** Audited actions (e.g. admin suspend): require a written reason before confirming. */
  readonly reason?: { readonly label: string; readonly minLength?: number };
  readonly titleId: string;
}

export type ConfirmResult = { confirmed: false } | { confirmed: true; reason?: string };

@Component({
  selector: 'bm-confirm-dialog',
  imports: [DialogShell, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <bm-dialog-shell [heading]="data.title" [titleId]="data.titleId">
      <p>{{ data.message }}</p>
      @if (data.reason; as r) {
        <label class="mt-4 block text-sm font-medium text-text" for="bm-confirm-reason">{{
          r.label
        }}</label>
        <textarea
          id="bm-confirm-reason"
          rows="3"
          class="mt-1.5 w-full rounded-md border border-border-strong bg-surface-raised p-3 text-text"
          [attr.aria-describedby]="'bm-confirm-reason-hint'"
          (input)="reason.set($any($event.target).value)"
        ></textarea>
        <p id="bm-confirm-reason-hint" class="mt-1 text-xs text-subtle">
          At least {{ minLength() }} characters. This is recorded in the audit log.
        </p>
      }
      <ng-container dialogActions>
        <button bmButton variant="ghost" type="button" (click)="close(false)">
          {{ data.cancelLabel ?? 'Cancel' }}
        </button>
        <button
          bmButton
          type="button"
          [variant]="data.tone === 'danger' ? 'danger' : 'primary'"
          [disabled]="!canConfirm()"
          (click)="close(true)"
        >
          {{ data.confirmLabel ?? 'Confirm' }}
        </button>
      </ng-container>
    </bm-dialog-shell>
  `,
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmOptions>(DIALOG_DATA);
  private readonly ref = inject<DialogRef<ConfirmResult>>(DialogRef);
  protected readonly reason = signal('');
  protected readonly minLength = computed(() => this.data.reason?.minLength ?? 10);
  protected readonly canConfirm = computed(
    () => !this.data.reason || this.reason().trim().length >= this.minLength(),
  );

  protected close(confirmed: boolean): void {
    if (!confirmed) return this.ref.close({ confirmed: false });
    if (!this.canConfirm()) return;
    this.ref.close(
      this.data.reason ? { confirmed: true, reason: this.reason().trim() } : { confirmed: true },
    );
  }
}
