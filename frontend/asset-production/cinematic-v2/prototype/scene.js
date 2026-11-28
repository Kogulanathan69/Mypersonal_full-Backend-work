import * as THREE from 'three';

const params = new URLSearchParams(location.search);
const shotId = params.get('shot') || 'SHOT-05';
const mode = params.get('mode') || 'material';
const quality = params.get('quality') || (innerWidth < 700 ? 'low' : 'standard');
const canvas = document.querySelector('#scene');
const status = document.querySelector('#status');
const model = await fetch('../residence.geometry.json').then((r) => r.json());
const cameraData = await fetch('../cameras.json').then((r) => r.json());
const shot = cameraData.shots.find((entry) => entry.id === shotId) || cameraData.shots[4];
document.querySelector('#shot-title').textContent = shot.name;
document.querySelector('#shot-note').textContent = mode === 'wireframe'
  ? 'Registered technical geometry using the authoritative residence source.'
  : 'Original procedural study. Illustrative marketing architecture only.';

const scene = new THREE.Scene();
scene.background = new THREE.Color(mode === 'blueprint' ? 0x071a20 : 0x6f827f);
scene.fog = mode === 'material' ? new THREE.FogExp2(0x6f827f, 0.011) : null;
const camera = new THREE.PerspectiveCamera(2 * Math.atan(18 / shot.lensMm) * 180 / Math.PI, 1, 0.1, 220);
camera.position.fromArray(shot.position);
camera.lookAt(...shot.target);

const renderer = new THREE.WebGLRenderer({ canvas, antialias: quality !== 'low', alpha: false, powerPreference: 'high-performance', preserveDrawingBuffer: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, quality === 'high' ? 1.75 : quality === 'standard' ? 1.35 : 1));
renderer.shadowMap.enabled = mode === 'material' && quality !== 'low';
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = mode === 'material' ? 1.05 : 0.85;

const proceduralTexture = (kind) => {
  const c = document.createElement('canvas'); c.width = c.height = 256;
  const x = c.getContext('2d');
  if (kind === 'concrete') {
    x.fillStyle = '#a7a49b'; x.fillRect(0,0,256,256);
    for(let i=0;i<2200;i++){ const v=145+Math.random()*45; x.fillStyle=`rgba(${v},${v},${v-4},${Math.random()*.08})`; x.fillRect(Math.random()*256,Math.random()*256,1,1); }
    x.strokeStyle='#85837d55'; for(let y=64;y<256;y+=64){x.beginPath();x.moveTo(0,y);x.lineTo(256,y);x.stroke();}
  } else if (kind === 'wood') {
    const g=x.createLinearGradient(0,0,256,0); g.addColorStop(0,'#6e3f22');g.addColorStop(.5,'#a66d37');g.addColorStop(1,'#704225');x.fillStyle=g;x.fillRect(0,0,256,256);
    x.strokeStyle='#40271955'; for(let i=0;i<70;i++){x.beginPath();x.moveTo(0,Math.random()*256);x.bezierCurveTo(70,Math.random()*256,180,Math.random()*256,256,Math.random()*256);x.stroke();}
  } else {
    x.fillStyle='#a8a08e';x.fillRect(0,0,256,256);for(let i=0;i<550;i++){x.fillStyle=`rgba(70,65,55,${Math.random()*.12})`;x.beginPath();x.arc(Math.random()*256,Math.random()*256,Math.random()*2.3,0,7);x.fill();}
  }
  const t=new THREE.CanvasTexture(c); t.colorSpace=THREE.SRGBColorSpace; t.wrapS=t.wrapT=THREE.RepeatWrapping; t.repeat.set(2,2); t.anisotropy=quality==='high'?4:2; return t;
};

