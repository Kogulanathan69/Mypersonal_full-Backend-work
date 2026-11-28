import { Component, Type, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AppError } from '../../core/api/app-error';
import { RuntimeConfigService } from '../../core/config/runtime-config.service';
import { Badge } from './badge/badge';
import { StatusChip } from './badge/status-chip';
import { Breadcrumb } from './breadcrumb/breadcrumb';
import { Button } from './button/button';
import { Card } from './card/card';
import { Icon } from './icon/icon';
import { Paginator } from './paginator/paginator';
import { Skeleton } from './skeleton/skeleton';
import { ConfigMissing } from './states/config-missing';
import { EmptyState } from './states/empty-state';
import { ErrorState } from './states/error-state';
import { StepItem, Stepper } from './stepper/stepper';
import { TabItem, Tabs } from './tabs/tabs';

async function render<T>(cmp: Type<T>, inputs: Record<string, unknown> = {}) {
  TestBed.configureTestingModule({ providers: [provideRouter([])] });
  const f = TestBed.createComponent(cmp);
  for (const [k, v] of Object.entries(inputs)) f.componentRef.setInput(k, v);
  await f.whenStable();
  return { f, el: f.nativeElement as HTMLElement };
}

@Component({
  imports: [Button],
  template: `
    <button bmButton type="button" [loading]="loading()" (click)="clicks = clicks + 1">Save</button>
    <a bmButton href="/x" [disabled]="true">Link</a>
  `,
})
class ButtonHost {
  loading = signal(false);
  clicks = 0;
}

describe('Button', () => {
  it('is disabled and aria-busy while loading, so it cannot be clicked twice', async () => {
    const { f, el } = await render(ButtonHost);
    const btn = el.querySelector('button') as HTMLButtonElement;
    btn.click();
    f.componentInstance.loading.set(true);
    await f.whenStable();
    expect(btn.disabled).toBe(true);
    expect(btn.getAttribute('aria-busy')).toBe('true');
    btn.click();
    expect(f.componentInstance.clicks).toBe(1);
  });

  it('marks disabled links with aria-disabled and removes them from tab order', async () => {
    const { el } = await render(ButtonHost);
    const a = el.querySelector('a') as HTMLAnchorElement;
    expect(a.getAttribute('aria-disabled')).toBe('true');
    expect(a.getAttribute('tabindex')).toBe('-1');
  });
});

