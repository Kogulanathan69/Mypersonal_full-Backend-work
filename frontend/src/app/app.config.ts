import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  ErrorHandler,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import {
  TitleStrategy,
  provideRouter,
  withComponentInputBinding,
  withInMemoryScrolling,
} from '@angular/router';
import { environment } from '../environments/environment';
import { BUILD_ENV } from './core/config/build-env.token';
import { AUTH_API } from './core/auth/auth-api';
import { LiveAuthApi } from './core/auth/live-auth-api';
import { RuntimeConfigService } from './core/config/runtime-config.service';
import { GlobalErrorHandler } from './core/errors/global-error-handler';
import { API_INTERCEPTORS } from './core/http';
import { BuildMindTitleStrategy } from './core/routing/title-strategy';
import { routes } from './app.routes';

/** Owner: F — Foundation. Zoneless (Angular 22 default); no zone.js is installed. */
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: BUILD_ENV, useValue: environment },
    { provide: ErrorHandler, useClass: GlobalErrorHandler },
    { provide: AUTH_API, useExisting: LiveAuthApi },
    { provide: TitleStrategy, useClass: BuildMindTitleStrategy },
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled', anchorScrolling: 'enabled' }),
    ),
    provideHttpClient(withFetch(), withInterceptors(API_INTERCEPTORS)),
    provideAppInitializer(() => inject(RuntimeConfigService).load()),
  ],
};