const isTechnical = mode !== 'material';
const concrete = new THREE.MeshStandardMaterial({ color: isTechnical ? 0x12343b : 0xb2afa5, roughness: .78, map: isTechnical ? null : proceduralTexture('concrete'), wireframe: mode === 'wireframe' });
const concreteDark = new THREE.MeshStandardMaterial({ color: isTechnical ? 0x174b55 : 0x5e625f, roughness: .82, wireframe: mode === 'wireframe' });
const wood = new THREE.MeshStandardMaterial({ color: isTechnical ? 0x2d7880 : 0x9b6334, roughness: .48, map: isTechnical ? null : proceduralTexture('wood'), wireframe: mode === 'wireframe' });
const stone = new THREE.MeshStandardMaterial({ color: isTechnical ? 0x1c535b : 0xa49e8f, roughness: .88, map: isTechnical ? null : proceduralTexture('stone'), wireframe: mode === 'wireframe' });
const metal = new THREE.MeshStandardMaterial({ color: 0x252c2b, roughness: .34, metalness: .65, wireframe: mode === 'wireframe' });
const glass = new THREE.MeshPhysicalMaterial({ color: isTechnical ? 0x5dd8d1 : 0x8bb7b8, roughness: .12, transmission: 0, transparent: true, opacity: isTechnical ? .25 : .22, depthWrite: false, ior: 1.45, thickness: .04, side: THREE.DoubleSide, wireframe: mode === 'wireframe' });
const warm = new THREE.MeshStandardMaterial({ color: 0xffc679, emissive: 0xff9f43, emissiveIntensity: mode === 'material' ? 1.25 : 0, roughness: .6 });
const cyan = new THREE.LineBasicMaterial({ color: 0x62e1dc, transparent: true, opacity: .85 });

const addBox = (name,w,h,d,mat,x,y,z,group=scene) => { const m=new THREE.Mesh(new THREE.BoxGeometry(w,h,d),mat);m.name=name;m.position.set(x,y,z);m.castShadow=m.receiveShadow=mode==='material';group.add(m);return m; };
const addEdges = (mesh) => { const e=new THREE.LineSegments(new THREE.EdgesGeometry(mesh.geometry),cyan);e.position.copy(mesh.position);e.rotation.copy(mesh.rotation);mesh.parent.add(e); };

const ground = new THREE.Mesh(new THREE.PlaneGeometry(80,80),new THREE.MeshStandardMaterial({color:isTechnical?0x09242a:0x566c58,roughness:1}));ground.rotation.x=-Math.PI/2;ground.receiveShadow=true;scene.add(ground);
const parcelShape=new THREE.Shape(model.parcel.map(([x,z])=>new THREE.Vector2(x,z)));const parcel=new THREE.LineLoop(new THREE.BufferGeometry().setFromPoints(model.parcel.map(([x,z])=>new THREE.Vector3(x,.03,z))),cyan);scene.add(parcel);

if(mode==='blueprint'){
  const grid=new THREE.GridHelper(60,60,0x2c6d73,0x123a40);scene.add(grid);
  for(const room of model.rooms){const pts=[[room.x0,room.z0],[room.x1,room.z0],[room.x1,room.z1],[room.x0,room.z1]].map(([x,z])=>new THREE.Vector3(x,.06,z));scene.add(new THREE.LineLoop(new THREE.BufferGeometry().setFromPoints(pts),cyan));}
}

