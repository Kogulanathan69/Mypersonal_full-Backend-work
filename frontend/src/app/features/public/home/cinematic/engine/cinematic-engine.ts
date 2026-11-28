/**
 * Cinematic Home Page engine (owner: A — Customer Portal). Lazy-loaded: Three.js and GSAP live
 * only in this chunk, so no other page downloads them.
 *
 * Draws the ILLUSTRATIVE house from house-model-data.ts and maps scroll progress (0→1) onto the
 * five story scenes defined in timeline.ts. Everything is procedural: there are no external
 * model files, textures or HDR maps to load or license.
 */
import * as THREE from 'three';
import { Line2 } from 'three/examples/jsm/lines/Line2.js';
import { LineGeometry } from 'three/examples/jsm/lines/LineGeometry.js';
import { LineMaterial } from 'three/examples/jsm/lines/LineMaterial.js';
import { LineSegments2 } from 'three/examples/jsm/lines/LineSegments2.js';
import { LineSegmentsGeometry } from 'three/examples/jsm/lines/LineSegmentsGeometry.js';
import {
  BUILDABLE,
  FOOTPRINT,
  LOUVRES,
  PARCEL,
  PATH_STONES,
  POOL,
  ROOMS,
  SLABS,
  TREES,
  Vec2,
  WALLS,
  WallRun,
  edgeLength,
} from '../house-model-data';
import { StageName, cameraAt, clamp01, lerp, segment, stage } from '../timeline';
import { SHOT_04_CAMERA, verticalFovDegrees } from '../registered-camera';

export interface EngineOptions {
  readonly mobile: boolean;
  /** Lower pixel ratio, no shadows. */
  readonly lowPower: boolean;
  /** Exposes window.__bmCinematic for browser verification (non-production builds only). */
  readonly debug: boolean;
  readonly onContextLost?: () => void;
}

export interface EngineDebugState {
  progress: number;
  frames: number;
  running: boolean;
  disposed: boolean;
  pixelRatio: number;
  activeEngines: number;
  intro?: number;
  gridOpacity?: number;
  boundaryVisible?: boolean;
}

let activeEngines = 0;

/** Reads a colour token (e.g. "--color-mk-cyan") so no colour values live in TypeScript. */
function token(element: Element, name: string): THREE.Color {
  const raw = getComputedStyle(element).getPropertyValue(name).trim();
  if (!raw) throw new Error(`Design token ${name} is not defined`);
  return new THREE.Color(raw);
}

interface Palette {
  navy: THREE.Color;
  surface: THREE.Color;
  cyan: THREE.Color;
  white: THREE.Color;
  gold: THREE.Color;
  ground: THREE.Color;
  platform: THREE.Color;
  concrete: THREE.Color;
  concreteDark: THREE.Color;
  wood: THREE.Color;
  glass: THREE.Color;
  foliage: THREE.Color;
  foliageLight: THREE.Color;
  trunk: THREE.Color;
  water: THREE.Color;
  warm: THREE.Color;
  stone: THREE.Color;
}

function readPalette(element: Element): Palette {
  return {
    navy: token(element, '--color-mk-navy'),
    surface: token(element, '--color-mk-surface'),
    cyan: token(element, '--color-mk-cyan'),
    white: token(element, '--color-mk-white'),
    gold: token(element, '--color-mk-gold'),
    ground: token(element, '--color-scene-ground'),
    platform: token(element, '--color-scene-platform'),
    concrete: token(element, '--color-scene-concrete'),
    concreteDark: token(element, '--color-scene-concrete-dark'),
    wood: token(element, '--color-scene-wood'),
    glass: token(element, '--color-scene-glass'),
    foliage: token(element, '--color-scene-foliage'),
    foliageLight: token(element, '--color-scene-foliage-light'),
    trunk: token(element, '--color-scene-trunk'),
    water: token(element, '--color-scene-water'),
    warm: token(element, '--color-scene-warm-light'),
    stone: token(element, '--color-scene-stone'),
  };
}

/** Even points along a polyline so a line can be "drawn" smoothly with instanceCount. */
function densify(points: readonly Vec2[], closed: boolean, step = 0.5): Vec2[] {
  const out: Vec2[] = [];
  const n = closed ? points.length : points.length - 1;
  for (let i = 0; i < n; i++) {
    const a = points[i]!;
    const b = points[(i + 1) % points.length]!;
    const len = edgeLength(a, b);
    const parts = Math.max(1, Math.ceil(len / step));
    for (let k = 0; k < parts; k++) {
      const t = k / parts;
      out.push([lerp(a[0], b[0], t), lerp(a[1], b[1], t)]);
    }
  }
  const last = closed ? points[0]! : points[points.length - 1]!;
  out.push(last);
  return out;
}

/** A fat line that can be revealed progressively. */
interface DrawnLine {
  readonly object: Line2 | LineSegments2;
  readonly material: LineMaterial;
  readonly total: number;
}

export class CinematicEngine {
  private readonly renderer: THREE.WebGLRenderer;
  private readonly scene = new THREE.Scene();
  private readonly camera: THREE.PerspectiveCamera;
  private readonly palette: Palette;
  private readonly lineMaterials: LineMaterial[] = [];
  private readonly textures: THREE.Texture[] = [];

