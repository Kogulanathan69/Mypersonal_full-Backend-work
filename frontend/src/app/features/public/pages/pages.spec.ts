import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Forbidden, SessionEnded } from '../../system/status-pages';
import { AboutPage } from './about-page';
import { ContactPage } from './contact-page';
import { PrivacyPage, TermsPage } from './policy-pages';

async function text<T>(cmp: Type<T>) {
  TestBed.configureTestingModule({ providers: [provideRouter([])] });
  const f = TestBed.createComponent(cmp);
  await f.whenStable();
  return f.nativeElement as HTMLElement;
}

describe('public information pages', () => {
  it('About states scope and exclusions honestly', async () => {
    const el = await text(AboutPage);
    expect(el.querySelector('h1')?.textContent).toBe('About BuildMind');
    expect(el.textContent).toContain('does not grant planning or building approval');
    expect(el.textContent).toContain('does not estimate construction costs');
  });

  it('Contact does not invent a support address', async () => {
    const el = await text(ContactPage);
    expect(el.textContent).toContain('have not been published yet');
    expect(el.textContent).not.toMatch(/@[a-z0-9-]+\.[a-z]/i);
  });

  it.each([
    [PrivacyPage, 'privacy policy'],
    [TermsPage, 'terms of use'],
  ] as const)('%o is marked as a draft pending legal review', async (page, doc) => {
    const el = await text(page as Type<unknown>);
    expect(el.querySelector('[role="note"]')?.textContent).toContain(
      'Draft — pending legal review',
    );
    expect(el.textContent).toContain(doc);
  });

  it('system pages have a single h1 and a way out', async () => {
    for (const page of [Forbidden, SessionEnded] as Type<unknown>[]) {
      TestBed.resetTestingModule();
      const el = await text(page);
      expect(el.querySelectorAll('h1')).toHaveLength(1);
      expect(el.querySelector('a')).not.toBeNull();
    }
  });
});
