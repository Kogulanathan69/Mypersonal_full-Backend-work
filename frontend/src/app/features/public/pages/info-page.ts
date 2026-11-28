import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Frame for plain public information pages (About, Contact, Privacy, Terms). Owner: A. */
@Component({
  selector: 'bm-info-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <article class="mx-auto max-w-3xl px-5 py-16 sm:px-8 sm:py-24">
      <p class="text-sm font-semibold tracking-[0.18em] text-mk-ink-muted uppercase">{{ eyebrow() }}</p>
      <h1 class="mt-3 text-4xl font-semibold text-mk-ink sm:text-5xl">{{ heading() }}</h1>
      @if (lead()) {
        <p class="mt-5 text-lg text-mk-ink-muted">{{ lead() }}</p>
      }
      <div
        class="mt-10 space-y-8 text-mk-ink-muted [&_a]:font-medium [&_a]:text-mk-ink [&_a]:underline [&_a]:decoration-mk-champagne [&_a]:underline-offset-4 [&_h2]:text-2xl [&_h2]:font-semibold [&_h2]:text-mk-ink [&_li]:mt-1.5 [&_ul]:list-disc [&_ul]:pl-5"
      >
        <ng-content />
      </div>
    </article>
  `,
})
export class InfoPage {
  readonly eyebrow = input.required<string>();
  readonly heading = input.required<string>();
  readonly lead = input('');
}

/** Notice for policy pages whose final text has not been approved. */
@Component({
  selector: 'bm-draft-notice',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      role="note"
      class="rounded-lg border border-mk-gold/60 bg-mk-surface-2 p-4 text-sm text-mk-white"
    >
      <strong class="text-mk-gold">Draft — pending legal review.</strong>
      This page summarises how BuildMind is designed to work. It is not yet the binding
      {{ document() }}, which will be published here after review.
    </div>
  `,
})
export class DraftNotice {
  readonly document = input.required<string>();
}
