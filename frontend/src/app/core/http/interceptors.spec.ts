import { HttpClient, HttpContext } from '@angular/common/http';
import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { TEST_API_BASE, provideCoreHttpTesting } from '../../../testing/core-test-providers';
import { AppError } from '../api/app-error';
import { IS_API_REQUEST, REQUEST_TIMEOUT_MS, SKIP_GLOBAL_LOADING } from '../api/http-context';
import { AuthTokenStore } from '../auth/auth-token.store';
import { SessionEventsService } from '../auth/session-events.service';
import { LoadingService } from '../feedback/loading.service';
import { CORRELATION_HEADER } from './correlation-id.interceptor';

const api = (extra?: (c: HttpContext) => HttpContext) => {
  const ctx = new HttpContext().set(IS_API_REQUEST, true);
  return { context: extra ? extra(ctx) : ctx };
};

function setup(
  options: Parameters<typeof provideCoreHttpTesting>[0] = {},
  token: string | null = null,
) {
  TestBed.configureTestingModule({
    providers: [...provideCoreHttpTesting(options)],
  });
  TestBed.inject(AuthTokenStore).set(token ?? '', null);
  if (!token) TestBed.inject(AuthTokenStore).clear();
  return {
    http: TestBed.inject(HttpClient),
    ctrl: TestBed.inject(HttpTestingController),
    loading: TestBed.inject(LoadingService),
    session: TestBed.inject(SessionEventsService),
  };
}

const capture = (p: Promise<unknown>) =>
  p.then(
    () => null,
    (e: unknown) => e as AppError,
  );

