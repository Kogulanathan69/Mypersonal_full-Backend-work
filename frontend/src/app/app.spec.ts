import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { TEST_ENV } from '../testing/core-test-providers';
import { App } from './app';
import { routes } from './app.routes';
import { BUILD_ENV } from './core/config/build-env.token';
import { SessionEventsService } from './core/auth/session-events.service';

describe('App shell', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), { provide: BUILD_ENV, useValue: TEST_ENV }],
    });
  });

  it('renders the router outlet and the notifications region', async () => {
    const f = TestBed.createComponent(App);
    await f.whenStable();
    expect(f.nativeElement.querySelector('router-outlet')).not.toBeNull();
    expect(f.nativeElement.querySelector('section[aria-label="Notifications"]')).not.toBeNull();
  });

  it('opens the session-expired dialog once and sends the user to sign in with a return URL', async () => {
    const f = TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const session = TestBed.inject(SessionEventsService);
    await f.whenStable();
    session.markExpired();
    await f.whenStable();
    await vi.waitFor(() =>
      expect(document.querySelector('bm-session-expired-dialog')).not.toBeNull(),
    );
    TestBed.tick();
    const dialogs = document.querySelectorAll('bm-session-expired-dialog');
    expect(dialogs).toHaveLength(1);
    const signIn = Array.from(dialogs[0]!.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Sign in again'),
    )!;
    signIn.click();
    await f.whenStable();
    expect(session.expired()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/auth/sign-in'], { queryParams: { returnUrl: '/' } });
  });

  it('shows the 404 page for unknown URLs', async () => {
    const f = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl('/definitely/not/here');
    await f.whenStable();
    expect(f.nativeElement.textContent).toContain('We could not find that page');
  });

  it('shows the Home Page inside the public layout at the root, with no dev links', async () => {
    const f = TestBed.createComponent(App);
    await TestBed.inject(Router).navigateByUrl('/');
    await f.whenStable();
    expect(f.nativeElement.querySelector('bm-public-layout bm-home-page')).not.toBeNull();
    expect(f.nativeElement.textContent).not.toContain('UI gallery');
  });
});