  private progress = 0;
  private running = false;
  private disposed = false;
  private rafId = 0;
  private frames = 0;
  private startTime = performance.now();
  private pixelRatio: number;
  private frameTimes: number[] = [];
  /** 0→1 over the first 2.4 s: draws the land behind the hero before any scrolling. */
  private intro = 0;
  private static readonly INTRO_MS = 2400;
  private lastFrame = 0;
  private readonly debugState?: EngineDebugState;

  // Scene parts animated by progress
  private grid!: THREE.LineSegments;
  private boundary!: DrawnLine;
  private setback!: DrawnLine;
  private measure!: DrawnLine;
  private plan!: DrawnLine;
  private openingsPlan!: DrawnLine;
  private readonly cornerPoints: THREE.Object3D[] = [];
  private readonly measureLabels: THREE.Sprite[] = [];
  private readonly roomLabels: THREE.Sprite[] = [];
  private readonly wallGroups: { group: THREE.Group; level: 0 | 1; order: number }[] = [];
  private readonly frameGroups: THREE.Group[] = [];
  private readonly slabGroups: { group: THREE.Group; level: 0 | 1 }[] = [];
  private readonly landscape: { object: THREE.Object3D; order: number }[] = [];
  private louvres!: THREE.Group;
  private lawn!: THREE.Mesh;
  private ground!: THREE.Mesh;

  private readonly mats: {
    concrete: THREE.MeshStandardMaterial;
    concreteDark: THREE.MeshStandardMaterial;
    wood: THREE.MeshStandardMaterial;
    glass: THREE.MeshStandardMaterial;
    edges: THREE.LineBasicMaterial;
    edgesUpper: THREE.LineBasicMaterial;
  };

  private readonly lights: {
    hemi: THREE.HemisphereLight;
    sun: THREE.DirectionalLight;
    rim: THREE.PointLight;
    interior: THREE.PointLight[];
  };

  private readonly onContextLost = (event: Event): void => {
    event.preventDefault();
    this.stop();
    this.options.onContextLost?.();
  };

  constructor(
    private readonly canvas: HTMLCanvasElement,
    private readonly options: EngineOptions,
  ) {
    this.palette = readPalette(canvas);
    this.renderer = new THREE.WebGLRenderer({
      canvas,
      antialias: !options.lowPower,
      alpha: false,
      powerPreference: options.lowPower ? 'low-power' : 'high-performance',
    });
    this.pixelRatio = Math.min(
      window.devicePixelRatio || 1,
      options.lowPower || options.mobile ? 1.25 : 1.75,
    );
    this.renderer.setPixelRatio(this.pixelRatio);
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.shadowMap.enabled = !options.lowPower && !options.mobile;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    canvas.addEventListener('webglcontextlost', this.onContextLost);

    const p = this.palette;
    this.scene.background = p.navy.clone();
    this.scene.fog = new THREE.Fog(p.navy.clone(), 55, 140);
    this.camera = new THREE.PerspectiveCamera(options.mobile ? 50 : 38, 1, 0.5, 400);

    this.mats = {
      concrete: new THREE.MeshStandardMaterial({
        color: p.concrete,
        roughness: 0.85,
        transparent: true,
      }),
      concreteDark: new THREE.MeshStandardMaterial({
        color: p.concreteDark,
        roughness: 0.7,
        transparent: true,
      }),
      wood: new THREE.MeshStandardMaterial({ color: p.wood, roughness: 0.6, transparent: true }),
      glass: new THREE.MeshStandardMaterial({
        color: p.glass,
        roughness: 0.08,
        metalness: 0.6,
        transparent: true,
        emissive: p.warm.clone(),
        emissiveIntensity: 0,
      }),
      edges: new THREE.LineBasicMaterial({ color: p.cyan, transparent: true }),
      edgesUpper: new THREE.LineBasicMaterial({ color: p.cyan, transparent: true }),
    };

    this.lights = {
      hemi: new THREE.HemisphereLight(p.cyan, p.navy, 0.4),
      sun: new THREE.DirectionalLight(p.warm, 0.2),
      rim: new THREE.PointLight(p.cyan, 0, 80, 1.6),
      interior: [new THREE.PointLight(p.warm, 0, 14, 2), new THREE.PointLight(p.warm, 0, 14, 2)],
    };

    this.buildLights();
    this.buildGround();
    this.buildLand();
    this.buildBlueprint();
    this.buildStructure();
    this.buildLandscape();

    activeEngines++;
    if (options.debug) {
      this.debugState = {
        progress: 0,
        frames: 0,
        running: false,
        disposed: false,
        pixelRatio: this.pixelRatio,
        activeEngines,
      };
      (globalThis as unknown as Record<string, unknown>)['__bmCinematic'] = this.debugState;
    }
    this.setProgress(0);
  }

  // ---------------------------------------------------------------------------------------------
  // Public API
  // ---------------------------------------------------------------------------------------------

  setProgress(p: number): void {
    this.progress = clamp01(p);
    if (this.debugState) this.debugState.progress = this.progress;
    this.applyProgress(this.progress);
    if (!this.running) this.render();
  }

  resize(width: number, height: number): void {
    if (this.disposed || width === 0 || height === 0) return;
    this.renderer.setSize(width, height, false);
    this.camera.aspect = width / height;
    this.camera.updateProjectionMatrix();
    const w = width * this.pixelRatio;
    const h = height * this.pixelRatio;
    for (const m of this.lineMaterials) m.resolution.set(w, h);
    if (!this.running) this.render();
  }

