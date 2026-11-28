import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AppError } from '../../../core/api/app-error';
import { applyServerErrors } from '../../../shared/forms/form-errors';
import { createSubmitState } from '../../../shared/forms/submit-state';
import { notBlank } from '../../../shared/forms/validators';
import { Button } from '../../../shared/ui/button/button';
import { FormAlert } from '../../../shared/ui/form-alert/form-alert';
import { FormField } from '../../../shared/ui/form-field/form-field';
import { BmInput } from '../../../shared/ui/form-field/input.directive';
import { ErrorState } from '../../../shared/ui/states/error-state';
import { Skeleton } from '../../../shared/ui/skeleton/skeleton';
import { CustomerApi, ProjectSummary } from '../customer-api';

@Component({
  selector: 'bm-edit-project-page',
  imports: [ReactiveFormsModule, RouterLink, Button, FormAlert, FormField, BmInput, ErrorState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-2xl">
      <a [routerLink]="['/app/projects', id()]" class="text-sm">Back to project</a>
      <h1 class="mt-5 text-3xl text-text">Edit project details</h1>
      <p class="mt-2 text-muted">Update the project context without changing its planning evidence.</p>
      @if (loading()) { <div class="mt-8"><bm-skeleton height="16rem" /></div> }
      @else if (loadError(); as error) { <div class="mt-8"><bm-error-state [error]="error" (retry)="load()" /></div> }
      @else {
        @if (submit$.error(); as error) {
          <bm-form-alert class="mt-6" tone="error" heading="Project not saved" [message]="error.message" [reference]="error.correlationId" />
          @if (error.kind === 'conflict') {
            <button class="mt-3" bmButton variant="secondary" size="sm" type="button" (click)="load()">Reload latest project</button>
          }
        }
        <form [formGroup]="form" (ngSubmit)="submit()" class="mt-8 flex flex-col gap-5" novalidate>
          <bm-form-field label="Project name" [control]="form.controls.name" [required]="true"><input bmInput formControlName="name" /></bm-form-field>
          <bm-form-field label="Project type" [control]="form.controls.projectType" [required]="true">
            <select bmInput formControlName="projectType"><option value="NewBuild">New home</option><option value="Renovation">Renovation</option><option value="Extension">Extension</option></select>
          </bm-form-field>
          <bm-form-field label="Location reference" [control]="form.controls.locationReference"><input bmInput formControlName="locationReference" autocomplete="address-level2" /></bm-form-field>
          <div class="mt-2 flex flex-wrap gap-3">
            <button bmButton type="submit" [loading]="submit$.pending()">Save changes</button>
            <a bmButton variant="secondary" [routerLink]="['/app/projects', id()]">Cancel</a>
          </div>
        </form>
      }
    </div>
  `,
})
export class EditProjectPage {
  readonly id = input.required<string>();
  private readonly api = inject(CustomerApi);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder).nonNullable;
  private revision = 0;
  protected readonly loading = signal(true);
  protected readonly loadError = signal<AppError | null>(null);
  protected readonly submit$ = createSubmitState();
  protected readonly form = this.fb.group({
    name: ['', [Validators.required, notBlank, Validators.maxLength(120)]],
    projectType: ['NewBuild', [Validators.required]],
    locationReference: ['', [Validators.maxLength(200)]],
  });

  constructor() {
    effect(() => { const error = this.submit$.error(); if (error) applyServerErrors(this.form, error); });
    queueMicrotask(() => this.load());
  }

  protected load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.submit$.reset();
    this.api.getProject(this.id()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (project) => { this.applyProject(project); this.loading.set(false); },
      error: (error: AppError) => { this.loadError.set(error); this.loading.set(false); },
    });
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const value = this.form.getRawValue();
    this.submit$.run(
      () => this.api.updateProject(this.id(), {
        name: value.name.trim(), projectType: value.projectType,
        locationReference: value.locationReference.trim() || null,
      }, this.revision),
      (project) => void this.router.navigate(['/app/projects', project.id]),
    );
  }

  private applyProject(project: ProjectSummary): void {
    this.revision = project.revision;
    this.form.setValue({
      name: project.name,
      projectType: project.projectType ?? 'NewBuild',
      locationReference: project.locationReference ?? '',
    });
  }
}
