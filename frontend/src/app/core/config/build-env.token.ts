import { InjectionToken } from '@angular/core';
import { BuildEnvironment } from '../../../environments/environment.model';

/** The build-time environment (production / development / mock). Provided in app.config.ts. */
export const BUILD_ENV = new InjectionToken<BuildEnvironment>('BUILD_ENV');

export type { BuildEnvironment };