  start(): void {
    if (this.running || this.disposed) return;
    this.running = true;
    this.lastFrame = performance.now();
    const loop = (): void => {
      if (!this.running) return;
      this.tick();
      this.rafId = requestAnimationFrame(loop);
    };
    this.rafId = requestAnimationFrame(loop);
    this.syncDebug();
  }

  stop(): void {
    this.running = false;
    cancelAnimationFrame(this.rafId);
    this.syncDebug();
  }

  dispose(): void {
    if (this.disposed) return;
    this.stop();
    this.disposed = true;
    this.canvas.removeEventListener('webglcontextlost', this.onContextLost);
    this.scene.traverse((obj) => {
      const mesh = obj as Partial<THREE.Mesh>;
      mesh.geometry?.dispose();
      const material = mesh.material;
      if (Array.isArray(material)) material.forEach((m) => m.dispose());
      else material?.dispose();
    });
    Object.values(this.mats).forEach((m) => m.dispose());
    this.lineMaterials.forEach((m) => m.dispose());
    this.textures.forEach((t) => t.dispose());
    this.renderer.dispose();
    this.renderer.forceContextLoss();
    activeEngines = Math.max(0, activeEngines - 1);
    this.syncDebug();
  }

  // ---------------------------------------------------------------------------------------------
  // Frame loop
  // ---------------------------------------------------------------------------------------------

  private tick(): void {
    const now = performance.now();
    const dt = now - this.lastFrame;
    this.lastFrame = now;
    this.adaptQuality(dt);
    if (this.intro < 1) {
      this.intro = Math.min(1, (now - this.startTime) / CinematicEngine.INTRO_MS);
      this.applyProgress(this.progress);
    }
    this.applyCamera(this.progress, (now - this.startTime) / 1000);
    this.render();
    this.frames++;
    if (this.frames % 30 === 0) this.syncDebug();
  }

  /** Lowers resolution when frames are slow (avg > 24 ms), never below 0.75×. */
  private adaptQuality(dt: number): void {
    this.frameTimes.push(dt);
    if (this.frameTimes.length < 45) return;
    const avg = this.frameTimes.reduce((a, b) => a + b, 0) / this.frameTimes.length;
    this.frameTimes = [];
    if (avg > 24 && this.pixelRatio > 0.75) {
      this.pixelRatio = Math.max(0.75, this.pixelRatio - 0.25);
      this.renderer.setPixelRatio(this.pixelRatio);
      const size = this.renderer.getSize(new THREE.Vector2());
      this.resize(size.x, size.y);
    }
  }

  private render(): void {
    if (this.disposed) return;
    if (!this.running) this.applyCamera(this.progress, 0);
    this.renderer.render(this.scene, this.camera);
  }

  private syncDebug(): void {
    if (!this.debugState) return;
    Object.assign(this.debugState, {
      progress: this.progress,
      frames: this.frames,
      running: this.running,
      disposed: this.disposed,
      pixelRatio: this.pixelRatio,
      activeEngines,
      intro: this.intro,
      gridOpacity: (this.grid.material as THREE.LineBasicMaterial).opacity,
      boundaryVisible: this.boundary.object.visible,
    });
  }

  private applyCamera(p: number, time: number): void {
    const pose = cameraAt(p, this.options.mobile);
    const registration = segment(p, 0.8, 0.82);
    const registered = registration >= 1 && p < 0.84 && !this.options.mobile;
    this.camera.fov = registered
      ? verticalFovDegrees(SHOT_04_CAMERA.lensMm, this.camera.aspect)
      : this.options.mobile
        ? 50
        : 38;
    this.camera.updateProjectionMatrix();
    // Movement stops at the material-registration gate so browser and Blender projections agree.
    const orbit = segment(p, 0.82, 1) * 0.18;
    const drift = registered ? 0 : Math.sin(time * 0.18) * 0.6;
    const angle = registered ? 0 : orbit + Math.sin(time * 0.08) * 0.02;
    const [x, y, z] = pose.position;
    const cos = Math.cos(angle);
    const sin = Math.sin(angle);
    this.camera.position.set(x * cos - z * sin + drift * 0.3, y + drift * 0.15, x * sin + z * cos);
    this.camera.lookAt(pose.target[0], pose.target[1], pose.target[2]);
  }

  // ---------------------------------------------------------------------------------------------
  // Progress → scene state
  // ---------------------------------------------------------------------------------------------

