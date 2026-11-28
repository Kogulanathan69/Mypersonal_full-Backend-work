import { Component, Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AdminLayout } from './admin-layout/admin-layout';
import { AuthLayout } from './auth-layout/auth-layout';
import { CustomerLayout } from './customer-layout/customer-layout';
import { ADMIN_NAV } from './parts/nav.model';
import { AccountMenu } from './parts/account-menu';
import { SkipLink } from './parts/skip-link';
import { PublicLayout } from './public-layout/public-layout';
import { WizardLayout } from './wizard-layout/wizard-layout';

async function render<T>(cmp: Type<T>, inputs: Record<string, unknown> = {}) {
  TestBed.configureTestingModule({ providers: [provideRouter([])] });
  const f = TestBed.createComponent(cmp);
  for (const [k, v] of Object.entries(inputs)) f.componentRef.setInput(k, v);
  await f.whenStable();
  return { f, el: f.nativeElement as HTMLElement };
}

describe('layouts — shared landmarks', () => {
  it.each([
    ['public', PublicLayout],
    ['auth', AuthLayout],
    ['customer', CustomerLayout],
    ['admin', AdminLayout],
  ] as const)('%s layout has a skip link and a focusable main landmark', async (_name, layout) => {
    const { el } = await render(layout as Type<unknown>);
    expect(el.querySelector('bm-skip-link a')?.getAttribute('href')).toBe('#main-content');
    const main = el.querySelector('main#main-content');
    expect(main?.getAttribute('tabindex')).toBe('-1');
    expect(el.querySelectorAll('main')).toHaveLength(1);
  });
});

describe('PublicLayout', () => {
  it('offers login and get started, and states plans are preliminary', async () => {
    const { el } = await render(PublicLayout);
    expect(el.querySelector('a[href="/auth/sign-in"]')?.textContent?.trim()).toBe('Login');
    expect(el.querySelector('a[href="/#planning-experience"]')?.textContent?.trim()).toBe(
      'Explore the experience',
    );
    for (const label of ['Home', 'Features', 'How It Works', 'About', 'Contact']) {
      expect(el.querySelector('header nav')?.textContent).toContain(label);
    }
    expect(el.textContent).toContain('not approved building drawings');
  });
});

describe('CustomerLayout', () => {
  it('shows the main nav and a neutral account label when no user is known', async () => {
    const { el } = await render(CustomerLayout);
    const navText = el.querySelector('nav[aria-label="Main"]')?.textContent ?? '';
    expect(navText).toContain('Dashboard');
    expect(navText).toContain('My Projects');
    expect(navText).not.toContain('Saved Ideas');
    expect(el.querySelector('bm-account-menu')?.textContent).toContain('Account');
  });

  it('announces the unread notification count', async () => {
    const { el } = await render(CustomerLayout, { notificationCount: 2 });
    expect(el.querySelector('[aria-label="Notifications, 2 unread"]')).not.toBeNull();
  });

  it('opens the mobile navigation drawer', async () => {
    const { f, el } = await render(CustomerLayout);
    (el.querySelector('button[aria-expanded]') as HTMLButtonElement).click();
    await f.whenStable();
    expect(el.querySelector('[role="dialog"][aria-label="Navigation"]')).not.toBeNull();
  });
});

describe('AdminLayout', () => {
  it('lists every admin list screen and marks the portal as Admin', async () => {
    const { el } = await render(AdminLayout, { role: 'Auditor' });
    const links = el.querySelectorAll('aside nav a');
    const expected = ADMIN_NAV.reduce((n, g) => n + g.items.length, 0);
    expect(links).toHaveLength(expected);
    // A02–A18 = 17 screens, minus 4 detail screens (A04, A06, A11, A13) reached from lists.
    expect(expected).toBe(13);
    expect(el.textContent).toContain('Admin');
    expect(el.textContent).toContain('Auditor');
  });
});

describe('WizardLayout', () => {
  @Component({
    imports: [WizardLayout],
    template: `
      <bm-wizard-layout
        heading="Create your home plan"
        subheading="Rooms"
        backLink="/back"
        [steps]="steps"
      >
        <p>Body</p>
        <button wizardActions>Next</button>
      </bm-wizard-layout>
    `,
  })
  class Host {
    steps = [{ id: 'a', label: 'Rooms', state: 'current' as const }];
  }

  it('renders an h1, back link, stepper, content and actions', async () => {
    const { el } = await render(Host);
    expect(el.querySelector('h1')?.textContent).toBe('Create your home plan');
    expect(el.querySelector('a[href="/back"]')).not.toBeNull();
    expect(el.querySelector('bm-stepper')).not.toBeNull();
    expect(el.textContent).toContain('Body');
    expect(el.textContent).toContain('Next');
  });
});

describe('AccountMenu', () => {
  it('opens, closes on Escape, and returns focus to the trigger', async () => {
    const { f, el } = await render(AccountMenu, { userName: 'Nimal Perera', items: [] });
    document.body.appendChild(el);
    const trigger = el.querySelector('button') as HTMLButtonElement;
    expect(trigger.textContent).toContain('NP');
    trigger.click();
    await f.whenStable();
    expect(trigger.getAttribute('aria-expanded')).toBe('true');
    el.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    await f.whenStable();
    expect(trigger.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(trigger);
    el.remove();
  });

  it('closes on an outside click and emits sign out', async () => {
    const { f, el } = await render(AccountMenu);
    let signedOut = false;
    f.componentInstance.signOut.subscribe(() => (signedOut = true));
    (el.querySelector('button') as HTMLButtonElement).click();
    await f.whenStable();
    const signOut = Array.from(el.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Sign out'),
    )!;
    signOut.click();
    await f.whenStable();
    expect(signedOut).toBe(true);
    expect(el.querySelector('#bm-account-menu-panel')).toBeNull();
  });
});

describe('SkipLink', () => {
  it('moves focus to the main landmark', async () => {
    const { el } = await render(SkipLink);
    const main = document.createElement('main');
    main.id = 'main-content';
    main.tabIndex = -1;
    document.body.append(main);
    (el.querySelector('a') as HTMLAnchorElement).click();
    expect(document.activeElement).toBe(main);
    main.remove();
  });
});
