import { Directive, inject } from '@angular/core';
import { FORM_FIELD } from './form-field';

export const INPUT_CLASSES =
  'w-full rounded-md border bg-surface-raised px-3.5 py-2.5 text-text placeholder:text-subtle ' +
  'transition-colors focus:border-accent focus:outline-none focus-visible:outline-2 focus-visible:outline-focus ' +
  'disabled:cursor-not-allowed disabled:opacity-60 min-h-11';

/** Styles a native input and connects it to the surrounding bm-form-field (id, hint, error). */
@Directive({
  selector: 'input[bmInput], select[bmInput], textarea[bmInput]',
  host: {
    '[id]': 'field?.inputId() ?? null',
    '[attr.aria-describedby]': 'field?.describedBy() ?? null',
    '[attr.aria-invalid]': 'field?.invalid() ? "true" : null',
    '[attr.aria-required]': 'field?.required() ? "true" : null',
    '[class]': 'classes()',
  },
})
export class BmInput {
  protected readonly field = inject(FORM_FIELD, { optional: true });
  protected classes(): string {
    return `${INPUT_CLASSES} ${this.field?.invalid() ? 'border-danger' : 'border-border-strong'}`;
  }
}
