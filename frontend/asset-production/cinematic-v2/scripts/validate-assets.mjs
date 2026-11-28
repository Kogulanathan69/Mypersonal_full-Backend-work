import { readFile, readdir, stat } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..');
const model = JSON.parse(await readFile(path.join(root, 'residence.geometry.json'), 'utf8'));
const cameras = JSON.parse(await readFile(path.join(root, 'cameras.json'), 'utf8'));
const errors=[];
if(model.geometryVersion !== cameras.geometryVersion) errors.push('Geometry/camera version mismatch');
if(model.rooms.length !== 7) errors.push('Expected seven ground-floor zones');
if(model.walls.length !== 14) errors.push('Expected fourteen wall runs');
if(cameras.shots.length !== 9) errors.push('Expected nine registered shots');
if(!cameras.shots.find(s=>s.id==='SHOT-04')) errors.push('Missing material registration camera');
for(const w of model.walls){
  const len=Math.hypot(w.b[0]-w.a[0],w.b[1]-w.a[1]);
  for(const o of w.openings) if(o[1]<0 || o[2]>len || o[1]>=o[2] || o[3]<0 || o[3]>=o[4] || o[4]>w.height) errors.push(`Invalid opening on ${w.id}`);
}
const previewDir=path.join(root,'previews');
const rows=[];
for(const name of (await readdir(previewDir)).sort()){
  const p=path.join(previewDir,name); const data=await readFile(p); const info=await stat(p);
  rows.push({name,size:info.size,sha256:createHash('sha256').update(data).digest('hex')});
}
console.table(rows);
if(errors.length){console.error(errors.join('\n'));process.exitCode=1}else console.log('Geometry, cameras, openings and preview inventory: PASS');
