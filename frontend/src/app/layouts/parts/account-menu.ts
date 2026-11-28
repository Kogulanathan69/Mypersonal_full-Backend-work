import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { Icon } from '../../shared/ui/icon/icon';
import { NavItem } from './nav.model';

/**
 * Account disclosure menu (button + list of links). Closes on Escape, outside click and
 * navigation. Uses the disclosure pattern rather than role="menu" so links behave normally.
 */
@Component({
  selector: 'bm-account-menu',
  imports: [RouterLink, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'relative',
    '(document:click)': 'onDocumentClick($event)',
    '(keydown.escape)': 'close(true)',
  },
  template: `
    <button
      #trigger
      type="button"
      class="flex items-center gap-2 rounded-pill py-1 pr-2 pl-1 text-sm text-text hover:bg-surface-raised"
      [attr.aria-expanded]="open()"
      aria-controls="bm-account-menu-panel"
      [attr.aria-label]="accessibleName()"
      (click)="open.set(!open())"
    >
      <span
        class="flex size-8 items-center justify-center rounded-pill bg-surface-raised text-xs font-semibold text-accent"
        aria-hidden="true"
      >
        {{ initials() }}
      </span>
      <span class="hidden max-w-32 truncate sm:inline">{{ displayName() }}</span>
      <bm-icon name="chevron-down" [size]="16" />
    </button>
    @if (open()) {
      <div
        id="bm-account-menu-panel"
        class="absolute right-0 z-40 mt-2 w-56 rounded-lg border border-border bg-surface-raised p-1.5 shadow-card"
      >
        <ul class="flex flex-col">
          @for (item of items(); track item.link) {
            <li>
              <a
                [routerLink]="item.link"
                class="flex items-center gap-3 rounded-md px-3 py-2.5 text-sm text-text no-underline hover:bg-surface"
                (click)="close(false)"
              >
                <bm-icon [name]="item.icon" [size]="18" class="text-muted" />{{ item.label }}
              </a>
            </li>
          }
          <li class="mt-1 border-t border-border pt-1">
            <button
              type="button"
              class="flex w-full items-center gap-3 rounded-md px-3 py-2.5 text-left text-sm text-text hover:bg-surface"
              (click)="signOut.emit(); close(false)"
            >
              <bm-icon name="logout" [size]="18" class="text-muted" />Sign out
            </button>
          </li>
        </ul>
      </div>
    }
  `,
})
export class AccountMenu {
  /** Null until the session store exists (step 7): shows a neutral "Account" label, never a fake name. */
  readonly userName = input<string | null>(null);
  readonly role = input<string | null>(null);
  readonly items = input<readonly NavItem[]>([]);
  readonly signOut = output<void>();

  protected readonly open = signal(false);
  private readonly host = inject(ElementRef<HTMLElement>);

  protected readonly displayName = computed(() => this.userName() ?? 'Account');
  /** Always present, including on phones where the visible name is hidden. */
  protected readonly accessibleName = computed(() =>
    [this.userName() ? `Account menu for ${this.userName()}` : 'Account menu', this.role()]
      .filter(Boolean)
      .join(', '),
  );
  protected readonly initials = computed(() => {
    const name = this.userName();
    if (!name) return '•';
    return name
      .split(/\s+/)
      .slice(0, 2)
      .map((p) => p.charAt(0).toUpperCase())
      .join('');
  });

  protected close(returnFocus: boolean): void {
    if (!this.open()) return;
    this.open.set(false);
    if (returnFocus) this.host.nativeElement.querySelector('button')?.focus();
  }

  protected onDocumentClick(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node))
      this.open.set(false);
  }
}
