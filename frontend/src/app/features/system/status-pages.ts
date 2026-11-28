import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Button } from '../../shared/ui/button/button';
import { Icon } from '../../shared/ui/icon/icon';

/** Unauthorised access. Shown when a signed-in user opens a page their role does not allow. */
@Component({
  selector: 'bm-forbidden',
  imports: [RouterLink, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main
      id="main-content"
      tabindex="-1"
      class="flex min-h-dvh items-center justify-center blueprint-grid px-6 py-16 focus:outline-none"
    >
      <div class="flex max-w-md flex-col items-center gap-4 surface-card p-8 text-center">
        <span
          class="flex size-14 items-center justify-center rounded-pill bg-surface-raised text-danger"
          ><bm-icon name="lock" [size]="28"
        /></span>
        <p class="text-sm font-medium tracking-wide text-accent uppercase">Error 403</p>
        <h1 class="text-2xl text-text">You don't have access to this page</h1>
        <p class="text-muted">
          Your account does not have permission to view it. If you think this is a mistake, contact
          your BuildMind administrator.
        </p>
        <a bmButton routerLink="/">Go to the start page</a>
      </div>
    </main>
  `,
})
export class Forbidden {}

/** Session ended (e.g. after expiry or sign-out from another device). */
@Component({
  selector: 'bm-session-ended',
  imports: [RouterLink, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main
      id="main-content"
      tabindex="-1"
      class="flex min-h-dvh items-center justify-center blueprint-grid px-6 py-16 focus:outline-none"
    >
      <div class="flex max-w-md flex-col items-center gap-4 surface-card p-8 text-center">
        <span
          class="flex size-14 items-center justify-center rounded-pill bg-surface-raised text-accent"
          ><bm-icon name="clock" [size]="28"
        /></span>
        <h1 class="text-2xl text-text">Your session has ended</h1>
        <p class="text-muted">
          For your security you were signed out. Sign in again to continue where you left off.
        </p>
        <a bmButton routerLink="/auth/sign-in">Sign in again</a>
      </div>
    </main>
  `,
})
export class SessionEnded {}
