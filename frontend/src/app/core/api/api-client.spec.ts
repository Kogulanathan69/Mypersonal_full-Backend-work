import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import * as z from 'zod/mini';
import { TEST_API_BASE, provideCoreHttpTesting } from '../../../testing/core-test-providers';
import { CORRELATION_HEADER } from '../http/correlation-id.interceptor';
import { ApiClient } from './api-client';
import { AppError } from './app-error';
import { declareContract, removeContractsForTesting } from './contract-status';

const itemSchema = z.object({ id: z.string(), name: z.string() });

function setup(env: { useMocks?: boolean } = {}) {
  TestBed.configureTestingModule({ providers: provideCoreHttpTesting({ env }) });
  return { api: TestBed.inject(ApiClient), http: TestBed.inject(HttpTestingController) };
}

describe('ApiClient', () => {
  beforeEach(() => {
    declareContract({
      id: 'test.verified',
      owner: 'F',
      status: 'VERIFIED',
      method: 'GET',
      path: 'items',
      evidence: 'unit test',
    });
    declareContract({ id: 'test.mock', owner: 'A', status: 'MOCK' });
    declareContract({ id: 'test.blocked', owner: 'B', status: 'BLOCKED' });
  });
  afterEach(() => removeContractsForTesting((id) => id.startsWith('test.')));

  it('calls the configured base URL and returns schema-validated data', async () => {
    const { api, http } = setup();
    const result = firstValueFrom(
      api.get('/items/1', itemSchema, { contractId: 'test.verified', params: { expand: true } }),
    );
    const req = http.expectOne(`${TEST_API_BASE}/items/1?expand=true`);
    expect(req.request.withCredentials).toBe(false);
    expect(req.request.headers.has(CORRELATION_HEADER)).toBe(true);
    req.flush({ id: '1', name: 'Kandy Home', extra: 'ignored' });
    await expect(result).resolves.toEqual({ id: '1', name: 'Kandy Home' });
    http.verify();
  });

  it('sends Idempotency-Key and If-Match when given', async () => {
    const { api, http } = setup();
    const result = firstValueFrom(
      api.post('items', { name: 'x' }, itemSchema, {
        contractId: 'test.verified',
        idempotencyKey: 'k-1',
        ifMatch: 'rev-7',
      }),
    );
    const req = http.expectOne(`${TEST_API_BASE}/items`);
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('Idempotency-Key')).toBe('k-1');
    expect(req.request.headers.get('If-Match')).toBe('rev-7');
    req.flush({ id: '2', name: 'x' });
    await result;
  });

  it('turns a response that breaks the schema into a contract error without leaking values', async () => {
    const { api, http } = setup();
    const result = firstValueFrom(api.get('items/1', itemSchema, { contractId: 'test.verified' }));
    http.expectOne(`${TEST_API_BASE}/items/1`).flush({ id: 1, secretValue: 'do-not-log' });
    const error = await result.catch((e: unknown) => e);
    expect(error).toBeInstanceOf(AppError);
    expect((error as AppError).kind).toBe('contract');
    expect((error as AppError).detail).toContain('id');
    expect((error as AppError).detail).not.toContain('do-not-log');
  });

  it('refuses MOCK contracts outside the mock build, without any network call', async () => {
    const { api, http } = setup({ useMocks: false });
    const error = await firstValueFrom(api.get('x', itemSchema, { contractId: 'test.mock' })).catch(
      (e: unknown) => e,
    );
    expect((error as AppError).kind).toBe('config');
    http.expectNone(() => true);
  });

  it('allows MOCK contracts in the mock build', () => {
    const { api, http } = setup({ useMocks: true });
    api.get('x', itemSchema, { contractId: 'test.mock' }).subscribe();
    http.expectOne(`${TEST_API_BASE}/x`);
  });

  it('refuses BLOCKED contracts in every build', async () => {
    const { api } = setup({ useMocks: true });
    const error = await firstValueFrom(
      api.get('x', itemSchema, { contractId: 'test.blocked' }),
    ).catch((e: unknown) => e);
    expect((error as AppError).kind).toBe('config');
  });

  it('throws loudly for undeclared contracts', () => {
    const { api } = setup();
    expect(() => api.get('x', itemSchema, { contractId: 'nope' })).toThrow(/undeclared contract/);
  });

  it('downloads private binary content through the authenticated API pipeline', async () => {
    const { api, http } = setup();
    const result = firstValueFrom(api.getBlob('items/private.pdf', { contractId: 'test.verified' }));
    const req = http.expectOne(`${TEST_API_BASE}/items/private.pdf`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    const blob = new Blob(['private survey'], { type: 'application/pdf' });
    req.flush(blob);
    await expect(result).resolves.toBeInstanceOf(Blob);
  });
});
