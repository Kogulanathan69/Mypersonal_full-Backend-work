import { DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Button } from '../button/button';
import { DialogShell } from './dialog-shell';

export type SessionExpiredChoice = 'sign-in' | 'dismiss';

/**
 * Shown when the backend reports the session has ended (401). The sign-in route and
 * return-to-page handling are wired in Phase 01 step 7.
 */
@Component({
  selector: 'bm-session-expired-dialog',
  imports: [DialogShell, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <bm-dialog-shell heading="Your session has ended" titleId="bm-session-expired-title">
      <p>
        For your security you have been signed out. Sign in again to continue. Unsaved changes on
        this page may be lost.
      </p>
      <ng-container dialogActions>
        <button bmButton variant="ghost" type="button" (click)="ref.close('dismiss')">
          Not now
        </button>
        <button bmButton type="button" (click)="ref.close('sign-in')">Sign in again</button>
      </ng-container>
    </bm-dialog-shell>
  `,
})
export class SessionExpiredDialog {
  protected readonly ref = inject<DialogRef<SessionExpiredChoice>>(DialogRef);
}
