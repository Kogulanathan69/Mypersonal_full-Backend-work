import { Dialog } from '@angular/cdk/dialog';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfirmDialog, ConfirmOptions, ConfirmResult } from './confirm-dialog';

let nextId = 0;

/** Promise-based confirmation for destructive or audited actions. */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly dialog = inject(Dialog);

  async confirm(options: Omit<ConfirmOptions, 'titleId' | 'reason'>): Promise<boolean> {
    return (await this.open(options)).confirmed;
  }

  /** Resolves to the entered reason, or null if cancelled. */
  async confirmWithReason(
    options: Omit<ConfirmOptions, 'titleId'> & { reason: NonNullable<ConfirmOptions['reason']> },
  ): Promise<string | null> {
    const result = await this.open(options);
    return result.confirmed ? (result.reason ?? null) : null;
  }

  private async open(options: Omit<ConfirmOptions, 'titleId'>): Promise<ConfirmResult> {
    const titleId = `bm-confirm-title-${nextId++}`;
    const ref = this.dialog.open<ConfirmResult, ConfirmOptions, ConfirmDialog>(ConfirmDialog, {
      data: { ...options, titleId },
      ariaLabelledBy: titleId,
      role: options.tone === 'danger' ? 'alertdialog' : 'dialog',
      autoFocus: options.reason
        ? 'first-tabbable'
        : options.tone === 'danger'
          ? 'first-tabbable'
          : 'dialog',
      restoreFocus: true,
      backdropClass: 'bm-dialog-backdrop',
    });
    return (await firstValueFrom(ref.closed)) ?? { confirmed: false };
  }
}
