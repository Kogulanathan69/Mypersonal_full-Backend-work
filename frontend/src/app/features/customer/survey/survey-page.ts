import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { AppError } from '../../../core/api/app-error';
import { Button } from '../../../shared/ui/button/button';
import { FileDrop } from '../../../shared/ui/file-drop/file-drop';
import { FormAlert } from '../../../shared/ui/form-alert/form-alert';
import { ErrorState } from '../../../shared/ui/states/error-state';
import { Skeleton } from '../../../shared/ui/skeleton/skeleton';
import { CustomerApi, SurveySummary } from '../customer-api';

@Component({
  selector: 'bm-survey-page',
  imports: [DatePipe, RouterLink, Button, FileDrop, FormAlert, ErrorState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a [routerLink]="['/app/projects', id()]" class="text-sm">Back to project</a>
    <header class="mt-5 border-b border-border pb-6">
      <h1 class="text-3xl text-text">Survey plan</h1>
      <p class="mt-2 max-w-2xl text-muted">Upload a survey PDF or image for secure extraction. BuildMind keeps the source evidence attached to this project.</p>
    </header>
    <div class="mt-8 grid gap-10 lg:grid-cols-[minmax(0,1fr)_22rem]">
      <section>
        <h2 class="text-xl text-text">Uploaded surveys</h2>
        @if (loading()) { <div class="mt-5"><bm-skeleton height="9rem" /></div> }
        @else if (loadError(); as error) { <div class="mt-5"><bm-error-state [error]="error" (retry)="load()" /></div> }
        @else if (surveys().length === 0) { <p class="mt-5 border-y border-border py-6 text-sm text-muted">No survey plan has been uploaded for this project.</p> }
        @else {
          <div class="mt-5 divide-y divide-border border-y border-border">
            @for (survey of surveys(); track survey.surveyId ?? $index) {
              <article class="flex flex-wrap items-center justify-between gap-4 py-4">
                <div>
                  <p class="font-medium text-text">{{ survey.originalFileName || 'Survey plan' }}</p>
                  <p class="mt-1 text-sm text-muted">Uploaded {{ survey.uploadedAtUtc ? (survey.uploadedAtUtc | date: 'medium') : 'recently' }}</p>
                </div>
                <div class="text-right"><p class="text-sm font-medium text-accent">{{ survey.status || 'Submitted' }}</p>
                  @if (survey.surveyId) { <a class="mt-1 inline-block text-sm" [routerLink]="['/app/projects', id(), 'survey', survey.surveyId]">Review evidence</a> }
                </div>
              </article>
            }
          </div>
        }
      </section>
      <aside class="border-t border-border pt-6 lg:border-l lg:border-t-0 lg:pl-8 lg:pt-0">
        <h2 class="text-xl text-text">Add survey</h2>
        <p class="mt-2 text-sm text-muted">One PDF, JPEG, or PNG up to 6 MB. The server validates the file again before processing.</p>
        @if (uploadError(); as error) { <bm-form-alert class="mt-5" tone="error" heading="Upload failed" [message]="error.message" [reference]="error.correlationId" /> }
        @if (uploadDone()) { <bm-form-alert class="mt-5" tone="success" message="Survey accepted for processing." /> }
        <bm-file-drop class="mt-5" [accept]="['.pdf', 'image/jpeg', 'image/png']" [maxBytes]="6291456" [disabled]="uploading()" buttonLabel="Choose survey file" (filesAccepted)="selectFile($event)" />
        @if (selectedFile(); as file) {
          <p class="mt-4 break-all text-sm text-text">Selected: {{ file.name }}</p>
          <button class="mt-4" bmButton type="button" [loading]="uploading()" (click)="upload()">Upload survey</button>
        }
      </aside>
    </div>
  `,
})
export class SurveyPage {
  readonly id = input.required<string>();
  private readonly api = inject(CustomerApi);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly surveys = signal<readonly SurveySummary[]>([]);
  protected readonly selectedFile = signal<File | null>(null);
  protected readonly loading = signal(true);
  protected readonly uploading = signal(false);
  protected readonly uploadDone = signal(false);
  protected readonly loadError = signal<AppError | null>(null);
  protected readonly uploadError = signal<AppError | null>(null);

  constructor() { queueMicrotask(() => this.load()); }

  protected load(): void {
    this.loading.set(true); this.loadError.set(null);
    this.api.listSurveys(this.id()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (surveys) => { this.surveys.set(surveys); this.loading.set(false); },
      error: (error: AppError) => { this.loadError.set(error); this.loading.set(false); },
    });
  }

  protected selectFile(files: File[]): void {
    this.selectedFile.set(files[0] ?? null); this.uploadError.set(null); this.uploadDone.set(false);
  }

  protected upload(): void {
    const file = this.selectedFile();
    if (!file || this.uploading()) return;
    this.uploading.set(true); this.uploadError.set(null); this.uploadDone.set(false);
    this.api.uploadSurvey(this.id(), file).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.uploading.set(false); this.uploadDone.set(true); this.selectedFile.set(null); this.load(); },
      error: (error: AppError) => { this.uploading.set(false); this.uploadError.set(error); },
    });
  }
}
