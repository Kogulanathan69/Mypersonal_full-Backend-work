import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Icon } from '../../shared/ui/icon/icon';
import { IconName } from '../../shared/ui/icon/icon-paths';
import { BrandLogo } from '../parts/brand-logo';
import { SkipLink } from '../parts/skip-link';

/**
 * Sign in, register, verify, forgot and reset password (C02–C05). Owner: F — Foundation.
 * The reference screens use a house photograph on the left; until image rights are confirmed
 * this uses a drawn blueprint panel instead.
 */
@Component({
  selector: 'bm-auth-layout',
  imports: [RouterOutlet, BrandLogo, SkipLink, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block min-h-dvh' },
  template: `
    <bm-skip-link />
    <div class="grid min-h-dvh lg:grid-cols-[minmax(0,1.1fr)_minmax(0,1fr)]">
      <aside
        class="relative hidden flex-col justify-between overflow-hidden border-r border-border bg-bg-deep blueprint-grid p-10 lg:flex"
        aria-label="About BuildMind"
      >
        <bm-brand-logo />
        <div class="max-w-lg">
          <p class="text-4xl leading-tight font-semibold text-text">
            Plan your home <span class="text-accent">from your land</span> to a practical 2D layout.
          </p>
          <div class="mt-6 gold-rule w-28" aria-hidden="true"></div>
          <ul class="mt-8 flex flex-col gap-4">
            @for (point of points; track point.text) {
              <li class="flex items-center gap-3 text-muted">
                <span
                  class="flex size-10 items-center justify-center rounded-pill border border-accent/60 text-accent"
                >
                  <bm-icon [name]="point.icon" [size]="20" />
                </span>
                {{ point.text }}
              </li>
            }
          </ul>
        </div>
        <p class="text-xs text-subtle">Preliminary plans only — not approved building drawings.</p>
      </aside>

      <div class="flex flex-col px-4 py-6 sm:px-8">
        <div class="lg:hidden"><bm-brand-logo /></div>
        <main
          id="main-content"
          tabindex="-1"
          class="flex flex-1 items-center justify-center py-8 focus:outline-none"
        >
          <div class="w-full max-w-md surface-card p-6 sm:p-8">
            <router-outlet />
          </div>
        </main>
      </div>
    </div>
  `,
})
export class AuthLayout {
  protected readonly points: readonly { icon: IconName; text: string }[] = [
    { icon: 'map-pin', text: 'Start from your land and survey plan' },
    { icon: 'layers', text: 'Compare three AI-generated 2D layouts' },
    { icon: 'edit', text: 'Edit, validate and confirm your plan' },
  ];
}
