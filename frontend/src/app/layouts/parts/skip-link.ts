import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** First focusable element on every page: jumps keyboard users past the navigation. */
@Component({
  selector: 'bm-skip-link',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a
      [href]="'#' + target()"
      class="sr-only z-50 rounded-md bg-accent px-4 py-2 font-medium text-on-accent focus:not-sr-only focus:fixed focus:top-3 focus:left-3"
      (click)="focusTarget($event)"
    >
      Skip to main content
    </a>
  `,
})
export class SkipLink {
  readonly target = input('main-content');

  protected focusTarget(event: Event): void {
    const el = document.getElementById(this.target());
    if (!el) return;
    event.preventDefault();
    el.focus();
  }
}
