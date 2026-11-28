import {
  ChangeDetectionStrategy,
  Component,
  computed,
  forwardRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { passwordStrength } from '../../forms/validators';
import { FORM_FIELD } from '../form-field/form-field';
import { INPUT_CLASSES } from '../form-field/input.directive';
import { Icon } from '../icon/icon';

const STRENGTH_LABEL = ['', 'Weak', 'Fair', 'Good', 'Strong'] as const;

/** Password field with show/hide toggle and an optional strength hint. Works with reactive forms. */
@Component({
  selector: 'bm-password-input',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => PasswordInput), multi: true },
  ],
  host: { class: 'block' },
  template: `
    <div class="relative">
      <input
        [type]="visible() ? 'text' : 'password'"
        [id]="field?.inputId() ?? null"
        [attr.aria-describedby]="describedBy()"
        [attr.aria-invalid]="field?.invalid() ? 'true' : null"
        [attr.aria-required]="field?.required() ? 'true' : null"
        [attr.autocomplete]="autocomplete()"
        [value]="value()"
        [disabled]="disabled()"
        (input)="onInput($any($event.target).value)"
        (blur)="onTouched()"
        spellcheck="false"
        autocapitalize="off"
        [class]="inputClasses()"
      />
      <button
        type="button"
        class="absolute inset-y-0 right-1 my-auto flex size-9 items-center justify-center rounded-md text-muted hover:text-text"
        [attr.aria-pressed]="visible()"
        (click)="visible.set(!visible())"
      >
        <bm-icon
          [name]="visible() ? 'eye-off' : 'eye'"
          [size]="18"
          [label]="visible() ? 'Hide password' : 'Show password'"
        />
      </button>
    </div>
    @if (showStrength() && value()) {
      <div class="mt-2 flex items-center gap-2" [id]="strengthId">
        <div class="flex flex-1 gap-1" aria-hidden="true">
          @for (i of [1, 2, 3, 4]; track i) {
            <span
              class="h-1 flex-1 rounded-pill"
              [class]="i <= strength() ? strengthTone() : 'bg-surface-raised'"
            ></span>
          }
        </div>
        <span class="text-xs text-muted">Strength: {{ strengthLabel() }}</span>
      </div>
    }
  `,
})
export class PasswordInput implements ControlValueAccessor {
  readonly autocomplete = input<'current-password' | 'new-password'>('current-password');
  readonly showStrength = input(false);

  protected readonly field = inject(FORM_FIELD, { optional: true });
  protected readonly visible = signal(false);
  protected readonly value = signal('');
  protected readonly disabled = signal(false);
  protected readonly strengthId = `bm-pw-strength-${Math.random().toString(36).slice(2, 8)}`;

  protected readonly strength = computed(() => passwordStrength(this.value()));
  protected readonly strengthLabel = computed(() => STRENGTH_LABEL[this.strength()]);
  protected readonly strengthTone = computed(() =>
    this.strength() <= 1 ? 'bg-danger' : this.strength() === 2 ? 'bg-warning' : 'bg-success',
  );
  protected readonly describedBy = computed(() => {
    const ids = [
      this.field?.describedBy() ?? '',
      this.showStrength() && this.value() ? this.strengthId : '',
    ].filter(Boolean);
    return ids.length ? ids.join(' ') : null;
  });
  protected readonly inputClasses = computed(
    () =>
      `${INPUT_CLASSES} pr-12 ${this.field?.invalid() ? 'border-danger' : 'border-border-strong'}`,
  );

  private onChange: (v: string) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  writeValue(value: string | null): void {
    this.value.set(value ?? '');
  }
  registerOnChange(fn: (v: string) => void): void {
    this.onChange = fn;
  }
  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }
  setDisabledState(disabled: boolean): void {
    this.disabled.set(disabled);
  }

  protected onInput(value: string): void {
    this.value.set(value);
    this.onChange(value);
  }
}
