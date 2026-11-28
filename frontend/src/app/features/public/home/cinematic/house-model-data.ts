/**
 * ILLUSTRATIVE marketing model for the public Home Page (owner: A — Customer Portal).
 * A pre-designed tropical-modern house on an invented parcel. It is NOT generated from any
 * customer's plan and is not a BuildMind result. Units: metres. Axes: x east, z south, y up.
 * Shared by the Three.js scene and the static SVG fallback so both always show the same house.
 */
export type Vec2 = readonly [number, number];

export interface Rect {
  readonly x0: number;
  readonly z0: number;
  readonly x1: number;
  readonly z1: number;
}

export interface Room extends Rect {
  readonly name: string;
}

/** An opening in a wall run, measured along the wall from its start. */
export interface Opening {
  readonly kind: 'door' | 'window';
  readonly from: number;
  readonly to: number;
  readonly sill: number;
  readonly head: number;
}

export interface WallRun {
  readonly a: Vec2;
  readonly b: Vec2;
  readonly thickness: number;
  readonly baseY: number;
  readonly height: number;
  readonly level: 0 | 1;
  readonly openings: readonly Opening[];
}

export interface Slab extends Rect {
  readonly y: number;
  readonly thickness: number;
  readonly level: 0 | 1;
}

/** Invented, irregular six-sided parcel (~650 m²). */
export const PARCEL: readonly Vec2[] = [
  [-15, -10],
  [13, -11.5],
  [16, 3],
  [11.5, 11],
  [-12, 10.5],
  [-16.5, 0.5],
];

/** Buildable area after illustrative setbacks (not a regulation result). */
export const BUILDABLE: readonly Vec2[] = [
  [-12.5, -7.5],
  [10.5, -8.7],
  [12.8, 2.6],
  [9.2, 8.3],
  [-10.2, 7.9],
  [-13.6, 0.6],
];

export const FLOOR_HEIGHT = 3.2;
export const WALL_HEIGHT = 3.0;

/** Ground-floor rooms (the 2D blueprint). */
export const ROOMS: readonly Room[] = [
  { name: 'Living', x0: -8, z0: -4.5, x1: -1, z1: 1 },
  { name: 'Kitchen & dining', x0: -1, z0: -4.5, x1: 4, z1: 1 },
  { name: 'Bedroom', x0: 4, z0: -4.5, x1: 8, z1: -1 },
  { name: 'Bath', x0: 4, z0: -1, x1: 8, z1: 1 },
  { name: 'Bedroom', x0: 4, z0: 1, x1: 8, z1: 4.5 },
  { name: 'Entrance & stair', x0: -1, z0: 1, x1: 4, z1: 4.5 },
  { name: 'Verandah', x0: -8, z0: 1, x1: -1, z1: 4.5 },
];

const W = (kind: Opening['kind'], from: number, to: number): Opening =>
  kind === 'door'
    ? { kind, from, to, sill: 0, head: 2.2 }
    : { kind, from, to, sill: 0.9, head: 2.3 };

