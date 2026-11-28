import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const width = 640;
const height = 360;
const cameras = JSON.parse(await readFile('asset-production/cinematic-v2/cameras.json', 'utf8'));
const shot = cameras.shots.find(({ id }) => id === 'SHOT-04');
const landmarks = [
  ['lower-nw', [-8.6, 3.25, -5.1]],
  ['lower-ne', [8.6, 3.25, -5.1]],
  ['lower-se', [8.6, 3.25, 5.6]],
  ['lower-sw', [-8.6, 3.25, 5.6]],
  ['upper-nw', [-4.2, 6.55, -4.6]],
  ['upper-ne', [10.8, 6.55, -4.6]],
  ['upper-se', [10.8, 6.55, 5.4]],
  ['upper-sw', [-4.2, 6.55, 5.4]],
  ['footprint-nw', [-8, 0, -4.5]],
  ['footprint-ne', [8, 0, -4.5]],
  ['footprint-se', [8, 0, 4.5]],
  ['footprint-sw', [-8, 0, 4.5]],
];

const sub = (a, b) => a.map((value, index) => value - b[index]);
const dot = (a, b) => a.reduce((sum, value, index) => sum + value * b[index], 0);
const cross = (a, b) => [
  a[1] * b[2] - a[2] * b[1],
  a[2] * b[0] - a[0] * b[2],
  a[0] * b[1] - a[1] * b[0],
];
const unit = (value) => {
  const length = Math.hypot(...value);
  return value.map((component) => component / length);
};

function project(point, camera, target, up) {
  const forward = unit(sub(target, camera));
  const right = unit(cross(forward, up));
  const cameraUp = cross(right, forward);
  const delta = sub(point, camera);
  const depth = dot(delta, forward);
  const focalPixels = (width * shot.lensMm) / 36;
  return [
    width / 2 + (dot(delta, right) / depth) * focalPixels,
    height / 2 - (dot(delta, cameraUp) / depth) * focalPixels,
  ];
}

const toBlender = ([x, y, z]) => [x, -z, y];
const rows = landmarks.map(([id, source]) => {
  const three = project(source, shot.position, shot.target, [0, 1, 0]);
  const blender = project(
    toBlender(source),
    toBlender(shot.position),
    toBlender(shot.target),
    [0, 0, 1],
  );
  const deviation = Math.hypot(three[0] - blender[0], three[1] - blender[1]);
  return { id, source, three, blender, deviationPixels: deviation };
});
const rms = Math.sqrt(rows.reduce((sum, row) => sum + row.deviationPixels ** 2, 0) / rows.length);
const maximum = Math.max(...rows.map((row) => row.deviationPixels));
const report = {
  generatedAt: new Date().toISOString(),
  shot: shot.id,
  frame: [width, height],
  sensorFit: 'HORIZONTAL',
  sensorWidthMm: 36,
  verticalFovDegrees: (2 * Math.atan(36 / (width / height) / (2 * shot.lensMm)) * 180) / Math.PI,
  landmarkCount: rows.length,
  rmsDeviationPixels: rms,
  maximumDeviationPixels: maximum,
  normalizedRms: rms / Math.hypot(width, height),
  acceptance: {
    rmsPixels: 2,
    maximumPixels: 4,
    mathematicalProjectionPass: rms <= 2 && maximum <= 4,
  },
  limitation:
    'Projection-matrix equivalence only; it does not establish rendered silhouette alignment.',
  landmarks: rows,
};
const circles = rows
  .map(
    ({ id, three: [x, y] }) =>
      `<g><circle cx="${x}" cy="${y}" r="4"/><text x="${x + 6}" y="${y - 6}">${id}</text></g>`,
  )
  .join('');
const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${width} ${height}"><style>rect{fill:#07161d}circle{fill:#66d6de;stroke:#fff;stroke-width:1}text{fill:#fff;font:10px monospace}</style><rect width="100%" height="100%"/>${circles}</svg>`;
const out = resolve('docs/cinematic-v2/calibration');
await mkdir(out, { recursive: true });
await writeFile(resolve(out, 'shot-04-projection.json'), `${JSON.stringify(report, null, 2)}\n`);
await writeFile(resolve(out, 'shot-04-landmarks.svg'), svg);
console.log(JSON.stringify({ rms, maximum, landmarks: rows.length, output: out }));
