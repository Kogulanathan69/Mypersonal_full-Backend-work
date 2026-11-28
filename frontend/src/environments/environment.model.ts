/**
 * Build-time environment. Holds no secrets and no backend URLs:
 * backend endpoints come from the runtime `config.json` (see core/config).
 */
export interface BuildEnvironment {
  /** Human-readable build name, shown in the dev gallery and error reports. */
  readonly name: 'production' | 'development' | 'mock';
  readonly production: boolean;
  /**
   * When true the app runs against labelled demo data only (MOCK / NOT INTEGRATED).
   * A permanent "Demo data" banner is shown on every page.
   */
  readonly useMocks: boolean;
  /** Enables /dev/* routes (UI gallery, layout previews). Never true in production. */
  readonly enableDevRoutes: boolean;
  /** Path of the runtime configuration file, relative to the app base href. */
  readonly runtimeConfigUrl: string;
}
