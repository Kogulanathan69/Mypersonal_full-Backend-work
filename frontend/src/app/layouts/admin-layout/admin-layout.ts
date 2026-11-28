import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Badge } from '../../shared/ui/badge/badge';
import { Drawer } from '../../shared/ui/drawer/drawer';
import { Icon } from '../../shared/ui/icon/icon';
import { AccountMenu } from '../parts/account-menu';
import { BrandLogo } from '../parts/brand-logo';
import { ADMIN_NAV } from '../parts/nav.model';
import { SkipLink } from '../parts/skip-link';

/**
 * Admin Portal shell (A02–A18): same brand, distinct structure — a sidebar for operational
 * sections and an "Admin" marker so staff always know which portal they are in.
 * Owner: F — Foundation. Role and name are wired to verified claims in step 7.
 */
@Component({
  selector: 'bm-admin-layout',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    NgTemplateOutlet,
    BrandLogo,
    SkipLink,
    AccountMenu,
    Drawer,
    Icon,
    Badge,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex min-h-dvh' },
  template: `
    <bm-skip-link />
    <ng-template #navList>
      <nav aria-label="Admin">
        @for (group of nav; track group.label) {
          <p
            class="mt-5 mb-2 px-3 text-[0.7rem] font-semibold tracking-wider text-subtle uppercase first:mt-0"
          >
            {{ group.label }}
          </p>
          <ul class="flex flex-col gap-0.5">
            @for (item of group.items; track item.link) {
              <li>
                <a
                  [routerLink]="item.link"
                  routerLinkActive="bg-surface-raised text-accent"
                  [routerLinkActiveOptions]="{ exact: !!item.exact }"
                  ariaCurrentWhenActive="page"
                  class="flex items-center gap-3 rounded-md px-3 py-2.5 text-sm text-muted no-underline hover:bg-surface-raised hover:text-text"
                  (click)="menuOpen.set(false)"
                >
                  <bm-icon [name]="item.icon" [size]="18" />{{ item.label }}
                </a>
              </li>
            }
          </ul>
        }
      </nav>
    </ng-template>

    <aside
      class="sticky top-0 z-30 hidden h-dvh w-64 shrink-0 flex-col border-r border-border bg-bg-deep lg:flex"
    >
      <div class="flex h-16 items-center gap-3 border-b border-border px-5">
        <bm-brand-logo link="/admin" [showTagline]="false" />
        <bm-badge tone="accent">Admin</bm-badge>
      </div>
      <div class="flex-1 overflow-y-auto px-3 py-5">
        <ng-container *ngTemplateOutlet="navList" />
      </div>
    </aside>

    <bm-drawer [(open)]="menuOpen" label="Admin navigation">
      <ng-container *ngTemplateOutlet="navList" />
    </bm-drawer>

    <div class="flex min-w-0 flex-1 flex-col">
      <header
        class="sticky top-0 z-20 flex h-16 items-center gap-3 border-b border-border bg-bg/95 px-4 backdrop-blur sm:px-6"
      >
        <button
          type="button"
          class="-ml-2 flex size-11 items-center justify-center rounded-md text-text lg:hidden"
          [attr.aria-expanded]="menuOpen()"
          (click)="menuOpen.set(true)"
        >
          <bm-icon name="menu" [size]="24" label="Open admin navigation" />
        </button>
        <div class="flex items-center gap-2 lg:hidden">
          <bm-brand-logo link="/admin" [showTagline]="false" />
          <bm-badge tone="accent">Admin</bm-badge>
        </div>
        <div class="ml-auto flex items-center gap-3">
          @if (role()) {
            <bm-badge tone="info" class="max-sm:hidden">{{ role() }}</bm-badge>
          }
          <bm-account-menu [userName]="userName()" [role]="role()" (signOut)="signOut.emit()" />
        </div>
      </header>
      <main
        id="main-content"
        tabindex="-1"
        class="flex-1 px-4 py-6 focus:outline-none sm:px-6 lg:px-8"
      >
        <router-outlet />
      </main>
    </div>
  `,
})
export class AdminLayout {
  readonly userName = input<string | null>(null);
  /** Display label of a verified role claim (e.g. "Auditor"). Null until step 7. */
  readonly role = input<string | null>(null);
  readonly signOut = output<void>();

  protected readonly nav = ADMIN_NAV;
  protected readonly menuOpen = signal(false);
}
