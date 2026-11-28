import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AUTH_API } from '../../core/auth/auth-api';
import { applyServerErrors } from '../../shared/forms/form-errors';
import { createSubmitState } from '../../shared/forms/submit-state';
import { fieldsMatch, passwordPolicy } from '../../shared/forms/validators';
import { Button } from '../../shared/ui/button/button';
import { FieldError } from '../../shared/ui/form-field/field-error';
import { FormAlert } from '../../shared/ui/form-alert/form-alert';
import { FormField } from '../../shared/ui/form-field/form-field';
import { BmInput } from '../../shared/ui/form-field/input.directive';
import { PasswordInput } from '../../shared/ui/password-input/password-input';
import { authAlert } from './auth-messages';

/**
 * C03 — Customer registration. Owner: F. Only customers can self-register; admin, staff and
 * architect accounts follow backend-defined onboarding. Collects only what planning needs.
 */
@Component({
  selector: 'bm-register',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    Button,
    FieldError,
    FormAlert,
    FormField,
    BmInput,
    PasswordInput,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (done()) {
      <h1 class="text-2xl text-text">Account created</h1>
      <p class="mt-2 text-sm text-muted">
        Your BuildMind account is ready. Sign in with
        <span class="text-text">{{ form.controls.email.value }}</span> to continue.
      </p>
      <a bmButton class="mt-6" [block]="true" routerLink="/auth/sign-in">Sign in</a>
    } @else {
      <h1 class="text-2xl text-text">Create your account</h1>
      <p class="mt-1 text-sm text-muted">Start planning your home on your own land.</p>

      @if (alert(); as a) {
        <bm-form-alert
          class="mt-5"
          [tone]="a.tone"
          [heading]="a.heading"
          [message]="a.message"
          [reference]="a.reference"
        />
      }

      <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="mt-6 flex flex-col gap-5">
        <bm-form-field label="Email" [control]="form.controls.email" [required]="true">
          <input
            bmInput
            type="email"
            formControlName="email"
            autocomplete="email"
            inputmode="email"
          />
        </bm-form-field>
        <bm-form-field
          label="Password"
          [control]="form.controls.password"
          [required]="true"
          hint="At least 8 characters with a letter and a number."
        >
          <bm-password-input
            formControlName="password"
            autocomplete="new-password"
            [showStrength]="true"
          />
        </bm-form-field>
        <div>
          <bm-form-field
            label="Confirm password"
            [control]="form.controls.confirm"
            [required]="true"
          >
            <bm-password-input formControlName="confirm" autocomplete="new-password" />
          </bm-form-field>
          <bm-field-error [control]="form" label="Passwords" errorId="register-match-error" />
        </div>
        <div>
          <label class="flex items-start gap-3 text-sm text-muted">
            <input
              type="checkbox"
              formControlName="acceptedTerms"
              class="mt-1 size-4 accent-accent"
              aria-describedby="register-terms-error"
            />
            <span
              >I agree to the <a routerLink="/terms" target="_blank">Terms</a> and have read the
              <a routerLink="/privacy" target="_blank">Privacy information</a>.</span
            >
          </label>
          @if (form.controls.acceptedTerms.touched && form.controls.acceptedTerms.invalid) {
            <p id="register-terms-error" class="mt-1.5 text-sm text-danger">
              You need to accept the terms to create an account.
            </p>
          }
        </div>
        <button bmButton type="submit" [block]="true" [loading]="submit$.pending()">
          Create account
        </button>
      </form>
      <p class="mt-6 text-center text-sm text-muted">
        Already have an account? <a routerLink="/auth/sign-in">Sign in</a>
      </p>
    }
  `,
})
export class Register {
  private readonly api = inject(AUTH_API);
  private readonly fb = inject(FormBuilder).nonNullable;

  protected readonly form = this.fb.group(
    {
      email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
      password: ['', [Validators.required, passwordPolicy()]],
      confirm: ['', [Validators.required]],
      acceptedTerms: [false, [Validators.requiredTrue]],
    },
    { validators: [fieldsMatch('password', 'confirm')] },
  );
  protected readonly submit$ = createSubmitState();
  protected readonly done = signal(false);
  protected readonly alert = computed(() => {
    const e = this.submit$.error();
    return e ? authAlert(e, 'register') : null;
  });

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    const v = this.form.getRawValue();
    this.submit$.run(
      () =>
        this.api.register({
          fullName: '',
          email: v.email.trim(),
          phone: null,
          password: v.password,
          acceptedTerms: v.acceptedTerms,
        }),
      () => this.done.set(true),
    );
  }

  constructor() {
    // Server field errors (when the contract is live) are shown on the matching inputs.
    effect(() => {
      const e = this.submit$.error();
      if (e) applyServerErrors(this.form, e);
    });
  }
}
