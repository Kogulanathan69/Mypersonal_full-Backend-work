import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Icon } from '../icon/icon';

export interface BreadcrumbItem {
  readonly label: string;
  readonly link?: string;
}

@Component({
  selector: 'bm-breadcrumb',
  imports: [RouterLink, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav aria-label="Breadcrumb">
      <ol class="flex flex-wrap items-center gap-1.5 text-sm text-muted">
        @for (item of items(); track $index; let last = $last) {
          <li class="flex items-center gap-1.5">
            @if (!last && item.link) {
              <a [routerLink]="item.link" class="text-muted no-underline hover:text-accent">{{
                item.label
              }}</a>
            } @else {
              <span [attr.aria-current]="last ? 'page' : null" [class.text-text]="last">{{
                item.label
              }}</span>
            }
            @if (!last) {
              <bm-icon name="chevron-right" [size]="14" class="text-subtle" />
            }
          </li>
        }
      </ol>
    </nav>
  `,
})
export class Breadcrumb {
  readonly items = input.required<readonly BreadcrumbItem[]>();
}
