/**
 * Unit conversion and formatting. Geometry is stored in millimetres (lengths) and
 * square metres (areas); these helpers only convert for display.
 */
export type LengthUnit = 'm' | 'ft-in';
export type AreaUnit = 'm2' | 'sqft' | 'perch';

export const MM_PER_FOOT = 304.8;
export const MM_PER_INCH = 25.4;
export const SQFT_PER_M2 = 10.763910416709722;
/** Sri Lankan perch: 272.25 sq ft = 25.29285264 m². */
export const M2_PER_PERCH = 25.29285264;

export const mmToMetres = (mm: number): number => mm / 1000;
export const metresToMm = (m: number): number => m * 1000;
export const m2ToSqft = (m2: number): number => m2 * SQFT_PER_M2;
export const sqftToM2 = (sqft: number): number => sqft / SQFT_PER_M2;
export const m2ToPerches = (m2: number): number => m2 / M2_PER_PERCH;
export const perchesToM2 = (perches: number): number => perches * M2_PER_PERCH;

/** Splits a length into whole feet and inches, rounding to the nearest inch. */
export function mmToFeetInches(mm: number): { feet: number; inches: number } {
  const totalInches = Math.round(Math.abs(mm) / MM_PER_INCH);
  const sign = mm < 0 ? -1 : 1;
  return { feet: sign * Math.floor(totalInches / 12), inches: totalInches % 12 };
}

export function formatLength(mm: number, unit: LengthUnit = 'm', decimals = 2): string {
  if (!Number.isFinite(mm)) return '—';
  if (unit === 'ft-in') {
    const { feet, inches } = mmToFeetInches(mm);
    return `${feet}′ ${inches}″`;
  }
  return `${mmToMetres(mm).toFixed(decimals)} m`;
}

export function formatArea(m2: number, unit: AreaUnit = 'm2', decimals = 2): string {
  if (!Number.isFinite(m2)) return '—';
  switch (unit) {
    case 'sqft':
      return `${m2ToSqft(m2).toLocaleString('en-LK', { maximumFractionDigits: 0 })} sq ft`;
    case 'perch':
      return `${m2ToPerches(m2).toFixed(decimals)} perches`;
    default:
      return `${m2.toFixed(decimals)} m²`;
  }
}
