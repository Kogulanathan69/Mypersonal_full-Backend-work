import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Eyebrow + heading + intro used by every Home Page section. */
@Component({
  selector: 'bm-section-heading',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block max-w-3xl' },
  template: `
    <p class="text-sm font-semibold tracking-[0.18em] text-mk-cyan uppercase">{{ eyebrow() }}</p>
    <h2 [id]="headingId()" class="mt-3 text-3xl font-semibold text-mk-white sm:text-4xl">
      {{ heading() }}
    </h2>
    @if (intro()) {
      <p class="mt-4 text-mk-muted sm:text-lg">{{ intro() }}</p>
    }
  `,
})
export class SectionHeading {
  readonly eyebrow = input.required<string>();
  readonly heading = input.required<string>();
  readonly intro = input('');
  readonly headingId = input.required<string>();
}

/** Small label that marks example content as illustrative. */
@Component({
  selector: 'bm-illustrative-tag',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span
    class="inline-flex items-center rounded-pill border border-mk-gold/60 px-2.5 py-0.5 text-xs font-medium text-mk-gold"
    >Illustrative example</span
  >`,
})
export class IllustrativeTag {}
