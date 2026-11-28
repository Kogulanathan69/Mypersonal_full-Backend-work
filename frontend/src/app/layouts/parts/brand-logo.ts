import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

/** BuildMind wordmark (original line mark + text). Links home; the text is the accessible name. */
@Component({
  selector: 'bm-brand-logo',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a [routerLink]="link()" class="flex items-center gap-2.5 text-text no-underline">
      <svg
        width="34"
        height="34"
        viewBox="0 0 34 34"
        fill="none"
        aria-hidden="true"
        class="shrink-0 text-accent"
      >
        <path
          d="M4 15.5 17 5l13 10.5"
          stroke="currentColor"
          stroke-width="2.4"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
        <path d="M8 13v15h18V13" stroke="currentColor" stroke-width="2.4" stroke-linejoin="round" />
        <path d="M14 28v-7h6v7" stroke="currentColor" stroke-width="2" stroke-linejoin="round" />
      </svg>
      <span class="flex flex-col leading-none">
        <span class="text-lg font-bold tracking-tight"
          >Build<span class="text-accent">Mind</span></span
        >
        @if (showTagline()) {
          <span class="mt-1 text-[0.65rem] font-medium tracking-wide text-muted"
            >AI Home Planning for Sri Lanka</span
          >
        }
      </span>
    </a>
  `,
})
export class BrandLogo {
  readonly link = input('/');
  readonly showTagline = input(true);
}
