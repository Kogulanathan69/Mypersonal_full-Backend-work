import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Button } from '../../shared/ui/button/button';
import { Drawer } from '../../shared/ui/drawer/drawer';
import { Icon } from '../../shared/ui/icon/icon';
import { AccountMenu } from '../parts/account-menu';
import { BrandLogo } from '../parts/brand-logo';
import { CUSTOMER_ACCOUNT_NAV, CUSTOMER_NAV, CUSTOMER_PRIMARY_ACTION } from '../parts/nav.model';
import { SkipLink } from '../parts/skip-link';

/**
 * Signed-in customer shell (C06 onward): top navigation as in the reference screens.
 * Owner: F — Foundation. User name and notification count are wired to the session in step 7.
 */
@Component({
  selector: 'bm-customer-layout',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    BrandLogo,
    SkipLink,
    AccountMenu,
    Drawer,
    Icon,
    Button,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex min-h-dvh flex-col' },
  template: `
    <bm-skip-link />
    <header class="sticky top-0 z-30 border-b border-border bg-bg-deep/90 backdrop-blur">
      <div class="mx-auto flex h-16 max-w-7xl items-center gap-3 px-4 sm:px-6">
        <button
          type="button"
          class="-ml-2 flex size-11 items-center justify-center rounded-md text-text md:hidden"
          [attr.aria-expanded]="menuOpen()"
          (click)="menuOpen.set(true)"
        >
          <bm-icon name="menu" [size]="24" label="Open navigation" />
        </button>
        <bm-brand-logo link="/app/dashboard" [showTagline]="false" />

        <nav aria-label="Main" class="ml-6 hidden md:block">
          <ul class="flex items-center gap-1">
            @for (item of nav; track item.link) {
              <li>
                <a
                  [routerLink]="item.link"
                  routerLinkActive="bg-surface-raised text-text"
                  ariaCurrentWhenActive="page"
                  class="flex items-center gap-2 rounded-pill px-4 py-2 text-sm text-muted no-underline hover:text-text"
                >
                  <bm-icon [name]="item.icon" [size]="18" />{{ item.label }}
                </a>
              </li>
            }
          </ul>
        </nav>

        <div class="ml-auto flex items-center gap-2 sm:gap-3">
          <a bmButton size="sm" [routerLink]="primary.link" class="max-sm:hidden">
            <bm-icon [name]="primary.icon" [size]="18" />{{ primary.label }}
          </a>
          <a
            routerLink="/app/notifications"
            class="relative flex size-11 items-center justify-center rounded-pill text-muted hover:text-text"
          >
            <bm-icon name="bell" [size]="22" [label]="notificationLabel()" />
            @if ((notificationCount() ?? 0) > 0) {
              <span
                class="absolute top-1.5 right-1.5 flex min-w-4.5 items-center justify-center rounded-pill bg-danger px-1 text-[0.65rem] font-bold text-on-accent"
                aria-hidden="true"
              >
                {{ notificationCount() }}
              </span>
            }
          </a>
          <bm-account-menu
            [userName]="userName()"
            [items]="accountNav"
            (signOut)="signOut.emit()"
          />
        </div>
      </div>
    </header>

    <bm-drawer [(open)]="menuOpen" label="Navigation">
      <a bmButton [block]="true" [routerLink]="primary.link" (click)="menuOpen.set(false)">
        <bm-icon [name]="primary.icon" [size]="18" />{{ primary.label }}
      </a>
      <nav aria-label="Main" class="mt-4">
        <ul class="flex flex-col gap-1">
          @for (item of nav; track item.link) {
            <li>
              <a
                [routerLink]="item.link"
                routerLinkActive="bg-surface-raised text-text"
                ariaCurrentWhenActive="page"
                class="flex items-center gap-3 rounded-md px-3 py-3 text-muted no-underline hover:text-text"
                (click)="menuOpen.set(false)"
              >
                <bm-icon [name]="item.icon" [size]="20" />{{ item.label }}
              </a>
            </li>
          }
        </ul>
      </nav>
    </bm-drawer>

    <main
      id="main-content"
      tabindex="-1"
      class="mx-auto w-full max-w-7xl flex-1 px-4 py-6 focus:outline-none sm:px-6 sm:py-8"
    >
      <router-outlet />
    </main>
  `,
})
export class CustomerLayout {
  readonly userName = input<string | null>(null);
  readonly notificationCount = input<number | null>(null);
  readonly signOut = output<void>();

  protected readonly nav = CUSTOMER_NAV;
  protected readonly primary = CUSTOMER_PRIMARY_ACTION;
  protected readonly accountNav = CUSTOMER_ACCOUNT_NAV;
  protected readonly menuOpen = signal(false);

  protected notificationLabel(): string {
    const n = this.notificationCount() ?? 0;
    return n > 0 ? `Notifications, ${n} unread` : 'Notifications';
  }
}
