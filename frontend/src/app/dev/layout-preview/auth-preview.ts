import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Skeleton } from '../../shared/ui/skeleton/skeleton';

/** DEV ONLY — placeholder for the auth card. The real C02–C05 forms arrive in step 9. */
@Component({
  selector: 'bm-auth-preview',
  imports: [Skeleton, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="text-2xl text-text">Form area</h1>
    <p class="mt-2 text-sm text-muted">
      Sign in, register and password screens render here (step 9).
    </p>
    <div class="mt-6"><bm-skeleton [lines]="5" height="2.75rem" /></div>
    <p class="mt-6 text-sm"><a routerLink="/dev/layouts">← All layouts</a></p>
  `,
})
export class AuthPreview {}