/** Exterior and interior walls, with openings. Upper floor is a cantilevered volume. */
export const WALLS: readonly WallRun[] = [
  // Ground floor exterior (verandah side z=4.5 between x -8..-1 is open)
  {
    a: [-8, -4.5],
    b: [8, -4.5],
    thickness: 0.22,
    baseY: 0,
    height: WALL_HEIGHT,
    level: 0,
    openings: [W('window', 1.2, 5.6), W('window', 8.2, 10.6), W('window', 12.8, 14.8)],
  },
  {
    a: [8, -4.5],
    b: [8, 4.5],
    thickness: 0.22,
    baseY: 0,
    height: WALL_HEIGHT,
    level: 0,
    openings: [W('window', 1, 2.8), W('window', 6.2, 8)],
  },
  {
    a: [8, 4.5],
    b: [-1, 4.5],
    thickness: 0.22,
    baseY: 0,
    height: WALL_HEIGHT,
    level: 0,
    openings: [W('window', 1, 2.8), W('door', 5.6, 7)],
  },
  {
    a: [-8, 1],
    b: [-8, -4.5],
    thickness: 0.22,
    baseY: 0,
    height: WALL_HEIGHT,
    level: 0,
    openings: [W('window', 1, 4.5)],
  },
  // Living / verandah glazing line
  {
    a: [-1, 1],
    b: [-8, 1],
    thickness: 0.12,
    baseY: 0,
    height: WALL_HEIGHT,
    level: 0,
    openings: [W('door', 0.6, 6.4)],
  },
  // Interior partitions
  {
    a: [-1, -4.5],
    b: [-1, 1],
    thickness: 0.12,
    baseY: 0,
    height: WALL_HEIGHT,
    level: 0,
    openings: [W('door', 3.6, 5.2)],
  },
  {
    a: [4, -4.5],
    b: [4, 4.5],
    thickness: 0.12,
    baseY: 0,
    height: WALL_HEIGHT,
    level: 0,
    openings: [W('door', 2, 2.9), W('door', 6, 6.9)],
  },
  {
    a: [4, -1],
    b: [8, -1],
    thickness: 0.12,
    baseY: 0,
    height: WALL_HEIGHT,
    level: 0,
    openings: [],
  },
  { a: [4, 1], b: [8, 1], thickness: 0.12, baseY: 0, height: WALL_HEIGHT, level: 0, openings: [] },
  {
    a: [-1, 1],
    b: [-1, 4.5],
    thickness: 0.12,
    baseY: 0,
    height: WALL_HEIGHT,
    level: 0,
    openings: [],
  },
  // Upper floor volume (x -3..9.5, z -3.5..4)
  {
    a: [-3, -3.5],
    b: [9.5, -3.5],
    thickness: 0.22,
    baseY: FLOOR_HEIGHT,
    height: WALL_HEIGHT,
    level: 1,
    openings: [W('window', 1.5, 6), W('window', 7.5, 11)],
  },
  {
    a: [9.5, -3.5],
    b: [9.5, 4],
    thickness: 0.22,
    baseY: FLOOR_HEIGHT,
    height: WALL_HEIGHT,
    level: 1,
    openings: [{ kind: 'window', from: 1, to: 6.5, sill: 0.4, head: 2.6 }],
  },
  {
    a: [9.5, 4],
    b: [-3, 4],
    thickness: 0.22,
    baseY: FLOOR_HEIGHT,
    height: WALL_HEIGHT,
    level: 1,
    openings: [{ kind: 'window', from: 1, to: 11.5, sill: 0.1, head: 2.7 }],
  },
  {
    a: [-3, 4],
    b: [-3, -3.5],
    thickness: 0.22,
    baseY: FLOOR_HEIGHT,
    height: WALL_HEIGHT,
    level: 1,
    openings: [{ kind: 'window', from: 1.5, to: 6, sill: 0.1, head: 2.7 }],
  },
];

/** Floor and roof slabs with tropical overhangs. */
export const SLABS: readonly Slab[] = [
  { x0: -8.6, z0: -5.1, x1: 8.6, z1: 5.6, y: FLOOR_HEIGHT - 0.2, thickness: 0.25, level: 0 },
  { x0: -4.2, z0: -4.6, x1: 10.8, z1: 5.4, y: FLOOR_HEIGHT * 2 - 0.15, thickness: 0.3, level: 1 },
];

/** Vertical timber louvres on the upper-floor south facade. */
export const LOUVRES = {
  x0: -2.4,
  x1: 3.4,
  z: 4.35,
  baseY: FLOOR_HEIGHT + 0.1,
  height: 2.9,
  count: 18,
} as const;

export const POOL: Rect = { x0: -13, z0: 2, x1: -9.2, z1: 7.4 };

/** Kept clear of the final camera's line of sight (from the south-west). */
export const TREES: readonly Vec2[] = [
  [-13.5, -6.5],
  [10.5, -8.2],
  [13.6, 1.2],
  [9.6, 8.4],
  [-14.4, -1.6],
  [2, -9.2],
];

export const PATH_STONES: readonly Vec2[] = [
  [2.6, 5.6],
  [2.3, 6.7],
  [2.7, 7.8],
  [2.4, 8.9],
  [2.8, 10],
];

/** Overall plan dimensions used for illustrative measurement annotations. */
export const FOOTPRINT: Rect = { x0: -8, z0: -4.5, x1: 8, z1: 4.5 };

export function polygonArea(points: readonly Vec2[]): number {
  let sum = 0;
  for (let i = 0; i < points.length; i++) {
    const [x1, z1] = points[i]!;
    const [x2, z2] = points[(i + 1) % points.length]!;
    sum += x1 * z2 - x2 * z1;
  }
  return Math.abs(sum) / 2;
}

export function edgeLength(a: Vec2, b: Vec2): number {
  return Math.hypot(b[0] - a[0], b[1] - a[1]);
}