  private applyProgress(p: number): void {
    const s = (name: StageName) => stage(p, name);
    const pal = this.palette;
    const blueprintFade = s('blueprintFade');
    const intro = this.intro * this.intro * (3 - 2 * this.intro);
    const landIn = (name: StageName) => Math.max(s(name), intro);

    // Scene 01 — land
    (this.grid.material as THREE.LineBasicMaterial).opacity =
      landIn('grid') * lerp(0.12, 0.03, blueprintFade);
    this.reveal(this.boundary, landIn('boundary'), lerp(1, 0.35, blueprintFade));
    this.cornerPoints.forEach((pt, i) => {
      const k = segment(
        landIn('points'),
        i / this.cornerPoints.length,
        (i + 1) / this.cornerPoints.length,
      );
      pt.scale.setScalar(Math.max(0.001, k) * lerp(1, 0.6, blueprintFade));
      pt.visible = k > 0.001;
    });
    const landLabels = 1 - s('landLabelsFade');
    this.reveal(this.measure, s('measurements'), landLabels);
    this.measureLabels.forEach((l) => {
      l.material.opacity = s('measurements') * landLabels;
      l.visible = l.material.opacity > 0.01;
    });
    this.reveal(this.setback, s('setback'), lerp(0.8, 0, blueprintFade));

    // Scene 02 — blueprint
    this.reveal(this.plan, s('planLines'), lerp(1, 0.15, s('materials')));
    this.reveal(this.openingsPlan, s('openingsOutline'), 1 - blueprintFade);
    this.roomLabels.forEach((l) => {
      l.material.opacity = s('roomLabels') * (1 - s('roomLabelsFade'));
      l.visible = l.material.opacity > 0.01;
    });

    // Scene 03 — structure rises (cyan wireframe)
    const ground = s('wallsGround');
    const upper = s('wallsUpper');
    const groundCount = this.wallGroups.filter((w) => w.level === 0).length;
    const upperCount = this.wallGroups.length - groundCount;
    for (const w of this.wallGroups) {
      const total = w.level === 0 ? groundCount : upperCount;
      const k = segment(
        w.level === 0 ? ground : upper,
        (w.order / total) * 0.6,
        (w.order / total) * 0.6 + 0.4,
      );
      w.group.scale.y = Math.max(0.001, k);
      w.group.visible = k > 0.001;
    }
    const frames = s('frames');
    this.frameGroups.forEach((g) => {
      g.visible = frames > 0.01;
      g.scale.setScalar(Math.max(0.001, frames));
    });
    const roof = s('roof');
    for (const slab of this.slabGroups) {
      const k = slab.level === 0 ? segment(roof, 0, 0.6) : segment(roof, 0.4, 1);
      slab.group.scale.set(Math.max(0.001, k), 1, Math.max(0.001, k));
      slab.group.visible = k > 0.001;
    }

    // Scene 04 — materials, glass, warm light
    const mat = s('materials');
    const wire = 1 - s('wireFade') * 0.94;
    this.mats.edges.opacity = wire;
    this.mats.edgesUpper.opacity = wire;
    const ghost = 0.06;
    const solidOpacity = lerp(ghost, 1, mat);
    for (const m of [this.mats.concrete, this.mats.concreteDark, this.mats.wood]) {
      m.opacity = solidOpacity;
      m.depthWrite = mat > 0.97;
      m.transparent = mat <= 0.97;
    }
    this.mats.concrete.color.copy(pal.cyan).lerp(pal.concrete, mat);
    this.mats.concreteDark.color.copy(pal.cyan).lerp(pal.concreteDark, mat);
    this.mats.wood.color.copy(pal.cyan).lerp(pal.wood, mat);
    const glass = s('glass');
    this.mats.glass.opacity = lerp(0, 0.55, glass);
    this.mats.glass.visible = glass > 0.01;
    this.mats.glass.emissiveIntensity = s('interiorGlow') * 0.9;
    this.louvres.visible = mat > 0.02;
    this.louvres.scale.y = Math.max(0.001, mat);

    const warm = s('warmLight');
    this.lights.hemi.intensity = lerp(0.45, 1.6, warm);
    this.lights.hemi.color.copy(pal.cyan).lerp(pal.white, warm);
    this.lights.hemi.groundColor.copy(pal.navy).lerp(pal.ground, warm);
    this.lights.sun.intensity = lerp(0.15, 3.4, warm);
    this.lights.rim.intensity = lerp(260, 0, warm);
    this.lights.interior.forEach((l) => (l.intensity = s('interiorGlow') * 30));
    this.renderer.toneMappingExposure = lerp(0.95, 1.18, warm);
    // Dusk sky: the background and fog lift slightly as the house becomes real.
    (this.scene.background as THREE.Color).copy(pal.navy).lerp(pal.surface, warm * 0.85);
    this.scene.fog?.color.copy(pal.navy).lerp(pal.surface, warm * 0.85);

    // Ground: technical dark → lawn and landscaping
    const land = s('landscape');
    (this.lawn.material as THREE.MeshStandardMaterial).color
      .copy(pal.platform)
      .lerp(pal.ground, land);
    (this.ground.material as THREE.MeshStandardMaterial).color
      .copy(pal.navy)
      .lerp(pal.surface, land * 0.6);
    for (const item of this.landscape) {
      const k = segment(land, item.order * 0.6, item.order * 0.6 + 0.4);
      item.object.scale.setScalar(Math.max(0.001, k));
      item.object.visible = k > 0.001;
    }
  }

  private reveal(line: DrawnLine, amount: number, opacity: number): void {
    const count = Math.round(line.total * clamp01(amount));
    (line.object.geometry as LineSegmentsGeometry).instanceCount = count;
    line.object.visible = count > 0 && opacity > 0.01;
    line.material.opacity = opacity;
  }

  // ---------------------------------------------------------------------------------------------
  // Builders
  // ---------------------------------------------------------------------------------------------

