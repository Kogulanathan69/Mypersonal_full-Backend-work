/** Pure Phase 03 timeline contract. The existing five-stage renderer remains unchanged. */
export type CinematicSceneId =
  | 'land-intelligence'
  | 'blueprint-formation'
  | 'architectural-wireframe'
  | 'material-transformation'
  | 'photorealistic-exterior'
  | 'interior-camera-journey'
  | 'editorial-transition'
  | 'product-experience';

export type RenderingMode = 'realtime' | 'prerendered' | 'static' | 'document';
export type MediaRequirement = 'none' | 'exterior' | 'interior';

export interface CameraStateContract {
  readonly posture: 'survey' | 'plan' | 'wireframe' | 'registration' | 'media' | 'document';
  readonly movement: 'dolly' | 'settle' | 'lower' | 'locked' | 'prerendered' | 'none';
}

export interface CinematicSceneDefinition {
  readonly id: CinematicSceneId;
  readonly number: string;
  readonly title: string;
  readonly text: string;
  readonly start: number;
  readonly end: number;
  readonly preferredMode: RenderingMode;
  readonly media: MediaRequirement;
  readonly camera: CameraStateContract;
}

export interface TimelineCapability {
  readonly reducedMotion: boolean;
  readonly webgl: boolean;
  readonly exteriorMedia: boolean;
  readonly interiorMedia: boolean;
}

export interface ResolvedCinematicScene {
  readonly scene: CinematicSceneDefinition;
  readonly progress: number;
  readonly localProgress: number;
  readonly mode: RenderingMode;
  readonly missingMedia: boolean;
  readonly atStart: boolean;
  readonly atEnd: boolean;
}

export const CINEMATIC_SCENES: readonly CinematicSceneDefinition[] = [
  {
    id: 'land-intelligence',
    number: '01',
    title: 'Read the land',
    text: 'Parcel edges and measured survey information establish the planning boundary.',
    start: 0,
    end: 0.14,
    preferredMode: 'realtime',
    media: 'none',
    camera: { posture: 'survey', movement: 'dolly' },
  },
  {
    id: 'blueprint-formation',
    number: '02',
    title: 'Form the plan',
    text: 'Rooms, openings and circulation take shape as an editable preliminary 2D layout.',
    start: 0.14,
    end: 0.29,
    preferredMode: 'realtime',
    media: 'none',
    camera: { posture: 'plan', movement: 'settle' },
  },
  {
    id: 'architectural-wireframe',
    number: '03',
    title: 'Understand the space',
    text: 'A consistent illustrative residence makes spatial relationships easier to read.',
    start: 0.29,
    end: 0.44,
    preferredMode: 'realtime',
    media: 'none',
    camera: { posture: 'wireframe', movement: 'lower' },
  },
  {
    id: 'material-transformation',
    number: '04',
    title: 'From line to material',
    text: 'Technical geometry resolves into a coherent architectural illustration.',
    start: 0.44,
    end: 0.57,
    preferredMode: 'realtime',
    media: 'none',
    camera: { posture: 'registration', movement: 'locked' },
  },
  {
    id: 'photorealistic-exterior',
    number: '05',
    title: 'Approach the residence',
    text: 'The same illustrative residence is framed at a more human scale.',
    start: 0.57,
    end: 0.7,
    preferredMode: 'prerendered',
    media: 'exterior',
    camera: { posture: 'media', movement: 'prerendered' },
  },
  {
    id: 'interior-camera-journey',
    number: '06',
    title: 'Move through the idea',
    text: 'An illustrative interior communicates continuity, light and spatial intent.',
    start: 0.7,
    end: 0.8,
    preferredMode: 'prerendered',
    media: 'interior',
    camera: { posture: 'media', movement: 'prerendered' },
  },
  {
    id: 'editorial-transition',
    number: '07',
    title: 'Return to the drawing table',
    text: 'The cinematic study gives way to the practical planning workflow.',
    start: 0.8,
    end: 0.87,
    preferredMode: 'document',
    media: 'none',
    camera: { posture: 'document', movement: 'none' },
  },
  {
    id: 'product-experience',
    number: '08',
    title: 'Plan with evidence',
    text: 'Review boundaries, planning evidence and layout alternatives with professional input.',
    start: 0.87,
    end: 1,
    preferredMode: 'document',
    media: 'none',
    camera: { posture: 'document', movement: 'none' },
  },
] as const;

export function clampProgress(progress: number): number {
  if (!Number.isFinite(progress)) return 0;
  return Math.min(1, Math.max(0, progress));
}

export function sceneAt(progress: number): CinematicSceneDefinition {
  const value = clampProgress(progress);
  return CINEMATIC_SCENES.find((scene, index) => {
    const isLast = index === CINEMATIC_SCENES.length - 1;
    return value >= scene.start && (value < scene.end || (isLast && value <= scene.end));
  })!;
}

export function sceneLocalProgress(scene: CinematicSceneDefinition, progress: number): number {
  const value = clampProgress(progress);
  return Math.min(1, Math.max(0, (value - scene.start) / (scene.end - scene.start)));
}

export function renderingMode(
  scene: CinematicSceneDefinition,
  capability: TimelineCapability,
): { mode: RenderingMode; missingMedia: boolean } {
  if (scene.preferredMode === 'document') return { mode: 'document', missingMedia: false };
  if (capability.reducedMotion) return { mode: 'static', missingMedia: false };
  if (scene.preferredMode === 'realtime') {
    return { mode: capability.webgl ? 'realtime' : 'static', missingMedia: false };
  }

  const available =
    scene.media === 'exterior' ? capability.exteriorMedia : capability.interiorMedia;
  return { mode: available ? 'prerendered' : 'static', missingMedia: !available };
}

export function resolveCinematicScene(
  progress: number,
  capability: TimelineCapability,
): ResolvedCinematicScene {
  const normalized = clampProgress(progress);
  const scene = sceneAt(normalized);
  const resolvedMode = renderingMode(scene, capability);
  return {
    scene,
    progress: normalized,
    localProgress: sceneLocalProgress(scene, normalized),
    ...resolvedMode,
    atStart: normalized === scene.start,
    atEnd: normalized === 1,
  };
}