const buildOpening = (wall,o,group,len,angle) => {
  const [kind,from,to,sill,head]=o, width=to-from, height=head-sill;
  const frame=addBox(`${wall.id}-${kind}`,width+.12,height+.12,.1,metal,from+width/2,sill+height/2,0,group);
  const pane=addBox(`${wall.id}-pane`,width,height,.045,kind==='door'&&width<2?wood:glass,from+width/2,sill+height/2,0,group);
  if(mode==='wireframe'){addEdges(frame);addEdges(pane);}
};
for(const wall of model.walls){
  const dx=wall.b[0]-wall.a[0], dz=wall.b[1]-wall.a[1], len=Math.hypot(dx,dz), angle=-Math.atan2(dz,dx);
  const g=new THREE.Group();g.name=wall.id;g.position.set(wall.a[0],wall.baseY,wall.a[1]);g.rotation.y=angle;scene.add(g);
  let cursor=0;const openings=[...wall.openings].sort((a,b)=>a[1]-b[1]);
  const piece=(x0,x1,y0,y1)=>{if(x1<=x0||y1<=y0)return;const m=addBox(`${wall.id}-wall`,x1-x0,y1-y0,wall.thickness,wall.thickness>.15?concrete:concreteDark,(x0+x1)/2,(y0+y1)/2,0,g);if(mode==='wireframe')addEdges(m);};
  for(const o of openings){piece(cursor,o[1],0,wall.height);piece(o[1],o[2],0,o[3]);piece(o[1],o[2],o[4],wall.height);buildOpening(wall,o,g,len,angle);cursor=o[2];}piece(cursor,len,0,wall.height);
}
for(const slab of model.slabs){const m=addBox(slab.id,slab.x1-slab.x0,slab.thickness,slab.z1-slab.z0,slab.level?concreteDark:concrete,(slab.x0+slab.x1)/2,slab.y+slab.thickness/2,(slab.z0+slab.z1)/2);if(mode==='wireframe')addEdges(m);}
for(let i=0;i<model.louvres.count;i++){const t=i/(model.louvres.count-1);addBox(`louvre-${i}`,.08,model.louvres.height,.34,wood,THREE.MathUtils.lerp(model.louvres.x0,model.louvres.x1,t),model.louvres.baseY+model.louvres.height/2,model.louvres.z);}
addBox('verandah-shade',7.8,.18,4.4,wood,-4.5,3.08,3.25);
const poolCoping=addBox('pool-coping',model.pool.x1-model.pool.x0+.6,.16,model.pool.z1-model.pool.z0+.6,stone,(model.pool.x0+model.pool.x1)/2,.08,(model.pool.z0+model.pool.z1)/2);
const water=new THREE.Mesh(new THREE.PlaneGeometry(model.pool.x1-model.pool.x0,model.pool.z1-model.pool.z0),new THREE.MeshPhysicalMaterial({color:0x3d8990,roughness:.16,metalness:.05,transmission:.25,transparent:true,opacity:.9}));water.rotation.x=-Math.PI/2;water.position.set((model.pool.x0+model.pool.x1)/2,.18,(model.pool.z0+model.pool.z1)/2);scene.add(water);

// Living-area furniture and reserved stair volume.
addBox('sofa',3,.72,1.05,new THREE.MeshStandardMaterial({color:0xbab3a4,roughness:.9}),-5,0.38,-1.6);
addBox('coffee-table',1.7,.35,.8,wood,-4.2,.22,-.15);
addBox('dining-table',2.2,.72,1.05,wood,1.2,.38,-1.7);
for(const [x,z] of [[.2,-1.7],[2.2,-1.7],[1.2,-2.6],[1.2,-.8]]) addBox('chair',.48,.78,.48,wood,x,.4,z);
const stair=addBox('reserved-stair-volume',3.6,2.7,2.5,mode==='material'?concreteDark:new THREE.MeshBasicMaterial({color:0x62e1dc,transparent:true,opacity:.12,wireframe:true}),1.4,1.35,2.7);stair.visible=shotId.startsWith('SHOT-0') && Number(shotId.slice(-1))>=7;

const trunkMat=new THREE.MeshStandardMaterial({color:0x60452c,roughness:1});const leafMat=new THREE.MeshStandardMaterial({color:0x315d42,roughness:1});
if(mode==='material') for(const [x,z] of model.trees){const trunk=new THREE.Mesh(new THREE.CylinderGeometry(.16,.24,3.4,8),trunkMat);trunk.position.set(x,1.7,z);trunk.castShadow=true;scene.add(trunk);for(const o of [[0,3.9,0],[.8,4.4,.3],[-.6,4.2,.4]]){const c=new THREE.Mesh(new THREE.IcosahedronGeometry(1.35,1),leafMat);c.position.set(x+o[0],o[1],z+o[2]);c.castShadow=true;scene.add(c);}}

