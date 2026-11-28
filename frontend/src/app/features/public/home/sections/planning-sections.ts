import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { Icon } from '../../../../shared/ui/icon/icon';
import { variantRooms, PlanDrawing, PlanVariant } from './plan-drawing';
import { IllustrativeTag, SectionHeading } from './section-heading';

/** 6 — Regulation evidence (RAG) showcase. Shows the STRUCTURE of evidence, never real citations. */
@Component({
  selector: 'bm-regulation-showcase',
  imports: [SectionHeading, IllustrativeTag, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section aria-labelledby="rag-heading" class="border-t border-mk-line py-20 sm:py-28">
      <div class="mx-auto grid max-w-7xl items-center gap-12 px-5 sm:px-8 lg:grid-cols-2">
        <div>
          <bm-section-heading
            headingId="rag-heading"
            eyebrow="Regulation evidence"
            heading="Planning rules with their sources, not guesses"
            intro="BuildMind retrieves passages from planning documents that may apply to your site and shows where each one comes from. Retrieved text stays separate from constraints a person has reviewed, and nothing is presented as legal approval."
          />
          <p class="mt-6 flex items-start gap-2 text-sm text-mk-muted">
            <bm-icon name="info" [size]="18" class="mt-0.5 text-mk-gold" />
            Plans are preliminary. Approval comes only from your local authority.
          </p>
        </div>
        <div class="border border-mk-line bg-mk-surface-2">
          <div class="flex items-center justify-between">
            <div class="p-5">
              <p class="text-sm font-semibold text-mk-white">Evidence review workspace</p>
              <p class="mt-1 text-xs text-mk-muted">
                Corpus connection not configured · demonstration structure only
              </p>
            </div>
            <bm-illustrative-tag />
          </div>
          <article class="border-t border-mk-line bg-mk-navy p-5">
            <div class="flex flex-wrap items-center gap-2 text-xs">
              <span class="rounded-pill border border-mk-cyan/60 px-2 py-0.5 text-mk-cyan"
                >Retrieved passage</span
              >
              <span class="rounded-pill border border-mk-gold/60 px-2 py-0.5 text-mk-gold"
                >Awaiting review</span
              >
            </div>
            <p class="mt-3 text-sm text-mk-white">
              Example requirement title: road-facing building line
            </p>
            <p class="mt-2 text-xs text-mk-muted">
              Source: unavailable in this demonstration · applicability: unconfirmed · review
              required
            </p>
            <dl class="mt-4 grid gap-3 text-xs sm:grid-cols-3">
              <div>
                <dt class="text-mk-muted">Evidence state</dt>
                <dd class="mt-1 text-mk-gold">Awaiting source</dd>
              </div>
              <div>
                <dt class="text-mk-muted">Site applicability</dt>
                <dd class="mt-1 text-mk-white">Not assessed</dd>
              </div>
              <div>
                <dt class="text-mk-muted">Reviewer</dt>
                <dd class="mt-1 text-mk-white">Not assigned</dd>
              </div>
            </dl>
          </article>
          <article class="border-t border-mk-line bg-mk-navy p-5">
            <div class="flex flex-wrap items-center gap-2 text-xs">
              <span class="rounded-pill border border-mk-white/40 px-2 py-0.5 text-mk-white"
                >Reviewed constraint</span
              >
            </div>
            <p class="mt-3 text-sm text-mk-white">
              Illustrative constraint preview: a setback may be drawn only after the applicable
              source and site context are confirmed.
            </p>
            <p class="mt-2 text-xs text-mk-muted">
              No statutory value, citation or compliance result is asserted here.
            </p>
          </article>
        </div>
      </div>
    </section>
  `,
})
export class RegulationShowcase {}

/** 7 — AI plan generation and 8 — layout alternatives. */
@Component({
  selector: 'bm-generation-showcase',
  imports: [SectionHeading, IllustrativeTag, PlanDrawing, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section aria-labelledby="gen-heading" class="border-t border-mk-line py-20 sm:py-28">
      <div class="mx-auto max-w-7xl px-5 sm:px-8">
        <bm-section-heading
          headingId="gen-heading"
          eyebrow="AI 2D plan generation"
          heading="Alternative layouts for your land, side by side"
          intro="BuildMind fits your rooms inside the buildable area of your confirmed land and produces alternative 2D layouts. Each one shows its rooms, areas, doors, windows and any validation warnings, so you can compare before you choose."
        />
        <div class="mt-10 flex items-center justify-between">
          <p class="text-sm text-mk-muted">Three arrangements of the same illustrative rooms</p>
          <bm-illustrative-tag />
        </div>
        <ul class="mt-4 grid gap-5 md:grid-cols-3">
          @for (v of variants; track v.id) {
            <li
              class="border bg-mk-surface-2 p-3"
              [class.border-mk-cyan]="chosen() === v.id"
              [class.border-mk-line]="chosen() !== v.id"
            >
              <button
                type="button"
                class="w-full text-left"
                [attr.aria-pressed]="chosen() === v.id"
                (click)="chosen.set(v.id)"
              >
                <bm-plan-drawing [variant]="v.id" [label]="v.name + ' illustrative layout'" />
                <span class="mt-3 flex items-center justify-between gap-3">
                  <span class="font-semibold text-mk-white">{{ v.name }}</span>
                  <span class="text-xs text-mk-muted">{{ v.area }} · {{ v.character }}</span>
                </span>
              </button>
              <button
                type="button"
                class="mt-3 inline-flex items-center gap-2 text-xs font-semibold text-mk-gold"
                (click)="expanded.set(v.id)"
              >
                <bm-icon name="eye" [size]="14" /> View detailed plan
              </button>
            </li>
          }
        </ul>
        <p class="mt-4 text-sm text-mk-muted" aria-live="polite">
          Selected: {{ chosenName() }}. In BuildMind, your choice becomes an editable plan and the
          other options stay available.
        </p>
        @if (expanded(); as open) {
          <div
            class="mt-8 border border-mk-line bg-mk-surface-2 p-4"
            aria-label="Expanded floor plan"
          >
            <div class="mb-4 flex flex-wrap items-center justify-between gap-3">
              <div>
                <p class="font-semibold text-mk-white">Expanded {{ variantName(open) }}</p>
                <p class="text-xs text-mk-muted">
                  Illustrative 118 m² residence · drawing scale 1:100
                </p>
              </div>
              <button type="button" class="bm-demo-tool" (click)="expanded.set(null)">
                <bm-icon name="x" [size]="15" /> Close
              </button>
            </div>
            <div class="mx-auto max-w-4xl"><bm-plan-drawing [variant]="open" /></div>
          </div>
        }
      </div>
    </section>
  `,
})
export class GenerationShowcase {
  protected readonly variants: readonly {
    id: PlanVariant;
    name: string;
    area: string;
    character: string;
  }[] = [
    { id: 'a', name: 'Option A', area: '118 m²', character: 'Courtyard living' },
    { id: 'b', name: 'Option B', area: '116 m²', character: 'Central living' },
    { id: 'c', name: 'Option C', area: '120 m²', character: 'Linear circulation' },
  ];
  protected readonly chosen = signal<PlanVariant>('a');
  protected readonly expanded = signal<PlanVariant | null>(null);
  protected readonly chosenName = () => `Option ${this.chosen().toUpperCase()}`;
  protected readonly variantName = (variant: PlanVariant) => `Option ${variant.toUpperCase()}`;
}

/** 9 — Interactive 2D editor showcase and 10 — natural-language AI editing. */
@Component({
  selector: 'bm-editor-showcase',
  imports: [SectionHeading, IllustrativeTag, PlanDrawing, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section aria-labelledby="editor-heading" class="border-t border-mk-line py-20 sm:py-28">
      <div class="mx-auto max-w-7xl px-5 sm:px-8">
        <bm-section-heading
          headingId="editor-heading"
          eyebrow="Interactive 2D editor"
          heading="Refine the plan yourself, with every change checked"
          intro="This local demonstration lets you inspect rooms and drawing detail. Protected project editing remains inside the authenticated application."
        />
        <div class="mt-10 grid border border-mk-line bg-mk-surface-2 lg:grid-cols-[1fr_17rem]">
          <div class="min-w-0 p-4">
            <div class="bm-demo-toolbar">
              <button type="button" class="bm-demo-tool" (click)="zoomOut()" aria-label="Zoom out">
                −
              </button>
              <span class="px-2 text-xs text-mk-muted">{{ (zoom() * 100).toFixed(0) }}%</span>
              <button type="button" class="bm-demo-tool" (click)="zoomIn()" aria-label="Zoom in">
                +
              </button>
              <span class="mx-1 h-5 w-px bg-mk-line"></span>
              <button type="button" class="bm-demo-tool" (click)="selected.set(null)">
                <bm-icon name="refresh" [size]="15" /> Reset selection
              </button>
              <span class="ml-auto"><bm-illustrative-tag /></span>
            </div>
            <bm-plan-drawing
              [selectable]="true"
              [showDimensions]="true"
              [selected]="selected()"
              [zoom]="zoom()"
              (selectedChange)="selected.set($event)"
              label="Illustrative plan. Select a room to see its size."
            />
          </div>
          <aside
            class="border-t border-mk-line p-5 lg:border-t-0 lg:border-l"
            aria-label="Room properties"
          >
            <p class="text-xs font-semibold tracking-widest text-mk-gold uppercase">
              Room properties
            </p>
            <p class="mt-3 font-semibold text-mk-white" aria-live="polite">
              {{ selectionTitle() }}
            </p>
            <p class="sr-only" aria-live="polite">{{ selectionText() }}</p>
            <dl class="mt-5 space-y-4 text-sm">
              <div>
                <dt class="text-mk-muted">Internal size</dt>
                <dd class="mt-1 text-mk-white">{{ selectionSize() }}</dd>
              </div>
              <div>
                <dt class="text-mk-muted">Drawing layer</dt>
                <dd class="mt-1 text-mk-white">Architecture / rooms</dd>
              </div>
              <div>
                <dt class="text-mk-muted">Review state</dt>
                <dd class="mt-1 text-mk-gold">Illustrative selection</dd>
              </div>
            </dl>
            <p class="mt-6 border-t border-mk-line pt-4 text-xs leading-5 text-mk-muted">
              Select a room directly on the drawing. Zoom and reset operate locally in this
              demonstration.
            </p>
          </aside>
        </div>
      </div>
    </section>

    <section aria-labelledby="ai-heading" class="border-t border-mk-line py-20 sm:py-28">
      <div class="mx-auto grid max-w-7xl items-center gap-12 px-5 sm:px-8 lg:grid-cols-2">
        <div>
          <bm-section-heading
            headingId="ai-heading"
            eyebrow="Natural-language editing"
            heading="Ask for a change. Approve it before it happens."
            intro="Describe what you want in your own words. BuildMind turns it into a proposed edit, previews it on your plan and checks it. Nothing changes until you accept, and rejected proposals leave your plan exactly as it was."
          />
        </div>
        <div
          class="border border-mk-line bg-mk-surface-2 p-5"
          aria-label="Illustrative AI editing conversation"
        >
          <div class="mb-4 flex justify-end"><bm-illustrative-tag /></div>
          <div
            class="ml-auto max-w-xs rounded-2xl rounded-br-sm bg-mk-cyan px-4 py-3 text-sm text-mk-navy"
          >
            Make the living room a little wider.
          </div>
          <div
            class="mt-5 grid gap-3 sm:grid-cols-2"
            aria-label="Illustrative before and after plans"
          >
            <figure>
              <figcaption class="mb-2 text-xs font-semibold text-mk-muted">
                Before · revision 04
              </figcaption>
              <bm-plan-drawing [variant]="'a'" label="Plan before proposed change" />
            </figure>
            <figure>
              <figcaption class="mb-2 text-xs font-semibold text-mk-gold">
                After · proposed only
              </figcaption>
              <bm-plan-drawing [variant]="'c'" label="Plan after proposed change" />
            </figure>
          </div>
          <div
            class="mt-4 max-w-sm rounded-2xl rounded-bl-sm border border-mk-line bg-mk-navy px-4 py-3 text-sm text-mk-white"
          >
            <p class="font-semibold">Proposed change</p>
            <p class="mt-1 text-mk-muted">
              Move the wall between the living room and kitchen. Preview shown on the plan;
              validation runs before you decide.
            </p>
            <div class="mt-3 flex gap-2">
              <span
                class="inline-flex items-center gap-1 rounded-pill bg-mk-cyan/15 px-3 py-1 text-xs text-mk-cyan"
                ><bm-icon name="check" [size]="14" /> Accept</span
              >
              <span
                class="inline-flex items-center gap-1 rounded-pill border border-mk-line px-3 py-1 text-xs text-mk-muted"
                ><bm-icon name="x" [size]="14" /> Reject</span
              >
            </div>
          </div>
          <p class="mt-4 text-xs text-mk-muted">
            Which instructions are supported depends on the AI service in use; unsupported requests
            are explained, not guessed.
          </p>
        </div>
      </div>
    </section>
  `,
})
export class EditorShowcase {
  protected readonly selected = signal<number | null>(null);
  protected readonly zoom = signal(1);
  protected zoomIn(): void {
    this.zoom.update((value) => Math.min(1.25, value + 0.1));
  }
  protected zoomOut(): void {
    this.zoom.update((value) => Math.max(0.8, value - 0.1));
  }
  protected readonly selectedRoom = () => {
    const index = this.selected();
    return index === null ? null : variantRooms('a')[index]!;
  };
  protected readonly selectionTitle = () => this.selectedRoom()?.name ?? 'No room selected';
  protected readonly selectionSize = () => {
    const room = this.selectedRoom();
    return room
      ? `${(room.x1 - room.x0).toFixed(1)} m × ${(room.z1 - room.z0).toFixed(1)} m`
      : 'Select a room to inspect';
  };
  protected readonly selectionText = () => {
    const i = this.selected();
    if (i === null) return 'No room selected.';
    const r = variantRooms('a')[i]!;
    return `${r.name}: ${(r.x1 - r.x0).toFixed(1)} m by ${(r.z1 - r.z0).toFixed(1)} m.`;
  };
}
