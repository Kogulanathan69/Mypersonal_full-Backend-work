import {
  HERO_END,
  STAGES,
  STORY_SCENES,
  cameraAt,
  captionOpacity,
  ease,
  sceneIndexAt,
  segment,
  stage,
} from './timeline';

describe('cinematic timeline', () => {
  it('defines five contiguous scenes covering 0 → 1', () => {
    expect(STORY_SCENES.map((s) => s.id)).toEqual([
      'land',
      'blueprint',
      'structure',
      'transformation',
      'vision',
    ]);
    expect(STORY_SCENES[0]!.start).toBe(0);
    for (let i = 1; i < STORY_SCENES.length; i++) {
      expect(STORY_SCENES[i]!.start).toBe(STORY_SCENES[i - 1]!.end);
    }
    expect(STORY_SCENES.at(-1)!.end).toBeGreaterThanOrEqual(1);
  });

  it('maps progress to the right scene, including the very end', () => {
    expect(sceneIndexAt(0)).toBe(0);
    expect(sceneIndexAt(0.3)).toBe(1);
    expect(sceneIndexAt(0.5)).toBe(2);
    expect(sceneIndexAt(0.7)).toBe(3);
    expect(sceneIndexAt(1)).toBe(4);
  });

  it('segment and ease are clamped and monotonic', () => {
    expect(segment(-1, 0, 1)).toBe(0);
    expect(segment(2, 0, 1)).toBe(1);
    expect(segment(0.5, 0.5, 0.5)).toBe(1);
    let prev = -1;
    for (let t = 0; t <= 1; t += 0.05) {
      const v = ease(t);
      expect(v).toBeGreaterThanOrEqual(prev);
      prev = v;
    }
    expect(ease(0)).toBe(0);
    expect(ease(1)).toBe(1);
  });

  it('every stage range is inside 0 → 1 and ordered', () => {
    for (const [name, [a, b]] of Object.entries(STAGES)) {
      expect(a, name).toBeGreaterThanOrEqual(0);
      expect(b, name).toBeLessThanOrEqual(1);
      expect(b, name).toBeGreaterThan(a);
    }
  });

  it('is reversible: the same progress always gives the same state', () => {
    for (const p of [0.13, 0.47, 0.81]) {
      expect(stage(p, 'wallsGround')).toBe(stage(p, 'wallsGround'));
      expect(cameraAt(p)).toEqual(cameraAt(p));
    }
  });

  it('walls rise only during the structure scene and materials only after it', () => {
    expect(stage(0.39, 'wallsGround')).toBe(0);
    expect(stage(0.5, 'wallsGround')).toBe(1);
    expect(stage(0.61, 'materials')).toBe(0);
    expect(stage(0.8, 'materials')).toBe(1);
  });

  it('shows one caption at a time after the hero, and keeps the last at the end', () => {
    expect(captionOpacity(0, 0)).toBe(0);
    expect(captionOpacity(0, HERO_END + 0.1)).toBeGreaterThan(0.9);
    expect(captionOpacity(1, 0.3)).toBe(1);
    expect(captionOpacity(0, 0.3)).toBe(0);
    expect(captionOpacity(4, 1)).toBe(1);
  });

  it('camera path is continuous and pulls back on mobile', () => {
    const a = cameraAt(0.399);
    const b = cameraAt(0.401);
    a.position.forEach((v, i) => expect(Math.abs(v - b.position[i]!)).toBeLessThan(0.5));
    const desk = cameraAt(0.9);
    const mob = cameraAt(0.9, true);
    expect(Math.hypot(...mob.position)).toBeGreaterThan(Math.hypot(...desk.position));
    expect(Math.abs(cameraAt(0, true).target[0])).toBeLessThan(Math.abs(cameraAt(0).target[0]));
  });
});
