import { ChangeDetectionStrategy, Component } from '@angular/core';
import { StaticHomeHero } from './static-home-hero';
import { ArchitectShowcase, ClosingSections } from './sections/closing-sections';
import {
  EditorShowcase,
  GenerationShowcase,
  RegulationShowcase,
} from './sections/planning-sections';
import { HowItWorks, LandShowcases } from './sections/workflow-sections';

/**
 * C01 — Public Home Page (owner: A — Customer Portal). Uses the scoped editorial marketing theme.
 * Sections: 1 static hero + planning journey, 3 how it works, 4–5 survey and land, 6 regulation evidence,
 * 7–8 generation and alternatives, 9–10 editor and AI editing, 11 architect review,
 * 12 benefits, 13 FAQ, 14 final CTA. 15 footer comes from the public layout.
 */
@Component({
  selector: 'bm-home-page',
  imports: [
    StaticHomeHero,
    HowItWorks,
    LandShowcases,
    RegulationShowcase,
    GenerationShowcase,
    EditorShowcase,
    ArchitectShowcase,
    ClosingSections,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block bg-mk-navy text-mk-white' },
  template: `
    <bm-static-home-hero />
    <div class="mk-editorial" id="planning-experience">
      <bm-how-it-works />
      <bm-land-showcases />
      <bm-regulation-showcase />
      <bm-generation-showcase />
      <bm-editor-showcase />
      <bm-architect-showcase />
      <bm-closing-sections />
    </div>
  `,
})
export class HomePage {}
