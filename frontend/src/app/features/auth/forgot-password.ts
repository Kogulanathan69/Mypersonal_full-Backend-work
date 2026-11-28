import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AUTH_API } from '../../core/auth/auth-api';
import { createSubmitState } from '../../shared/forms/submit-state';
import { Button } from '../../shared/ui/button/button';
import { FormAlert } from '../../shared/ui/form-alert/form-alert';
import { FormField } from '../../shared/ui/form-field/form-field';
import { BmInput } from '../../shared/ui/form-field/input.directive';
import { authAlert } from './auth-messages';

/**
 * C04 — Forgot password. The confirmation is deliberately neutral: it never reveals whether an
 * account exists for the email (prevents account enumeration).
 */
@Component({
  selector: 'bm-forgot-password',
  imports: [ReactiveFormsModule, RouterLink, Button, FormAlert, FormField, BmInput],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="text-2xl text-text">Reset your password</h1>
    <p class="mt-1 text-sm text-muted">Enter your account email and we'll send a reset link.</p>
    @if (sent()) {
      <bm-form-alert
        class="mt-5"
        tone="success"
        heading="Check your email"
        message="If an account exists for that email, we've sent a link to reset the password. The link expires after a short time."
      />
    }
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
      <button bmButton type="submit" [block]="true" [loading]="submit$.pending()">
        {{ sent() ? 'Send again' : 'Send reset link' }}
      </button>
    </form>
    <p class="mt-6 text-center text-sm"><a routerLink="/auth/sign-in">Back to sign in</a></p>
  `,
})
export class ForgotPassword {
  private readonly api = inject(AUTH_API);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });
  protected readonly submit$ = createSubmitState();
  protected readonly sent = signal(false);
  protected readonly alert = computed(() => {
    const e = this.submit$.error();
    return e ? authAlert(e, 'forgot') : null;
  });

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.submit$.run(
      () => this.api.forgotPassword(this.form.controls.email.value.trim()),
      () => this.sent.set(true),
    );
  }
}
