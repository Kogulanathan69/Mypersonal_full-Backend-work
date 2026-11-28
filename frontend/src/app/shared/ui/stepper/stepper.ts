import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Icon } from '../icon/icon';

export type StepState = 'complete' | 'current' | 'upcoming' | 'blocked';

export interface StepItem {
  readonly id: string;
  readonly label: string;
  readonly state: StepState;
  /** Only completed steps may link back. Mandatory steps cannot be skipped forward. */
  readonly link?: string;
}

/**
 * Wizard progress. Full horizontal list from 768px; a compact "Step 3 of 9" summary below that.
 * The state for each step must come from the backend project stage, not from the browser.
 */
@Component({
  selector: 'bm-stepper',
  imports: [RouterLink, NgTemplateOutlet, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav [attr.aria-label]="label()">
      <p class="text-sm text-muted md:hidden">
        <span class="font-semibold text-accent"
          >Step {{ currentIndex() + 1 }} of {{ steps().length }}</span
        >
        @if (current(); as c) {
          <span> · {{ c.label }}</span>
        }
      </p>
      <ol class="hidden items-center gap-2 md:flex">
        @for (step of steps(); track step.id; let i = $index; let last = $last) {
          <li class="flex min-w-0 flex-1 items-center gap-2">
            @if (step.state === 'complete' && step.link) {
              <a
                [routerLink]="step.link"
                class="flex min-w-0 items-center gap-2 rounded-pill no-underline"
              >
                <ng-container
                  *ngTemplateOutlet="stepContent; context: { $implicit: step, index: i }"
                />
              </a>
            } @else {
              <span
                class="flex min-w-0 items-center gap-2"
                [attr.aria-current]="step.state === 'current' ? 'step' : null"
              >
                <ng-container
                  *ngTemplateOutlet="stepContent; context: { $implicit: step, index: i }"
                />
              </span>
            }
            @if (!last) {
              <span class="h-px min-w-4 flex-1 bg-border" aria-hidden="true"></span>
            }
          </li>
        }
      </ol>
    </nav>

    <ng-template #stepContent let-step let-index="index">
      <span
        class="flex size-8 shrink-0 items-center justify-center rounded-pill border text-sm font-semibold"
        [class]="circleClass(step.state)"
      >
        @switch (step.state) {
          @case ('complete') {
            <bm-icon name="check" [size]="16" />
          }
          @case ('blocked') {
            <bm-icon name="lock" [size]="14" />
          }
          @default {
            {{ index + 1 }}
          }
        }
      </span>
      <span class="truncate text-sm" [class]="labelClass(step.state)">{{ step.label }}</span>
      <span class="sr-only">({{ stateText(step.state) }})</span>
    </ng-template>
  `,
})
export class Stepper {
  readonly steps = input.required<readonly StepItem[]>();
  readonly label = input('Progress');

  protected readonly currentIndex = computed(() =>
    Math.max(
      0,
      this.steps().findIndex((s) => s.state === 'current'),
    ),
  );
  protected readonly current = computed(() => this.steps()[this.currentIndex()]);

  protected circleClass(state: StepState): string {
    return {
      complete: 'border-success bg-success text-on-accent',
      current: 'border-accent bg-accent text-on-accent',
      upcoming: 'border-border-strong text-muted',
      blocked: 'border-border text-subtle',
    }[state];
  }

  protected labelClass(state: StepState): string {
    return state === 'current'
      ? 'font-semibold text-text'
      : state === 'complete'
        ? 'text-text'
        : 'text-muted';
  }

  protected stateText(state: StepState): string {
    return {
      complete: 'completed',
      current: 'current step',
      upcoming: 'not started',
      blocked: 'locked',
    }[state];
  }
}
