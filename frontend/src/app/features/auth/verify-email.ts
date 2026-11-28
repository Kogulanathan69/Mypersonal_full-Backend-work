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
import { createSubmitState } from '../../shared/forms/submit-state';
import { Button } from '../../shared/ui/button/button';
import { FormAlert } from '../../shared/ui/form-alert/form-alert';
import { FormField } from '../../shared/ui/form-field/form-field';
import { BmInput } from '../../shared/ui/form-field/input.directive';
import { Spinner } from '../../shared/ui/spinner/spinner';
import { authAlert, isInvalidTokenError } from './auth-messages';
import { readLinkToken } from './link-token';

/** Email verification (C03 follow-up): verifies a link token, or lets the user resend a link. */
@Component({
  selector: 'bm-verify-email',
  imports: [ReactiveFormsModule, RouterLink, Button, FormAlert, FormField, BmInput, Spinner],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @switch (state()) {
      @case ('verifying') {
        <h1 class="text-2xl text-text">Verifying your email</h1>
        <p class="mt-4 flex items-center gap-3 text-sm text-muted" role="status">
          <bm-spinner [size]="18" /> Checking your link…
        </p>
      }
      @case ('verified') {
        <h1 class="text-2xl text-text">Email verified</h1>
        <p class="mt-2 text-sm text-muted">Your account is ready. You can sign in now.</p>
        <a bmButton class="mt-6" [block]="true" routerLink="/auth/sign-in">Sign in</a>
      }
      @case ('invalid') {
        <h1 class="text-2xl text-text">This link has expired or is invalid</h1>
        <p class="mt-2 text-sm text-muted">
          Verification links work once and expire after a while. Request a new one below.
        </p>
      }
      @default {
        <h1 class="text-2xl text-text">Verify your email</h1>
        <p class="mt-2 text-sm text-muted">
          Open the link we emailed you to activate your account.
        </p>
      }
    }

    @if (verifyAlert(); as a) {
      <bm-form-alert
        class="mt-5"
        [tone]="a.tone"
        [heading]="a.heading"
        [message]="a.message"
        [reference]="a.reference"
      />
    }

    @if (state() !== 'verified' && state() !== 'verifying') {
      <form [formGroup]="form" (ngSubmit)="resend()" novalidate class="mt-6 flex flex-col gap-4">
        <bm-form-field label="Email" [control]="form.controls.email" [required]="true">
          <input bmInput type="email" formControlName="email" autocomplete="email" />
        </bm-form-field>
        @if (resent()) {
          <bm-form-alert
            tone="success"
            message="If an account needs verifying for this email, a new link is on its way."
          />
        }
        @if (resendAlert(); as a) {
          <bm-form-alert
            [tone]="a.tone"
            [heading]="a.heading"
            [message]="a.message"
            [reference]="a.reference"
          />
        }
        <button
          bmButton
          variant="secondary"
          type="submit"
          [block]="true"
          [loading]="resend$.pending()"
        >
          Send a new link
        </button>
      </form>
      <p class="mt-6 text-center text-sm"><a routerLink="/auth/sign-in">Back to sign in</a></p>
    }
  `,
})
export class VerifyEmail {
  private readonly api = inject(AUTH_API);
  private readonly link = readLinkToken();
  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: [this.link.email ?? '', [Validators.required, Validators.email]],
  });
  protected readonly state = signal<'idle' | 'verifying' | 'verified' | 'invalid' | 'failed'>(
    'idle',
  );
  private readonly verify$ = createSubmitState();
  protected readonly resend$ = createSubmitState();
  protected readonly resent = signal(false);
  protected readonly verifyAlert = computed(() => {
    const e = this.verify$.error();
    return e && !isInvalidTokenError(e) ? authAlert(e, 'verify') : null;
  });
  protected readonly resendAlert = computed(() => {
    const e = this.resend$.error();
    return e ? authAlert(e, 'verify') : null;
  });

  constructor() {
    const token = this.link.token();
    if (!token) return;
    this.state.set('verifying');
    effect(() => {
      const e = this.verify$.error();
      if (e) this.state.set(isInvalidTokenError(e) ? 'invalid' : 'failed');
    });
    this.verify$.run(
      () => this.api.verifyEmail(token),
      () => this.state.set('verified'),
    );
  }

  protected resend(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.resent.set(false);
    this.resend$.run(
      () => this.api.resendVerification(this.form.controls.email.value.trim()),
      () => this.resent.set(true),
    );
  }
}
