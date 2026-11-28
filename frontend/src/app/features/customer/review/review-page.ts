import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { AppError } from '../../../core/api/app-error';
import { ErrorState } from '../../../shared/ui/states/error-state';
import { Skeleton } from '../../../shared/ui/skeleton/skeleton';
import { ArchitectDirectoryEntry, ArchitectRequest, CustomerApi } from '../customer-api';

@Component({
  selector: 'bm-review-page',
  imports: [RouterLink, ErrorState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a [routerLink]="['/app/projects', id()]" class="text-sm">Back to project</a>
    <header class="mt-5 border-b border-border pb-6">
      <h1 class="text-3xl text-text">Architect review</h1>
      <p class="mt-2 max-w-3xl text-muted">
        Review verified architect recipients and track consented professional-review requests.
      </p>
    </header>
    @if (loading()) {
      <div class="mt-8"><bm-skeleton height="20rem" /></div>
    } @else if (error(); as failure) {
      <div class="mt-8"><bm-error-state [error]="failure" (retry)="load()" /></div>
    } @else {
      <div class="mt-8 grid gap-10 lg:grid-cols-2">
        <section>
          <h2 class="text-xl text-text">Verified directory</h2>
          <div class="mt-5 divide-y divide-border border-y border-border">
            @for (architect of architects(); track architect.recipientId) {
              <article class="py-4">
                <p class="font-medium text-text">{{ architect.displayName }}</p>
                <p class="mt-1 text-sm text-muted">{{ architect.verificationStatus }}</p>
              </article>
            } @empty {
              <p class="py-5 text-sm text-muted">No architects are currently listed.</p>
            }
          </div>
        </section>
        <section class="lg:border-l lg:border-border lg:pl-8">
          <h2 class="text-xl text-text">Review requests</h2>
          <div class="mt-5 divide-y divide-border border-y border-border">
            @for (request of requests(); track request.requestId) {
              <article class="py-4">
                <div class="flex justify-between gap-4">
                  <p class="font-medium text-text">{{ request.status }}</p>
                  <span class="text-sm text-muted">{{ request.delivery }}</span>
                </div>
                <p class="mt-1 text-sm text-muted">
                  Policy {{ request.policyVersion }} · explicit consent recorded
                  {{ request.consentedAtUtc }}
                </p>
              </article>
            } @empty {
              <p class="py-5 text-sm text-muted">No architect review has been requested.</p>
            }
          </div>
          <p class="mt-6 text-sm text-muted">
            A new request requires an authoritative current policy version, a ready report and
            explicit consent. The backend exposes no policy-discovery operation, so this client
            will not guess or hard-code a version.
          </p>
        </section>
      </div>
    }
  `,
})
export class ReviewPage {
  readonly id = input.required<string>();
  private readonly api = inject(CustomerApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly architects = signal<readonly ArchitectDirectoryEntry[]>([]);
  protected readonly requests = signal<readonly ArchitectRequest[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<AppError | null>(null);

  constructor() {
    queueMicrotask(() => this.load());
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      architects: this.api.listArchitects(),
      requests: this.api.listArchitectRequests(this.id()),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (value) => {
          this.architects.set(value.architects);
          this.requests.set(value.requests);
          this.loading.set(false);
        },
        error: (error: AppError) => {
          this.error.set(error);
          this.loading.set(false);
        },
      });
  }
}
