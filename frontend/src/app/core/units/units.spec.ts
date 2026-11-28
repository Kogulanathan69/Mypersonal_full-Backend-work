import {
  formatArea,
  formatLength,
  m2ToPerches,
  m2ToSqft,
  mmToFeetInches,
  perchesToM2,
  sqftToM2,
} from './units';

describe('units', () => {
  it('converts one Sri Lankan perch to 25.29285264 m² and back', () => {
    expect(perchesToM2(1)).toBeCloseTo(25.29285264, 8);
    expect(m2ToPerches(316.8)).toBeCloseTo(12.525, 3);
  });

  it('round-trips square metres and square feet', () => {
    expect(sqftToM2(m2ToSqft(145.62))).toBeCloseTo(145.62, 10);
    expect(m2ToSqft(1)).toBeCloseTo(10.7639, 4);
  });

  it('splits millimetres into feet and inches', () => {
    expect(mmToFeetInches(3352.8)).toEqual({ feet: 11, inches: 0 });
    expect(mmToFeetInches(3962.4)).toEqual({ feet: 13, inches: 0 });
    expect(mmToFeetInches(1524 + 254)).toEqual({ feet: 5, inches: 10 });
  });

  it('formats lengths in metres and feet-inches', () => {
    expect(formatLength(3900)).toBe('3.90 m');
    expect(formatLength(3352.8, 'ft-in')).toBe('11′ 0″');
    expect(formatLength(Number.NaN)).toBe('—');
  });

  it('formats areas in m², sq ft and perches', () => {
    expect(formatArea(12.87)).toBe('12.87 m²');
    expect(formatArea(316.8, 'perch')).toBe('12.53 perches');
    expect(formatArea(100, 'sqft')).toBe('1,076 sq ft');
    expect(formatArea(Number.POSITIVE_INFINITY)).toBe('—');
  });
});