  private lineMaterial(color: THREE.Color, widthPx: number, dashed = false): LineMaterial {
    const m = new LineMaterial({
      color: color.getHex(),
      linewidth: widthPx,
      transparent: true,
      dashed,
      dashSize: 0.6,
      gapSize: 0.4,
      worldUnits: false,
    });
    this.lineMaterials.push(m);
    return m;
  }

  private polyline(
    points: readonly Vec2[],
    y: number,
    closed: boolean,
    material: LineMaterial,
  ): DrawnLine {
    const pts = densify(points, closed);
    const geometry = new LineGeometry();
    geometry.setPositions(pts.flatMap(([x, z]) => [x, y, z]));
    const line = new Line2(geometry, material);
    line.computeLineDistances();
    this.scene.add(line);
    return { object: line, material, total: pts.length - 1 };
  }

  private segments(pairs: readonly [Vec2, Vec2][], y: number, material: LineMaterial): DrawnLine {
    const geometry = new LineSegmentsGeometry();
    geometry.setPositions(pairs.flatMap(([a, b]) => [a[0], y, a[1], b[0], y, b[1]]));
    const line = new LineSegments2(geometry, material);
    this.scene.add(line);
    return { object: line, material, total: pairs.length };
  }

  private label(text: string, color: THREE.Color, heightWorld: number, weight = 600): THREE.Sprite {
    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d')!;
    const fontPx = 64;
    ctx.font = `${weight} ${fontPx}px Poppins, system-ui, sans-serif`;
    const width = Math.ceil(ctx.measureText(text).width) + 32;
    canvas.width = width;
    canvas.height = fontPx + 32;
    ctx.font = `${weight} ${fontPx}px Poppins, system-ui, sans-serif`;
    ctx.fillStyle = `#${color.getHexString()}`;
    ctx.textBaseline = 'middle';
    ctx.fillText(text, 16, canvas.height / 2);
    const texture = new THREE.CanvasTexture(canvas);
    texture.colorSpace = THREE.SRGBColorSpace;
    texture.anisotropy = 4;
    this.textures.push(texture);
    const sprite = new THREE.Sprite(
      new THREE.SpriteMaterial({
        map: texture,
        transparent: true,
        depthWrite: false,
        depthTest: false,
      }),
    );
    sprite.scale.set((heightWorld * canvas.width) / canvas.height, heightWorld, 1);
    sprite.renderOrder = 10;
    this.scene.add(sprite);
    return sprite;
  }

  private buildLights(): void {
    const { hemi, sun, rim, interior } = this.lights;
    sun.position.set(-18, 26, 14);
    sun.castShadow = this.renderer.shadowMap.enabled;
    sun.shadow.mapSize.set(1024, 1024);
    const cam = sun.shadow.camera as THREE.OrthographicCamera;
    cam.left = -22;
    cam.right = 22;
    cam.top = 22;
    cam.bottom = -22;
    cam.far = 80;
    sun.shadow.bias = -0.0008;
    rim.position.set(12, 14, -10);
    interior[0]!.position.set(-4, 1.8, -1.5);
    interior[1]!.position.set(3, 4.9, 0.5);
    this.scene.add(hemi, sun, rim, ...interior);
  }

  private buildGround(): void {
    const p = this.palette;
    this.ground = new THREE.Mesh(
      new THREE.CircleGeometry(160, 64),
      new THREE.MeshStandardMaterial({ color: p.navy, roughness: 1 }),
    );
    this.ground.rotation.x = -Math.PI / 2;
    this.ground.position.y = -0.02;
    this.ground.receiveShadow = true;
    this.scene.add(this.ground);

    const shape = new THREE.Shape(PARCEL.map(([x, z]) => new THREE.Vector2(x, -z)));
    this.lawn = new THREE.Mesh(
      new THREE.ShapeGeometry(shape),
      new THREE.MeshStandardMaterial({ color: p.platform, roughness: 0.95 }),
    );
    this.lawn.rotation.x = -Math.PI / 2;
    this.lawn.position.y = 0;
    this.lawn.receiveShadow = true;
    this.scene.add(this.lawn);

    // Survey grid: 2 m spacing, 70 m wide
    const half = 35;
    const positions: number[] = [];
    for (let v = -half; v <= half; v += 2) {
      positions.push(-half, 0.01, v, half, 0.01, v, v, 0.01, -half, v, 0.01, half);
    }
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
    this.grid = new THREE.LineSegments(
      geometry,
      new THREE.LineBasicMaterial({
        color: p.cyan,
        transparent: true,
        opacity: 0,
        depthWrite: false,
      }),
    );
    this.scene.add(this.grid);
  }

