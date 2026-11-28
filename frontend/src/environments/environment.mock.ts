import { BuildEnvironment } from './environment.model';

/**
 * MOCK / NOT INTEGRATED build. Runs on labelled demo data only and shows a
 * permanent "Demo data" banner. Used for UI review and end-to-end tests.
 */
export const environment: BuildEnvironment = {
  name: 'mock',
  production: false,
  useMocks: true,
  enableDevRoutes: true,
  runtimeConfigUrl: 'config.json',
};
