import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Card } from '../../shared/ui/card/card';
import { Icon } from '../../shared/ui/icon/icon';

/** DEV ONLY — index of layout previews. */
@Component({
  selector: 'bm-layout-index',
  imports: [RouterLink, Card, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-4xl px-4 py-10 sm:px-6">
      <h1 class="text-3xl text-text">Layout previews</h1>
      <div class="mt-3 gold-rule w-24" aria-hidden="true"></div>
      <p class="mt-3 text-muted">
        Each shell with placeholder content. Navigation links point at routes added in later steps,
        so they show the 404 page for now.
      </p>
      <ul class="mt-6 grid gap-4 sm:grid-cols-2">
        @for (l of layouts; track l.link) {
          <li>
            <a [routerLink]="l.link" class="block no-underline">
              <bm-card [heading]="l.name" [subheading]="l.use" [level]="2">
                <span class="flex items-center gap-1 text-sm text-accent"
                  >Open preview <bm-icon name="arrow-right" [size]="16"
                /></span>
              </bm-card>
            </a>
          </li>
        }
      </ul>
      <p class="mt-6"><a routerLink="/dev/ui">← Back to UI gallery</a></p>
    </div>
  `,
})
export class LayoutIndex {
  protected readonly layouts = [
    { name: 'Public', use: 'Welcome and legal pages (C01)', link: '/dev/layouts/public' },
    { name: 'Auth', use: 'Sign in, register, reset (C02–C05)', link: '/dev/layouts/auth' },
    { name: 'Customer', use: 'Signed-in customer pages (C06+)', link: '/dev/layouts/customer' },
    {
      name: 'Customer + wizard frame',
      use: 'Requirements and planning steps',
      link: '/dev/layouts/customer/wizard',
    },
    { name: 'Admin', use: 'Admin Portal (A01–A18)', link: '/dev/layouts/admin' },
  ];
}
