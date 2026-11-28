import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { timer } from 'rxjs';
import { AppError } from '../../core/api/app-error';
import { USER_MESSAGES } from '../../core/api/user-messages';
import { SessionEventsService } from '../../core/auth/session-events.service';
import { LoadingService } from '../../core/feedback/loading.service';
import { ToastService } from '../../core/feedback/toast.service';
import { applyServerErrors } from '../../shared/forms/form-errors';
import { createSubmitState } from '../../shared/forms/submit-state';
import {
  fieldsMatch,
  notBlank,
  passwordPolicy,
  sriLankaPhone,
} from '../../shared/forms/validators';
import { AreaPipe, LengthPipe } from '../../shared/pipes/units.pipes';
import { Badge } from '../../shared/ui/badge/badge';
import { StatusChip, UiStatus } from '../../shared/ui/badge/status-chip';
import { Breadcrumb } from '../../shared/ui/breadcrumb/breadcrumb';
import { Button } from '../../shared/ui/button/button';
import { Card } from '../../shared/ui/card/card';
import { BmCell, DataTable, SortState, TableColumn } from '../../shared/ui/data-table/data-table';
import { ConfirmService } from '../../shared/ui/dialog/confirm.service';
import { Drawer } from '../../shared/ui/drawer/drawer';
import { FileDrop, RejectedFile } from '../../shared/ui/file-drop/file-drop';
import { FieldError } from '../../shared/ui/form-field/field-error';
import { FormField } from '../../shared/ui/form-field/form-field';
import { BmInput } from '../../shared/ui/form-field/input.directive';
import { Icon } from '../../shared/ui/icon/icon';
import { ICON_PATHS, IconName } from '../../shared/ui/icon/icon-paths';
import { Paginator } from '../../shared/ui/paginator/paginator';
import { PasswordInput } from '../../shared/ui/password-input/password-input';
import { Skeleton } from '../../shared/ui/skeleton/skeleton';
import { ConfigMissing } from '../../shared/ui/states/config-missing';
import { EmptyState } from '../../shared/ui/states/empty-state';
import { ErrorState } from '../../shared/ui/states/error-state';
import { StepItem, Stepper } from '../../shared/ui/stepper/stepper';
import { TabItem, Tabs } from '../../shared/ui/tabs/tabs';

interface SampleRow {
  readonly id: string;
  readonly name: string;
  readonly status: UiStatus;
  readonly updated: string;
}

/**
 * DEV ONLY — component gallery for visual review against the reference screens.
 * Not shipped in production. All rows and texts are component-review placeholders, not data.
 */
