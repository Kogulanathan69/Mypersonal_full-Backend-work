import { TestBed } from '@angular/core/testing';
import { ToastService } from './toast.service';

describe('ToastService', () => {
  let service: ToastService;

  beforeEach(() => {
    vi.useFakeTimers();
    service = TestBed.inject(ToastService);
  });

  afterEach(() => {
    service.clear();
    vi.useRealTimers();
  });

  it('announces every toast, errors assertively', () => {
    service.success('Saved', 'Draft saved');
    expect(service.announcement()).toMatchObject({
      text: 'Saved. Draft saved',
      politeness: 'polite',
    });
    service.error('Failed');
    expect(service.announcement()).toMatchObject({ text: 'Failed', politeness: 'assertive' });
  });

  it('auto-dismisses success after its timeout but keeps errors until dismissed', () => {
    service.success('Saved');
    const errorId = service.error('Failed');
    vi.advanceTimersByTime(5001);
    expect(service.toasts().map((t) => t.id)).toEqual([errorId]);
    service.dismiss(errorId);
    expect(service.toasts()).toHaveLength(0);
  });

  it('shows at most four toasts, dropping the oldest', () => {
    for (let i = 1; i <= 6; i++) service.info(`t${i}`);
    expect(service.toasts().map((t) => t.title)).toEqual(['t3', 't4', 't5', 't6']);
  });
});
