import { TestBed } from '@angular/core/testing';
import { AppError } from '../api/app-error';
import { ToastService } from '../feedback/toast.service';
import { GlobalErrorHandler } from './global-error-handler';

describe('GlobalErrorHandler', () => {
  let handler: GlobalErrorHandler;
  let toasts: ToastService;
  let log: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [GlobalErrorHandler] });
    handler = TestBed.inject(GlobalErrorHandler);
    toasts = TestBed.inject(ToastService);
    log = vi.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => {
    log.mockRestore();
    toasts.clear();
  });

  it('logs a redacted summary and shows a safe toast with the reference id', () => {
    handler.handleError(
      new AppError({
        kind: 'server',
        message: 'Something went wrong.',
        status: 500,
        correlationId: 'c-42',
        detail: 'Bearer abc.def',
      }),
    );
    const line = String(log.mock.calls[0]?.[0]);
    expect(line).toContain('server');
    expect(line).toContain('c-42');
    expect(line).not.toContain('abc.def');
    expect(toasts.toasts()[0]).toMatchObject({
      kind: 'error',
      title: 'Something went wrong.',
      message: 'Reference: c-42',
    });
  });

  it('does not toast 401s (the session dialog handles them)', () => {
    handler.handleError(new AppError({ kind: 'unauthorized', message: 'x' }));
    expect(toasts.toasts()).toHaveLength(0);
  });

  it('unwraps promise rejections and redacts emails from plain errors', () => {
    handler.handleError({ rejection: new Error('failed for nimal@example.lk') });
    const line = String(log.mock.calls[0]?.[0]);
    expect(line).toContain('[REDACTED_EMAIL]');
    expect(line).not.toContain('nimal@example.lk');
    expect(toasts.toasts()).toHaveLength(1);
  });
});
