import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { AbstractControl } from '@angular/forms';
import { controlView } from '../../forms/control-state';

/** Standalone error line, e.g. for a FormGroup-level error such as "passwords do not match". */
@Component({
  selector: 'bm-field-error',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p
      [id]="errorId()"
      class="mt-1.5 flex items-start gap-1.5 text-sm text-danger"
      [hidden]="!view.message()"
    >
      <span aria-hidden="true">⚠</span><span>{{ view.message() }}</span>
    </p>
  `,
})
export class FieldError {
  readonly control = input.required<AbstractControl | null>();
  readonly label = input('This field');
  readonly errorId = input.required<string>();
  protected readonly view = controlView(this.control, this.label);
}