scene.add(new THREE.HemisphereLight(mode==='material'?0xb7cfcb:0x4db2b8,mode==='material'?0x243529:0x061519,mode==='material'?1.25:.7));
const sun=new THREE.DirectionalLight(0xffd4a0,mode==='material'?3.3:1.2);sun.position.set(-18,26,14);sun.target.position.set(0,1,0);sun.castShadow=renderer.shadowMap.enabled;sun.shadow.mapSize.set(quality==='high'?2048:1024,quality==='high'?2048:1024);sun.shadow.camera.left=-25;sun.shadow.camera.right=25;sun.shadow.camera.top=20;sun.shadow.camera.bottom=-20;scene.add(sun,sun.target);
if(mode==='material') for(const [x,y,z,power] of [[-5,2.1,-1,38],[1.5,2.1,-1.5,32],[2,5,1,26]]){const l=new THREE.PointLight(0xffa95f,power,8,2);l.position.set(x,y,z);l.castShadow=false;scene.add(l);const glow=new THREE.Mesh(new THREE.SphereGeometry(.08,8,6),warm);glow.position.copy(l.position);scene.add(glow);}

const resize=()=>{const w=innerWidth,h=innerHeight;renderer.setSize(w,h,false);camera.aspect=w/h;camera.updateProjectionMatrix();};addEventListener('resize',resize);resize();
let frames=0,start=performance.now(),last=start,min=Infinity,max=0,sum=0;
const tick=(now)=>{const dt=now-last;last=now;if(frames>5){min=Math.min(min,dt);max=Math.max(max,dt);sum+=dt;}frames++;renderer.render(scene,camera);if(now-start<5000)requestAnimationFrame(tick);else finish(now);};
const finish=(now)=>{renderer.render(scene,camera);const gl=renderer.getContext();const dbg=gl.getExtension('WEBGL_debug_renderer_info');const info=renderer.info;const sampled=Math.max(0,frames-6);const fps=sampled?sampled/((now-start)/1000):0;window.__prototypeMetrics={ready:true,shot:shotId,mode,quality,resolution:[renderer.domElement.width,renderer.domElement.height],pixelRatio:renderer.getPixelRatio(),sampledFrames:sampled,fps:+fps.toFixed(1),frameMs:{avg:sampled?+(sum/sampled).toFixed(2):null,min:Number.isFinite(min)?+min.toFixed(2):null,max:max?+max.toFixed(2):null},renderer:dbg?gl.getParameter(dbg.UNMASKED_RENDERER_WEBGL):gl.getParameter(gl.RENDERER),vendor:dbg?gl.getParameter(dbg.UNMASKED_VENDOR_WEBGL):gl.getParameter(gl.VENDOR),webgl2:renderer.capabilities.isWebGL2,drawCalls:info.render.calls,triangles:info.render.triangles,geometries:info.memory.geometries,textures:info.memory.textures,estimatedTextureMemoryBytes:3*256*256*4+(renderer.shadowMap.enabled?(quality==='high'?2048*2048*4:1024*1024*4):0),shadowMap:renderer.shadowMap.enabled,toneMapping:'ACESFilmic',geometryVersion:model.geometryVersion};status.textContent=`${quality} · ${window.__prototypeMetrics.fps} fps · ${info.render.calls} calls · ${info.render.triangles.toLocaleString()} tris`;document.documentElement.dataset.ready='true';console.log('PROTOTYPE_METRICS',JSON.stringify(window.__prototypeMetrics));};
requestAnimationFrame(tick);