describe('Icon', () => {
  it('is hidden from assistive tech unless labelled', async () => {
    const { f, el } = await render(Icon, { name: 'home' });
    expect(el.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    f.componentRef.setInput('label', 'Home');
    await f.whenStable();
    expect(el.querySelector('svg')?.getAttribute('aria-label')).toBe('Home');
    expect(el.querySelector('svg')?.getAttribute('role')).toBe('img');
  });
});

describe('Card, Badge, StatusChip, Skeleton', () => {
  it('card renders heading at the requested level', async () => {
    const { el } = await render(Card, { heading: 'Rooms', level: 3 });
    const h = el.querySelector('[role="heading"]');
    expect(h?.textContent?.trim()).toBe('Rooms');
    expect(h?.getAttribute('aria-level')).toBe('3');
  });

  it('badge applies its tone', async () => {
    const { el } = await render(Badge, { tone: 'danger' });
    expect(el.className).toContain('text-danger');
  });

  it('status chip shows a text label, not colour alone', async () => {
    const { el } = await render(StatusChip, { status: 'in-progress' });
    expect(el.textContent?.trim()).toBe('In progress');
  });

  it('skeleton is hidden from screen readers and renders the line count', async () => {
    const { el } = await render(Skeleton, { lines: 4 });
    expect(el.getAttribute('aria-hidden')).toBe('true');
    expect(el.querySelectorAll('.animate-pulse')).toHaveLength(4);
  });
});

describe('Stepper', () => {
  const steps: StepItem[] = [
    { id: 'a', label: 'Rooms', state: 'complete', link: '/rooms' },
    { id: 'b', label: 'Spaces', state: 'current' },
    { id: 'c', label: 'Method', state: 'upcoming', link: '/method' },
  ];

  it('marks the current step and only links completed steps', async () => {
    const { el } = await render(Stepper, { steps });
    expect(el.querySelector('[aria-current="step"]')?.textContent).toContain('Spaces');
    const links = Array.from(el.querySelectorAll('a')).map((a) => a.getAttribute('href'));
    expect(links).toEqual(['/rooms']);
    expect(el.textContent).toContain('Step 2 of 3');
  });
});

describe('Tabs', () => {
  @Component({
    imports: [Tabs],
    template: `<bm-tabs [tabs]="tabs" label="Issues" [(selected)]="sel" />`,
  })
  class TabsHost {
    tabs: TabItem[] = [
      { id: 'a', label: 'A' },
      { id: 'b', label: 'B' },
      { id: 'c', label: 'C', count: 3 },
    ];
    sel = signal('a');
  }

  it('uses roving tabindex and moves with arrow keys, Home and End', async () => {
    const { f, el } = await render(TabsHost);
    const tabs = () => Array.from(el.querySelectorAll<HTMLButtonElement>('[role="tab"]'));
    expect(tabs().map((t) => t.tabIndex)).toEqual([0, -1, -1]);
    tabs()[0]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft' }));
    await f.whenStable();
    expect(f.componentInstance.sel()).toBe('c');
    expect(tabs()[2]?.getAttribute('aria-selected')).toBe('true');
    tabs()[2]?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Home' }));
    await f.whenStable();
    expect(f.componentInstance.sel()).toBe('a');
  });
});

describe('Breadcrumb', () => {
  it('links earlier items and marks the last as the current page', async () => {
    const { el } = await render(Breadcrumb, {
      items: [{ label: 'Projects', link: '/p' }, { label: 'Kandy' }],
    });
    expect(el.querySelector('a')?.textContent?.trim()).toBe('Projects');
    expect(el.querySelector('[aria-current="page"]')?.textContent?.trim()).toBe('Kandy');
  });
});

describe('Paginator', () => {
  it('shows the range and disables buttons at the ends', async () => {
    const { f, el } = await render(Paginator, { page: 1, pageSize: 10, total: 42 });
    expect(el.textContent).toContain('1–10');
    expect(el.textContent).toContain('Page 1 of 5');
    const [prev, next] = Array.from(el.querySelectorAll('button'));
    expect(prev?.disabled).toBe(true);
    const emitted: number[] = [];
    f.componentInstance.pageChange.subscribe((p) => emitted.push(p));
    next?.click();
    expect(emitted).toEqual([2]);
  });

  it('handles an empty list', async () => {
    const { el } = await render(Paginator, { page: 1, pageSize: 10, total: 0 });
    expect(el.textContent).toContain('No results');
  });
});

describe('States', () => {
  it('empty state shows title and message', async () => {
    const { el } = await render(EmptyState, { title: 'No projects', message: 'Create one.' });
    expect(el.textContent).toContain('No projects');
    expect(el.textContent).toContain('Create one.');
  });

  it('error state shows the safe message and reference, and retry only when retryable', async () => {
    const { f, el } = await render(ErrorState, {
      error: new AppError({ kind: 'network', message: 'Could not reach.', correlationId: 'ref-9' }),
    });
    expect(el.querySelector('[role="alert"]')).not.toBeNull();
    expect(el.textContent).toContain('Connection problem');
    expect(el.textContent).toContain('ref-9');
    let retried = false;
    f.componentInstance.retry.subscribe(() => (retried = true));
    (el.querySelector('button') as HTMLButtonElement).click();
    expect(retried).toBe(true);

    f.componentRef.setInput('error', new AppError({ kind: 'forbidden', message: 'No.' }));
    await f.whenStable();
    expect(el.querySelector('button')).toBeNull();
  });

  it('config-missing names the feature and can list problems', async () => {
    TestBed.overrideProvider(RuntimeConfigService, {
      useValue: { problems: signal(['api.mainBaseUrl: Invalid input']).asReadonly() },
    });
    const { el } = await render(ConfigMissing, { feature: 'The land map', showDetails: true });
    expect(el.textContent).toContain('The land map is not configured');
    expect(el.textContent).toContain('api.mainBaseUrl');
  });
});
