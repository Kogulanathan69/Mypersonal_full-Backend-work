import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Badge, BadgeTone } from './badge';

/**
 * Lifecycle statuses shown in lists. Labels are UI wording only; the backend status enum
 * is BACKEND CONTRACT REQUIRED and will be mapped onto these in each module's data layer.
 */
export type UiStatus =
  | 'draft'
  | 'in-progress'
  | 'completed'
  | 'queued'
  | 'running'
  | 'failed'
  | 'blocked'
  | 'confirmed'
  | 'unknown';

const STATUS: Record<UiStatus, { label: string; tone: BadgeTone }> = {
  draft: { label: 'Draft', tone: 'neutral' },
  'in-progress': { label: 'In progress', tone: 'info' },
  completed: { label: 'Completed', tone: 'success' },
  queued: { label: 'Queued', tone: 'neutral' },
  running: { label: 'Running', tone: 'info' },
  failed: { label: 'Failed', tone: 'danger' },
  blocked: { label: 'Blocked', tone: 'warning' },
  confirmed: { label: 'Confirmed', tone: 'success' },
  unknown: { label: 'Unknown', tone: 'neutral' },
};

@Component({
  selector: 'bm-status-chip',
  imports: [Badge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<bm-badge [tone]="meta().tone" [dot]="true">{{ meta().label }}</bm-badge>`,
})
export class StatusChip {
  readonly status = input.required<UiStatus>();
  protected readonly meta = computed(() => STATUS[this.status()]);
}
