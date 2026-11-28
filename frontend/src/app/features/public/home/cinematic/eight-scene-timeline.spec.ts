import {
  CINEMATIC_SCENES,
  TimelineCapability,
  clampProgress,
  resolveCinematicScene,
  sceneAt,
  sceneLocalProgress,
} from './eight-scene-timeline';

const capable: TimelineCapability = {
  reducedMotion: false,
  webgl: true,
  exteriorMedia: true,
  interiorMedia: true,
};

describe('eight-scene cinematic timeline', () => {
  it('defines contiguous deterministic scene boundaries from zero to one', () => {
    expect(CINEMATIC_SCENES).toHaveLength(8);
    expect(CINEMATIC_SCENES[0]?.start).toBe(0);
    expect(CINEMATIC_SCENES.at(-1)?.end).toBe(1);
    for (let i = 1; i < CINEMATIC_SCENES.length; i++) {
      expect(CINEMATIC_SCENES[i]?.start).toBe(CINEMATIC_SCENES[i - 1]?.end);
    }
  });

  it.each([
    [0, 'land-intelligence'],
    [0.139999, 'land-intelligence'],
    [0.14, 'blueprint-formation'],
    [0.29, 'architectural-wireframe'],
    [0.44, 'material-transformation'],
    [0.57, 'photorealistic-exterior'],
    [0.7, 'interior-camera-journey'],
    [0.8, 'editorial-transition'],
    [0.87, 'product-experience'],
    [1, 'product-experience'],
  ] as const)('maps progress %s to %s', (progress, id) => {
    expect(sceneAt(progress).id).toBe(id);
  });

  it('clamps invalid and out-of-range progress', () => {
    expect(clampProgress(-1)).toBe(0);
    expect(clampProgress(2)).toBe(1);
    expect(clampProgress(Number.NaN)).toBe(0);
    expect(resolveCinematicScene(2, capable).progress).toBe(1);
  });

  it('computes scene-local progress', () => {
    const scene = CINEMATIC_SCENES[1]!;
    expect(sceneLocalProgress(scene, 0.14)).toBe(0);
    expect(sceneLocalProgress(scene, 0.215)).toBeCloseTo(0.5);
    expect(sceneLocalProgress(scene, 0.29)).toBe(1);
  });

  it('is deterministic while scrolling forward and backward', () => {
    const forward = [0.1, 0.3, 0.6, 0.9].map((p) => resolveCinematicScene(p, capable).scene.id);
    const reverse = [0.9, 0.6, 0.3, 0.1].map((p) => resolveCinematicScene(p, capable).scene.id);
    expect(reverse).toEqual([...forward].reverse());
    expect(resolveCinematicScene(0.6, capable)).toEqual(resolveCinematicScene(0.6, capable));
  });

  it('selects static rendering when reduced motion is requested', () => {
    const reduced = { ...capable, reducedMotion: true };
    expect(resolveCinematicScene(0.2, reduced).mode).toBe('static');
    expect(resolveCinematicScene(0.6, reduced).mode).toBe('static');
    expect(resolveCinematicScene(0.82, reduced).mode).toBe('document');
  });

  it('falls back when WebGL or required media is unavailable', () => {
    expect(resolveCinematicScene(0.2, { ...capable, webgl: false }).mode).toBe('static');
    const exterior = resolveCinematicScene(0.6, { ...capable, exteriorMedia: false });
    expect(exterior.mode).toBe('static');
    expect(exterior.missingMedia).toBe(true);
    const interior = resolveCinematicScene(0.75, { ...capable, interiorMedia: false });
    expect(interior.mode).toBe('static');
    expect(interior.missingMedia).toBe(true);
  });

  it('preserves document rendering for editorial and product scenes', () => {
    const unavailable = {
      reducedMotion: true,
      webgl: false,
      exteriorMedia: false,
      interiorMedia: false,
    };
    expect(resolveCinematicScene(0.82, unavailable).mode).toBe('document');
    expect(resolveCinematicScene(0.9, unavailable).mode).toBe('document');
  });
});

