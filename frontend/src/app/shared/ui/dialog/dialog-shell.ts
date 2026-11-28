import { ChangeDetectionStrategy, Component, input } from '@angular/core';

let nextId = 0;

/**
 * Standard dialog frame used inside CDK dialogs: title, body and an actions row.
 * CDK Dialog provides the focus trap, Escape to close, aria-modal and focus restore.
 * Pass `[titleId]` to the dialog config's ariaLabelledBy.
 */
@Component({
  selector: 'bm-dialog-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'block w-[min(92vw,32rem)] rounded-xl border border-border bg-surface p-6 shadow-card',
  },
  template: `
    <h2 [id]="titleId()" class="text-xl text-text">{{ heading() }}</h2>
    <div class="mt-3 text-sm text-muted"><ng-content /></div>
    <div class="mt-6 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
      <ng-content select="[dialogActions]" />
    </div>
  `,
})
export class DialogShell {
  readonly heading = input.required<string>();
  readonly titleId = input(`bm-dialog-title-${nextId++}`);
}
