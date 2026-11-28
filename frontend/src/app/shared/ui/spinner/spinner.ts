import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Indeterminate spinner. Decorative unless `label` is set (then it is announced as status). */
@Component({
  selector: 'bm-spinner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'inline-flex',
    '[attr.role]': "label() ? 'status' : null",
    '[attr.aria-label]': 'label() || null',
    '[attr.aria-hidden]': "label() ? null : 'true'",
  },
  template: `
    <svg
      [attr.width]="size()"
      [attr.height]="size()"
      viewBox="0 0 24 24"
      class="animate-spin"
      fill="none"
    >
      <circle cx="12" cy="12" r="9" stroke="currentColor" stroke-opacity="0.25" stroke-width="3" />
      <path
        d="M21 12a9 9 0 0 0-9-9"
        stroke="currentColor"
        stroke-width="3"
        stroke-linecap="round"
      />
    </svg>
  `,
})
export class Spinner {
  readonly size = input(18);
  readonly label = input('');
}
