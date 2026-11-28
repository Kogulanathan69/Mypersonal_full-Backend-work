import { BuildEnvironment } from './environment.model';

/** Local development against a real (dev) backend configured in config.json. */
export const environment: BuildEnvironment = {
  name: 'development',
  production: false,
  useMocks: false,
  enableDevRoutes: true,
  runtimeConfigUrl: 'config.json',
};
