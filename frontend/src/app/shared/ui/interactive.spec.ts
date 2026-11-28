import { Dialog } from '@angular/cdk/dialog';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { BUILD_ENV } from '../../core/config/build-env.token';
import { LoadingService } from '../../core/feedback/loading.service';
import { ToastService } from '../../core/feedback/toast.service';
import { OnlineStatusService } from '../../core/network/online-status.service';
import { TEST_ENV } from '../../../testing/core-test-providers';
import { DataTable, SortState, TableColumn } from './data-table/data-table';
import { ConfirmService } from './dialog/confirm.service';
import { Drawer } from './drawer/drawer';
import { DemoDataBanner } from './feedback/demo-data-banner';
import { GlobalProgress } from './feedback/global-progress';
import { OfflineBanner } from './feedback/offline-banner';
import { ToastHost } from './feedback/toast-host';
import { FileDrop, checkFile, formatBytes } from './file-drop/file-drop';
import { FormField } from './form-field/form-field';
import { BmInput } from './form-field/input.directive';
import { PasswordInput } from './password-input/password-input';

describe('feedback components', () => {
  it('global progress appears only while loading', async () => {
    const f = TestBed.createComponent(GlobalProgress);
    await f.whenStable();
    expect(f.nativeElement.querySelector('[role="progressbar"]')).toBeNull();
    TestBed.inject(LoadingService).begin();
    await f.whenStable();
    expect(f.nativeElement.querySelector('[role="progressbar"]')).not.toBeNull();
  });

  it('offline banner follows the browser state', async () => {
    const f = TestBed.createComponent(OfflineBanner);
    TestBed.inject(OnlineStatusService);
    window.dispatchEvent(new Event('offline'));
    await f.whenStable();
    expect(f.nativeElement.textContent).toContain('You are offline');
    window.dispatchEvent(new Event('online'));
    await f.whenStable();
    expect(f.nativeElement.textContent).not.toContain('You are offline');
  });

  it('demo-data banner shows only in the mock build', async () => {
    TestBed.configureTestingModule({
      providers: [{ provide: BUILD_ENV, useValue: { ...TEST_ENV, useMocks: true } }],
    });
    const f = TestBed.createComponent(DemoDataBanner);
    await f.whenStable();
    expect(f.nativeElement.querySelector('[data-testid="demo-data-banner"]')).not.toBeNull();
  });

  it('demo-data banner never shows in production', async () => {
    TestBed.configureTestingModule({ providers: [{ provide: BUILD_ENV, useValue: TEST_ENV }] });
    const f = TestBed.createComponent(DemoDataBanner);
    await f.whenStable();
    expect(f.nativeElement.querySelector('[data-testid="demo-data-banner"]')).toBeNull();
  });

  it('toast host renders toasts and dismisses them', async () => {
    const f = TestBed.createComponent(ToastHost);
    const toasts = TestBed.inject(ToastService);
    toasts.error('Could not save', 'Reference: r1');
    await f.whenStable();
    expect(f.nativeElement.textContent).toContain('Could not save');
    (f.nativeElement.querySelector('button') as HTMLButtonElement).click();
    await f.whenStable();
    expect(toasts.toasts()).toHaveLength(0);
  });
});

