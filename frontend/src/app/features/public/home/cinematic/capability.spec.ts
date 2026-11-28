import { decideCapability, readPreference, writePreference } from './capability';

const base = {
  reducedMotion: false,
  webgl: { available: true, majorCaveat: false },
  nav: { deviceMemory: 8, hardwareConcurrency: 8 },
  viewportWidth: 1440,
  preference: null as 'on' | 'off' | null,
};

describe('decideCapability', () => {
  it('uses WebGL on a capable desktop', () => {
    expect(decideCapability(base)).toMatchObject({ mode: 'webgl', lowPower: false, mobile: false });
  });

  it.each([
    [{ webgl: { available: false, majorCaveat: false } }, 'no-webgl'],
    [{ reducedMotion: true }, 'reduced-motion'],
    [{ nav: { connection: { saveData: true } } }, 'save-data'],
    [{ nav: { deviceMemory: 2 } }, 'low-memory'],
    [{ preference: 'off' as const }, 'user-off'],
  ])('falls back to static for %o', (override, reason) => {
    expect(decideCapability({ ...base, ...override })).toMatchObject({ mode: 'static', reason });
  });

  it('lets an explicit "on" preference override reduced motion, but never missing WebGL', () => {
    expect(decideCapability({ ...base, reducedMotion: true, preference: 'on' }).mode).toBe('webgl');
    expect(
      decideCapability({
        ...base,
        webgl: { available: false, majorCaveat: false },
        preference: 'on',
      }).mode,
    ).toBe('static');
  });

  it('marks phones and weak GPUs as low power', () => {
    expect(decideCapability({ ...base, viewportWidth: 390 })).toMatchObject({
      mobile: true,
      lowPower: true,
    });
    expect(
      decideCapability({ ...base, webgl: { available: true, majorCaveat: true } }).lowPower,
    ).toBe(true);
  });
});

describe('animation preference storage', () => {
  it('reads valid values and ignores junk or unavailable storage', () => {
    expect(readPreference({ getItem: () => 'off' })).toBe('off');
    expect(readPreference({ getItem: () => 'maybe' })).toBeNull();
    expect(readPreference(null)).toBeNull();
    expect(
      readPreference({
        getItem: () => {
          throw new Error('denied');
        },
      }),
    ).toBeNull();
  });

  it('never throws when storage is blocked', () => {
    expect(() =>
      writePreference(
        {
          setItem: () => {
            throw new Error('denied');
          },
        },
        'on',
      ),
    ).not.toThrow();
  });
});
