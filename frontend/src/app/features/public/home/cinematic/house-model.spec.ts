import { BUILDABLE, PARCEL, ROOMS, WALLS, edgeLength, polygonArea } from './house-model-data';
import { buildIllustration } from './static-house-illustration';

const inside = (
  [x, z]: readonly [number, number],
  poly: readonly (readonly [number, number])[],
) => {
  let hit = false;
  for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
    const [xi, zi] = poly[i]!;
    const [xj, zj] = poly[j]!;
    if (zi > z !== zj > z && x < ((xj - xi) * (z - zi)) / (zj - zi) + xi) hit = !hit;
  }
  return hit;
};

describe('illustrative house model', () => {
  it('has a parcel of a plausible size and a buildable area inside it', () => {
    expect(polygonArea(PARCEL)).toBeGreaterThan(500);
    expect(polygonArea(BUILDABLE)).toBeLessThan(polygonArea(PARCEL));
    BUILDABLE.forEach((p) => expect(inside(p, PARCEL)).toBe(true));
  });

  it('keeps every room inside the buildable area', () => {
    for (const r of ROOMS) {
      for (const corner of [
        [r.x0, r.z0],
        [r.x1, r.z1],
      ] as const)
        expect(inside(corner, BUILDABLE)).toBe(true);
    }
  });

  it('places every opening within its wall and with head above sill', () => {
    for (const w of WALLS) {
      const len = edgeLength(w.a, w.b);
      for (const o of w.openings) {
        expect(o.from).toBeGreaterThanOrEqual(0);
        expect(o.to).toBeLessThanOrEqual(len + 1e-9);
        expect(o.head).toBeGreaterThan(o.sill);
        expect(o.head).toBeLessThanOrEqual(w.height);
      }
    }
  });

  it('static illustration adds detail stage by stage', () => {
    const counts = [0, 1, 2, 3, 4].map((s) => buildIllustration(s).length);
    expect(counts[1]).toBeGreaterThan(0);
    expect(counts[2]).toBeGreaterThan(counts[1]!);
    expect(buildIllustration(4).some((s) => s.cls.includes('foliage'))).toBe(true);
    expect(buildIllustration(0).some((s) => s.cls.includes('foliage'))).toBe(false);
    expect(buildIllustration(9)).toEqual(buildIllustration(4));
  });
});
