import { HttpClient, HttpContext } from '@angular/common/http';
import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, UrlSegment, UrlTree, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { TEST_API_BASE, provideCoreHttpTesting } from '../../../testing/core-test-providers';
import { AppError } from '../api/app-error';
import { IS_API_REQUEST, SUPPRESS_SESSION_EXPIRY } from '../api/http-context';
import { listContracts } from '../api/contract-status';
import { UnavailableAuthApi } from './auth-api';
import { authGuard, guestGuard, permissionGuard } from './auth.guards';
import { PERMISSION_CHECKER } from './permission';
import { safeReturnUrl } from './return-url';
import { SessionEventsService } from './session-events.service';
import { SessionStore } from './session.store';

describe('safeReturnUrl', () => {
  it.each([
    ['/app/projects/7', '/app/projects/7'],
    ['%2Fapp%2Fdashboard', '/app/dashboard'],
    ['/app?tab=plans#x', '/app?tab=plans#x'],
  ])('accepts in-app path %s', (raw, expected) => expect(safeReturnUrl(raw)).toBe(expected));

  it.each([
    'https://evil.example',
    '//evil.example',
    '/\\evil.example',
    '%2F%2Fevil.example',
    'javascript:alert(1)',
    '/javascript:alert(1)',
    'app/projects',
    '/auth/sign-in',
    '/auth',
    '%E0%A4%A',
    '/line\nbreak',
    '',
    null,
    42,
  ])('rejects %s', (raw) => expect(safeReturnUrl(raw)).toBe('/'));

  it('uses the given fallback', () =>
    expect(safeReturnUrl('https://x.example', '/app')).toBe('/app'));
});

describe('SessionStore and permissions', () => {
  it('holds the user in memory, answers role and permission checks, and clears', () => {
    const store = TestBed.inject(SessionStore);
    expect(store.isAuthenticated()).toBe(false);
    store.set({
      id: 'user-1',
      email: 'a@example.invalid',
      displayName: 'A',
      roles: ['customer'],
      permissions: ['projects.read'],
    });
    expect(store.hasRole('customer')).toBe(true);
    expect(TestBed.inject(PERMISSION_CHECKER)('projects.read')).toBe(true);
    expect(TestBed.inject(PERMISSION_CHECKER)('admin.users.suspend')).toBe(false);
    store.clear();
    expect(store.isAuthenticated()).toBe(false);
    expect(localStorage.length + sessionStorage.length).toBe(0);
  });
});

describe('guards', () => {
  const run = (guard: typeof authGuard, path = 'app/projects') =>
    TestBed.runInInjectionContext(() =>
      guard(
        {},
        path.split('/').map((p) => new UrlSegment(p, {})),
        {} as Parameters<typeof authGuard>[2],
      ),
    );

  beforeEach(() => TestBed.configureTestingModule({ providers: [provideRouter([])] }));

  it('authGuard sends signed-out visitors to sign-in with the return URL', () => {
    const result = run(authGuard) as UrlTree;
    expect(TestBed.inject(Router).serializeUrl(result)).toBe(
      '/auth/sign-in?returnUrl=%2Fapp%2Fprojects',
    );
  });

  it('authGuard lets signed-in users through; guestGuard sends them home', () => {
    TestBed.inject(SessionStore).set({
      id: 'user-1',
      email: 'a@example.invalid',
      displayName: 'A',
      roles: [],
      permissions: [],
    });
    expect(run(authGuard)).toBe(true);
    expect(TestBed.inject(Router).serializeUrl(run(guestGuard) as UrlTree)).toBe('/');
  });

  it('permissionGuard sends users without the permission to /forbidden', () => {
    const tree = run(permissionGuard('admin.users.read')) as UrlTree;
    expect(TestBed.inject(Router).serializeUrl(tree)).toBe('/forbidden');
  });
});

describe('UnavailableAuthApi fallback', () => {
  it('records verified auth contracts and keeps incompatible verification flows blocked', () => {
    TestBed.inject(UnavailableAuthApi);
    const auth = listContracts().filter((c) => c.id.startsWith('auth.'));
    expect(auth.length).toBe(9);
    expect(auth.filter((c) => c.status === 'VERIFIED')).toHaveLength(7);
    expect(auth.filter((c) => c.status === 'BLOCKED').map((c) => c.id)).toEqual([
      'auth.resendVerification',
      'auth.verifyEmail',
    ]);
  });

  it('fails with a truthful not-connected error and sends no request', async () => {
    TestBed.configureTestingModule({ providers: provideCoreHttpTesting() });
    const api = TestBed.inject(UnavailableAuthApi);
    const error = await firstValueFrom(api.signIn()).catch((e: unknown) => e as AppError);
    expect(error).toBeInstanceOf(AppError);
    expect((error as AppError).kind).toBe('config');
    expect((error as AppError).message).toMatch(/not connected yet/);
    TestBed.inject(HttpTestingController).expectNone(() => true);
  });
});

describe('401 handling for sign-in style requests', () => {
  it('does not raise session-expired when SUPPRESS_SESSION_EXPIRY is set', async () => {
    TestBed.configureTestingModule({ providers: provideCoreHttpTesting() });
    const http = TestBed.inject(HttpClient);
    const ctx = new HttpContext().set(IS_API_REQUEST, true).set(SUPPRESS_SESSION_EXPIRY, true);
    const result = firstValueFrom(http.post('login', {}, { context: ctx })).catch(
      (e: unknown) => e as AppError,
    );
    TestBed.inject(HttpTestingController)
      .expectOne(`${TEST_API_BASE}/login`)
      .flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(((await result) as AppError).kind).toBe('unauthorized');
    expect(TestBed.inject(SessionEventsService).expired()).toBe(false);
  });
});
