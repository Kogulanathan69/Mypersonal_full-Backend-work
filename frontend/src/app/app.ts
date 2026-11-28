import { ChangeDetectionStrategy, Component, Injector, effect, inject } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { SessionEventsService } from './core/auth/session-events.service';
import { DemoDataBanner } from './shared/ui/feedback/demo-data-banner';
import { GlobalProgress } from './shared/ui/feedback/global-progress';
import { OfflineBanner } from './shared/ui/feedback/offline-banner';
import { ToastHost } from './shared/ui/feedback/toast-host';
import type { SessionExpiredChoice } from './shared/ui/dialog/session-expired-dialog';

/** Root shell. Owner: F — Foundation. Global feedback lives here so every page gets it. */
@Component({
  selector: 'bm-root',
  imports: [RouterOutlet, GlobalProgress, DemoDataBanner, OfflineBanner, ToastHost],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <bm-global-progress />
    <bm-demo-data-banner />
    <bm-offline-banner />
    <router-outlet />
    <bm-toast-host />
  `,
})
export class App {
  private readonly session = inject(SessionEventsService);
  private readonly injector = inject(Injector);
  private readonly router = inject(Router);
  private dialogOpen = false;

  constructor() {
    effect(() => {
      if (this.session.expired() && !this.dialogOpen) this.openSessionExpired();
    });
  }

  /**
   * The dialog code (CDK overlay, ~55 kB) is loaded only when a session actually expires,
   * keeping it out of the initial bundle.
   */
  private async openSessionExpired(): Promise<void> {
    this.dialogOpen = true;
    const [{ Dialog }, { SessionExpiredDialog }] = await Promise.all([
      import('@angular/cdk/dialog'),
      import('./shared/ui/dialog/session-expired-dialog'),
    ]);
    const ref = this.injector.get(Dialog).open<SessionExpiredChoice>(SessionExpiredDialog, {
      ariaLabelledBy: 'bm-session-expired-title',
      role: 'alertdialog',
      disableClose: true,
      backdropClass: 'bm-dialog-backdrop',
    });
    ref.closed.subscribe((choice) => {
      this.dialogOpen = false;
      this.session.acknowledge();
      if (choice === 'sign-in') {
        void this.router.navigate(['/auth/sign-in'], {
          queryParams: { returnUrl: this.router.url },
        });
      }
    });
  }
}