@Component({
  selector: 'bm-ui-gallery',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    Badge,
    StatusChip,
    Breadcrumb,
    Button,
    Card,
    DataTable,
    BmCell,
    Drawer,
    FileDrop,
    FieldError,
    FormField,
    BmInput,
    Icon,
    Paginator,
    PasswordInput,
    Skeleton,
    ConfigMissing,
    EmptyState,
    ErrorState,
    Stepper,
    Tabs,
    LengthPipe,
    AreaPipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto flex max-w-6xl flex-col gap-10 px-4 py-8 sm:px-6">
      <header>
        <bm-breadcrumb [items]="[{ label: 'Dev', link: '/dev' }, { label: 'UI gallery' }]" />
        <h1 class="mt-3 text-3xl text-text">BuildMind UI gallery</h1>
        <div class="mt-3 gold-rule w-24" aria-hidden="true"></div>
        <p class="mt-3 max-w-3xl text-muted">
          Development-only page for reviewing Phase 01 components against the reference screens.
          Everything here is a placeholder for component review — no BuildMind data.
          <a routerLink="/dev/layouts">Preview layouts →</a>
        </p>
      </header>

      <section aria-labelledby="g-colours" class="flex flex-col gap-4">
        <h2 id="g-colours" class="text-xl">Colour roles</h2>
        <ul class="grid grid-cols-2 gap-3 sm:grid-cols-4 lg:grid-cols-6">
          @for (s of swatches; track s.name) {
            <li class="overflow-hidden rounded-lg border border-border">
              <div class="h-14" [class]="s.cls"></div>
              <p class="bg-surface px-3 py-2 text-xs text-muted">{{ s.name }}</p>
            </li>
          }
        </ul>
      </section>

      <section aria-labelledby="g-type" class="flex flex-col gap-2">
        <h2 id="g-type" class="text-xl">Typography (Poppins, provisional)</h2>
        <p class="text-4xl font-semibold">
          Plan your dream home <span class="text-accent">with BuildMind</span>
        </p>
        <p class="text-2xl font-semibold">Section heading</p>
        <p class="text-base text-text">Body text in ivory on navy for long reading.</p>
        <p class="text-sm text-muted">Secondary text for descriptions and helper copy.</p>
        <p class="text-xs text-subtle">Caption text for hints and metadata.</p>
      </section>

      <section aria-labelledby="g-buttons" class="flex flex-col gap-4">
        <h2 id="g-buttons" class="text-xl">Buttons</h2>
        <div class="flex flex-wrap items-center gap-3">
          <button bmButton type="button">Primary</button>
          <button bmButton variant="secondary" type="button">Secondary</button>
          <button bmButton variant="ghost" type="button">Ghost</button>
          <button bmButton variant="danger" type="button">Delete</button>
          <button bmButton type="button" [loading]="true">Saving</button>
          <button bmButton type="button" [disabled]="true">Disabled</button>
        </div>
        <div class="flex flex-wrap items-center gap-3">
          <button bmButton size="sm" type="button">Small</button>
          <button bmButton size="md" type="button">Medium</button>
          <button bmButton size="lg" type="button">
            <bm-icon name="sparkles" [size]="20" /> Generate 3 AI plans
          </button>
          <a bmButton variant="secondary" routerLink="/dev/layouts">Link styled as button</a>
        </div>
      </section>

      <section aria-labelledby="g-badges" class="flex flex-col gap-4">
        <h2 id="g-badges" class="text-xl">Badges and status chips</h2>
        <div class="flex flex-wrap gap-2">
          <bm-badge>Neutral</bm-badge>
          <bm-badge tone="accent">Accent</bm-badge>
          <bm-badge tone="success">Success</bm-badge>
          <bm-badge tone="warning">Warning</bm-badge>
          <bm-badge tone="danger">Danger</bm-badge>
          <bm-badge tone="info">Info</bm-badge>
        </div>
        <div class="flex flex-wrap gap-2">
          @for (s of statuses; track s) {
            <bm-status-chip [status]="s" />
          }
        </div>
      </section>

      <section aria-labelledby="g-cards" class="flex flex-col gap-4">
        <h2 id="g-cards" class="text-xl">Cards</h2>
        <div class="grid gap-4 md:grid-cols-3">
          <bm-card heading="Default card" subheading="Surface with border and shadow">
            <p class="text-sm text-muted">Card content.</p>
          </bm-card>
          <bm-card
            heading="Selected card"
            subheading="Gold outline, as in the references"
            variant="highlight"
          >
            <p class="text-sm text-muted">Used for the chosen option.</p>
          </bm-card>
          <bm-card heading="Flat card" variant="flat">
            <button bmButton variant="ghost" size="sm" type="button" cardActions>
              <bm-icon name="edit" [size]="16" /> Edit
            </button>
            <p class="text-sm text-muted">Inner rows and summaries.</p>
          </bm-card>
        </div>
      </section>

      <section aria-labelledby="g-nav" class="flex flex-col gap-6">
        <h2 id="g-nav" class="text-xl">Stepper and tabs</h2>
        <bm-stepper [steps]="steps" label="Sample progress" />
        <div>
          <bm-tabs [tabs]="tabs" label="Sample validation tabs" idPrefix="g" [(selected)]="tab" />
          <div
            role="tabpanel"
            [id]="'g-panel-' + tab()"
            [attr.aria-labelledby]="'g-tab-' + tab()"
            class="py-4 text-sm text-muted"
          >
            Panel for “{{ tab() }}”. Use the arrow keys to move between tabs.
          </div>
        </div>
      </section>

      <section aria-labelledby="g-forms" class="flex flex-col gap-4">
        <h2 id="g-forms" class="text-xl">Forms</h2>
        <bm-card>
          <form
            [formGroup]="form"
            (ngSubmit)="submit()"
            novalidate
            class="grid gap-5 md:grid-cols-2"
          >
            <bm-form-field label="Full name" [control]="form.controls.name" [required]="true">
              <input bmInput formControlName="name" autocomplete="name" />
            </bm-form-field>
            <bm-form-field
              label="Email"
              [control]="form.controls.email"
              [required]="true"
              hint="We never share your email."
            >
              <input bmInput type="email" formControlName="email" autocomplete="email" />
            </bm-form-field>
            <bm-form-field
              label="Mobile number"
              [control]="form.controls.phone"
              hint="Sri Lankan number, e.g. 071 234 5678"
            >
              <input bmInput type="tel" formControlName="phone" autocomplete="tel" />
            </bm-form-field>
            <bm-form-field label="District" [control]="form.controls.district" [required]="true">
              <select bmInput formControlName="district">
                <option value="">Choose a district</option>
                <option value="sample-1">Sample option 1</option>
                <option value="sample-2">Sample option 2</option>
              </select>
            </bm-form-field>
            <bm-form-field label="Password" [control]="form.controls.password" [required]="true">
              <bm-password-input
                formControlName="password"
                autocomplete="new-password"
                [showStrength]="true"
              />
            </bm-form-field>
            <div>
              <bm-form-field
                label="Confirm password"
                [control]="form.controls.confirm"
                [required]="true"
              >
                <bm-password-input formControlName="confirm" autocomplete="new-password" />
              </bm-form-field>
              <bm-field-error [control]="form" label="Passwords" errorId="g-match-error" />
            </div>
            <div class="flex flex-wrap gap-3 md:col-span-2">
              <button bmButton type="submit" [loading]="submitState.pending()">
                Submit (demo)
              </button>
              <button bmButton variant="secondary" type="button" (click)="simulateServerErrors()">
                Simulate server field error
              </button>
              <button bmButton variant="ghost" type="button" (click)="form.reset()">Reset</button>
            </div>
            @if (submitCount() > 0) {
              <p class="text-sm text-success md:col-span-2" role="status">
                Demo submit ran {{ submitCount() }} time(s). Rapid clicks while saving are ignored.
              </p>
            }
          </form>
        </bm-card>
      </section>

      <section aria-labelledby="g-upload" class="flex flex-col gap-4">
        <h2 id="g-upload" class="text-xl">File drop</h2>
        <bm-file-drop
          [accept]="['.pdf', '.jpg', '.jpeg', '.png']"
          [maxBytes]="20 * 1024 * 1024"
          buttonLabel="Choose survey plan"
          (filesAccepted)="onAccepted($event)"
          (filesRejected)="onRejected($event)"
        />
        @if (acceptedNames().length) {
          <p class="text-sm text-muted">
            Accepted locally (not uploaded): {{ acceptedNames().join(', ') }}
          </p>
        }
      </section>

      <section aria-labelledby="g-table" class="flex flex-col gap-4">
        <h2 id="g-table" class="text-xl">Data table and paginator</h2>
        <bm-data-table
          caption="Placeholder rows for component review"
          [columns]="columns"
          [rows]="rows"
          rowKey="id"
          [(sort)]="sort"
        >
          <ng-template bmCell="status" let-row
            ><bm-status-chip [status]="row.status"
          /></ng-template>
        </bm-data-table>
        <bm-paginator
          [page]="page()"
          [pageSize]="10"
          [total]="42"
          (pageChange)="page.set($event)"
        />
        <p class="text-xs text-subtle">
          Sort state: {{ sort()?.key ?? 'none' }} {{ sort()?.direction ?? '' }}
        </p>
      </section>

      <section aria-labelledby="g-states" class="flex flex-col gap-4">
        <h2 id="g-states" class="text-xl">Loading, empty, error and configuration states</h2>
        <div class="grid gap-4 md:grid-cols-2">
          <bm-card heading="Skeleton" [level]="3"
            ><div aria-busy="true"><bm-skeleton [lines]="4" /></div
          ></bm-card>
          <bm-empty-state
            title="No projects yet"
            message="Create your first project to start planning."
            icon="folder"
          >
            <button bmButton size="sm" type="button">
              <bm-icon name="plus" [size]="16" /> Create project
            </button>
          </bm-empty-state>
          <bm-error-state [error]="networkError" (retry)="toasts.info('Retry clicked')" />
          <bm-error-state [error]="forbiddenError" />
          <bm-config-missing
            feature="The land map"
            explanation="A map provider and public token must be configured before the map can load."
          />
          <bm-error-state [error]="conflictError" />
        </div>
      </section>

      <section aria-labelledby="g-feedback" class="flex flex-col gap-4">
        <h2 id="g-feedback" class="text-xl">Global feedback</h2>
        <div class="flex flex-wrap gap-3">
          <button
            bmButton
            variant="secondary"
            type="button"
            (click)="toasts.success('Draft saved', 'Your changes are stored.')"
          >
            Success toast
          </button>
          <button
            bmButton
            variant="secondary"
            type="button"
            (click)="toasts.warning('Check your boundary', 'Two sides are very short.')"
          >
            Warning toast
          </button>
          <button
            bmButton
            variant="secondary"
            type="button"
            (click)="toasts.error('Could not save', 'Reference: demo-123')"
          >
            Error toast
          </button>
          <button bmButton variant="secondary" type="button" (click)="showLoading()">
            Show loading bar (2 s)
          </button>
          <button bmButton variant="secondary" type="button" (click)="confirmDelete()">
            Confirm (danger)
          </button>
          <button bmButton variant="secondary" type="button" (click)="confirmReason()">
            Confirm with reason
          </button>
          <button bmButton variant="secondary" type="button" (click)="session.markExpired()">
            Session expired
          </button>
          <button bmButton variant="secondary" type="button" (click)="simulateOffline()">
            Offline (3 s)
          </button>
          <button bmButton variant="secondary" type="button" (click)="drawerOpen.set(true)">
            Open drawer
          </button>
        </div>
        <bm-drawer [(open)]="drawerOpen" label="Sample panel" side="right">
          <p class="text-sm text-muted">Drawers trap focus and close with Escape.</p>
        </bm-drawer>
      </section>

      <section aria-labelledby="g-units" class="flex flex-col gap-2">
        <h2 id="g-units" class="text-xl">Units</h2>
        <p class="text-sm text-muted">
          3900 mm = {{ 3900 | bmLength }} = {{ 3900 | bmLength: 'ft-in' }} · 316.8 m² =
          {{ 316.8 | bmArea: 'perch' }} =
          {{ 316.8 | bmArea: 'sqft' }}
        </p>
      </section>

      <section aria-labelledby="g-icons" class="flex flex-col gap-4">
        <h2 id="g-icons" class="text-xl">Icons ({{ icons.length }})</h2>
        <ul class="grid grid-cols-3 gap-3 sm:grid-cols-6 lg:grid-cols-8">
          @for (name of icons; track name) {
            <li
              class="flex flex-col items-center gap-2 rounded-md border border-border p-3 text-center"
            >
              <bm-icon [name]="name" [size]="22" class="text-accent" />
              <span class="text-[0.65rem] break-all text-subtle">{{ name }}</span>
            </li>
          }
        </ul>
      </section>
    </div>
  `,
})
export class UiGallery {
  protected readonly toasts = inject(ToastService);
  protected readonly session = inject(SessionEventsService);
  private readonly loading = inject(LoadingService);
  private readonly confirmService = inject(ConfirmService);
  private readonly fb = inject(FormBuilder).nonNullable;

  protected readonly swatches = [
    { name: 'bg', cls: 'bg-bg' },
    { name: 'bg-deep', cls: 'bg-bg-deep' },
    { name: 'surface', cls: 'bg-surface' },
    { name: 'surface-raised', cls: 'bg-surface-raised' },
    { name: 'border', cls: 'bg-border' },
    { name: 'border-strong', cls: 'bg-border-strong' },
    { name: 'accent', cls: 'bg-accent' },
    { name: 'accent-hover', cls: 'bg-accent-hover' },
    { name: 'text', cls: 'bg-text' },
    { name: 'muted', cls: 'bg-muted' },
    { name: 'success', cls: 'bg-success' },
    { name: 'warning', cls: 'bg-warning' },
    { name: 'danger', cls: 'bg-danger' },
    { name: 'info', cls: 'bg-info' },
    { name: 'paper', cls: 'bg-paper' },
    { name: 'ink', cls: 'bg-ink' },
  ];
  protected readonly statuses: UiStatus[] = [
    'draft',
    'in-progress',
    'completed',
    'queued',
    'running',
    'failed',
    'blocked',
    'confirmed',
  ];
  protected readonly steps: StepItem[] = [
    { id: '1', label: 'Ground floor', state: 'complete', link: '/dev/ui' },
    { id: '2', label: 'Additional spaces', state: 'complete' },
    { id: '3', label: 'Relationships & summary', state: 'current' },
    { id: '4', label: 'Generate plans', state: 'upcoming' },
  ];
  protected readonly tabs: TabItem[] = [
    { id: 'critical', label: 'Critical', count: 0 },
    { id: 'warnings', label: 'Warnings', count: 2 },
    { id: 'info', label: 'Info' },
    { id: 'unknown', label: 'Unverified' },
  ];
  protected readonly tab = signal('critical');

  protected readonly columns: TableColumn[] = [
    { key: 'id', header: 'ID', hideOnMobile: true },
    { key: 'name', header: 'Name', sortable: true },
    { key: 'status', header: 'Status' },
    { key: 'updated', header: 'Updated', sortable: true, align: 'end' },
  ];
  protected readonly rows: SampleRow[] = [
    { id: 'row-1', name: 'Placeholder row A', status: 'draft', updated: '—' },
    { id: 'row-2', name: 'Placeholder row B', status: 'in-progress', updated: '—' },
    { id: 'row-3', name: 'Placeholder row C', status: 'completed', updated: '—' },
  ];
  protected readonly sort = signal<SortState | null>(null);
  protected readonly page = signal(1);

  protected readonly networkError = new AppError({
    kind: 'network',
    message: USER_MESSAGES.network,
    correlationId: 'demo-ref-001',
  });
  protected readonly forbiddenError = new AppError({
    kind: 'forbidden',
    message: USER_MESSAGES.forbidden,
  });
  protected readonly conflictError = new AppError({
    kind: 'conflict',
    message: USER_MESSAGES.conflict,
  });

  protected readonly form = this.fb.group(
    {
      name: ['', [Validators.required, notBlank, Validators.maxLength(80)]],
      email: ['', [Validators.required, Validators.email]],
      phone: ['', [sriLankaPhone]],
      district: ['', [Validators.required]],
      password: ['', [Validators.required, passwordPolicy()]],
      confirm: ['', [Validators.required]],
    },
    { validators: [fieldsMatch('password', 'confirm')] },
  );
  protected readonly submitState = createSubmitState();
  protected readonly submitCount = signal(0);
  protected readonly acceptedNames = signal<string[]>([]);
  protected readonly drawerOpen = signal(false);
  protected readonly icons = Object.keys(ICON_PATHS) as IconName[];

  protected submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      this.toasts.warning('Please check the form', 'Some fields need your attention.');
      return;
    }
    // Local timer only: demonstrates double-submit protection without any network call.
    this.submitState.run(
      () => timer(1500),
      () => this.submitCount.update((n) => n + 1),
    );
  }

  protected simulateServerErrors(): void {
    const unmatched = applyServerErrors(
      this.form,
      new AppError({
        kind: 'validation',
        message: 'x',
        fieldErrors: { email: ['This email is already registered.'] },
      }),
    );
    if (unmatched.length) this.toasts.error(unmatched.join(' '));
  }

  protected onAccepted(files: File[]): void {
    this.acceptedNames.set(files.map((f) => f.name));
  }

  protected onRejected(files: RejectedFile[]): void {
    this.toasts.warning('Some files were not accepted', `${files.length} file(s) rejected.`);
  }

  protected showLoading(): void {
    this.loading.track(timer(2000)).subscribe();
  }

  protected async confirmDelete(): Promise<void> {
    const ok = await this.confirmService.confirm({
      title: 'Delete this draft?',
      message: 'The draft and its uploaded files will be removed. This cannot be undone.',
      confirmLabel: 'Delete draft',
      tone: 'danger',
    });
    this.toasts.info(ok ? 'Confirmed' : 'Cancelled');
  }

  protected async confirmReason(): Promise<void> {
    const reason = await this.confirmService.confirmWithReason({
      title: 'Suspend this account?',
      message: 'The user will be signed out and cannot sign in until reactivated.',
      confirmLabel: 'Suspend',
      tone: 'danger',
      reason: { label: 'Reason for suspension', minLength: 10 },
    });
    this.toasts.info(reason ? `Confirmed with reason: ${reason}` : 'Cancelled');
  }

  protected simulateOffline(): void {
    window.dispatchEvent(new Event('offline'));
    setTimeout(() => window.dispatchEvent(new Event('online')), 3000);
  }
}