  private buildLand(): void {
    const p = this.palette;
    this.boundary = this.polyline(PARCEL, 0.05, true, this.lineMaterial(p.cyan, 3.2));
    this.setback = this.polyline(BUILDABLE, 0.04, true, this.lineMaterial(p.gold, 1.6, true));

    // Corner points: a disc and a ring
    PARCEL.forEach(([x, z]) => {
      const group = new THREE.Group();
      const disc = new THREE.Mesh(
        new THREE.CylinderGeometry(0.32, 0.32, 0.08, 20),
        new THREE.MeshBasicMaterial({ color: p.cyan }),
      );
      const ring = new THREE.Mesh(
        new THREE.RingGeometry(0.6, 0.72, 32),
        new THREE.MeshBasicMaterial({
          color: p.cyan,
          transparent: true,
          opacity: 0.55,
          side: THREE.DoubleSide,
        }),
      );
      ring.rotation.x = -Math.PI / 2;
      ring.position.y = 0.06;
      group.add(disc, ring);
      group.position.set(x, 0.06, z);
      this.scene.add(group);
      this.cornerPoints.push(group);
    });

    // Dimension lines offset outward from each parcel edge, with end ticks and length labels
    const centroid = PARCEL.reduce<[number, number]>(
      (acc, [x, z]) => [acc[0] + x / PARCEL.length, acc[1] + z / PARCEL.length],
      [0, 0],
    );
    const pairs: [Vec2, Vec2][] = [];
    PARCEL.forEach((a, i) => {
      const b = PARCEL[(i + 1) % PARCEL.length]!;
      const dx = b[0] - a[0];
      const dz = b[1] - a[1];
      const len = Math.hypot(dx, dz);
      let nx = -dz / len;
      let nz = dx / len;
      const mid: Vec2 = [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2];
      if ((mid[0] - centroid[0]) * nx + (mid[1] - centroid[1]) * nz < 0) {
        nx = -nx;
        nz = -nz;
      }
      const off = 1.6;
      const a2: Vec2 = [a[0] + nx * off, a[1] + nz * off];
      const b2: Vec2 = [b[0] + nx * off, b[1] + nz * off];
      pairs.push([a2, b2]);
      pairs.push([
        [a[0] + nx * 0.6, a[1] + nz * 0.6],
        [a[0] + nx * 2.1, a[1] + nz * 2.1],
      ]);
      pairs.push([
        [b[0] + nx * 0.6, b[1] + nz * 0.6],
        [b[0] + nx * 2.1, b[1] + nz * 2.1],
      ]);
      const label = this.label(`${len.toFixed(1)} m`, p.white, 1.1, 500);
      label.position.set(mid[0] + nx * 3, 0.3, mid[1] + nz * 3);
      this.measureLabels.push(label);
    });
    this.measure = this.segments(pairs, 0.05, this.lineMaterial(p.gold, 1.4));
  }

  private buildBlueprint(): void {
    const p = this.palette;
    const pairs: [Vec2, Vec2][] = [];
    for (const r of ROOMS) {
      pairs.push(
        [
          [r.x0, r.z0],
          [r.x1, r.z0],
        ],
        [
          [r.x1, r.z0],
          [r.x1, r.z1],
        ],
        [
          [r.x1, r.z1],
          [r.x0, r.z1],
        ],
        [
          [r.x0, r.z1],
          [r.x0, r.z0],
        ],
      );
    }
    // Overall plan dimensions
    const f = FOOTPRINT;
    pairs.push(
      [
        [f.x0, f.z0 - 1.6],
        [f.x1, f.z0 - 1.6],
      ],
      [
        [f.x1 + 1.6, f.z0],
        [f.x1 + 1.6, f.z1],
      ],
    );
    this.plan = this.segments(pairs, 0.08, this.lineMaterial(p.cyan, 2));

    for (const r of ROOMS) {
      const label = this.label(r.name, p.white, 0.75, 500);
      label.position.set((r.x0 + r.x1) / 2, 0.4, (r.z0 + r.z1) / 2);
      this.roomLabels.push(label);
    }
    const w = this.label(`${(f.x1 - f.x0).toFixed(1)} m`, p.gold, 0.95);
    w.position.set((f.x0 + f.x1) / 2, 0.4, f.z0 - 2.6);
    const d = this.label(`${(f.z1 - f.z0).toFixed(1)} m`, p.gold, 0.95);
    d.position.set(f.x1 + 3.2, 0.4, (f.z0 + f.z1) / 2);
    this.roomLabels.push(w, d);

    // Door swings and window marks on ground-floor walls
    const marks: [Vec2, Vec2][] = [];
    for (const wall of WALLS.filter((x) => x.level === 0)) {
      const [ax, az] = wall.a;
      const len = edgeLength(wall.a, wall.b);
      const ux = (wall.b[0] - ax) / len;
      const uz = (wall.b[1] - az) / len;
      const nx = -uz;
      const nz = ux;
      for (const o of wall.openings) {
        const s: Vec2 = [ax + ux * o.from, az + uz * o.from];
        const e: Vec2 = [ax + ux * o.to, az + uz * o.to];
        if (o.kind === 'window') {
          const t = wall.thickness / 2 + 0.05;
          marks.push([
            [s[0] + nx * t, s[1] + nz * t],
            [e[0] + nx * t, e[1] + nz * t],
          ]);
          marks.push([
            [s[0] - nx * t, s[1] - nz * t],
            [e[0] - nx * t, e[1] - nz * t],
          ]);
        } else {
          const r = Math.min(o.to - o.from, 1.4);
          const steps = 8;
          let prev: Vec2 = [s[0] + nx * r, s[1] + nz * r];
          marks.push([s, prev]);
          for (let k = 1; k <= steps; k++) {
            const ang = (k / steps) * (Math.PI / 2);
            const pt: Vec2 = [
              s[0] + (nx * Math.cos(ang) + ux * Math.sin(ang)) * r,
              s[1] + (nz * Math.cos(ang) + uz * Math.sin(ang)) * r,
            ];
            marks.push([prev, pt]);
            prev = pt;
          }
        }
      }
    }
    this.openingsPlan = this.segments(marks, 0.09, this.lineMaterial(p.gold, 1.4));
  }

