import { ChangeDetectionStrategy, Component } from '@angular/core';
import { WizardLayout } from '../../layouts/wizard-layout/wizard-layout';
import { Button } from '../../shared/ui/button/button';
import { Card } from '../../shared/ui/card/card';
import { Icon } from '../../shared/ui/icon/icon';
import { Skeleton } from '../../shared/ui/skeleton/skeleton';
import { StepItem } from '../../shared/ui/stepper/stepper';

/** DEV ONLY — wizard frame inside the customer layout, with placeholder content. */
@Component({
  selector: 'bm-wizard-preview',
  imports: [WizardLayout, Button, Card, Icon, Skeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <bm-wizard-layout
      heading="Create your home plan"
      subheading="Wizard frame preview: header, stepper, content and action bar."
      backLink="/dev/layouts"
      backLabel="Back to layout previews"
      [steps]="steps"
      stepsLabel="Requirements progress"
    >
      <div
        wizardAside
        class="flex items-center gap-3 rounded-lg border border-accent/60 bg-surface px-4 py-3"
      >
        <bm-icon name="sparkles" [size]="22" class="text-accent" />
        <div>
          <p class="text-sm font-semibold text-text">Assistant panel slot</p>
          <p class="text-xs text-muted">Reserved for module A</p>
        </div>
      </div>

      <div class="grid gap-4 lg:grid-cols-2">
        <bm-card heading="Left panel" [level]="2"
          ><bm-skeleton [lines]="6" height="2.25rem"
        /></bm-card>
        <bm-card heading="Right panel" [level]="2"><bm-skeleton [lines]="6" /></bm-card>
      </div>

      <ng-container wizardActions>
        <button bmButton variant="secondary" type="button">
          <bm-icon name="arrow-left" [size]="18" /> Back
        </button>
        <div class="flex gap-3">
          <button bmButton variant="secondary" type="button">
            <bm-icon name="save" [size]="18" /> Save draft
          </button>
          <button bmButton type="button">Next <bm-icon name="arrow-right" [size]="18" /></button>
        </div>
      </ng-container>
    </bm-wizard-layout>
  `,
})
export class WizardPreview {
  protected readonly steps: StepItem[] = [
    {
      id: 'rooms',
      label: 'Ground floor rooms',
      state: 'complete',
      link: '/dev/layouts/customer/wizard',
    },
    { id: 'spaces', label: 'Additional spaces', state: 'complete' },
    { id: 'summary', label: 'Relationships & summary', state: 'current' },
    { id: 'method', label: 'Design method', state: 'upcoming' },
  ];
}
