import { describe, expect, it } from 'vitest';
import { SHOT_04_CAMERA, projectLandmark, verticalFovDegrees } from './registered-camera';

describe('registered camera conversion', () => {
  it('converts Blender horizontal sensor fit to Three.js vertical FOV', () => {
    expect(verticalFovDegrees(35, 16 / 9)).toBeCloseTo(32.268, 2);
  });

  it('projects the camera target to the exact frame centre', () => {
    const [x, y] = projectLandmark(SHOT_04_CAMERA.target, SHOT_04_CAMERA, 640, 360);
    expect(x).toBeCloseTo(320, 10);
    expect(y).toBeCloseTo(180, 10);
  });

  it('keeps the authoritative projection deterministic', () => {
    const landmark: [number, number, number] = [-8, 6.2, -4.5];
    expect(projectLandmark(landmark, SHOT_04_CAMERA, 640, 360)).toEqual(
      projectLandmark(landmark, SHOT_04_CAMERA, 640, 360),
    );
  });
});
