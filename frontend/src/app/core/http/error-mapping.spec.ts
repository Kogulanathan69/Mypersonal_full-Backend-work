import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { AppError } from '../api/app-error';
import { USER_MESSAGES } from '../api/user-messages';
import { toAppError } from './error-mapping';

const http = (status: number, body: unknown = null, headers?: HttpHeaders) =>
  new HttpErrorResponse({ status, statusText: 'x', error: body, headers });

describe('toAppError', () => {
  it.each([
    [400, 'validation'],
    [403, 'forbidden'],
    [409, 'conflict'],
    [412, 'conflict'],
    [429, 'rate-limited'],
    [502, 'server'],
    [418, 'unknown'],
  ])('maps HTTP %i to %s', (status, kind) => {
    expect(toAppError(http(status)).kind).toBe(kind);
  });

  it('distinguishes offline from network failure for status 0', () => {
    expect(toAppError(http(0), true).kind).toBe('network');
    expect(toAppError(http(0), false).kind).toBe('offline');
  });

  it('prefers the server correlation header over the request id', () => {
    const e = toAppError(
      http(500, null, new HttpHeaders({ 'X-Correlation-ID': 'srv-1' })),
      true,
      'req-1',
    );
    expect(e.correlationId).toBe('srv-1');
  });

  it('falls back to ProblemDetails traceId', () => {
    expect(toAppError(http(500, { traceId: 'trace-9' })).correlationId).toBe('trace-9');
  });

  it('returns AppErrors unchanged', () => {
    const original = new AppError({ kind: 'config', message: 'x' });
    expect(toAppError(original)).toBe(original);
  });

  it('wraps unknown errors with a generic, safe message', () => {
    const e = toAppError(new TypeError('secret detail'));
    expect(e.kind).toBe('unknown');
    expect(e.message).toBe(USER_MESSAGES.unknown);
    expect(e.detail).toBe('TypeError');
  });

  it('ignores malformed error bodies', () => {
    const e = toAppError(http(422, { errors: 'not-a-record' }));
    expect(e.kind).toBe('validation');
    expect(e.fieldErrors).toEqual({});
  });
});