describe('ConfirmService', () => {
  it('resolves true on confirm and uses alertdialog for danger', async () => {
    const service = TestBed.inject(ConfirmService);
    const open = vi.spyOn(TestBed.inject(Dialog), 'open');
    const result = service.confirm({
      title: 'Delete?',
      message: 'Gone for good.',
      tone: 'danger',
      confirmLabel: 'Delete',
    });
    expect(open.mock.calls[0]?.[1]?.role).toBe('alertdialog');
    TestBed.tick();
    const buttons = Array.from(
      document.querySelectorAll<HTMLButtonElement>('bm-confirm-dialog button'),
    );
    buttons.find((b) => b.textContent?.includes('Delete'))?.click();
    await expect(result).resolves.toBe(true);
  });

  it('requires a long-enough reason before confirming', async () => {
    const service = TestBed.inject(ConfirmService);
    const result = service.confirmWithReason({
      title: 'Suspend?',
      message: 'm',
      reason: { label: 'Reason', minLength: 5 },
    });
    TestBed.tick();
    const confirm = () =>
      Array.from(document.querySelectorAll<HTMLButtonElement>('bm-confirm-dialog button')).at(-1)!;
    const textarea = document.querySelector<HTMLTextAreaElement>('#bm-confirm-reason')!;
    TestBed.tick();
    expect(confirm().disabled).toBe(true);
    textarea.value = 'Fraud report #12';
    textarea.dispatchEvent(new Event('input'));
    TestBed.tick();
    expect(confirm().disabled).toBe(false);
    confirm().click();
    await expect(result).resolves.toBe('Fraud report #12');
  });

  it('resolves false / null on cancel', async () => {
    const service = TestBed.inject(ConfirmService);
    const result = service.confirm({ title: 'Leave?', message: 'm' });
    TestBed.tick();
    document.querySelector<HTMLButtonElement>('bm-confirm-dialog button')?.click();
    await expect(result).resolves.toBe(false);
  });
});

describe('Drawer', () => {
  @Component({
    imports: [Drawer],
    template: `<button id="opener" (click)="open.set(true)">Open</button
      ><bm-drawer [(open)]="open" label="Nav"><a href="/x">Link</a></bm-drawer>`,
  })
  class DrawerHost {
    open = signal(false);
  }

  it('opens as a labelled modal, closes on Escape and returns focus', async () => {
    const f = TestBed.createComponent(DrawerHost);
    document.body.appendChild(f.nativeElement);
    await f.whenStable();
    const opener = f.nativeElement.querySelector('#opener') as HTMLButtonElement;
    opener.focus();
    opener.click();
    await f.whenStable();
    const dialog = f.nativeElement.querySelector('[role="dialog"]');
    expect(dialog?.getAttribute('aria-label')).toBe('Nav');
    expect(dialog?.getAttribute('aria-modal')).toBe('true');
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    await f.whenStable();
    expect(f.componentInstance.open()).toBe(false);
    expect(document.activeElement).toBe(opener);
    f.nativeElement.remove();
  });
});

describe('FileDrop', () => {
  const file = (name: string, size: number, type = '') =>
    new File([new Uint8Array(size)], name, { type });

  it('formats sizes for people', () => {
    expect(formatBytes(500)).toBe('500 B');
    expect(formatBytes(20 * 1024 * 1024)).toBe('20 MB');
    expect(formatBytes(1536)).toBe('1.5 KB');
  });

  it('checks type and size', () => {
    expect(checkFile(file('plan.PDF', 10), ['.pdf'], 100)).toBeNull();
    expect(checkFile(file('a.exe', 10), ['.pdf'], 100)?.reason).toBe('type');
    expect(checkFile(file('a.pdf', 200), ['.pdf'], 100)?.reason).toBe('size');
    expect(checkFile(file('a.png', 10, 'image/png'), ['image/*'], 100)).toBeNull();
  });

  it('emits accepted and rejected files separately and limits count', async () => {
    const f = TestBed.createComponent(FileDrop);
    f.componentRef.setInput('accept', ['.pdf']);
    f.componentRef.setInput('maxBytes', 100);
    await f.whenStable();
    const accepted: string[] = [];
    const rejected: string[] = [];
    f.componentInstance.filesAccepted.subscribe((fs) => accepted.push(...fs.map((x) => x.name)));
    f.componentInstance.filesRejected.subscribe((rs) => rejected.push(...rs.map((r) => r.reason)));
    f.componentInstance.handle([
      file('a.pdf', 10),
      file('b.pdf', 10),
      file('c.doc', 10),
      file('d.pdf', 999),
    ]);
    await f.whenStable();
    expect(accepted).toEqual(['a.pdf']);
    expect(rejected).toEqual(['count', 'type', 'size']);
    expect(f.nativeElement.textContent).toContain('larger than 100 B');
  });
});

