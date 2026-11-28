import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..');
const out = path.join(root, 'previews');
const model = JSON.parse(await readFile(path.join(root, 'residence.geometry.json'), 'utf8'));
await mkdir(out, { recursive: true });

const esc = (s) => String(s).replaceAll('&', '&amp;').replaceAll('<', '&lt;');
const sx = (x) => 60 + (x + 17) * 26;
const sz = (z) => 40 + (z + 12) * 26;
const points = (items) => items.map(([x,z]) => `${sx(x)},${sz(z)}`).join(' ');
const header = (title, viewBox='0 0 920 680') => `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${viewBox}" role="img" aria-label="${esc(title)}"><style>text{font-family:Arial,sans-serif}.t{fill:#e9f2ef}.m{fill:#9fb5ad}.line{fill:none;stroke:#5dd8d1;stroke-width:2}.thin{fill:none;stroke:#55736e;stroke-width:1}.room{fill:#15343a;stroke:#5dd8d1;stroke-width:1.5}.paper{fill:#f2ecdf}.ink{stroke:#233a39;fill:none;stroke-width:2}.wood{fill:#a87845}.stone{fill:#b6ad99}.glass{fill:#6fa8ad;fill-opacity:.55}</style><rect width="100%" height="100%" fill="#0d1e24"/><text x="40" y="42" class="t" font-size="22">${esc(title)}</text><text x="40" y="64" class="m" font-size="12">BM-CV2-RES-001 · illustrative marketing geometry · metres</text>`;
const footer = '</svg>';

let plan = header('Authoritative plan registration');
plan += `<polygon points="${points(model.parcel)}" class="line"/><polygon points="${points(model.buildable)}" class="thin" stroke-dasharray="8 6"/>`;
for (const r of model.rooms) {
  plan += `<rect x="${sx(r.x0)}" y="${sz(r.z0)}" width="${(r.x1-r.x0)*26}" height="${(r.z1-r.z0)*26}" class="room"/><text x="${sx((r.x0+r.x1)/2)}" y="${sz((r.z0+r.z1)/2)}" class="t" font-size="11" text-anchor="middle">${esc(r.name)}</text>`;
}
plan += `<rect x="${sx(model.pool.x0)}" y="${sz(model.pool.z0)}" width="${(model.pool.x1-model.pool.x0)*26}" height="${(model.pool.z1-model.pool.z0)*26}" class="glass"/><text x="40" y="650" class="m" font-size="12">Footprint 16.0 × 9.0 m · parcel and buildable polygon retained from live model</text>${footer}`;
await writeFile(path.join(out, '01-plan-registration.svg'), plan);

const iso = (x,y,z) => [460 + (x-z)*18, 500 + (x+z)*8 - y*28];
const poly = (pts, cls) => `<polygon points="${pts.map(p=>iso(...p).join(',')).join(' ')}" class="${cls}"/>`;
let mass = header('Authoritative massing and registration');
mass += poly(model.parcel.map(([x,z])=>[x,0,z]), 'thin');
for (const slab of model.slabs) mass += poly([[slab.x0,slab.y+slab.thickness,slab.z0],[slab.x1,slab.y+slab.thickness,slab.z0],[slab.x1,slab.y+slab.thickness,slab.z1],[slab.x0,slab.y+slab.thickness,slab.z1]], slab.level ? 'stone' : 'paper');
for (const wall of model.walls) {
  const [a,b]=[wall.a,wall.b];
  mass += poly([[a[0],wall.baseY,a[1]],[b[0],wall.baseY,b[1]],[b[0],wall.baseY+wall.height,b[1]],[a[0],wall.baseY+wall.height,a[1]]], wall.level ? 'wood' : 'stone');
}
mass += `<text x="40" y="650" class="m" font-size="12">Same footprint, walls, levels, openings and roofs govern every production shot.</text>${footer}`;
await writeFile(path.join(out, '02-massing-registration.svg'), mass);

let contact = header('Phase 04 technical preview contact sheet', '0 0 1200 760');
contact += `<rect x="40" y="100" width="530" height="420" rx="4" fill="#15343a"/><g transform="translate(30 95) scale(.55)"><polygon points="${points(model.parcel)}" class="line"/><polygon points="${points(model.buildable)}" class="thin" stroke-dasharray="8 6"/>`;
for (const r of model.rooms) contact += `<rect x="${sx(r.x0)}" y="${sz(r.z0)}" width="${(r.x1-r.x0)*26}" height="${(r.z1-r.z0)*26}" class="room"/>`;
contact += `</g><text x="60" y="555" class="t" font-size="18">Plan registration</text><text x="60" y="580" class="m" font-size="13">Authoritative footprint and room layout</text>`;
contact += `<rect x="630" y="100" width="530" height="420" rx="4" fill="#15343a"/><g transform="translate(590 60) scale(.72)">`;
for (const slab of model.slabs) contact += poly([[slab.x0,slab.y+slab.thickness,slab.z0],[slab.x1,slab.y+slab.thickness,slab.z0],[slab.x1,slab.y+slab.thickness,slab.z1],[slab.x0,slab.y+slab.thickness,slab.z1]], slab.level ? 'wood' : 'stone');
for (const wall of model.walls) contact += poly([[wall.a[0],wall.baseY,wall.a[1]],[wall.b[0],wall.baseY,wall.b[1]],[wall.b[0],wall.baseY+wall.height,wall.b[1]],[wall.a[0],wall.baseY+wall.height,wall.a[1]]], wall.level ? 'wood' : 'stone');
contact += `</g>`;
contact += `<text x="650" y="555" class="t" font-size="18">Massing registration</text><text x="650" y="580" class="m" font-size="13">Two-storey tropical-modern volume</text><text x="40" y="700" class="m" font-size="13">Technical SVG evidence only — not a photorealistic production render.</text>${footer}`;
await writeFile(path.join(out, '00-contact-sheet.svg'), contact);

console.log(`Generated 3 SVG previews in ${out}`);
