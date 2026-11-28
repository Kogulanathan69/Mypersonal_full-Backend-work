import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ICON_PATHS, IconName } from './icon-paths';

/**
 * Decorative by default (aria-hidden). Pass `label` when the icon is the only content of a
 * control, so assistive technology announces it.
 */
@Component({
  selector: 'bm-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'inline-flex shrink-0',
    '[style.width.px]': 'size()',
    '[style.height.px]': 'size()',
  },
  template: `
    <svg
      viewBox="0 0 24 24"
      [attr.width]="size()"
      [attr.height]="size()"
      fill="none"
      stroke="currentColor"
      stroke-width="1.75"
      stroke-linecap="round"
      stroke-linejoin="round"
      [attr.role]="label() ? 'img' : null"
      [attr.aria-label]="label() || null"
      [attr.aria-hidden]="label() ? null : 'true'"
      focusable="false"
    >
      <path [attr.d]="path()" />
    </svg>
  `,
})
export class Icon {
  readonly name = input.required<IconName>();
  readonly size = input(20);
  readonly label = input<string>('');
  protected readonly path = computed(() => ICON_PATHS[this.name()]);
}
