export interface RegisteredCamera {
  readonly position: readonly [number, number, number];
  readonly target: readonly [number, number, number];
  readonly lensMm: number;
}

/** Blender's default 36 mm horizontal sensor converted to Three.js' vertical field of view. */
export function verticalFovDegrees(lensMm: number, aspect: number, sensorWidthMm = 36): number {
  if (lensMm <= 0 || aspect <= 0 || sensorWidthMm <= 0) return 0;
  const sensorHeight = sensorWidthMm / aspect;
  return (2 * Math.atan(sensorHeight / (2 * lensMm)) * 180) / Math.PI;
}

export const SHOT_04_CAMERA: RegisteredCamera = {
  position: [-22, 9.5, 22],
  target: [0, 2.6, 0],
  lensMm: 35,
};

/** Projects a source-coordinate landmark using the same horizontal-sensor camera as Blender. */
export function projectLandmark(
  point: readonly [number, number, number],
  camera: RegisteredCamera,
  width: number,
  height: number,
): readonly [number, number] {
  const [px, py, pz] = point;
  const [cx, cy, cz] = camera.position;
  const [tx, ty, tz] = camera.target;
  const normalize = (v: [number, number, number]): [number, number, number] => {
    const length = Math.hypot(...v);
    return [v[0] / length, v[1] / length, v[2] / length];
  };
  const cross = (a: readonly number[], b: readonly number[]): [number, number, number] => [
    a[1]! * b[2]! - a[2]! * b[1]!,
    a[2]! * b[0]! - a[0]! * b[2]!,
    a[0]! * b[1]! - a[1]! * b[0]!,
  ];
  const forward = normalize([tx - cx, ty - cy, tz - cz]);
  const right = normalize(cross(forward, [0, 1, 0]));
  const up = cross(right, forward);
  const delta = [px - cx, py - cy, pz - cz];
  const dot = (a: readonly number[], b: readonly number[]) =>
    a[0]! * b[0]! + a[1]! * b[1]! + a[2]! * b[2]!;
  const depth = dot(delta, forward);
  const focalPixels = (width * camera.lensMm) / 36;
  return [
    width / 2 + (dot(delta, right) / depth) * focalPixels,
    height / 2 - (dot(delta, up) / depth) * focalPixels,
  ];
}
