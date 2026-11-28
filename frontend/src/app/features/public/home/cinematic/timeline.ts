/**
 * Scroll timeline for the cinematic Home Page. Pure functions only (no Three.js), so the
 * sequence can be unit-tested and shared by the WebGL engine and the captions.
 * Progress `p` runs 0 → 1 as the visitor scrolls through the story; scrolling back reverses it.
 */

export interface StoryScene {
  readonly id: 'land' | 'blueprint' | 'structure' | 'transformation' | 'vision';
  readonly number: string;
  readonly title: string;
  readonly text: string;
  readonly start: number;
  readonly end: number;
}

export const STORY_SCENES: readonly StoryScene[] = [
  {
    id: 'land',
    number: '01',
    title: 'The land',
    text: 'Every plan starts from your parcel: its boundary, corner points and measured edges.',
    start: 0,
    end: 0.2,
  },
  {
    id: 'blueprint',
    number: '02',
    title: 'Intelligent blueprint',
    text: 'Rooms, walls, doors and windows are laid out in 2D inside the buildable area.',
    start: 0.2,
    end: 0.4,
  },
  {
    id: 'structure',
    number: '03',
    title: 'Blueprint to structure',
    text: 'Walls rise from the plan so you can understand the spaces you are shaping.',
    start: 0.4,
    end: 0.62,
  },
  {
    id: 'transformation',
    number: '04',
    title: 'Architectural transformation',
    text: 'Technical lines give way to concrete, timber, glass and warm light.',
    start: 0.62,
    end: 0.82,
  },
  {
    id: 'vision',
    number: '05',
    title: 'Your vision',
    text: 'An illustrative home. BuildMind delivers your plan as a reviewed, editable 2D drawing.',
    start: 0.82,
    end: 1.0001,
  },
];

/** Hero text is shown at the very start, then hands over to the scene captions. */
export const HERO_END = 0.06;

export const clamp01 = (v: number): number => (v < 0 ? 0 : v > 1 ? 1 : v);

/** Local 0→1 progress of `p` inside [start, end]. */
export function segment(p: number, start: number, end: number): number {
  if (end <= start) return p >= end ? 1 : 0;
  return clamp01((p - start) / (end - start));
}

/** Smooth ease in-out (smoothstep). */
export const ease = (t: number): number => {
  const c = clamp01(t);
  return c * c * (3 - 2 * c);
};

export const lerp = (a: number, b: number, t: number): number => a + (b - a) * t;

export function sceneIndexAt(p: number): number {
  const i = STORY_SCENES.findIndex((s) => p >= s.start && p < s.end);
  return i === -1 ? STORY_SCENES.length - 1 : i;
}

/**
 * Opacity of a scene caption: fades in over the first 25% of its range and out over the last
 * 20%, except the first caption (visible after the hero) and the last (stays at the end).
 */
export function captionOpacity(index: number, p: number): number {
  const s = STORY_SCENES[index];
  if (!s) return 0;
  const len = s.end - s.start;
  const start = index === 0 ? HERO_END : s.start;
  const fadeIn = segment(p, start, start + len * 0.25);
  const fadeOut = index === STORY_SCENES.length - 1 ? 0 : segment(p, s.end - len * 0.2, s.end);
  return ease(fadeIn) * (1 - ease(fadeOut));
}

export interface CameraPose {
  readonly position: readonly [number, number, number];
  readonly target: readonly [number, number, number];
}

interface Key extends CameraPose {
  readonly p: number;
}

/** Camera path keyframes (metres). Mobile poses pull back so the house fits a tall screen. */
const KEYS: readonly Key[] = [
  { p: 0, position: [-6, 35, 35], target: [-13, 0, 3] },
  { p: 0.2, position: [7, 31, 25], target: [-1, 0, 0] },
  { p: 0.4, position: [1, 33, 7], target: [0, 0, 0] },
  { p: 0.62, position: [25, 15, 24], target: [0, 2.2, 0] },
  { p: 0.82, position: [-22, 9.5, 22], target: [0, 2.6, 0] },
  { p: 1, position: [-13, 4.6, 21], target: [0.5, 2.8, 0] },
];

export function cameraAt(p: number, mobile = false): CameraPose {
  const t = clamp01(p);
  let i = 0;
  while (i < KEYS.length - 2 && t > KEYS[i + 1]!.p) i++;
  const a = KEYS[i]!;
  const b = KEYS[i + 1]!;
  const k = ease(segment(t, a.p, b.p));
  const scale = mobile ? 1.45 : 1;
  const heightScale = mobile ? 1.25 : 1;
  const pos = [0, 1, 2].map(
    (j) => lerp(a.position[j]!, b.position[j]!, k) * (j === 1 ? heightScale : scale),
  ) as [number, number, number];
  const target = [0, 1, 2].map((j) => lerp(a.target[j]!, b.target[j]!, k)) as [
    number,
    number,
    number,
  ];
  // On phones the hero text covers the full width, so keep the land centred.
  if (mobile) target[0] *= 0.25;
  return { position: pos, target };
}

/** Named sub-stages, each a [start, end] range of overall progress. */
export const STAGES = {
  grid: [0, 0.06],
  boundary: [0.02, 0.13],
  points: [0.08, 0.16],
  measurements: [0.12, 0.2],
  landLabelsFade: [0.34, 0.42],
  setback: [0.15, 0.22],
  planLines: [0.2, 0.33],
  roomLabels: [0.27, 0.36],
  roomLabelsFade: [0.43, 0.5],
  openingsOutline: [0.31, 0.4],
  wallsGround: [0.4, 0.5],
  wallsUpper: [0.47, 0.56],
  frames: [0.5, 0.58],
  roof: [0.53, 0.62],
  materials: [0.62, 0.78],
  glass: [0.68, 0.8],
  warmLight: [0.62, 0.84],
  wireFade: [0.7, 0.84],
  landscape: [0.7, 0.86],
  blueprintFade: [0.64, 0.8],
  interiorGlow: [0.84, 0.96],
} as const satisfies Record<string, readonly [number, number]>;

export type StageName = keyof typeof STAGES;

export function stage(p: number, name: StageName): number {
  const [a, b] = STAGES[name];
  return ease(segment(p, a, b));
}
