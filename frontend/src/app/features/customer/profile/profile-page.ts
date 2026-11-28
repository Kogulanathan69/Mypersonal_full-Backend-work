import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AppError } from '../../../core/api/app-error';
import { SessionStore } from '../../../core/auth/session.store';
import { applyServerErrors } from '../../../shared/forms/form-errors';
import { createSubmitState } from '../../../shared/forms/submit-state';
import { fieldsMatch, notBlank, passwordPolicy, sriLankaPhone } from '../../../shared/forms/validators';
import { Button } from '../../../shared/ui/button/button';
import { FormAlert } from '../../../shared/ui/form-alert/form-alert';
import { FormField } from '../../../shared/ui/form-field/form-field';
import { BmInput } from '../../../shared/ui/form-field/input.directive';
import { PasswordInput } from '../../../shared/ui/password-input/password-input';
import { ErrorState } from '../../../shared/ui/states/error-state';
import { AccountSession, CustomerApi, CustomerProfile, SecurityEvent, SecurityStatus } from '../customer-api';

@Component({
  selector: 'bm-profile-page',
  imports: [DatePipe, ReactiveFormsModule, Button, FormAlert, FormField, BmInput, PasswordInput, ErrorState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="border-b border-border pb-6">
      <h1 class="text-3xl text-text">Profile and security</h1>
      <p class="mt-2 text-muted">Manage the contact details and password attached to your BuildMind account.</p>
    </header>
    @if (loadError(); as failure) {
      <div class="mt-8"><bm-error-state [error]="failure" (retry)="load()" /></div>
    } @else {
      <div class="mt-8 grid gap-12 lg:grid-cols-2">
        <section>
          <h2 class="text-xl text-text">Contact details</h2>
          @if (profileDone()) { <bm-form-alert class="mt-5" tone="success" message="Profile details saved." /> }
          @if (profileSubmit.error(); as error) { <bm-form-alert class="mt-5" tone="error" heading="Profile not saved" [message]="error.message" /> }
          <form [formGroup]="profileForm" (ngSubmit)="saveProfile()" class="mt-6 flex flex-col gap-5" novalidate>
            <bm-form-field label="Email"><input bmInput [value]="profile()?.email ?? ''" disabled /></bm-form-field>
            <bm-form-field label="Full name" [control]="profileForm.controls.fullName" [required]="true"><input bmInput formControlName="fullName" autocomplete="name" /></bm-form-field>
            <bm-form-field label="Mobile number" [control]="profileForm.controls.phoneNumber" hint="Sri Lankan number, if you want to provide one."><input bmInput formControlName="phoneNumber" autocomplete="tel" inputmode="tel" /></bm-form-field>
            <button bmButton type="submit" [loading]="profileSubmit.pending()">Save profile</button>
          </form>
        </section>
        <section class="lg:border-l lg:border-border lg:pl-10">
          <h2 class="text-xl text-text">Account security</h2>
          @if (security(); as state) {
            <p class="mt-3 text-sm text-muted">Email status: <span class="text-text">{{ state.emailVerified ? 'Verified' : state.verificationPending ? 'Verification pending' : 'Not verified' }}</span></p>
          }
          @if (passwordDone()) { <bm-form-alert class="mt-5" tone="success" message="Password changed. Sign in again on other devices when asked." /> }
          @if (passwordSubmit.error(); as error) { <bm-form-alert class="mt-5" tone="error" heading="Password not changed" [message]="error.message" /> }
          <form [formGroup]="passwordForm" (ngSubmit)="changePassword()" class="mt-6 flex flex-col gap-5" novalidate>
            <bm-form-field label="Current password" [control]="passwordForm.controls.currentPassword" [required]="true"><bm-password-input formControlName="currentPassword" autocomplete="current-password" /></bm-form-field>
            <bm-form-field label="New password" [control]="passwordForm.controls.newPassword" [required]="true" hint="At least 8 characters with a letter and a number."><bm-password-input formControlName="newPassword" autocomplete="new-password" [showStrength]="true" /></bm-form-field>
            <bm-form-field label="Confirm new password" [control]="passwordForm.controls.confirmPassword" [required]="true"><bm-password-input formControlName="confirmPassword" autocomplete="new-password" /></bm-form-field>
            <button bmButton type="submit" [loading]="passwordSubmit.pending()">Change password</button>
          </form>
        </section>
      </div>
      <div class="mt-12 grid gap-10 border-t border-border pt-10 lg:grid-cols-2">
        <section>
          <h2 class="text-xl text-text">Active sessions</h2>
          <p class="mt-2 text-sm text-muted">Review sessions issued for this account. Revoking a session signs that device out when it next contacts BuildMind.</p>
          @if (sessionsError(); as error) { <bm-form-alert class="mt-5" tone="error" heading="Sessions unavailable" [message]="error.message" /> }
          @else if (sessions().length === 0) { <p class="mt-5 text-sm text-muted">No session records are available.</p> }
          @else {
            <div class="mt-5 divide-y divide-border border-y border-border">
              @for (item of sessions(); track item.sessionId ?? $index) {
                <article class="flex flex-wrap items-center justify-between gap-4 py-4">
                  <div class="text-sm">
                    <p class="font-medium text-text">{{ item.active === false ? 'Inactive session' : 'Active session' }}</p>
                    <p class="mt-1 text-muted">Started {{ item.startedAtUtc ? (item.startedAtUtc | date: 'medium') : 'at an unavailable time' }}</p>
                  </div>
                  @if (item.sessionId && item.active !== false) {
                    <button bmButton variant="danger" size="sm" type="button" [loading]="revokingSession() === item.sessionId" (click)="revokeSession(item.sessionId)">Revoke</button>
                  }
                </article>
              }
            </div>
          }
        </section>
        <section class="lg:border-l lg:border-border lg:pl-10">
          <h2 class="text-xl text-text">Recent security activity</h2>
          <p class="mt-2 text-sm text-muted">The latest account-security events recorded by BuildMind.</p>
          @if (eventsError(); as error) { <bm-form-alert class="mt-5" tone="error" heading="Security activity unavailable" [message]="error.message" /> }
          @else if (events().length === 0) { <p class="mt-5 text-sm text-muted">No security events are available.</p> }
          @else {
            <ol class="mt-5 divide-y divide-border border-y border-border">
              @for (event of events(); track event.eventId) {
                <li class="py-4 text-sm">
                  <p class="font-medium text-text">{{ event.kind }}</p>
                  <time class="mt-1 block text-muted" [attr.datetime]="event.occurredAtUtc">{{ event.occurredAtUtc | date: 'medium' }}</time>
                </li>
              }
            </ol>
          }
        </section>
      </div>
    }
  `,
})
export class ProfilePage {
  private readonly api = inject(CustomerApi);
  private readonly session = inject(SessionStore);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder).nonNullable;
  protected readonly profile = signal<CustomerProfile | null>(null);
  protected readonly security = signal<SecurityStatus | null>(null);
  protected readonly sessions = signal<readonly AccountSession[]>([]);
  protected readonly events = signal<readonly SecurityEvent[]>([]);
  protected readonly sessionsError = signal<AppError | null>(null);
  protected readonly eventsError = signal<AppError | null>(null);
  protected readonly revokingSession = signal<string | null>(null);
  protected readonly loadError = signal<AppError | null>(null);
  protected readonly profileDone = signal(false);
  protected readonly passwordDone = signal(false);
  protected readonly profileSubmit = createSubmitState();
  protected readonly passwordSubmit = createSubmitState();
  protected readonly profileForm = this.fb.group({
    fullName: ['', [Validators.required, notBlank, Validators.maxLength(100)]],
    phoneNumber: ['', [sriLankaPhone]],
  });
  protected readonly passwordForm = this.fb.group(
    {
      currentPassword: ['', [Validators.required]],
      newPassword: ['', [Validators.required, passwordPolicy()]],
      confirmPassword: ['', [Validators.required]],
    },
    { validators: [fieldsMatch('newPassword', 'confirmPassword')] },
  );

  constructor() {
    effect(() => { const error = this.profileSubmit.error(); if (error) applyServerErrors(this.profileForm, error); });
    effect(() => { const error = this.passwordSubmit.error(); if (error) applyServerErrors(this.passwordForm, error); });
    this.load();
  }

  protected load(): void {
    this.loadError.set(null);
    this.api.getProfile().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.profileForm.setValue({
          fullName: typeof profile.fullName === 'string' ? profile.fullName : '',
          phoneNumber: typeof profile.phoneNumber === 'string' ? profile.phoneNumber : '',
        });
      },
      error: (error: AppError) => this.loadError.set(error),
    });
    this.api.getSecurity().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (value) => this.security.set(value) });
    this.loadSessions();
    this.api.listSecurityEvents().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (events) => this.events.set(events),
      error: (error: AppError) => this.eventsError.set(error),
    });
  }

  private loadSessions(): void {
    this.sessionsError.set(null);
    this.api.listSessions().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (sessions) => this.sessions.set(sessions),
      error: (error: AppError) => this.sessionsError.set(error),
    });
  }

  protected revokeSession(sessionId: string): void {
    if (this.revokingSession()) return;
    this.revokingSession.set(sessionId);
    this.api.revokeSession(sessionId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.revokingSession.set(null); this.loadSessions(); },
      error: (error: AppError) => { this.revokingSession.set(null); this.sessionsError.set(error); },
    });
  }

  protected saveProfile(): void {
    this.profileForm.markAllAsTouched();
    if (this.profileForm.invalid) return;
    this.profileDone.set(false);
    const value = this.profileForm.getRawValue();
    this.profileSubmit.run(
      () => this.api.updateProfile(value.fullName.trim(), value.phoneNumber.trim() || null),
      (profile) => {
        this.profile.set(profile);
        const current = this.session.user();
        if (current) this.session.set({ ...current, displayName: profile.fullName as string });
        this.profileDone.set(true);
      },
    );
  }

  protected changePassword(): void {
    this.passwordForm.markAllAsTouched();
    if (this.passwordForm.invalid) return;
    this.passwordDone.set(false);
    const value = this.passwordForm.getRawValue();
    this.passwordSubmit.run(
      () => this.api.changePassword(value.currentPassword, value.newPassword),
      () => { this.passwordForm.reset(); this.passwordDone.set(true); },
    );
  }
}
