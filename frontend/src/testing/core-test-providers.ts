import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { EnvironmentProviders, Provider, computed, signal } from '@angular/core';
import { BuildEnvironment } from '../environments/environment.model';
import { BUILD_ENV } from '../app/core/config/build-env.token';
import {
  RuntimeConfig,
  UNCONFIGURED_RUNTIME_CONFIG,
} from '../app/core/config/runtime-config.schema';
import { RuntimeConfigService } from '../app/core/config/runtime-config.service';
import { API_INTERCEPTORS } from '../app/core/http';

/** Test-only helpers. Owner: F — Foundation. Excluded from the application build. */
export const TEST_ENV: BuildEnvironment = {
  name: 'production',
  production: true,
  useMocks: false,
  enableDevRoutes: false,
  runtimeConfigUrl: 'config.json',
};

export const TEST_API_BASE = 'https://api.test.buildmind.invalid';

export function runtimeConfigStub(
  overrides: Partial<RuntimeConfig['api']> = {},
): Partial<RuntimeConfigService> {
  const config = signal<RuntimeConfig>({
    ...UNCONFIGURED_RUNTIME_CONFIG,
    api: { ...UNCONFIGURED_RUNTIME_CONFIG.api, mainBaseUrl: TEST_API_BASE, ...overrides },
  });
  return {
    config: config.asReadonly(),
    apiConfigured: computed(() => config().api.mainBaseUrl.length > 0),
  };
}

/** Full HTTP stack (all interceptors) against HttpTestingController. */
export function provideCoreHttpTesting(
  options: { env?: Partial<BuildEnvironment>; api?: Partial<RuntimeConfig['api']> } = {},
): (Provider | EnvironmentProviders)[] {
  return [
    { provide: BUILD_ENV, useValue: { ...TEST_ENV, ...options.env } },
    { provide: RuntimeConfigService, useValue: runtimeConfigStub(options.api) },
    provideHttpClient(withInterceptors(API_INTERCEPTORS)),
    provideHttpClientTesting(),
  ];
}
