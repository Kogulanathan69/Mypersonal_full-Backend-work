import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { PARCEL, POOL, ROOMS, SLABS, TREES, Vec2, WALLS, edgeLength } from './house-model-data';

/**
 * Static, original SVG drawing of the illustrative house at one of the five story stages
 * (0 land, 1 blueprint, 2 structure, 3 materials, 4 vision). Used when WebGL is unavailable,
 * when the visitor prefers reduced motion, or when they turn the animation off.
 * Projected isometrically from the same model data as the 3D scene.
 */
interface Shape {
  readonly d: string;
  readonly cls: string;
}

const COS30 = Math.cos(Math.PI / 6);
const SIN30 = 0.5;
const SCALE = 10;

const iso = (x: number, y: number, z: number): [number, number] => [
  (x - z) * COS30 * SCALE,
  ((x + z) * SIN30 - y) * SCALE,
];

const path = (pts: readonly [number, number][], close = true): string =>
  pts.map(([x, y], i) => `${i ? 'L' : 'M'}${x.toFixed(1)} ${y.toFixed(1)}`).join(' ') +
  (close ? ' Z' : '');

const flat = (pts: readonly Vec2[], y = 0): string => path(pts.map(([x, z]) => iso(x, y, z)));

const rectFlat = (x0: number, z0: number, x1: number, z1: number, y: number): string =>
  flat(
    [
      [x0, z0],
      [x1, z0],
      [x1, z1],
      [x0, z1],
    ],
    y,
  );

