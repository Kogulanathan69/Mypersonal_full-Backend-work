import { BuildEnvironment } from './environment.model';

/** Production build. Real backend only; no dev routes; no demo data. */
export const environment: BuildEnvironment = {
  name: 'production',
  production: true,
  useMocks: false,
  enableDevRoutes: false,
  runtimeConfigUrl: 'config.json',
};
