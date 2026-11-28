import { Pipe, PipeTransform } from '@angular/core';

const UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 31_536_000],
  ['month', 2_592_000],
  ['week', 604_800],
  ['day', 86_400],
  ['hour', 3_600],
  ['minute', 60],
];

/** Pure function so it can be unit-tested with a fixed "now". */
export function relativeTime(
  value: Date | string | number,
  now: number = Date.now(),
  locale = 'en',
): string {
  const time = new Date(value).getTime();
  if (Number.isNaN(time)) return '—';
  const seconds = Math.round((time - now) / 1000);
  const fmt = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });
  for (const [unit, size] of UNITS) {
    if (Math.abs(seconds) >= size) return fmt.format(Math.round(seconds / size), unit);
  }
  return fmt.format(0, 'minute') === 'this minute' ? 'just now' : fmt.format(0, 'minute');
}

/**
 * {{ project.updatedAt | bmRelativeTime }} → "3 days ago". Impure-free: updates when the input
 * changes, which is enough for list views. Show the exact date in a title/tooltip as well.
 */
@Pipe({ name: 'bmRelativeTime' })
export class RelativeTimePipe implements PipeTransform {
  transform(value: Date | string | number | null | undefined): string {
    return value == null ? '—' : relativeTime(value);
  }
}
