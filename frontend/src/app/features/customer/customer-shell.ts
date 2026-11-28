import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { AUTH_API } from '../../core/auth/auth-api';
import { SessionStore } from '../../core/auth/session.store';
import { CustomerLayout } from '../../layouts/customer-layout/customer-layout';
import { CustomerApi } from './customer-api';

@Component({
  selector: 'bm-customer-shell',
  imports: [CustomerLayout],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <bm-customer-layout
      [userName]="session.user()?.displayName ?? null"
      [notificationCount]="unread()"
      (signOut)="signOut()"
    />
  `,
})
export class CustomerShell {
  protected readonly session = inject(SessionStore);
  protected readonly unread = signal<number | null>(null);
  private readonly auth = inject(AUTH_API);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  constructor() {
    inject(CustomerApi)
      .unreadNotifications()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (count) => this.unread.set(count), error: () => this.unread.set(null) });
  }

  protected signOut(): void {
    this.auth
      .signOut()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => void this.router.navigate(['/auth/sign-in']),
        error: () => void this.router.navigate(['/auth/sign-in']),
      });
  }
}
