import { HttpBackend, HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BUILD_ENV } from './build-env.token';
import {
  RuntimeConfig,
  UNCONFIGURED_RUNTIME_CONFIG,
  runtimeConfigSchema,
} from './runtime-config.schema';

export type RuntimeConfigStatus = 'pending' | 'loaded' | 'missing' | 'invalid';

/**
 * Loads public/config.json once at startup (see provideAppInitializer in app.config.ts).
 * Uses HttpBackend so the request bypasses the API interceptors.
 * Never throws: on failure the app starts with UNCONFIGURED_RUNTIME_CONFIG and
 * screens show a "configuration missing" state.
 */
@Injectable({ providedIn: 'root' })
export class RuntimeConfigService {
  private readonly http = new HttpClient(inject(HttpBackend));
  private readonly env = inject(BUILD_ENV);

  private readonly _config = signal<RuntimeConfig>(UNCONFIGURED_RUNTIME_CONFIG);
  private readonly _status = signal<RuntimeConfigStatus>('pending');
  private readonly _problems = signal<readonly string[]>([]);

  readonly config = this._config.asReadonly();
  readonly status = this._status.asReadonly();
  /** Human-readable reasons when status is 'missing' or 'invalid'. Never contains secrets. */
  readonly problems = this._problems.asReadonly();

  readonly apiConfigured = computed(() => this._config().api.mainBaseUrl.length > 0);
  readonly mapConfigured = computed(() => {
    const map = this._config().map;
    return map.provider !== 'none' && map.publicToken.length > 0;
  });

  async load(): Promise<void> {
    try {
      const raw = await firstValueFrom(this.http.get<unknown>(this.env.runtimeConfigUrl));
      const parsed = runtimeConfigSchema.safeParse(raw);
      if (!parsed.success) {
        this._status.set('invalid');
        this._problems.set(
          parsed.error.issues.map((i) => `${i.path.join('.') || '(root)'}: ${i.message}`),
        );
        return;
      }
      this._config.set(parsed.data);
      this._status.set('loaded');
      this._problems.set([]);
    } catch {
      this._status.set('missing');
      this._problems.set([`Could not load ${this.env.runtimeConfigUrl}`]);
    }
  }
}