describe('API interceptors', () => {
  afterEach(() => vi.useRealTimers());

  it('leave non-API requests untouched: no base URL, credentials, correlation id or token', () => {
    const { http, ctrl } = setup({}, 'secret-token');
    http.get('config.json').subscribe();
    const req = ctrl.expectOne('config.json');
    expect(req.request.withCredentials).toBe(false);
    expect(req.request.headers.has(CORRELATION_HEADER)).toBe(false);
    expect(req.request.headers.has('Authorization')).toBe(false);
  });

  it('prefix API paths with the base URL, without doubling slashes', () => {
    const { http, ctrl } = setup({ api: { mainBaseUrl: `${TEST_API_BASE}/` } });
    http.get('/projects', api()).subscribe();
    ctrl.expectOne(`${TEST_API_BASE}/projects`);
  });

  it('fail with a config error when no base URL is configured', async () => {
    const { http, ctrl } = setup({ api: { mainBaseUrl: '' } });
    const error = await capture(firstValueFrom(http.get('projects', api())));
    expect(error).toBeInstanceOf(AppError);
    expect((error as AppError).kind).toBe('config');
    ctrl.expectNone(() => true);
  });

  it('add a unique correlation id to each API request', () => {
    const { http, ctrl } = setup();
    http.get('a', api()).subscribe();
    http.get('b', api()).subscribe();
    const [a, b] = [ctrl.expectOne(`${TEST_API_BASE}/a`), ctrl.expectOne(`${TEST_API_BASE}/b`)];
    expect(a.request.headers.get(CORRELATION_HEADER)).toBeTruthy();
    expect(a.request.headers.get(CORRELATION_HEADER)).not.toBe(
      b.request.headers.get(CORRELATION_HEADER),
    );
  });

  it('attach a bearer token only when the provider returns one', () => {
    const { http, ctrl } = setup({}, 'in-memory-token');
    http.get('a', api()).subscribe();
    expect(ctrl.expectOne(`${TEST_API_BASE}/a`).request.headers.get('Authorization')).toBe(
      'Bearer in-memory-token',
    );
  });

  it('sends neither Authorization nor browser credentials when there is no token', () => {
    const { http, ctrl } = setup({}, null);
    http.get('a', api()).subscribe();
    const req = ctrl.expectOne(`${TEST_API_BASE}/a`);
    expect(req.request.headers.has('Authorization')).toBe(false);
    expect(req.request.withCredentials).toBe(false);
  });

  it('time out slow requests with a timeout error', async () => {
    vi.useFakeTimers();
    const { http, ctrl } = setup();
    const result = capture(
      firstValueFrom(
        http.get(
          'slow',
          api((c) => c.set(REQUEST_TIMEOUT_MS, 500)),
        ),
      ),
    );
    const req = ctrl.expectOne(`${TEST_API_BASE}/slow`);
    vi.advanceTimersByTime(501);
    const error = (await result) as AppError;
    expect(error.kind).toBe('timeout');
    expect(req.cancelled).toBe(true);
  });

  it('map 422 ProblemDetails to camelCase field errors and the server title', async () => {
    const { http, ctrl } = setup();
    const result = capture(firstValueFrom(http.post('users', {}, api())));
    ctrl.expectOne(`${TEST_API_BASE}/users`).flush(
      {
        title: 'Please check the highlighted fields.',
        status: 422,
        errors: { Email: ['Email is already registered.'] },
      },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    const error = (await result) as AppError;
    expect(error.kind).toBe('validation');
    expect(error.message).toBe('Please check the highlighted fields.');
    expect(error.fieldErrors).toEqual({ email: ['Email is already registered.'] });
  });

  it('never show raw server text for non-validation errors', async () => {
    const { http, ctrl } = setup();
    const result = capture(firstValueFrom(http.get('boom', api())));
    ctrl
      .expectOne(`${TEST_API_BASE}/boom`)
      .flush(
        { title: 'NullReferenceException at Foo.Bar()' },
        { status: 500, statusText: 'Server Error' },
      );
    const error = (await result) as AppError;
    expect(error.kind).toBe('server');
    expect(error.message).not.toContain('NullReference');
    expect(error.retryable).toBe(true);
  });

  it('raise the session-expired event on 401', async () => {
    const { http, ctrl, session } = setup();
    const result = capture(firstValueFrom(http.get('me', api())));
    ctrl.expectOne(`${TEST_API_BASE}/me`).flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(((await result) as AppError).kind).toBe('unauthorized');
    expect(session.expired()).toBe(true);
  });

  it('rotates a refresh token once and retries the original request', async () => {
    const { http, ctrl, session } = setup({}, 'expired-access');
    TestBed.inject(AuthTokenStore).set('expired-access', 'refresh-1');
    const result = firstValueFrom(http.get<{ ok: boolean }>('api/v1/projects', api()));

    ctrl
      .expectOne(`${TEST_API_BASE}/api/v1/projects`)
      .flush(null, { status: 401, statusText: 'Unauthorized' });
    ctrl.expectOne(`${TEST_API_BASE}/api/v1/auth/refresh`).flush({
      accessToken: 'access-2',
      tokenType: 'Bearer',
      expiresAtUtc: '2026-11-28T06:00:00Z',
      refreshToken: 'refresh-2',
      refreshTokenExpiresAtUtc: '2026-12-05T06:00:00Z',
      user: {
        id: 'user-1',
        email: 'nimal@example.lk',
        isActive: true,
        createdAtUtc: '2026-11-28T05:00:00Z',
      },
    });
    const retried = ctrl.expectOne(`${TEST_API_BASE}/api/v1/projects`);
    expect(retried.request.headers.get('Authorization')).toBe('Bearer access-2');
    retried.flush({ ok: true });

    await expect(result).resolves.toEqual({ ok: true });
    expect(TestBed.inject(AuthTokenStore).refreshToken()).toBe('refresh-2');
    expect(session.expired()).toBe(false);
  });

  it('shares one refresh rotation across concurrent unauthorized requests', async () => {
    const { http, ctrl } = setup({}, 'expired-access');
    TestBed.inject(AuthTokenStore).set('expired-access', 'refresh-1');
    const projects = firstValueFrom(http.get('api/v1/projects', api()));
    const profile = firstValueFrom(http.get('api/v1/users/me', api()));

    ctrl
      .expectOne(`${TEST_API_BASE}/api/v1/projects`)
      .flush(null, { status: 401, statusText: 'Unauthorized' });
    ctrl
      .expectOne(`${TEST_API_BASE}/api/v1/users/me`)
      .flush(null, { status: 401, statusText: 'Unauthorized' });
    const refresh = ctrl.expectOne(`${TEST_API_BASE}/api/v1/auth/refresh`);
    ctrl.expectNone(`${TEST_API_BASE}/api/v1/auth/refresh`);
    refresh.flush({
      accessToken: 'access-2',
      tokenType: 'Bearer',
      expiresAtUtc: '2026-11-28T06:00:00Z',
      refreshToken: 'refresh-2',
      refreshTokenExpiresAtUtc: '2026-12-05T06:00:00Z',
      user: {
        id: 'user-1',
        email: 'nimal@example.lk',
        isActive: true,
        createdAtUtc: '2026-11-28T05:00:00Z',
      },
    });
    ctrl.expectOne(`${TEST_API_BASE}/api/v1/projects`).flush([]);
    ctrl.expectOne(`${TEST_API_BASE}/api/v1/users/me`).flush({ id: 'user-1' });

    await expect(Promise.all([projects, profile])).resolves.toEqual([[], { id: 'user-1' }]);
  });

  it('does not retry after the refreshed request is also unauthorized', async () => {
    const { http, ctrl, session } = setup({}, 'expired-access');
    TestBed.inject(AuthTokenStore).set('expired-access', 'refresh-1');
    const result = capture(firstValueFrom(http.get('api/v1/projects', api())));

    ctrl
      .expectOne(`${TEST_API_BASE}/api/v1/projects`)
      .flush(null, { status: 401, statusText: 'Unauthorized' });
    ctrl.expectOne(`${TEST_API_BASE}/api/v1/auth/refresh`).flush({
      accessToken: 'access-2',
      tokenType: 'Bearer',
      expiresAtUtc: '2026-11-28T06:00:00Z',
      refreshToken: 'refresh-2',
      refreshTokenExpiresAtUtc: '2026-12-05T06:00:00Z',
      user: {
        id: 'user-1',
        email: 'nimal@example.lk',
        isActive: true,
        createdAtUtc: '2026-11-28T05:00:00Z',
      },
    });
    ctrl
      .expectOne(`${TEST_API_BASE}/api/v1/projects`)
      .flush(null, { status: 401, statusText: 'Unauthorized' });
    ctrl.expectNone(`${TEST_API_BASE}/api/v1/auth/refresh`);

    expect(((await result) as AppError).kind).toBe('unauthorized');
    expect(session.expired()).toBe(true);
  });

  it('keep the request correlation id on the error for support reference', async () => {
    const { http, ctrl } = setup();
    const result = capture(firstValueFrom(http.get('gone', api())));
    const req = ctrl.expectOne(`${TEST_API_BASE}/gone`);
    const sent = req.request.headers.get(CORRELATION_HEADER);
    req.flush(null, { status: 404, statusText: 'Not Found' });
    const error = (await result) as AppError;
    expect(error.kind).toBe('not-found');
    expect(error.correlationId).toBe(sent);
  });

  it('show the loading bar while requests are in flight, except when skipped', async () => {
    const { http, ctrl, loading } = setup();
    const a = firstValueFrom(http.get('a', api()));
    http
      .get(
        'poll',
        api((c) => c.set(SKIP_GLOBAL_LOADING, true)),
      )
      .subscribe();
    expect(loading.pendingCount()).toBe(1);
    ctrl.expectOne(`${TEST_API_BASE}/a`).flush({});
    await a;
    expect(loading.isLoading()).toBe(false);
  });

  it('end the loading state on error too', async () => {
    const { http, ctrl, loading } = setup();
    const r = capture(firstValueFrom(http.get('a', api())));
    ctrl.expectOne(`${TEST_API_BASE}/a`).flush(null, { status: 503, statusText: 'Unavailable' });
    await r;
    expect(loading.pendingCount()).toBe(0);
  });
});
