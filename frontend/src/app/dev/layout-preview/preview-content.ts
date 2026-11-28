import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { Card } from '../../shared/ui/card/card';
import { Skeleton } from '../../shared/ui/skeleton/skeleton';

/** DEV ONLY — neutral placeholder content shown inside a layout preview. */
@Component({
  selector: 'bm-preview-content',
  imports: [Card, Skeleton, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="flex flex-col gap-6"
      [class]="padded() ? 'mx-auto max-w-7xl px-4 py-10 sm:px-6' : ''"
    >
      <div>
        <p class="text-sm font-medium tracking-wide text-accent uppercase">Layout preview</p>
        <h1 class="mt-1 text-2xl text-text sm:text-3xl">{{ name() }} layout</h1>
        <p class="mt-2 max-w-2xl text-muted">
          Placeholder content. Screens are added by the owning module in later milestones.
        </p>
        <p class="mt-2"><a routerLink="/dev/layouts">← All layouts</a></p>
      </div>
      <div class="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        @for (i of [1, 2, 3]; track i) {
          <bm-card [heading]="'Content area ' + i" [level]="2">
            <bm-skeleton [lines]="3" />
          </bm-card>
        }
      </div>
    </div>
  `,
})
export class PreviewContent {
  private readonly data = toSignal(
    inject(ActivatedRoute).data.pipe(map((d) => d as { name?: string; padded?: boolean })),
  );
  protected readonly name = () => this.data()?.name ?? 'Preview';
  protected readonly padded = () => this.data()?.padded ?? false;
}
