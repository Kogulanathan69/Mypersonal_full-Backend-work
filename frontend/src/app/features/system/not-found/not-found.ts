import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Button } from '../../../shared/ui/button/button';
import { Icon } from '../../../shared/ui/icon/icon';

/** 404 for unknown URLs. Owner: F — Foundation. */
@Component({
  selector: 'bm-not-found',
  imports: [RouterLink, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="flex min-h-dvh items-center justify-center blueprint-grid px-6 py-16">
      <div class="flex max-w-md flex-col items-center gap-4 surface-card p-8 text-center">
        <span
          class="flex size-14 items-center justify-center rounded-pill bg-surface-raised text-accent"
        >
          <bm-icon name="compass" [size]="28" />
        </span>
        <p class="text-sm font-medium tracking-wide text-accent uppercase">Error 404</p>
        <h1 class="text-2xl text-text">We could not find that page</h1>
        <p class="text-muted">The link may be out of date, or the page may have moved.</p>
        <a bmButton routerLink="/">Go to the start page</a>
      </div>
    </main>
  `,
})
export class NotFound {}
