import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { notBlank } from '../../../shared/forms/validators';
import { createSubmitState } from '../../../shared/forms/submit-state';
import { applyServerErrors } from '../../../shared/forms/form-errors';
import { Button } from '../../../shared/ui/button/button';
import { FormAlert } from '../../../shared/ui/form-alert/form-alert';
import { FormField } from '../../../shared/ui/form-field/form-field';
import { BmInput } from '../../../shared/ui/form-field/input.directive';
import { CustomerApi } from '../customer-api';

@Component({
  selector: 'bm-create-project-page',
  imports: [ReactiveFormsModule, RouterLink, Button, FormAlert, FormField, BmInput],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-2xl">
      <a routerLink="/app/projects" class="text-sm">Back to projects</a>
      <h1 class="mt-5 text-3xl text-text">Create a project</h1>
      <p class="mt-2 text-muted">Start with the property name and planning context. You can add survey evidence next.</p>

      @if (submit$.error(); as error) {
        <bm-form-alert class="mt-6" tone="error" heading="Project not created" [message]="error.message" [reference]="error.correlationId" />
      }

      <form [formGroup]="form" (ngSubmit)="submit()" class="mt-8 flex flex-col gap-5" novalidate>
        <bm-form-field label="Project name" [control]="form.controls.name" [required]="true" hint="For example, Kandy family home.">
          <input bmInput formControlName="name" autocomplete="off" />
        </bm-form-field>
        <bm-form-field label="Project type" [control]="form.controls.projectType" [required]="true">
          <select bmInput formControlName="projectType">
            <option value="NewBuild">New home</option>
            <option value="Renovation">Renovation</option>
            <option value="Extension">Extension</option>
          </select>
        </bm-form-field>
        <bm-form-field label="Location reference" [control]="form.controls.locationReference" hint="Town, district, or another reference you use for this site.">
          <input bmInput formControlName="locationReference" autocomplete="address-level2" />
        </bm-form-field>
        <div class="mt-2 flex flex-wrap gap-3">
          <button bmButton type="submit" [loading]="submit$.pending()">Create project</button>
          <a bmButton variant="secondary" routerLink="/app/projects">Cancel</a>
        </div>
      </form>
    </div>
  `,
})
export class CreateProjectPage {
  private readonly api = inject(CustomerApi);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly submit$ = createSubmitState();
  protected readonly form = this.fb.group({
    name: ['', [Validators.required, notBlank, Validators.maxLength(120)]],
    projectType: ['NewBuild', [Validators.required]],
    locationReference: ['', [Validators.maxLength(200)]],
  });

  constructor() {
    effect(() => { const error = this.submit$.error(); if (error) applyServerErrors(this.form, error); });
  }

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const value = this.form.getRawValue();
    this.submit$.run(
      () => this.api.createProject({
        name: value.name.trim(),
        projectType: value.projectType,
        locationReference: value.locationReference.trim() || null,
      }),
      (project) => void this.router.navigate(['/app/projects', project.id]),
    );
  }
}
