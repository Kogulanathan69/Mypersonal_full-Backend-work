import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { AppError } from '../../../core/api/app-error';
import { Button } from '../../../shared/ui/button/button';
import { FormAlert } from '../../../shared/ui/form-alert/form-alert';
import { ErrorState } from '../../../shared/ui/states/error-state';
import { Skeleton } from '../../../shared/ui/skeleton/skeleton';
import { CustomerApi, LandEdge, LandGeometry } from '../customer-api';

@Component({
  selector: 'bm-land-page', imports: [RouterLink, Button, FormAlert, ErrorState, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a [routerLink]="['/app/projects', id()]" class="text-sm">Back to project</a>
    <header class="mt-5 border-b border-border pb-6"><h1 class="text-3xl text-text">Land interpretation</h1><p class="mt-2 max-w-3xl text-muted">Inspect the current server-owned boundary before confirming it for planning. Measurements are evidence, not a cadastral survey.</p></header>
    @if (loading()) { <div class="mt-8"><bm-skeleton height="30rem" /></div> }
    @else if (error(); as failure) { <div class="mt-8"><bm-error-state [error]="failure" (retry)="load()" /></div> }
    @else if (land(); as geometry) {
      <div class="mt-8 grid gap-8 xl:grid-cols-[minmax(0,1fr)_20rem]">
        <section>
          <div class="flex flex-wrap items-center justify-between gap-3"><h2 class="text-xl text-text">Boundary drawing</h2><div class="flex gap-2"><button type="button" class="text-sm" (click)="zoomOut()">Zoom out</button><button type="button" class="text-sm" (click)="zoomIn()">Zoom in</button></div></div>
          <div class="mt-5 overflow-auto border border-border bg-paper p-5">
            <svg class="mx-auto block min-h-[28rem] min-w-[36rem]" [attr.viewBox]="viewBox()" [style.width.%]="zoom()" role="img" aria-label="Authoritative land boundary">
              <defs><pattern id="minor-grid" width="10" height="10" patternUnits="userSpaceOnUse"><path d="M 10 0 L 0 0 0 10" fill="none" stroke="var(--color-border-strong)" stroke-width="0.25" /></pattern></defs>
              <rect [attr.x]="bounds().minX - 10" [attr.y]="bounds().minY - 10" [attr.width]="bounds().width + 20" [attr.height]="bounds().height + 20" fill="url(#minor-grid)" />
              <polygon [attr.points]="polygonPoints()" fill="color-mix(in srgb, var(--color-accent) 16%, transparent)" stroke="var(--color-ink)" stroke-width="0.7" vector-effect="non-scaling-stroke" />
              @for (point of geometry.exterior; track $index) { <circle [attr.cx]="point.x" [attr.cy]="point.y" r="0.8" fill="var(--color-accent)" stroke="var(--color-ink)" stroke-width="0.25" (click)="selectedPoint.set($index)" class="cursor-pointer" /><text [attr.x]="point.x + 1" [attr.y]="point.y - 1" font-size="2.2" fill="var(--color-ink)">P{{ $index + 1 }}</text> }
              <g transform="translate(0 0)"><path d="M 0 7 L 0 -5 M -2 -1 L 0 -5 L 2 -1" stroke="var(--color-ink)" stroke-width="0.5" fill="none" /><text x="0" y="-7" font-size="2.5" text-anchor="middle" fill="var(--color-ink)">N</text></g>
            </svg>
          </div>
          <p class="mt-3 text-sm text-muted">Coordinate system: {{ geometry.coordinateSystem.type }} · {{ geometry.coordinateSystem.unit }} · X {{ geometry.coordinateSystem.xDirection }}, Y {{ geometry.coordinateSystem.yDirection }}</p>
        </section>
        <aside class="border-l border-border pl-6">
          <h2 class="text-xl text-text">Geometry inspection</h2>
          <dl class="mt-5 divide-y divide-border text-sm"><div class="pb-3"><dt class="text-muted">Area</dt><dd class="mt-1 tabular-nums text-text">{{ geometry.areaM2 }} m²</dd></div><div class="py-3"><dt class="text-muted">Revision</dt><dd class="mt-1 text-text">{{ geometry.revisionNumber }}</dd></div><div class="py-3"><dt class="text-muted">Provenance</dt><dd class="mt-1 text-text">{{ geometry.provenance }}</dd></div>@if (selectedPointValue()) { <div class="py-3"><dt class="text-muted">{{ selectedPointLabel() }}</dt><dd class="mt-1 tabular-nums text-text">{{ selectedPointCoordinates() }}</dd></div> }</dl>
          <h3 class="mt-7 font-medium text-text">Boundary edges</h3><ol class="mt-3 divide-y divide-border">@for (edge of edges(); track edge.edgeId) { <li class="py-3 text-sm"><div class="flex justify-between gap-3"><span class="text-text">{{ edge.edgeId }}</span><span class="tabular-nums text-text">{{ edge.lengthM }} m</span></div><p class="mt-1 text-muted">{{ edge.gridBearingDeg }}° · {{ edge.role }}</p></li> }</ol>
          <p class="mt-6 text-sm text-muted">Setbacks and buildable area are not shown because the current land contract supplies no authoritative statutory constraints.</p>
          @if (validationMessage()) { <bm-form-alert class="mt-5" [tone]="valid() ? 'success' : 'error'" [message]="validationMessage()!" /> }
          @if (confirmError(); as message) { <bm-form-alert class="mt-5" tone="error" heading="Land not confirmed" [message]="message" /> }
          <div class="mt-5 flex flex-wrap gap-3"><button bmButton type="button" variant="secondary" [loading]="validating()" (click)="validate(geometry)">Validate boundary</button><button bmButton type="button" [disabled]="!valid()" [loading]="confirming()" (click)="confirm(geometry)">Confirm land</button></div>
        </aside>
      </div>
    }
  `,
})
export class LandPage {
  readonly id = input.required<string>(); private readonly api = inject(CustomerApi); private readonly destroyRef = inject(DestroyRef);
  protected readonly land = signal<LandGeometry | null>(null); protected readonly edges = signal<readonly LandEdge[]>([]); protected readonly loading = signal(true); protected readonly error = signal<AppError | null>(null);
  protected readonly zoom = signal(100); protected readonly selectedPoint = signal<number | null>(null); protected readonly validating = signal(false); protected readonly confirming = signal(false); protected readonly valid = signal(false); protected readonly validationMessage = signal<string | null>(null); protected readonly confirmError = signal<string | null>(null);
  protected readonly bounds = computed(() => { const points = this.land()?.exterior ?? [{ x: 0, y: 0 }]; const xs = points.map(p => p.x), ys = points.map(p => p.y); const minX = Math.min(...xs), maxX = Math.max(...xs), minY = Math.min(...ys), maxY = Math.max(...ys); return { minX, minY, width: Math.max(maxX-minX, 10), height: Math.max(maxY-minY, 10) }; });
  protected readonly selectedPointValue = computed(() => { const index = this.selectedPoint(); if (index === null) return null; const point = this.land()?.exterior[index]; return point ? { ...point, index } : null; });
  protected readonly selectedPointLabel = computed(() => { const point = this.selectedPointValue(); return point ? `Selected point P${point.index + 1}` : ''; });
  protected readonly selectedPointCoordinates = computed(() => { const point = this.selectedPointValue(); return point ? `X ${point.x}, Y ${point.y}` : ''; });
  protected readonly viewBox = computed(() => { const b=this.bounds(); return `${b.minX-10} ${b.minY-10} ${b.width+20} ${b.height+20}`; }); protected readonly polygonPoints = computed(() => (this.land()?.exterior ?? []).map(p=>`${p.x},${p.y}`).join(' '));
  constructor() { queueMicrotask(() => this.load()); }
  protected load(): void { this.loading.set(true); this.error.set(null); this.api.getLand(this.id()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: land => { this.land.set(land); this.loading.set(false); this.api.getLandEdges(this.id()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: edges => this.edges.set(edges) }); }, error: (error: AppError) => { this.error.set(error); this.loading.set(false); } }); }
  protected zoomIn(): void { this.zoom.update(v=>Math.min(200,v+25)); } protected zoomOut(): void { this.zoom.update(v=>Math.max(50,v-25)); }
  protected validate(land: LandGeometry): void { this.validating.set(true); this.api.validateLand(this.id(), land.exterior).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: result => { this.valid.set(result.valid); this.validationMessage.set(result.valid ? `Boundary valid · ${result.areaM2 ?? land.areaM2} m² · ${result.edgeCount ?? land.edgeCount} edges` : `Boundary invalid: ${result.code}`); this.validating.set(false); }, error: (e: AppError) => { this.validationMessage.set(e.message); this.validating.set(false); } }); }
  protected confirm(land: LandGeometry): void { if (!this.valid()) return; this.confirming.set(true); this.confirmError.set(null); this.api.confirmLand(this.id(), land).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: value => { this.land.set(value); this.confirming.set(false); }, error: (e: AppError) => { this.confirmError.set(e.message); this.confirming.set(false); } }); }
}
