import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TEST_ENV } from '../../../../testing/core-test-providers';
import { BUILD_ENV } from '../../../core/config/build-env.token';
import { HomePage } from './home-page';

describe('HomePage', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: BUILD_ENV, useValue: TEST_ENV }],
    });
  });

  it('renders the hero heading, CTAs and every section', async () => {
    const f = TestBed.createComponent(HomePage);
    await f.whenStable();
    TestBed.tick();
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('h1')?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
      'Plan your home with confidence.',
    );
    expect(el.querySelector('a[href="/auth/register"]')?.textContent).toContain(
      'Start the Planning Experience',
    );
    for (const id of ['how-it-works', 'features', 'about'])
      expect(el.querySelector(`#${id}`), id).not.toBeNull();
    for (const heading of [
      'rag-heading',
      'gen-heading',
      'editor-heading',
      'ai-heading',
      'architect-heading',
      'faq-heading',
      'cta-heading',
    ]) {
      expect(el.querySelector(`#${heading}`), heading).not.toBeNull();
    }
  });

  it('renders a static five-stage journey without WebGL or animation controls', async () => {
    const f = TestBed.createComponent(HomePage);
    await f.whenStable();
    TestBed.tick();
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('canvas')).toBeNull();
    expect(el.querySelectorAll('ol[aria-label="Static planning journey"] li')).toHaveLength(5);
    expect(el.textContent).not.toContain('Turn animation off');
  });

  it('labels every showcase as illustrative and makes no approval claims', async () => {
    const f = TestBed.createComponent(HomePage);
    await f.whenStable();
    const text = (f.nativeElement as HTMLElement).textContent ?? '';
    expect((text.match(/Illustrative example/g) ?? []).length).toBeGreaterThanOrEqual(5);
    expect(text).not.toMatch(/approved by|guaranteed compliance|certified plan/i);
  });

  it('lets keyboard users select a room in the editor showcase', async () => {
    const f = TestBed.createComponent(HomePage);
    await f.whenStable();
    const el = f.nativeElement as HTMLElement;
    const room = el.querySelector<SVGGElement>('bm-editor-showcase g[role="button"]')!;
    room.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await f.whenStable();
    expect(el.querySelector('bm-editor-showcase')?.textContent).toContain(
      'Living: 7.0 m by 5.5 m.',
    );
  });
});
