import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { Drawer } from '../../shared/ui/drawer/drawer';
import { Icon } from '../../shared/ui/icon/icon';
import { BrandLogo } from '../parts/brand-logo';
import { PUBLIC_NAV } from '../parts/nav.model';
import { SkipLink } from '../parts/skip-link';

/**
 * Public website shell (Home, About, Contact, legal pages). Owner: F — Foundation.
 * Uses the scoped editorial marketing theme (mk-*). Authenticated portals keep the navy/gold theme.
 */
@Component({
  selector: 'bm-public-layout',
  imports: [RouterOutlet, RouterLink, BrandLogo, SkipLink, Drawer, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex min-h-dvh flex-col bg-mk-paper text-mk-ink' },
  template: `
    <bm-skip-link />
    <header
      class="mk-public-header sticky top-0 z-30 border-b border-mk-white/10 bg-mk-cinema/95 text-mk-white backdrop-blur-md"
    >
      <div
        class="mx-auto flex h-16 max-w-7xl items-center justify-between gap-4 px-4 sm:px-6 lg:h-18"
      >
        <bm-brand-logo />
        <nav aria-label="Main" class="hidden lg:block">
          <ul class="flex items-center gap-8 text-sm font-medium">
            @for (item of nav; track item.label) {
              <li>
                <a
                  [routerLink]="item.link"
                  [fragment]="item.fragment"
                  class="relative text-mk-muted no-underline transition-colors after:absolute after:-bottom-2 after:left-0 after:h-px after:w-0 after:bg-mk-gold after:transition-all hover:text-mk-white hover:after:w-full"
                  >{{ item.label }}</a
                >
              </li>
            }
          </ul>
        </nav>
        <div class="hidden items-center gap-3 sm:flex">
          <a
            routerLink="/auth/sign-in"
            class="inline-flex h-10 items-center rounded-pill border border-mk-white/35 px-5 text-sm font-medium text-mk-white no-underline transition-colors hover:border-mk-gold"
            >Login</a
          >
          <a
            routerLink="/"
            fragment="planning-experience"
            class="inline-flex h-10 items-center rounded-pill bg-mk-gold px-5 text-sm font-semibold text-mk-ink no-underline transition-colors hover:bg-mk-white"
            >Explore the experience</a
          >
        </div>
        <button
          type="button"
          class="flex size-11 items-center justify-center rounded-md text-mk-white lg:hidden"
          [attr.aria-expanded]="menuOpen()"
          (click)="menuOpen.set(true)"
        >
          <bm-icon name="menu" [size]="24" label="Open menu" />
        </button>
      </div>
    </header>

    <bm-drawer [(open)]="menuOpen" label="Menu" side="right">
      <nav aria-label="Main">
        <ul class="flex flex-col gap-1">
          @for (item of nav; track item.label) {
            <li>
              <a
                [routerLink]="item.link"
                [fragment]="item.fragment"
                class="block rounded-md px-3 py-3 text-text no-underline hover:bg-surface-raised"
                (click)="menuOpen.set(false)"
              >
                {{ item.label }}
              </a>
            </li>
          }
        </ul>
      </nav>
      <div class="mt-6 flex flex-col gap-3">
        <a
          routerLink="/auth/sign-in"
          class="inline-flex h-11 items-center justify-center rounded-pill border border-border-strong font-medium text-text no-underline"
          (click)="menuOpen.set(false)"
          >Login</a
        >
        <a
          routerLink="/"
          fragment="planning-experience"
          class="inline-flex h-11 items-center justify-center rounded-pill bg-mk-gold px-4 font-semibold text-mk-ink no-underline"
          (click)="menuOpen.set(false)"
          >Explore the experience</a
        >
      </div>
    </bm-drawer>

    <main id="main-content" tabindex="-1" class="flex-1 focus:outline-none">
      <router-outlet />
    </main>

    <footer class="border-t border-mk-white/12 bg-mk-ink text-mk-white">
      <div class="mx-auto grid max-w-7xl gap-10 px-4 py-12 sm:px-6 md:grid-cols-[1.4fr_1fr_1fr]">
        <div>
          <bm-brand-logo />
          <p class="mt-4 max-w-md text-xs text-mk-muted">
            BuildMind prepares preliminary 2D house plans for discussion. They are not approved
            building drawings. Planning approval must be obtained from your local authority, and
            plans should be reviewed by a qualified professional.
          </p>
        </div>
        <nav aria-label="Product">
          <p class="text-sm font-semibold text-mk-white">Product</p>
          <ul class="mt-3 space-y-2 text-sm">
            <li>
              <a
                routerLink="/"
                fragment="features"
                class="text-mk-muted no-underline hover:text-mk-white"
                >Features</a
              >
            </li>
            <li>
              <a
                routerLink="/"
                fragment="how-it-works"
                class="text-mk-muted no-underline hover:text-mk-white"
                >How It Works</a
              >
            </li>
            <li>
              <a routerLink="/auth/register" class="text-mk-muted no-underline hover:text-mk-white"
                >Get Started</a
              >
            </li>
          </ul>
        </nav>
        <nav aria-label="Company and legal">
          <p class="text-sm font-semibold text-mk-white">Company</p>
          <ul class="mt-3 space-y-2 text-sm">
            <li>
              <a routerLink="/about" class="text-mk-muted no-underline hover:text-mk-white"
                >About</a
              >
            </li>
            <li>
              <a routerLink="/contact" class="text-mk-muted no-underline hover:text-mk-white"
                >Contact</a
              >
            </li>
            <li>
              <a routerLink="/privacy" class="text-mk-muted no-underline hover:text-mk-white"
                >Privacy</a
              >
            </li>
            <li>
              <a routerLink="/terms" class="text-mk-muted no-underline hover:text-mk-white"
                >Terms</a
              >
            </li>
          </ul>
        </nav>
      </div>
    </footer>
  `,
})
export class PublicLayout {
  protected readonly nav = PUBLIC_NAV;
  protected readonly menuOpen = signal(false);
}
