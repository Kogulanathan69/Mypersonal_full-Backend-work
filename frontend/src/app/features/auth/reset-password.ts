import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AUTH_API } from '../../core/auth/auth-api';
import { createSubmitState } from '../../shared/forms/submit-state';
import { fieldsMatch, passwordPolicy } from '../../shared/forms/validators';
import { Button } from '../../shared/ui/button/button';
import { FieldError } from '../../shared/ui/form-field/field-error';
import { FormAlert } from '../../shared/ui/form-alert/form-alert';
import { FormField } from '../../shared/ui/form-field/form-field';
import { PasswordInput } from '../../shared/ui/password-input/password-input';
import { authAlert, isInvalidTokenError } from './auth-messages';
import { readLinkToken } from './link-token';

/** C05 — Reset password from an emailed link. The token is kept in memory and removed from the URL. */
@Component({
  selector: 'bm-reset-password',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    Button,
    FieldError,
    FormAlert,
    FormField,
    PasswordInput,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!token() || !linkEmail || invalid()) {
      <h1 class="text-2xl text-text">This reset link has expired or is invalid</h1>
      <p class="mt-2 text-sm text-muted">
        Reset links work once and expire after a short time. Request a new one to continue.
      </p>
      <a bmButton class="mt-6" [block]="true" routerLink="/auth/forgot-password"
        >Request a new link</a
      >
    } @else if (done()) {
      <h1 class="text-2xl text-text">Password updated</h1>
      <p class="mt-2 text-sm text-muted">You can now sign in with your new password.</p>
      <a bmButton class="mt-6" [block]="true" routerLink="/auth/sign-in">Sign in</a>
    } @else {
      <h1 class="text-2xl text-text">Choose a new password</h1>
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
        <bm-form-field
          label="New password"
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
            label="Confirm new password"
            [control]="form.controls.confirm"
            [required]="true"
          >
            <bm-password-input formControlName="confirm" autocomplete="new-password" />
          </bm-form-field>
          <bm-field-error [control]="form" label="Passwords" errorId="reset-match-error" />
        </div>
        <button bmButton type="submit" [block]="true" [loading]="submit$.pending()">
          Update password
        </button>
      </form>
    }
  `,
})
export class ResetPassword {
  private readonly api = inject(AUTH_API);
  private readonly link = readLinkToken();
  protected readonly token = this.link.token;
  protected readonly linkEmail = this.link.email;
  protected readonly form = inject(FormBuilder).nonNullable.group(
    {
      password: ['', [Validators.required, passwordPolicy()]],
      confirm: ['', [Validators.required]],
    },
    { validators: [fieldsMatch('password', 'confirm')] },
  );
  protected readonly submit$ = createSubmitState();
  protected readonly done = signal(false);
  protected readonly invalid = computed(() => {
    const e = this.submit$.error();
    return !!e && e.kind !== 'config' && isInvalidTokenError(e) && e.kind !== 'validation';
  });
  protected readonly alert = computed(() => {
    const e = this.submit$.error();
    return e && !this.invalid() ? authAlert(e, 'reset') : null;
  });

  protected submit(): void {
    this.form.markAllAsTouched();
    const token = this.token();
    if (this.form.invalid || !token || !this.link.email) return;
    this.submit$.run(
      () =>
        this.api.resetPassword({
          email: this.link.email!,
          token,
          password: this.form.controls.password.value,
        }),
      () => this.done.set(true),
    );
  }
}