  /** Solid box plus its cyan edges, positioned along a wall. */
  private boxWithEdges(
    w: number,
    h: number,
    d: number,
    material: THREE.Material,
    edges: THREE.LineBasicMaterial | null,
  ): THREE.Group {
    const g = new THREE.Group();
    const geometry = new THREE.BoxGeometry(w, h, d);
    const mesh = new THREE.Mesh(geometry, material);
    mesh.castShadow = true;
    mesh.receiveShadow = true;
    g.add(mesh);
    if (edges) g.add(new THREE.LineSegments(new THREE.EdgesGeometry(geometry), edges));
    return g;
  }

  private buildWall(wall: WallRun, order: number): void {
    const [ax, az] = wall.a;
    const len = edgeLength(wall.a, wall.b);
    const angle = -Math.atan2(wall.b[1] - az, wall.b[0] - ax);
    const group = new THREE.Group();
    group.position.set(ax, wall.baseY, az);
    group.rotation.y = angle;
    const edges = wall.level === 0 ? this.mats.edges : this.mats.edgesUpper;
    const material = wall.thickness > 0.15 ? this.mats.concrete : this.mats.concreteDark;
    const piece = (x0: number, x1: number, y0: number, y1: number): void => {
      if (x1 - x0 < 0.01 || y1 - y0 < 0.01) return;
      const b = this.boxWithEdges(x1 - x0, y1 - y0, wall.thickness, material, edges);
      b.position.set((x0 + x1) / 2, (y0 + y1) / 2, 0);
      group.add(b);
    };
    const openings = [...wall.openings].sort((a, b) => a.from - b.from);
    let cursor = 0;
    for (const o of openings) {
      piece(cursor, o.from, 0, wall.height);
      piece(o.from, o.to, 0, o.sill);
      piece(o.from, o.to, o.head, wall.height);
      cursor = o.to;
      this.buildOpening(wall, o.from, o.to, o.sill, o.head, o.kind);
    }
    piece(cursor, len, 0, wall.height);
    this.scene.add(group);
    this.wallGroups.push({ group, level: wall.level, order });
  }

  private buildOpening(
    wall: WallRun,
    from: number,
    to: number,
    sill: number,
    head: number,
    kind: 'door' | 'window',
  ): void {
    const [ax, az] = wall.a;
    const len = edgeLength(wall.a, wall.b);
    const ux = (wall.b[0] - ax) / len;
    const uz = (wall.b[1] - az) / len;
    const mid = (from + to) / 2;
    const width = to - from;
    const height = head - sill;
    const group = new THREE.Group();
    group.position.set(ax + ux * mid, wall.baseY + sill + height / 2, az + uz * mid);
    group.rotation.y = -Math.atan2(uz, ux);

    const frameT = 0.07;
    const frameMat = this.mats.concreteDark;
    const parts: [number, number, number, number][] = [
      [0, height / 2 - frameT / 2, width, frameT],
      [0, -height / 2 + frameT / 2, width, frameT],
      [-width / 2 + frameT / 2, 0, frameT, height],
      [width / 2 - frameT / 2, 0, frameT, height],
    ];
    for (const [x, y, w, h] of parts) {
      const b = this.boxWithEdges(w, h, wall.thickness + 0.04, frameMat, this.mats.edges);
      b.position.set(x, y, 0);
      group.add(b);
    }
    const glazed = kind === 'window' || width > 2;
    const pane = new THREE.Mesh(
      new THREE.BoxGeometry(width - frameT * 2, height - frameT * 2, 0.03),
      glazed ? this.mats.glass : this.mats.wood,
    );
    group.add(pane);
    this.scene.add(group);
    this.frameGroups.push(group);
  }

  private buildStructure(): void {
    let g = 0;
    let u = 0;
    for (const wall of WALLS) this.buildWall(wall, wall.level === 0 ? g++ : u++);

    for (const slab of SLABS) {
      const w = slab.x1 - slab.x0;
      const d = slab.z1 - slab.z0;
      const group = new THREE.Group();
      group.position.set(
        (slab.x0 + slab.x1) / 2,
        slab.y + slab.thickness / 2,
        (slab.z0 + slab.z1) / 2,
      );
      group.add(
        this.boxWithEdges(
          w,
          slab.thickness,
          d,
          this.mats.concrete,
          slab.level === 0 ? this.mats.edges : this.mats.edgesUpper,
        ),
      );
      this.scene.add(group);
      this.slabGroups.push({ group, level: slab.level });
    }

    // Slender steel columns under the verandah roof (part of the ground slab group timing)
    for (const x of [-8.2, -4.6]) {
      const col = this.boxWithEdges(0.16, 3, 0.16, this.mats.concreteDark, this.mats.edges);
      col.position.set(x, 1.5, 5.25);
      this.scene.add(col);
      this.frameGroups.push(col);
    }

    // Timber louvres (appear with materials)
    this.louvres = new THREE.Group();
    const step = (LOUVRES.x1 - LOUVRES.x0) / (LOUVRES.count - 1);
    for (let i = 0; i < LOUVRES.count; i++) {
      const fin = new THREE.Mesh(new THREE.BoxGeometry(0.07, LOUVRES.height, 0.28), this.mats.wood);
      fin.position.set(LOUVRES.x0 + i * step, LOUVRES.height / 2, LOUVRES.z + 0.25);
      fin.castShadow = true;
      this.louvres.add(fin);
    }
    this.louvres.position.y = LOUVRES.baseY;
    this.scene.add(this.louvres);
  }

