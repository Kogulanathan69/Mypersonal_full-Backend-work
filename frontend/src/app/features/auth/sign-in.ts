import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { AUTH_API } from '../../core/auth/auth-api';
import { safeReturnUrl } from '../../core/auth/return-url';
import { SessionStore } from '../../core/auth/session.store';
import { applyServerErrors } from '../../shared/forms/form-errors';
import { createSubmitState } from '../../shared/forms/submit-state';
import { Button } from '../../shared/ui/button/button';
import { FormAlert } from '../../shared/ui/form-alert/form-alert';
import { FormField } from '../../shared/ui/form-field/form-field';
import { BmInput } from '../../shared/ui/form-field/input.directive';
import { PasswordInput } from '../../shared/ui/password-input/password-input';
import { authAlert } from './auth-messages';

/** C02 — Sign in. Owner: F — Foundation. */
@Component({
  selector: 'bm-sign-in',
  imports: [ReactiveFormsModule, RouterLink, Button, FormAlert, FormField, BmInput, PasswordInput],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="text-2xl text-text">Welcome back</h1>
    <p class="mt-1 text-sm text-muted">Sign in to continue planning your home.</p>

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
          autocomplete="username"
          inputmode="email"
        />
      </bm-form-field>
      <bm-form-field label="Password" [control]="form.controls.password" [required]="true">
        <bm-password-input formControlName="password" autocomplete="current-password" />
      </bm-form-field>
      <div class="-mt-2 text-right text-sm">
        <a routerLink="/auth/forgot-password">Forgot password?</a>
      </div>
      <button bmButton type="submit" [block]="true" [loading]="submit$.pending()">Sign in</button>
    </form>

    <p class="mt-6 text-center text-sm text-muted">
      New to BuildMind? <a routerLink="/auth/register">Create an account</a>
    </p>
  `,
})
export class SignIn {
  private readonly api = inject(AUTH_API);
  private readonly session = inject(SessionStore);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder).nonNullable;

  private readonly returnUrl = toSignal(
    inject(ActivatedRoute).queryParamMap.pipe(
      map((q) => safeReturnUrl(q.get('returnUrl'), '/app/dashboard')),
    ),
    { initialValue: '/app/dashboard' },
  );

  protected readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });
  protected readonly submit$ = createSubmitState();
  protected readonly alert = computed(() => {
    const e = this.submit$.error();
    return e ? authAlert(e, 'sign-in') : null;
  });

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.submit$.run(
      () => this.api.signIn(this.form.getRawValue()),
      (user) => {
        this.session.set(user);
        void this.router.navigateByUrl(this.returnUrl());
      },
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
