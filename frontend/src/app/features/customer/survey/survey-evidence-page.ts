import { ChangeDetectionStrategy, Component, DestroyRef, OnDestroy, inject, input, signal } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { AppError } from '../../../core/api/app-error';
import { ErrorState } from '../../../shared/ui/states/error-state';
import { Skeleton } from '../../../shared/ui/skeleton/skeleton';
import { Button } from '../../../shared/ui/button/button';
import { FormAlert } from '../../../shared/ui/form-alert/form-alert';
import { CustomerApi, SurveyExtraction, SurveyReviewDecision } from '../customer-api';

@Component({
  selector: 'bm-survey-evidence-page',
  imports: [FormsModule, RouterLink, Button, FormAlert, ErrorState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a [routerLink]="['/app/projects', id(), 'survey']" class="text-sm">Back to survey plans</a>
    <header class="mt-5 border-b border-border pb-6">
      <h1 class="text-3xl text-text">Survey extraction evidence</h1>
      <p class="mt-2 max-w-3xl text-muted">Inspect only values returned from the uploaded document. Source text and page references remain visible for review.</p>
    </header>
    @if (loading()) { <div class="mt-8"><bm-skeleton height="18rem" /></div> }
    @else if (error(); as failure) { <div class="mt-8"><bm-error-state [error]="failure" (retry)="load()" /></div> }
    @else if (result(); as data) {
      <section class="mt-8">
        <div class="flex flex-wrap items-center justify-between gap-4 border-y border-border py-4 text-sm">
          <span class="text-muted">Processing status</span><strong class="font-medium text-accent">{{ data.processingStatus }}</strong>
        </div>
        @if (!data.extraction) {
          <div class="mt-8 border border-border bg-surface p-6">
            <h2 class="text-xl text-text">Extraction evidence unavailable</h2>
            <p class="mt-3 max-w-2xl text-muted">{{ data.notice }}</p>
            @if (data.failureCode) { <p class="mt-3 text-sm text-muted">Failure code: {{ data.failureCode }}</p> }
          </div>
        } @else {
          <div class="mt-8 grid gap-10 xl:grid-cols-[minmax(22rem,0.8fr)_minmax(0,1.2fr)]">
            <section>
              <div class="flex flex-wrap items-center justify-between gap-3"><h2 class="text-xl text-text">Source document</h2>
                <div class="flex gap-2"><button type="button" class="text-sm" (click)="zoomOut()">Zoom out</button><button type="button" class="text-sm" (click)="zoomIn()">Zoom in</button></div>
              </div>
              @if (fileError(); as message) { <p class="mt-5 border border-border bg-surface p-4 text-sm text-muted">{{ message }}</p> }
              @else if (fileUrl(); as url) {
                <div class="mt-5 max-h-[44rem] overflow-auto border border-border bg-surface p-3">
                  @if (fileType() === 'application/pdf') { <iframe title="Uploaded survey document" class="h-[40rem] w-full" [src]="safeFileUrl()"></iframe> }
                  @else { <img alt="Uploaded survey document" class="mx-auto max-w-none origin-top transition-transform" [style.width.%]="zoom()" [src]="url" /> }
                </div>
              } @else { <div class="mt-5"><bm-skeleton height="24rem" /></div> }
            </section>
            <section>
              <h2 class="text-xl text-text">Extracted measurements</h2>
              @if (data.extraction.evidence.measurements.length === 0) { <p class="mt-5 text-sm text-muted">No measurements were extracted.</p> }
              @else {
                <div class="mt-5 overflow-x-auto border-y border-border">
                  <table class="w-full text-left text-sm">
                    <thead class="text-muted"><tr><th class="py-3 pr-4 font-medium">Field</th><th class="py-3 pr-4 font-medium">Value</th><th class="py-3 pr-4 font-medium">Review</th><th class="py-3 font-medium">Source</th></tr></thead>
                    <tbody class="divide-y divide-border">
                      @for (measurement of data.extraction.evidence.measurements; track $index) {
                        <tr><td class="py-4 pr-4 text-text">{{ measurement.measurement_name }}</td><td class="py-4 pr-4 tabular-nums text-text">{{ measurement.value }} {{ measurement.unit }}</td><td class="py-4 pr-4 text-muted">{{ measurement.review_status }}</td><td class="py-4 text-muted">Page {{ measurement.source.page_number }} · {{ measurement.source.reference_text }}</td></tr>
                        <tr><td colspan="4" class="pb-4"><label class="text-sm text-muted">Review decision
                          <select class="ml-3 rounded-md border border-border bg-surface px-3 py-2 text-text" [ngModel]="decisionAt($index).action" (ngModelChange)="setAction($index, $event)">
                            <option value="CONFIRM">Confirm extracted value</option><option value="CORRECT">Correct value</option><option value="REJECT">Reject value</option>
                          </select></label>
                          @if (decisionAt($index).action === 'CORRECT') {
                            <input aria-label="Corrected value" type="number" min="0.0001" class="ml-3 w-32 rounded-md border border-border bg-surface px-3 py-2 text-text" [ngModel]="decisionAt($index).correctedValue" (ngModelChange)="setCorrectedValue($index, $event)" />
                            <select aria-label="Corrected unit" class="ml-2 rounded-md border border-border bg-surface px-3 py-2 text-text" [ngModel]="decisionAt($index).correctedUnit ?? 'm'" (ngModelChange)="setCorrectedUnit($index, $event)"><option value="m">metres</option><option value="ft">feet</option></select>
                          }
                        </td></tr>
                      }
                    </tbody>
                  </table>
                </div>
              }
              @if (reviewError(); as message) { <bm-form-alert class="mt-5" tone="error" heading="Review not recorded" [message]="message" /> }
              @if (reviewDone()) { <bm-form-alert class="mt-5" tone="success" message="Review recorded as a new immutable revision." /> }
              @if (data.extraction.evidence.measurements.length) {
                <label class="mt-6 block text-sm text-muted">Review note<textarea class="mt-2 min-h-24 w-full rounded-md border border-border bg-surface p-3 text-text" maxlength="1000" [ngModel]="reviewNote()" (ngModelChange)="reviewNote.set($event)"></textarea></label>
                <button class="mt-4" bmButton type="button" [loading]="reviewing()" (click)="submitReview(data)">Record review</button>
              }
            </section>
            <dl class="border-l border-border pl-6 text-sm xl:col-start-2">
              <div class="pb-4"><dt class="text-muted">Lot reference</dt><dd class="mt-1 text-text">{{ data.extraction.evidence.lot_reference.value }}</dd></div>
              <div class="border-t border-border py-4"><dt class="text-muted">Reported area</dt><dd class="mt-1 tabular-nums text-text">{{ data.extraction.evidence.reported_area.value }} {{ data.extraction.evidence.reported_area.unit }}</dd></div>
              <div class="border-t border-border pt-4"><dt class="text-muted">Method</dt><dd class="mt-1 text-text">{{ data.extraction.method }}</dd></div>
            </dl>
          </div>
          <p class="mt-8 border-t border-border pt-5 text-sm text-muted">{{ data.notice }}</p>
        }
      </section>
    }
  `,
})
export class SurveyEvidencePage implements OnDestroy {
  readonly id = input.required<string>();
  readonly surveyId = input.required<string>();
  private readonly api = inject(CustomerApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly sanitizer = inject(DomSanitizer);
  protected readonly loading = signal(true);
  protected readonly error = signal<AppError | null>(null);
  protected readonly result = signal<SurveyExtraction | null>(null);
  protected readonly fileUrl = signal<string | null>(null);
  protected readonly safeFileUrl = signal<SafeResourceUrl | null>(null);
  protected readonly fileType = signal<string | null>(null);
  protected readonly fileError = signal<string | null>(null);
  protected readonly zoom = signal(100);
  protected readonly decisions = signal<SurveyReviewDecision[]>([]);
  protected readonly reviewNote = signal('');
  protected readonly reviewing = signal(false);
  protected readonly reviewDone = signal(false);
  protected readonly reviewError = signal<string | null>(null);
  private reviewVersion = 0;

  constructor() { queueMicrotask(() => this.load()); }

  protected load(): void {
    this.loading.set(true); this.error.set(null);
    this.api.getSurveyExtraction(this.id(), this.surveyId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (result) => { this.result.set(result); this.decisions.set(result.extraction?.evidence.measurements.map((_, measurementIndex) => ({ measurementIndex, action: 'CONFIRM' as const })) ?? []); this.loading.set(false); },
      error: (error: AppError) => { this.error.set(error); this.loading.set(false); },
    });
    this.loadFile();
  }

  private loadFile(): void {
    this.fileError.set(null);
    this.api.getSurveyFile(this.id(), this.surveyId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (blob) => {
        if (!['application/pdf', 'image/png', 'image/jpeg'].includes(blob.type)) {
          this.fileError.set('The stored document has an unsupported media type and cannot be previewed safely.'); return;
        }
        this.revokeFileUrl();
        const url = URL.createObjectURL(blob);
        this.fileType.set(blob.type); this.fileUrl.set(url);
        this.safeFileUrl.set(this.sanitizer.bypassSecurityTrustResourceUrl(url));
      },
      error: () => this.fileError.set('The original document is unavailable. Your extraction evidence remains unchanged.'),
    });
  }

  protected zoomIn(): void { this.zoom.update((value) => Math.min(200, value + 25)); }
  protected zoomOut(): void { this.zoom.update((value) => Math.max(50, value - 25)); }
  protected decisionAt(index: number): SurveyReviewDecision { return this.decisions()[index] ?? { measurementIndex: index, action: 'CONFIRM' }; }
  protected setAction(index: number, action: SurveyReviewDecision['action']): void { this.updateDecision(index, { measurementIndex: index, action }); }
  protected setCorrectedValue(index: number, value: number): void { this.updateDecision(index, { ...this.decisionAt(index), correctedValue: Number(value) }); }
  protected setCorrectedUnit(index: number, unit: 'm' | 'ft'): void { this.updateDecision(index, { ...this.decisionAt(index), correctedUnit: unit }); }
  private updateDecision(index: number, decision: SurveyReviewDecision): void { this.decisions.update((items) => items.map((item, i) => i === index ? decision : item)); }
  protected submitReview(data: SurveyExtraction): void {
    if (!data.extraction || this.reviewing()) return;
    this.reviewing.set(true); this.reviewError.set(null); this.reviewDone.set(false);
    this.api.submitSurveyReview(this.id(), this.surveyId(), data.extraction.extractionId, this.reviewVersion, this.decisions(), this.reviewNote().trim() || null).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (response) => { this.reviewVersion = response.review.version; this.reviewing.set(false); this.reviewDone.set(true); },
      error: (error: AppError) => { this.reviewing.set(false); this.reviewError.set(error.message); },
    });
  }
  ngOnDestroy(): void { this.revokeFileUrl(); }
  private revokeFileUrl(): void {
    const url = this.fileUrl(); if (url) URL.revokeObjectURL(url);
    this.fileUrl.set(null); this.safeFileUrl.set(null);
  }
}
