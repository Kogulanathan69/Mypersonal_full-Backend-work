import * as z from 'zod/mini';

/**
 * Shape of public/config.json, loaded at startup so one build can serve every environment.
 * Holds no secrets. Empty strings mean "not configured" and produce a visible
 * missing-configuration state instead of a silent failure.
 *
 * BuildMind uses `zod/mini` (tree-shakable) everywhere; the full `zod` API is blocked by lint
 * because it adds ~440 kB to the initial bundle.
 */
export const runtimeConfigSchema = z.object({
  api: z.object({
    /** Base URL of the BuildMind main API. Empty until a verified environment exists. */
    mainBaseUrl: z.string().check(z.trim()),
    requestTimeoutMs: z._default(
      z.number().check(z.int(), z.minimum(1000), z.maximum(300_000)),
      30_000,
    ),
  }),
  map: z._default(
    z.object({
      provider: z._default(z.enum(['none', 'mapbox']), 'none'),
      /** Public, URL-restricted map token only. Never a secret key. */
      publicToken: z._default(z.string(), ''),
    }),
    { provider: 'none', publicToken: '' },
  ),
  features: z._default(
    z.object({
      upperFloors: z._default(z.boolean(), false),
      exportFormats: z._default(z.array(z.enum(['pdf', 'png', 'svg', 'dxf', 'json'])), ['pdf']),
    }),
    { upperFloors: false, exportFormats: ['pdf'] },
  ),
});

export type RuntimeConfig = z.infer<typeof runtimeConfigSchema>;

/** Safe defaults used when config.json is missing or invalid. Nothing is "configured". */
export const UNCONFIGURED_RUNTIME_CONFIG: RuntimeConfig = {
  api: { mainBaseUrl: '', requestTimeoutMs: 30_000 },
  map: { provider: 'none', publicToken: '' },
  features: { upperFloors: false, exportFormats: ['pdf'] },
};
