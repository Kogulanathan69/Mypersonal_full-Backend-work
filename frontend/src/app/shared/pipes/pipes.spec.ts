import { AreaPipe, LengthPipe } from './units.pipes';
import { RelativeTimePipe, relativeTime } from './relative-time.pipe';

describe('pipes', () => {
  it('bmLength and bmArea format and handle null', () => {
    expect(new LengthPipe().transform(3900)).toBe('3.90 m');
    expect(new LengthPipe().transform(null)).toBe('—');
    expect(new AreaPipe().transform(316.8, 'perch')).toBe('12.53 perches');
    expect(new AreaPipe().transform(undefined)).toBe('—');
  });

  it('relativeTime describes past and future times', () => {
    const now = Date.parse('2026-11-22T12:00:00Z');
    expect(relativeTime('2026-11-19T12:00:00Z', now)).toBe('3 days ago');
    expect(relativeTime('2026-11-22T14:00:00Z', now)).toBe('in 2 hours');
    expect(relativeTime('2026-11-22T11:59:40Z', now)).toBe('just now');
    expect(relativeTime('not a date', now)).toBe('—');
    expect(new RelativeTimePipe().transform(null)).toBe('—');
  });
});