export function buildIllustration(stage: number): Shape[] {
  const shapes: Shape[] = [];
  const s = Math.max(0, Math.min(4, Math.round(stage)));

  // Ground / parcel
  shapes.push({
    d: flat(PARCEL),
    cls: s >= 3 ? 'fill-scene-ground stroke-mk-cyan/40' : 'fill-mk-surface-2 stroke-mk-cyan',
  });

  if (s <= 2) {
    for (let v = -14; v <= 14; v += 4) {
      shapes.push({
        d: path([iso(v, 0, -12), iso(v, 0, 12)], false),
        cls: 'fill-none stroke-mk-cyan/15',
      });
      shapes.push({
        d: path([iso(-16, 0, v), iso(16, 0, v)], false),
        cls: 'fill-none stroke-mk-cyan/15',
      });
    }
  }
  if (s <= 1) {
    for (const [x, z] of PARCEL) {
      const [cx, cy] = iso(x, 0, z);
      shapes.push({
        d: `M${cx - 4} ${cy} a4 4 0 1 0 8 0 a4 4 0 1 0 -8 0`,
        cls: 'fill-mk-cyan stroke-none',
      });
    }
  }
  if (s >= 3) {
    shapes.push({
      d: rectFlat(POOL.x0, POOL.z0, POOL.x1, POOL.z1, 0.1),
      cls: 'fill-scene-water stroke-scene-stone',
    });
  }
  if (s >= 1) {
    for (const r of ROOMS) {
      shapes.push({
        d: rectFlat(r.x0, r.z0, r.x1, r.z1, 0.02),
        cls: s === 1 ? 'fill-mk-cyan/5 stroke-mk-cyan' : 'fill-none stroke-mk-cyan/40',
      });
    }
  }

  if (s >= 2) {
    const solid = s >= 3;
    const wallsByLevel = (level: 0 | 1) =>
      WALLS.filter((w) => w.level === level).sort(
        (a, b) => a.a[0] + a.a[1] + a.b[0] + a.b[1] - (b.a[0] + b.a[1] + b.b[0] + b.b[1]),
      );
    const drawWalls = (level: 0 | 1): void => {
      for (const w of wallsByLevel(level)) {
        const y0 = w.baseY;
        const y1 = w.baseY + w.height;
        shapes.push({
          d: path([
            iso(w.a[0], y0, w.a[1]),
            iso(w.b[0], y0, w.b[1]),
            iso(w.b[0], y1, w.b[1]),
            iso(w.a[0], y1, w.a[1]),
          ]),
          cls: solid
            ? 'fill-scene-concrete stroke-scene-concrete-dark/60'
            : 'fill-mk-cyan/5 stroke-mk-cyan',
        });
        const len = edgeLength(w.a, w.b);
        const ux = (w.b[0] - w.a[0]) / len;
        const uz = (w.b[1] - w.a[1]) / len;
        for (const o of w.openings) {
          const p = (t: number, y: number) => iso(w.a[0] + ux * t, y0 + y, w.a[1] + uz * t);
          shapes.push({
            d: path([p(o.from, o.sill), p(o.to, o.sill), p(o.to, o.head), p(o.from, o.head)]),
            cls: solid
              ? s === 4
                ? 'fill-scene-warm-light/80 stroke-scene-concrete-dark'
                : 'fill-scene-glass stroke-scene-concrete-dark'
              : 'fill-none stroke-mk-cyan/70',
          });
        }
      }
    };
    const drawSlab = (level: 0 | 1): void => {
      for (const slab of SLABS.filter((x) => x.level === level)) {
        const top = slab.y + slab.thickness;
        shapes.push({
          d: rectFlat(slab.x0, slab.z0, slab.x1, slab.z1, top),
          cls: solid
            ? 'fill-scene-concrete stroke-scene-concrete-dark/60'
            : 'fill-mk-cyan/5 stroke-mk-cyan',
        });
        shapes.push({
          d: path([
            iso(slab.x0, top, slab.z1),
            iso(slab.x1, top, slab.z1),
            iso(slab.x1, slab.y, slab.z1),
            iso(slab.x0, slab.y, slab.z1),
          ]),
          cls: solid ? 'fill-scene-concrete-dark stroke-none' : 'fill-none stroke-mk-cyan',
        });
      }
    };
    drawWalls(0);
    drawSlab(0);
    drawWalls(1);
    drawSlab(1);
  }

  if (s === 4) {
    for (const [x, z] of [...TREES].sort((a, b) => a[0] + a[1] - (b[0] + b[1]))) {
      const [bx, by] = iso(x, 0, z);
      const [tx, ty] = iso(x, 4, z);
      shapes.push({
        d: path(
          [
            [bx, by],
            [tx, ty],
          ],
          false,
        ),
        cls: 'fill-none stroke-scene-trunk stroke-[3]',
      });
      shapes.push({
        d: `M${tx - 18} ${ty} a18 18 0 1 0 36 0 a18 18 0 1 0 -36 0`,
        cls: 'fill-scene-foliage stroke-scene-foliage-light',
      });
    }
  }
  return shapes;
}

function viewBox(): string {
  const pts: [number, number][] = [];
  for (const [x, z] of PARCEL) {
    pts.push(iso(x, 0, z), iso(x, 8, z));
  }
  const xs = pts.map((p) => p[0]);
  const ys = pts.map((p) => p[1]);
  const pad = 24;
  const minX = Math.min(...xs) - pad;
  const minY = Math.min(...ys) - pad;
  return `${minX.toFixed(0)} ${minY.toFixed(0)} ${(Math.max(...xs) - minX + pad).toFixed(0)} ${(Math.max(...ys) - minY + pad).toFixed(0)}`;
}

const VIEWBOX = viewBox();

@Component({
  selector: 'bm-static-house-illustration',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <svg [attr.viewBox]="viewBox" class="h-auto w-full" role="img" [attr.aria-label]="label()">
      @for (shape of shapes(); track $index) {
        <path
          [attr.d]="shape.d"
          [attr.class]="shape.cls"
          stroke-width="1.5"
          stroke-linejoin="round"
        />
      }
    </svg>
  `,
})
export class StaticHouseIllustration {
  readonly stage = input(4);
  readonly label = input('Illustrative drawing of a modern house on its land');
  protected readonly viewBox = VIEWBOX;
  protected readonly shapes = computed(() => buildIllustration(this.stage()));
}
