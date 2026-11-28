import {
  ChangeDetectionStrategy,
  Component,
  InjectionToken,
  Signal,
  computed,
  input,
} from '@angular/core';
import { AbstractControl } from '@angular/forms';
import { controlView } from '../../forms/control-state';

export interface FormFieldContext {
  readonly inputId: Signal<string>;
  readonly describedBy: Signal<string | null>;
  readonly invalid: Signal<boolean>;
  readonly required: Signal<boolean>;
}

export const FORM_FIELD = new InjectionToken<FormFieldContext>('FORM_FIELD');

let nextId = 0;

/**
 * Label + control + hint + error, wired for screen readers.
 * Put `bmInput` on the native input/select/textarea inside, or use a component that reads FORM_FIELD.
 */
@Component({
  selector: 'bm-form-field',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: FORM_FIELD, useExisting: FormField }],
  host: { class: 'block' },
  template: `
    <label [for]="inputId()" class="mb-1.5 block text-sm font-medium text-text">
      {{ label() }}
      @if (required()) {
        <span class="text-accent" aria-hidden="true">*</span>
      } @else if (showOptional()) {
        <span class="font-normal text-subtle">(optional)</span>
      }
    </label>
    <ng-content />
    @if (hint()) {
      <p [id]="hintId" class="mt-1.5 text-xs text-subtle">{{ hint() }}</p>
    }
    <p
      [id]="errorId"
      class="mt-1.5 flex items-start gap-1.5 text-sm text-danger"
      [hidden]="!view.message()"
    >
      <span aria-hidden="true">⚠</span><span>{{ view.message() }}</span>
    </p>
  `,
})
export class FormField implements FormFieldContext {
  readonly label = input.required<string>();
  readonly control = input<AbstractControl | null>(null);
  readonly hint = input('');
  readonly required = input(false);
  readonly showOptional = input(true);
  readonly fieldId = input('');

  private readonly autoId = `bm-field-${nextId++}`;
  protected readonly hintId = `${this.autoId}-hint`;
  protected readonly errorId = `${this.autoId}-error`;

  protected readonly view = controlView(this.control, this.label);

  readonly inputId = computed(() => this.fieldId() || `${this.autoId}-input`);
  readonly invalid = this.view.invalid;
  readonly describedBy = computed(() => {
    const ids = [this.hint() ? this.hintId : '', this.view.message() ? this.errorId : ''].filter(
      Boolean,
    );
    return ids.length ? ids.join(' ') : null;
  });
}
