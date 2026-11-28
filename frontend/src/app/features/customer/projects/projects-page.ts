import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AppError } from '../../../core/api/app-error';
import { Button } from '../../../shared/ui/button/button';
import { EmptyState } from '../../../shared/ui/states/empty-state';
import { ErrorState } from '../../../shared/ui/states/error-state';
import { Skeleton } from '../../../shared/ui/skeleton/skeleton';
import { CustomerApi, ProjectSummary } from '../customer-api';

@Component({
  selector: 'bm-projects-page',
  imports: [DatePipe, FormsModule, RouterLink, Button, EmptyState, ErrorState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="flex flex-wrap items-end justify-between gap-4 border-b border-border pb-6">
      <div>
        <h1 class="text-3xl text-text">Your projects</h1>
        <p class="mt-2 max-w-2xl text-muted">Manage each home-planning project and continue from its current stage.</p>
      </div>
      <a bmButton routerLink="/app/start">Create project</a>
    </header>

    <section class="mt-8" aria-live="polite">
      @if (loading()) {
        <div class="grid gap-3" aria-label="Loading projects">
          @for (item of [1, 2, 3]; track item) { <bm-skeleton height="5rem" /> }
        </div>
      } @else if (error(); as failure) {
        <bm-error-state [error]="failure" (retry)="load()" />
      } @else if (projects().length === 0) {
        <bm-empty-state
          title="Create your first project"
          message="Add the site and project basics before uploading a survey plan."
        >
          <a bmButton routerLink="/app/start">Create project</a>
        </bm-empty-state>
      } @else {
        <div class="mb-5 max-w-md">
          <label for="project-search" class="text-sm font-medium text-text">Search projects</label>
          <input id="project-search" type="search" class="mt-2 h-11 w-full rounded-md border border-border bg-surface px-4 text-text outline-none focus:border-accent focus:ring-2 focus:ring-accent/30" placeholder="Name, location, or status" [ngModel]="query()" (ngModelChange)="query.set($event)" />
        </div>
        @if (filteredProjects().length === 0) {
          <p class="border-y border-border py-6 text-sm text-muted">No projects match “{{ query() }}”.</p>
        } @else {
        <div class="overflow-hidden rounded-lg border border-border bg-surface">
          @for (project of filteredProjects(); track project.id) {
            <a
              [routerLink]="['/app/projects', project.id]"
              class="grid gap-3 border-b border-border px-5 py-4 text-text no-underline last:border-b-0 hover:bg-surface-raised sm:grid-cols-[1fr_auto] sm:items-center"
            >
              <div>
                <h2 class="text-lg font-medium">{{ project.name }}</h2>
                <p class="mt-1 text-sm text-muted">
                  {{ project.projectType || 'Home planning' }}
                  @if (project.locationReference) { · {{ project.locationReference }} }
                </p>
              </div>
              <div class="text-sm sm:text-right">
                <p class="font-medium text-accent">{{ project.status }}</p>
                <p class="mt-1 text-muted">Updated {{ (project.updatedAtUtc || project.createdAtUtc) | date: 'mediumDate' }}</p>
              </div>
            </a>
          }
        </div>
        }
      }
    </section>
  `,
})
export class ProjectsPage {
  private readonly api = inject(CustomerApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly projects = signal<readonly ProjectSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<AppError | null>(null);
  protected readonly query = signal('');
  protected readonly filteredProjects = computed(() => {
    const query = this.query().trim().toLocaleLowerCase();
    if (!query) return this.projects();
    return this.projects().filter((project) =>
      [project.name, project.locationReference ?? '', project.status, project.projectType ?? '']
        .some((value) => value.toLocaleLowerCase().includes(query)),
    );
  });

  constructor() { this.load(); }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.listProjects().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (projects) => { this.projects.set(projects); this.loading.set(false); },
      error: (error: AppError) => { this.error.set(error); this.loading.set(false); },
    });
  }
}