describe('DataTable', () => {
  @Component({
    imports: [DataTable],
    template: `<bm-data-table
      caption="Users"
      [columns]="cols"
      [rows]="rows"
      rowKey="id"
      [(sort)]="sort"
    />`,
  })
  class TableHost {
    cols: TableColumn[] = [
      { key: 'id', header: 'ID', hideOnMobile: true },
      { key: 'name', header: 'Name', sortable: true },
    ];
    rows = [{ id: '1', name: 'Row one' }];
    sort = signal<SortState | null>(null);
  }

  it('has a caption and toggles aria-sort', async () => {
    const f = TestBed.createComponent(TableHost);
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('caption')?.textContent?.trim()).toBe('Users');
    const th = el.querySelectorAll('th')[1]!;
    expect(th.getAttribute('aria-sort')).toBe('none');
    th.querySelector('button')!.click();
    await f.whenStable();
    expect(f.componentInstance.sort()).toEqual({ key: 'name', direction: 'asc' });
    expect(th.getAttribute('aria-sort')).toBe('ascending');
    th.querySelector('button')!.click();
    await f.whenStable();
    expect(th.getAttribute('aria-sort')).toBe('descending');
    expect(el.querySelector('ul dl')?.textContent).not.toContain('ID');
  });
});

describe('FormField + bmInput + PasswordInput', () => {
  @Component({
    imports: [ReactiveFormsModule, FormField, BmInput, PasswordInput],
    template: `
      <form [formGroup]="form">
        <bm-form-field
          label="Email"
          [control]="form.controls.email"
          [required]="true"
          hint="Work email"
        >
          <input bmInput formControlName="email" />
        </bm-form-field>
        <bm-form-field label="Password" [control]="form.controls.password">
          <bm-password-input formControlName="password" [showStrength]="true" />
        </bm-form-field>
      </form>
    `,
  })
  class FormHost {
    form = new FormGroup({
      email: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, Validators.email],
      }),
      password: new FormControl('', { nonNullable: true }),
    });
  }

  it('links label, hint and error to the input and shows errors after touch', async () => {
    const f = TestBed.createComponent(FormHost);
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    const input = el.querySelector('input[formControlName="email"]') as HTMLInputElement;
    const label = el.querySelector('label') as HTMLLabelElement;
    expect(label.htmlFor).toBe(input.id);
    expect(input.getAttribute('aria-required')).toBe('true');
    expect(input.getAttribute('aria-invalid')).toBeNull();
    expect(input.getAttribute('aria-describedby')).toMatch(/-hint$/);

    f.componentInstance.form.controls.email.markAsTouched();
    await f.whenStable();
    expect(input.getAttribute('aria-invalid')).toBe('true');
    const describedBy = input.getAttribute('aria-describedby')!.split(' ');
    const errorEl = el.querySelector(`#${describedBy[1]}`);
    expect(errorEl?.textContent).toContain('Email is required.');
  });

  it('password input toggles visibility and writes to the form', async () => {
    const f = TestBed.createComponent(FormHost);
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    const pw = el.querySelector('bm-password-input input') as HTMLInputElement;
    const toggle = el.querySelector('bm-password-input button') as HTMLButtonElement;
    expect(pw.type).toBe('password');
    toggle.click();
    await f.whenStable();
    expect(pw.type).toBe('text');
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
    pw.value = 'Abcdefgh1';
    pw.dispatchEvent(new Event('input'));
    await f.whenStable();
    expect(f.componentInstance.form.controls.password.value).toBe('Abcdefgh1');
    expect(el.textContent).toContain('Strength:');
    expect(pw.id).toBe(el.querySelectorAll('label')[1]?.htmlFor);
  });
});

describe('ToastHost live regions', () => {
  it('announces errors assertively and other toasts politely', async () => {
    const f = TestBed.createComponent(ToastHost);
    const toasts = TestBed.inject(ToastService);
    toasts.success('Saved');
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('[aria-live="polite"]')?.textContent).toContain('Saved');
    toasts.error('Failed', 'Try again');
    await f.whenStable();
    expect(el.querySelector('[aria-live="assertive"]')?.textContent).toContain('Failed. Try again');
    toasts.clear();
  });
});
