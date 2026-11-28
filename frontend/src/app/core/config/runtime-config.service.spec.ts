import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TEST_ENV } from '../../../testing/core-test-providers';
import { BUILD_ENV } from './build-env.token';
import { RuntimeConfigService } from './runtime-config.service';

describe('RuntimeConfigService', () => {
  let service: RuntimeConfigService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: BUILD_ENV, useValue: TEST_ENV },
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(RuntimeConfigService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('starts pending with nothing configured', () => {
    expect(service.status()).toBe('pending');
    expect(service.apiConfigured()).toBe(false);
    expect(service.mapConfigured()).toBe(false);
  });

  it('loads a valid config and applies defaults for omitted sections', async () => {
    const done = service.load();
    http.expectOne('config.json').flush({ api: { mainBaseUrl: ' https://api.example.invalid ' } });
    await done;
    expect(service.status()).toBe('loaded');
    expect(service.config().api.mainBaseUrl).toBe('https://api.example.invalid');
    expect(service.config().api.requestTimeoutMs).toBe(30_000);
    expect(service.config().features.exportFormats).toEqual(['pdf']);
    expect(service.apiConfigured()).toBe(true);
  });

  it('reports invalid config with field paths and keeps the unconfigured defaults', async () => {
    const done = service.load();
    http.expectOne('config.json').flush({ api: { mainBaseUrl: 42, requestTimeoutMs: 5 } });
    await done;
    expect(service.status()).toBe('invalid');
    expect(service.problems().some((p) => p.startsWith('api.mainBaseUrl'))).toBe(true);
    expect(service.problems().some((p) => p.startsWith('api.requestTimeoutMs'))).toBe(true);
    expect(service.apiConfigured()).toBe(false);
  });

  it('reports missing config without throwing', async () => {
    const done = service.load();
    http.expectOne('config.json').flush('not found', { status: 404, statusText: 'Not Found' });
    await done;
    expect(service.status()).toBe('missing');
    expect(service.problems()).toEqual(['Could not load config.json']);
  });

  it('treats a map provider without a token as not configured', async () => {
    const done = service.load();
    http
      .expectOne('config.json')
      .flush({ api: { mainBaseUrl: '' }, map: { provider: 'mapbox', publicToken: '' } });
    await done;
    expect(service.mapConfigured()).toBe(false);
  });
});
