import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  input,
} from '@angular/core';
import { Spinner } from '../spinner/spinner';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger';
export type ButtonSize = 'sm' | 'md' | 'lg';

const BASE =
  'inline-flex items-center justify-center gap-2 rounded-pill font-medium transition-colors duration-[var(--duration-fast)] ' +
  'disabled:cursor-not-allowed disabled:opacity-50 aria-disabled:cursor-not-allowed aria-disabled:opacity-50 select-none';

const VARIANTS: Record<ButtonVariant, string> = {
  primary: 'bg-accent text-on-accent hover:bg-accent-hover shadow-glow',
  secondary:
    'border border-border-strong text-text hover:border-accent hover:text-accent bg-transparent',
  ghost: 'text-muted hover:text-text hover:bg-surface-raised bg-transparent',
  danger: 'border border-danger text-danger hover:bg-danger hover:text-on-accent bg-transparent',
};

const SIZES: Record<ButtonSize, string> = {
  sm: 'h-9 px-4 text-sm',
  md: 'h-11 px-5 text-[0.95rem]',
  lg: 'h-13 px-7 text-base',
};

/**
 * Use on native elements: `<button bmButton>` or `<a bmButton routerLink=…>`.
 * Minimum 44px touch target at md/lg. While `loading`, the button is disabled and
 * reports aria-busy so a second click cannot submit twice.
 */
@Component({
  // eslint-disable-next-line @angular-eslint/component-selector
  selector: 'button[bmButton], a[bmButton]',
  imports: [Spinner],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[class]': 'classes()',
    '[attr.aria-busy]': 'loading() || null',
    '[attr.disabled]': 'isButton && (disabled() || loading()) ? "" : null',
    '[attr.aria-disabled]': '!isButton && (disabled() || loading()) ? "true" : null',
    '[attr.tabindex]': '!isButton && (disabled() || loading()) ? -1 : null',
  },
  template: `
    @if (loading()) {
      <bm-spinner [size]="16" />
    }
    <ng-content />
  `,
})
export class Button {
  readonly variant = input<ButtonVariant>('primary');
  readonly size = input<ButtonSize>('md');
  readonly loading = input(false);
  readonly disabled = input(false);
  readonly block = input(false);

  protected readonly isButton = inject(ElementRef<HTMLElement>).nativeElement.tagName === 'BUTTON';

  protected readonly classes = computed(() =>
    [BASE, VARIANTS[this.variant()], SIZES[this.size()], this.block() ? 'w-full' : ''].join(' '),
  );
}
