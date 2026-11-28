import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Icon } from '../../../../shared/ui/icon/icon';
import { IconName } from '../../../../shared/ui/icon/icon-paths';
import { StaticHouseIllustration } from '../cinematic/static-house-illustration';
import { PlanDrawing } from './plan-drawing';
import { SectionHeading } from './section-heading';

/** 11 — Architect review workflow. */
@Component({
  selector: 'bm-architect-showcase',
  imports: [SectionHeading, Icon, PlanDrawing],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section aria-labelledby="architect-heading" class="border-t border-mk-line py-20 sm:py-28">
      <div class="mx-auto max-w-7xl px-5 sm:px-8">
        <bm-section-heading
          headingId="architect-heading"
          eyebrow="Professional review"
          heading="Share your plan with an architect — only when you choose"
          intro="When your plan is ready, you can ask an architect on BuildMind to review it. They see exactly the plan and report you shared, and you can withdraw access at any time."
        />
        <div class="mt-12 grid border border-mk-line bg-mk-surface-2 lg:grid-cols-[1.2fr_.8fr]">
          <div class="border-b border-mk-line p-4 lg:border-r lg:border-b-0">
            <div class="mb-3 flex items-center justify-between">
              <p class="text-sm font-semibold text-mk-white">Plan revision 04</p>
              <span class="text-xs text-mk-gold">Consent granted · illustrative</span>
            </div>
            <bm-plan-drawing [variant]="'a'" label="Illustrative plan under professional review" />
          </div>
          <div class="p-5">
            <p class="text-xs font-semibold tracking-widest text-mk-gold uppercase">
              Review activity
            </p>
            <ol class="mt-5 space-y-5">
              @for (s of steps; track s.title; let index = $index) {
                <li
                  class="grid grid-cols-[2rem_1fr] gap-3 border-b border-mk-line pb-4 last:border-0"
                >
                  <span class="grid size-8 place-items-center border border-mk-line text-mk-gold"
                    ><bm-icon [name]="s.icon" [size]="16"
                  /></span>
                  <div>
                    <p class="text-xs text-mk-muted">Stage 0{{ index + 1 }}</p>
                    <h3 class="font-semibold text-mk-white">{{ s.title }}</h3>
                    <p class="mt-1 text-xs leading-5 text-mk-muted">{{ s.text }}</p>
                  </div>
                </li>
              }
            </ol>
            <div class="mt-5 border-l-2 border-mk-gold bg-mk-navy p-4">
              <p class="text-xs text-mk-muted">Illustrative review note</p>
              <p class="mt-1 text-sm text-mk-white">
                Clarify the kitchen-to-dining circulation before the next revision.
              </p>
              <p class="mt-2 text-xs text-mk-muted">
                No real architect identity, approval or certification is represented.
              </p>
            </div>
          </div>
        </div>
      </div>
    </section>
  `,
})
export class ArchitectShowcase {
  protected readonly steps: readonly { icon: IconName; title: string; text: string }[] = [
    {
      icon: 'users',
      title: 'Choose an architect',
      text: 'Browse architects and their verification status.',
    },
    {
      icon: 'lock',
      title: 'Give consent',
      text: 'Pick the exact plan and report snapshot to share.',
    },
    {
      icon: 'file-text',
      title: 'Receive feedback',
      text: 'The architect reviews and replies inside BuildMind.',
    },
    {
      icon: 'x',
      title: 'Withdraw any time',
      text: 'Revoking consent removes their access to your files.',
    },
  ];
}

/** 12 — Benefits, 13 — FAQ, 14 — final call to action. */
@Component({
  selector: 'bm-closing-sections',
  imports: [SectionHeading, Icon, RouterLink, StaticHouseIllustration],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section
      id="about"
      aria-labelledby="benefits-heading"
      class="scroll-mt-20 border-t border-mk-line py-20 sm:py-28"
    >
      <div class="mx-auto max-w-7xl px-5 sm:px-8">
        <bm-section-heading
          headingId="benefits-heading"
          eyebrow="Why BuildMind"
          heading="Built for homeowners planning in Sri Lanka"
        />
        <ul class="mt-12 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
          @for (b of benefits; track b.title) {
            <li class="rounded-xl border border-mk-line bg-mk-surface-2 p-6">
              <bm-icon [name]="b.icon" [size]="24" class="text-mk-cyan" />
              <h3 class="mt-4 text-lg font-semibold text-mk-white">{{ b.title }}</h3>
              <p class="mt-2 text-sm text-mk-muted">{{ b.text }}</p>
            </li>
          }
        </ul>
      </div>
    </section>

    <section aria-labelledby="faq-heading" class="border-t border-mk-line py-20 sm:py-28">
      <div class="mx-auto max-w-3xl px-5 sm:px-8">
        <bm-section-heading
          headingId="faq-heading"
          eyebrow="Questions"
          heading="Frequently asked questions"
        />
        <div class="mt-10 divide-y divide-mk-line rounded-xl border border-mk-line bg-mk-surface-2">
          @for (f of faqs; track f.q) {
            <details class="group px-5 py-4">
              <summary
                class="flex cursor-pointer list-none items-center justify-between gap-4 font-medium text-mk-white"
              >
                {{ f.q }}
                <bm-icon
                  name="chevron-down"
                  [size]="18"
                  class="text-mk-cyan transition-transform group-open:rotate-180"
                />
              </summary>
              <p class="mt-3 text-sm text-mk-muted">{{ f.a }}</p>
            </details>
          }
        </div>
      </div>
    </section>

    <section
      aria-labelledby="cta-heading"
      class="relative overflow-hidden border-t border-mk-line py-24 sm:py-32"
    >
      <bm-static-house-illustration
        [stage]="2"
        class="pointer-events-none absolute -right-24 bottom-0 w-[42rem] opacity-20"
        label=""
        aria-hidden="true"
      />
      <div class="relative mx-auto max-w-7xl px-5 sm:px-8">
        <h2 id="cta-heading" class="max-w-2xl text-4xl font-semibold text-mk-white sm:text-5xl">
          Start with your land. <span class="text-mk-gold">Shape the home.</span>
        </h2>
        <p class="mt-5 max-w-xl text-mk-muted">
          Explore how BuildMind presents survey information, planning evidence and preliminary 2D
          layout possibilities. Account services are not connected yet.
        </p>
        <div class="mt-8 flex flex-wrap gap-3">
          <a
            routerLink="/"
            fragment="planning-experience"
            class="inline-flex h-12 items-center gap-2 rounded-md bg-mk-cyan px-6 font-semibold text-mk-navy no-underline hover:bg-mk-white"
          >
            Explore the planning experience <bm-icon name="arrow-right" [size]="18" />
          </a>
          <a
            routerLink="/"
            fragment="how-it-works"
            class="inline-flex h-12 items-center rounded-md border border-mk-white/40 px-6 font-medium text-mk-white no-underline hover:border-mk-cyan hover:text-mk-cyan"
            >See how BuildMind works</a
          >
        </div>
      </div>
    </section>
  `,
})
export class ClosingSections {
  protected readonly benefits: readonly { icon: IconName; title: string; text: string }[] = [
    {
      icon: 'map-pin',
      title: 'Starts from your actual land',
      text: 'Plans are fitted to your surveyed boundary, not a generic plot.',
    },
    {
      icon: 'ruler',
      title: 'Metres, feet and perches',
      text: 'See areas and lengths in the units you use.',
    },
    {
      icon: 'shield',
      title: 'Evidence you can trace',
      text: 'Planning evidence shows its source and whether a person has reviewed it.',
    },
    {
      icon: 'layers',
      title: 'Alternatives, not a single answer',
      text: 'Compare layouts before choosing the one to refine.',
    },
    {
      icon: 'check-circle',
      title: 'You approve every change',
      text: 'AI suggestions are proposals; your plan changes only when you accept.',
    },
    {
      icon: 'clock',
      title: 'Every version kept',
      text: 'Revisions are saved, so you can look back or start again from an earlier plan.',
    },
  ];
  protected readonly faqs: readonly { q: string; a: string }[] = [
    {
      q: 'Is a BuildMind plan approved for construction?',
      a: 'No. BuildMind produces preliminary 2D plans for planning and discussion. Building approval must come from your local authority, and plans should be reviewed by a qualified professional.',
    },
    {
      q: 'What do I need to get started?',
      a: 'An account and, for the best results, the survey plan of your land as a PDF or image.',
    },
    {
      q: 'Will the AI change my plan without asking?',
      a: 'No. AI suggestions are shown as proposals with a preview. Your plan only changes when you accept a proposal.',
    },
    {
      q: 'Does BuildMind estimate construction costs?',
      a: 'No. BuildMind focuses on land, requirements and 2D planning. Cost estimation is not part of the service.',
    },
    {
      q: 'Is the 3D house on this page my design?',
      a: 'No. It is an illustrative model created for this page. BuildMind delivers your plan as an editable 2D drawing.',
    },
    {
      q: 'Who can see my plans?',
      a: 'Only you, unless you choose to share a specific plan and report with an architect. You can withdraw that consent at any time.',
    },
  ];
}