  private buildLandscape(): void {
    const p = this.palette;
    const items: THREE.Object3D[] = [];

    // Pool with stone coping
    const pool = new THREE.Group();
    const pw = POOL.x1 - POOL.x0;
    const pd = POOL.z1 - POOL.z0;
    const coping = new THREE.Mesh(
      new THREE.BoxGeometry(pw + 0.8, 0.12, pd + 0.8),
      new THREE.MeshStandardMaterial({ color: p.stone, roughness: 0.8 }),
    );
    const water = new THREE.Mesh(
      new THREE.BoxGeometry(pw, 0.06, pd),
      new THREE.MeshStandardMaterial({
        color: p.water,
        roughness: 0.05,
        metalness: 0.3,
        emissive: p.water,
        emissiveIntensity: 0.25,
      }),
    );
    water.position.y = 0.05;
    coping.receiveShadow = true;
    pool.add(coping, water);
    pool.position.set((POOL.x0 + POOL.x1) / 2, 0.06, (POOL.z0 + POOL.z1) / 2);
    items.push(pool);

    // Trees: trunk + two canopy volumes
    const trunkMat = new THREE.MeshStandardMaterial({ color: p.trunk, roughness: 0.9 });
    const leafMat = new THREE.MeshStandardMaterial({
      color: p.foliage,
      roughness: 0.85,
      flatShading: true,
    });
    const leafMat2 = new THREE.MeshStandardMaterial({
      color: p.foliageLight,
      roughness: 0.85,
      flatShading: true,
    });
    TREES.forEach(([x, z], i) => {
      const tree = new THREE.Group();
      const h = 3 + (i % 3) * 0.8;
      const trunk = new THREE.Mesh(new THREE.CylinderGeometry(0.14, 0.22, h, 8), trunkMat);
      trunk.position.y = h / 2;
      const c1 = new THREE.Mesh(new THREE.IcosahedronGeometry(1.7 + (i % 2) * 0.4, 1), leafMat);
      c1.position.y = h + 0.6;
      const c2 = new THREE.Mesh(new THREE.IcosahedronGeometry(1.1, 1), leafMat2);
      c2.position.set(0.8, h + 1.4, 0.4);
      [trunk, c1, c2].forEach((m) => (m.castShadow = true));
      tree.add(trunk, c1, c2);
      tree.position.set(x, 0, z);
      items.push(tree);
    });

    // Low shrubs along the verandah and path stones to the entrance
    const shrubMat = new THREE.MeshStandardMaterial({
      color: p.foliageLight,
      roughness: 0.9,
      flatShading: true,
    });
    for (let i = 0; i < 7; i++) {
      const shrub = new THREE.Mesh(
        new THREE.IcosahedronGeometry(0.5 + (i % 3) * 0.12, 0),
        shrubMat,
      );
      shrub.position.set(-8 + i * 1.1, 0.35, 6.4);
      items.push(shrub);
    }
    const stoneMat = new THREE.MeshStandardMaterial({ color: p.stone, roughness: 0.85 });
    for (const [x, z] of PATH_STONES) {
      const stone = new THREE.Mesh(new THREE.CylinderGeometry(0.45, 0.5, 0.08, 14), stoneMat);
      stone.position.set(x, 0.04, z);
      stone.receiveShadow = true;
      items.push(stone);
    }

    items.forEach((object, i) => {
      this.scene.add(object);
      this.landscape.push({ object, order: i / items.length });
    });
  }
}

/** Waits for the brand font (labels are drawn into canvases) then builds the engine. */
export async function createCinematicEngine(
  canvas: HTMLCanvasElement,
  options: EngineOptions,
): Promise<CinematicEngine> {
  try {
    await Promise.race([
      document.fonts?.load('600 64px Poppins') ?? Promise.resolve(),
      new Promise((resolve) => setTimeout(resolve, 1500)),
    ]);
  } catch {
    // Font loading is best-effort; labels fall back to the system font.
  }
  return new CinematicEngine(canvas, options);
}

/**
 * Binds scroll progress through the story section to the engine with GSAP ScrollTrigger.
 * `scrub` smooths the value; scrolling up reverses the sequence. No pinning or snapping, so
 * native scrolling is never hijacked. Returns a cleanup function.
 */
export async function bindScrollProgress(
  section: HTMLElement,
  onProgress: (p: number) => void,
): Promise<() => void> {
  const [{ gsap }, { ScrollTrigger }] = await Promise.all([
    import('gsap'),
    import('gsap/ScrollTrigger'),
  ]);
  gsap.registerPlugin(ScrollTrigger);
  const state = { p: 0 };
  const tween = gsap.to(state, {
    p: 1,
    ease: 'none',
    onUpdate: () => onProgress(state.p),
    scrollTrigger: { trigger: section, start: 'top top', end: 'bottom bottom', scrub: 0.7 },
  });
  ScrollTrigger.refresh();
  return () => {
    tween.scrollTrigger?.kill();
    tween.kill();
  };
}
