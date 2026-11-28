import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Icon } from '../../shared/ui/icon/icon';
import { StepItem, Stepper } from '../../shared/ui/stepper/stepper';

/**
 * Page frame for multi-step flows (requirements wizard, generation, editor hand-offs), used
 * inside the customer layout. Matches the reference: back arrow, title, subtitle, optional
 * assistant card, stepper, content, and an action bar (Back / Save draft / Next) that sticks to
 * the bottom of the screen on small devices. Owner: F — Foundation.
 */
@Component({
  selector: 'bm-wizard-layout',
  imports: [RouterLink, Icon, Stepper],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-6' },
  template: `
    <div class="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
      <div class="flex items-start gap-4">
        @if (backLink()) {
          <a
            [routerLink]="backLink()"
            class="mt-1 flex size-11 shrink-0 items-center justify-center rounded-pill border border-border-strong text-text hover:border-accent hover:text-accent"
          >
            <bm-icon name="arrow-left" [size]="20" [label]="backLabel()" />
          </a>
        }
        <div>
          <h1 class="text-2xl text-text sm:text-3xl">{{ heading() }}</h1>
          @if (subheading()) {
            <p class="mt-1 text-muted">{{ subheading() }}</p>
          }
        </div>
      </div>
      <ng-content select="[wizardAside]" />
    </div>

    @if (steps().length) {
      <bm-stepper [steps]="steps()" [label]="stepsLabel()" />
    }

    <div class="min-w-0"><ng-content /></div>

    <div
      class="sticky bottom-0 -mx-4 flex flex-wrap items-center justify-between gap-3 border-t border-border bg-bg/95 px-4 py-3 backdrop-blur sm:static sm:mx-0 sm:border-0 sm:bg-transparent sm:px-0 sm:py-0"
    >
      <ng-content select="[wizardActions]" />
    </div>
  `,
})
export class WizardLayout {
  readonly heading = input.required<string>();
  readonly subheading = input('');
  readonly steps = input<readonly StepItem[]>([]);
  readonly stepsLabel = input('Progress');
  readonly backLink = input<string | null>(null);
  readonly backLabel = input('Back');
}
