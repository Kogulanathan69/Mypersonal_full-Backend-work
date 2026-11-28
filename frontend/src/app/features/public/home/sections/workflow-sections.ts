import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { Icon } from '../../../../shared/ui/icon/icon';
import { IconName } from '../../../../shared/ui/icon/icon-paths';
import { PARCEL, edgeLength, polygonArea } from '../cinematic/house-model-data';
import { m2ToPerches } from '../../../../core/units/units';
import { IllustrativeTag, SectionHeading } from './section-heading';

/** 3 — How BuildMind works. */
@Component({
  selector: 'bm-how-it-works',
  imports: [SectionHeading, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section id="how-it-works" aria-labelledby="how-heading" class="scroll-mt-20 py-20 sm:py-28">
      <div class="mx-auto max-w-7xl px-5 sm:px-8">
        <bm-section-heading
          headingId="how-heading"
          eyebrow="How BuildMind works"
          heading="Six steps from survey to a plan you can discuss"
          intro="You stay in control at every step: BuildMind proposes, you review, and nothing is final until you confirm it."
        />
        <ol class="mt-12 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
          @for (step of steps; track step.title; let i = $index) {
            <li class="rounded-xl border border-mk-line bg-mk-surface-2 p-6">
              <div class="flex items-center gap-3">
                <span
                  class="flex size-11 items-center justify-center rounded-pill border border-mk-cyan/60 text-mk-cyan"
                >
                  <bm-icon [name]="step.icon" [size]="20" />
                </span>
                <span class="font-mono text-sm text-mk-muted">Step {{ i + 1 }}</span>
              </div>
              <h3 class="mt-4 text-lg font-semibold text-mk-white">{{ step.title }}</h3>
              <p class="mt-2 text-sm text-mk-muted">{{ step.text }}</p>
            </li>
          }
        </ol>
      </div>
    </section>
  `,
})
export class HowItWorks {
  protected readonly steps: readonly { icon: IconName; title: string; text: string }[] = [
    {
      icon: 'upload',
      title: 'Upload your survey plan',
      text: 'Add the survey plan for your land as a PDF or image.',
    },
    {
      icon: 'map-pin',
      title: 'Confirm your land',
      text: 'Review the extracted boundary, measurements and coordinate system, and correct anything uncertain.',
    },
    {
      icon: 'ruler',
      title: 'Describe your home',
      text: 'Choose rooms, sizes and how spaces should relate to each other.',
    },
    {
      icon: 'shield',
      title: 'Check the rules',
      text: 'See the planning evidence that applies to your site, with sources and review status.',
    },
    {
      icon: 'sparkles',
      title: 'Generate 2D layouts',
      text: 'Compare alternative layouts produced for your land and requirements, then choose one.',
    },
    {
      icon: 'edit',
      title: 'Edit, confirm and share',
      text: 'Refine the plan yourself or with AI suggestions you approve, then download a preliminary report.',
    },
  ];
}

/** 4 — AI survey extraction, 5 — land boundary and CRS. */
@Component({
  selector: 'bm-land-showcases',
  imports: [SectionHeading, IllustrativeTag],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section
      id="features"
      aria-labelledby="survey-heading"
      class="scroll-mt-20 border-t border-mk-line py-20 sm:py-28"
    >
      <div class="mx-auto grid max-w-7xl items-center gap-12 px-5 sm:px-8 lg:grid-cols-2">
        <div>
          <bm-section-heading
            headingId="survey-heading"
            eyebrow="AI survey extraction"
            heading="Your survey plan, read and laid out for review"
            intro="BuildMind reads the lot details, boundary lengths, bearings and coordinates from your survey plan. Values it is unsure about are flagged so you can check them against the original page."
          />
          <ul class="mt-6 space-y-2 text-sm text-mk-muted">
            <li>· Original page and extracted values side by side</li>
            <li>· Uncertain readings marked for your review, never confirmed automatically</li>
            <li>· Corrections kept with the land revision history</li>
          </ul>
        </div>
        <div class="border border-mk-line bg-mk-surface-2 p-4">
          <div class="mb-4 flex items-center justify-between">
            <div>
              <p class="text-sm font-semibold text-mk-white">Survey extraction review</p>
              <p class="text-xs text-mk-muted">
                Local illustrative interaction · no live AI request
              </p>
            </div>
            <bm-illustrative-tag />
          </div>
          <div class="bm-demo-toolbar">
            <button
              type="button"
              class="bm-demo-tool"
              (click)="surveyZoomOut()"
              aria-label="Zoom survey out"
            >
              −
            </button>
            <span class="px-2 text-xs text-mk-muted">{{ (surveyZoom() * 100).toFixed(0) }}%</span>
            <button
              type="button"
              class="bm-demo-tool"
              (click)="surveyZoomIn()"
              aria-label="Zoom survey in"
            >
              +
            </button>
            <span class="ml-auto text-xs text-mk-muted"
              >Selected: {{ selectedFieldLabel() }}</span
            >
          </div>
          <div class="grid gap-4 lg:grid-cols-[1.2fr_.8fr]">
            <div class="overflow-hidden border border-mk-line bg-mk-chalk p-3">
              <svg
                viewBox="0 0 420 520"
                class="w-full"
                [style.transform]="'scale(' + surveyZoom() + ')'"
                style="transform-origin:center"
                role="img"
                aria-label="Detailed illustrative survey plan for Lot 7"
              >
                <rect x="4" y="4" width="412" height="512" class="fill-mk-chalk stroke-mk-ink" />
                <text
                  x="210"
                  y="30"
                  text-anchor="middle"
                  class="fill-mk-ink"
                  font-size="14"
                  font-weight="700"
                >
                  ILLUSTRATIVE SURVEY PLAN
                </text>
                <text x="210" y="48" text-anchor="middle" class="fill-mk-ink-muted" font-size="9">
                  LOT 7 · SAMPLE LAND RECORD · NOT FOR CERTIFICATION
                </text>
                <path d="M365 92l10-34 10 34-10-8z" class="fill-mk-ink" />
                <text x="375" y="108" text-anchor="middle" class="fill-mk-ink" font-size="11">
                  N
                </text>
                <polygon
                  points="95,140 300,120 342,260 285,395 108,370 62,245"
                  class="fill-mk-aperture/10 stroke-mk-ink"
                  stroke-width="2"
                />
                <g class="fill-mk-ink" font-size="9">
                  <text x="82" y="132">P1</text>
                  <text x="302" y="112">P2</text>
                  <text x="347" y="260">P3</text>
                  <text x="290" y="410">P4</text>
                  <text x="92" y="385">P5</text>
                  <text x="43" y="245">P6</text>
                </g>
                <g class="fill-mk-ink-muted" font-size="9">
                  <text x="185" y="122">N 84° 12' E · 20.6 m</text>
                  <text x="325" y="188" transform="rotate(73 325 188)">14.8 m</text>
                  <text x="318" y="330" transform="rotate(-67 318 330)">9.2 m</text>
                  <text x="190" y="396">S 87° 05' W · 20.5 m</text>
                  <text x="70" y="320" transform="rotate(74 70 320)">11.0 m</text>
                  <text x="72" y="187" transform="rotate(-73 72 187)">10.6 m</text>
                </g>
                <rect
                  x="292"
                  y="173"
                  width="55"
                  height="94"
                  [class]="
                    selectedField() === 2
                      ? 'fill-mk-aperture/25 stroke-mk-aperture'
                      : 'fill-none stroke-transparent'
                  "
                />
                <line x1="55" y1="435" x2="165" y2="435" class="stroke-mk-ink" stroke-width="3" />
                <line x1="55" y1="429" x2="55" y2="441" class="stroke-mk-ink" />
                <line x1="110" y1="429" x2="110" y2="441" class="stroke-mk-ink" />
                <line x1="165" y1="429" x2="165" y2="441" class="stroke-mk-ink" />
                <text x="110" y="454" text-anchor="middle" class="fill-mk-ink" font-size="9">
                  0 · 5 · 10 m
                </text>
                <g class="fill-mk-ink" font-size="9">
                  <text x="225" y="440">Extent: 630 m² / 24.9 perches</text>
                  <text x="225" y="455">Local coordinate example: E 1000.00 / N 1000.00</text>
                  <text x="225" y="470">Drawing scale: 1:500 at original sheet size</text>
                </g>
              </svg>
            </div>
            <div class="space-y-3 text-sm">
              @for (f of fields; track f.label; let index = $index) {
                <button
                  type="button"
                  class="block w-full border bg-mk-navy px-3 py-3 text-left"
                  [class.border-mk-gold]="selectedField() === index"
                  [class.border-mk-line]="selectedField() !== index"
                  (click)="selectedField.set(index)"
                >
                  <span class="block text-xs text-mk-muted">{{ f.label }}</span>
                  <span class="mt-0.5 flex items-center justify-between gap-2 text-mk-white">
                    {{ f.value }}
                    @if (f.review) {
                      <span class="rounded-pill bg-mk-gold/15 px-2 text-xs text-mk-gold"
                        >Check</span
                      >
                    }
                  </span>
                </button>
              }
            </div>
          </div>
        </div>
      </div>
    </section>

    <section aria-labelledby="land-heading" class="border-t border-mk-line py-20 sm:py-28">
      <div class="mx-auto grid max-w-7xl items-center gap-12 px-5 sm:px-8 lg:grid-cols-2">
        <div class="order-2 border border-mk-line bg-mk-surface-2 p-5 lg:order-1">
          <div class="mb-4 flex items-center justify-between">
            <p class="text-sm font-semibold text-mk-white">Land boundary</p>
            <bm-illustrative-tag />
          </div>
          <svg
            viewBox="-19 -14 38 28"
            class="w-full"
            role="img"
            aria-label="Illustrative parcel boundary with corner points and edge lengths"
          >
            <rect x="-19" y="9.5" width="38" height="4.5" class="fill-mk-aperture/15" />
            <text x="0" y="12.2" text-anchor="middle" class="fill-mk-gold" font-size="1">
              ILLUSTRATIVE ROAD FRONTAGE · 6 m
            </text>
            @for (v of grid; track v) {
              <line
                [attr.x1]="v"
                y1="-14"
                [attr.x2]="v"
                y2="14"
                class="stroke-mk-cyan/10"
                stroke-width="0.08"
              />
              <line
                x1="-19"
                [attr.y1]="v"
                x2="19"
                [attr.y2]="v"
                class="stroke-mk-cyan/10"
                stroke-width="0.08"
              />
            }
            <polygon
              [attr.points]="points"
              class="fill-mk-cyan/10 stroke-mk-cyan"
              stroke-width="0.25"
            />
            <polygon
              points="-10,-7 9,-7.7 12,4.8 8,7 -8,6.5 -11,1"
              class="fill-none stroke-mk-gold"
              stroke-width="0.18"
              stroke-dasharray="0.8 0.5"
            />
            <polygon
              points="-7,-4.5 6.5,-5 8,3.2 5.5,4.8 -5.8,4.5 -8,0.8"
              class="fill-mk-gold/10 stroke-mk-gold"
              stroke-width="0.14"
            />
            <text x="0" y="0" text-anchor="middle" class="fill-mk-gold" font-size="1">
              ILLUSTRATIVE BUILDABLE AREA
            </text>
            <path d="M14-8l1.2-4 1.2 4-1.2-1z" class="fill-mk-white" />
            <text x="15.2" y="-6.2" text-anchor="middle" class="fill-mk-white" font-size="1">
              N
            </text>
            @for (e of edges; track $index) {
              <text
                [attr.x]="e.x"
                [attr.y]="e.y"
                text-anchor="middle"
                class="fill-mk-gold"
                font-size="1.1"
              >
                {{ e.len }} m
              </text>
            }
            @for (p of parcel; track $index; let i = $index) {
              <circle [attr.cx]="p[0]" [attr.cy]="p[1]" r="0.45" class="fill-mk-cyan" />
              <text [attr.x]="p[0] + 0.8" [attr.y]="p[1] - 0.6" class="fill-mk-white" font-size="1">
                P{{ i + 1 }}
              </text>
            }
          </svg>
          <p class="mt-3 text-xs text-mk-muted">
            Area {{ area }} m² ({{ perches }} perches) · local frame in metres
          </p>
          <div class="mt-4 flex flex-wrap gap-4 border-t border-mk-line pt-3 text-xs text-mk-muted">
            <span class="text-mk-gold">— Confirmed sample boundary</span
            ><span>--- Illustrative setback</span><span class="text-mk-white">■ Road frontage</span>
          </div>
        </div>
        <div class="order-1 lg:order-2">
          <bm-section-heading
            headingId="land-heading"
            eyebrow="Land boundary and coordinates"
            heading="An exact boundary, in a coordinate system you can see"
            intro="Move corner points, check edge lengths and mark the road-facing side. BuildMind shows which coordinate reference system your survey uses and warns you when it cannot be determined, rather than guessing."
          />
          <ul class="mt-6 space-y-2 text-sm text-mk-muted">
            <li>· Areas in square metres, square feet or perches</li>
            <li>· Coordinate system and transformation warnings shown, never silently assumed</li>
            <li>· Every confirmed boundary saved as its own revision</li>
          </ul>
        </div>
      </div>
    </section>
  `,
})
export class LandShowcases {
  protected readonly surveyZoom = signal(1);
  protected readonly selectedField = signal(0);
  protected readonly selectedFieldLabel = () => this.fields[this.selectedField()]?.label ?? 'None';
  protected surveyZoomIn(): void {
    this.surveyZoom.update((value) => Math.min(1.3, value + 0.1));
  }
  protected surveyZoomOut(): void {
    this.surveyZoom.update((value) => Math.max(0.9, value - 0.1));
  }
  protected readonly fields = [
    { label: 'Lot', value: 'Lot 7 (example)', review: false },
    { label: 'Reported extent', value: '25.6 perches (example)', review: false },
    { label: 'Boundary length P2–P3', value: '14.8 m (example)', review: true },
    { label: 'Coordinate system', value: 'To be confirmed', review: true },
  ];
  protected readonly grid = [-16, -12, -8, -4, 0, 4, 8, 12, 16];
  protected readonly parcel = PARCEL;
  protected readonly points = PARCEL.map(([x, z]) => `${x},${z}`).join(' ');
  protected readonly edges = PARCEL.map((a, i) => {
    const b = PARCEL[(i + 1) % PARCEL.length]!;
    return { x: (a[0] + b[0]) / 2, y: (a[1] + b[1]) / 2 + 0.4, len: edgeLength(a, b).toFixed(1) };
  });
  protected readonly area = polygonArea(PARCEL).toFixed(0);
  protected readonly perches = m2ToPerches(polygonArea(PARCEL)).toFixed(1);
}
