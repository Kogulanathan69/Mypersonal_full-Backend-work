/**
 * Decides whether the Home Page may run the WebGL story or must use the static illustration.
 * Heavy rendering is never forced on low-capability devices or on visitors who ask for less motion.
 */
export type CinematicMode = 'webgl' | 'static';

export type StaticReason =
  | 'reduced-motion'
  | 'no-webgl'
  | 'save-data'
  | 'mobile'
  | 'low-memory'
  | 'user-off'
  | 'error'
  | 'context-lost';

export interface Capability {
  readonly mode: CinematicMode;
  readonly reason?: StaticReason;
  readonly webglAvailable: boolean;
  readonly lowPower: boolean;
  readonly mobile: boolean;
}

export const PREFERENCE_KEY = 'bm-home-animation';

interface NavigatorHints {
  readonly deviceMemory?: number;
  readonly hardwareConcurrency?: number;
  readonly connection?: { readonly saveData?: boolean };
}

export function detectWebgl(doc: Document): { available: boolean; majorCaveat: boolean } {
  try {
    const canvas = doc.createElement('canvas');
    const strict = canvas.getContext('webgl2', { failIfMajorPerformanceCaveat: true });
    if (strict) return { available: true, majorCaveat: false };
    const relaxed =
      doc.createElement('canvas').getContext('webgl2') ??
      doc.createElement('canvas').getContext('webgl');
    return { available: !!relaxed, majorCaveat: !!relaxed };
  } catch {
    return { available: false, majorCaveat: false };
  }
}

export function readPreference(storage: Pick<Storage, 'getItem'> | null): 'on' | 'off' | null {
  try {
    const v = storage?.getItem(PREFERENCE_KEY);
    return v === 'on' || v === 'off' ? v : null;
  } catch {
    return null;
  }
}

export function writePreference(
  storage: Pick<Storage, 'setItem'> | null,
  value: 'on' | 'off',
): void {
  try {
    storage?.setItem(PREFERENCE_KEY, value);
  } catch {
    // Storage can be unavailable (private mode); the choice then lasts for this visit only.
  }
}

export function decideCapability(input: {
  readonly reducedMotion: boolean;
  readonly webgl: { available: boolean; majorCaveat: boolean };
  readonly nav: NavigatorHints;
  readonly viewportWidth: number;
  readonly preference: 'on' | 'off' | null;
}): Capability {
  const mobile = input.viewportWidth < 768;
  const memory = input.nav.deviceMemory;
  const cores = input.nav.hardwareConcurrency ?? 4;
  const lowPower =
    input.webgl.majorCaveat || (memory !== undefined && memory <= 4) || cores <= 4 || mobile;
  const base = { webglAvailable: input.webgl.available, lowPower, mobile };

  if (!input.webgl.available) return { ...base, mode: 'static', reason: 'no-webgl' };
  if (input.preference === 'off') return { ...base, mode: 'static', reason: 'user-off' };
  if (input.preference === 'on') return { ...base, mode: 'webgl' };
  if (input.reducedMotion) return { ...base, mode: 'static', reason: 'reduced-motion' };
  if (input.nav.connection?.saveData) return { ...base, mode: 'static', reason: 'save-data' };
  if (mobile) return { ...base, mode: 'static', reason: 'mobile' };
  if (memory !== undefined && memory <= 2) return { ...base, mode: 'static', reason: 'low-memory' };
  return { ...base, mode: 'webgl' };
}

export const STATIC_REASON_TEXT: Record<StaticReason, string> = {
  'reduced-motion': 'Showing still images because your device asks for reduced motion.',
  'no-webgl': 'Showing still images because 3D graphics are not available in this browser.',
  'save-data': 'Showing still images to save data.',
  mobile: 'Showing a lightweight architectural story for this screen size.',
  'low-memory': 'Showing still images to keep this device responsive.',
  'user-off': 'Animation is turned off.',
  error: 'The 3D scene could not be started, so still images are shown instead.',
  'context-lost': 'The 3D scene stopped unexpectedly, so still images are shown instead.',
};
