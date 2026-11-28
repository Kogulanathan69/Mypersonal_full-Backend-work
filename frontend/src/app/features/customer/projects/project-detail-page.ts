import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { AppError } from '../../../core/api/app-error';
import { ErrorState } from '../../../shared/ui/states/error-state';
import { Skeleton } from '../../../shared/ui/skeleton/skeleton';
import { CustomerApi, ProjectLifecycle, ProjectSummary } from '../customer-api';

@Component({
  selector: 'bm-project-detail-page',
  imports: [DatePipe, RouterLink, ErrorState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a routerLink="/app/projects" class="text-sm">Back to projects</a>
    @if (loading()) {
      <div class="mt-6"><bm-skeleton height="12rem" /></div>
    } @else if (error(); as failure) {
      <div class="mt-6"><bm-error-state [error]="failure" (retry)="load()" /></div>
    } @else if (project(); as item) {
      <header class="mt-5 border-b border-border pb-6">
        <div class="flex flex-wrap items-center justify-between gap-4">
          <p class="text-sm font-medium text-accent">{{ item.status }}</p>
          <a [routerLink]="['/app/projects', item.id, 'edit']" class="text-sm">Edit project details</a>
        </div>
        <h1 class="mt-2 text-3xl text-text">{{ item.name }}</h1>
        <p class="mt-2 text-muted">{{ item.projectType || 'Home planning' }}@if (item.locationReference) { · {{ item.locationReference }} }</p>
      </header>
      <section class="mt-8 grid gap-8 lg:grid-cols-[minmax(0,1fr)_18rem]">
        <div>
          <h2 class="text-xl text-text">Planning workspace</h2>
          <p class="mt-2 max-w-2xl text-muted">Continue with the survey plan, land interpretation, and household requirements for this project.</p>
          <div class="mt-6 divide-y divide-border border-y border-border">
            <a class="flex items-center justify-between py-4 text-text" [routerLink]="['/app/projects', item.id, 'survey']">Survey plan <span>Continue</span></a>
            <a class="flex items-center justify-between py-4 text-text" [routerLink]="['/app/projects', item.id, 'land']">Land boundary <span>Review</span></a>
            <a class="flex items-center justify-between py-4 text-text" [routerLink]="['/app/projects', item.id, 'requirements']">Home requirements <span>Define</span></a>
            <a class="flex items-center justify-between py-4 text-text" [routerLink]="['/app/projects', item.id, 'generation']">Layout generation <span>Generate</span></a>
            <a class="flex items-center justify-between py-4 text-text" [routerLink]="['/app/projects', item.id, 'reports']">Project reports <span>View</span></a>
            <a class="flex items-center justify-between py-4 text-text" [routerLink]="['/app/projects', item.id, 'review']">Architect review <span>Review</span></a>
            <a class="flex items-center justify-between py-4 text-text" [routerLink]="['/app/projects', item.id, 'regulations']">Regulation evidence <span>Check</span></a>
          </div>
        </div>
        <dl class="border-l border-border pl-6 text-sm">
          <div class="pb-4"><dt class="text-muted">Created</dt><dd class="mt-1 text-text">{{ item.createdAtUtc | date: 'medium' }}</dd></div>
          <div class="border-t border-border py-4"><dt class="text-muted">Revision</dt><dd class="mt-1 text-text">{{ item.revision }}</dd></div>
          <div class="border-t border-border pt-4"><dt class="text-muted">Project ID</dt><dd class="mt-1 break-all font-mono text-text">{{ item.id }}</dd></div>
        </dl>
      </section>
      @if (lifecycle(); as flow) {
        <section class="mt-12 border-t border-border pt-8">
          <p class="text-sm font-medium uppercase tracking-wider text-accent">Current stage · {{ flow.stage }}</p>
          <h2 class="mt-2 text-xl text-text">Project lifecycle</h2>
          <div class="mt-6 grid gap-8 lg:grid-cols-2">
            <ol class="divide-y divide-border border-y border-border">
              @for (milestone of flow.milestones; track milestone.code) {
                <li class="flex items-center justify-between gap-4 py-3 text-sm">
                  <span class="text-text">{{ milestone.code }}</span>
                  <span [class]="milestone.satisfied ? 'text-accent' : 'text-muted'">{{ milestone.satisfied ? 'Complete' : milestone.required ? 'Required' : 'Optional' }}</span>
                </li>
              }
            </ol>
            <div>
              <h3 class="font-medium text-text">Next actions</h3>
              @if (flow.nextActions.length === 0) { <p class="mt-3 text-sm text-muted">No next actions were returned.</p> }
              @else {
                <ul class="mt-3 list-disc space-y-2 pl-5 text-sm text-muted">
                  @for (action of flow.nextActions; track action) { <li>{{ action }}</li> }
                </ul>
              }
              @if (staleCount(flow) > 0) { <p class="mt-5 text-sm text-warning">{{ staleCount(flow) }} superseded or stale project records need review.</p> }
            </div>
          </div>
        </section>
      }
    }
  `,
})
export class ProjectDetailPage {
  readonly id = input.required<string>();
  private readonly api = inject(CustomerApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly project = signal<ProjectSummary | null>(null);
  protected readonly lifecycle = signal<ProjectLifecycle | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<AppError | null>(null);

  constructor() { queueMicrotask(() => this.load()); }
  protected load(): void {
    this.loading.set(true); this.error.set(null);
    this.api.getProject(this.id()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (project) => { this.project.set(project); this.loading.set(false); },
      error: (error: AppError) => { this.error.set(error); this.loading.set(false); },
    });
    this.api.getProjectLifecycle(this.id()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (lifecycle) => this.lifecycle.set(lifecycle),
    });
  }

  protected staleCount(lifecycle: ProjectLifecycle): number {
    return Object.values(lifecycle.stale).reduce((total, count) => total + count, 0);
  }
}
