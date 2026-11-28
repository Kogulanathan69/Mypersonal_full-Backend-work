import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { ApiClient } from '../api/api-client';
import { AuthTokenStore } from './auth-token.store';
import { LiveAuthApi } from './live-auth-api';

describe('LiveAuthApi', () => {
  it('holds credentials in memory and maps the verified profile after sign in', async () => {
    const api = {
      post: vi.fn(() =>
        of({
          accessToken: 'access-1',
          expiresAtUtc: '2026-11-28T06:00:00Z',
          refreshToken: 'refresh-1',
          refreshTokenExpiresAtUtc: '2026-12-05T06:00:00Z',
          tokenType: 'Bearer',
          user: {
            id: 'user-1',
            email: 'nimal@example.lk',
            isActive: true,
            createdAtUtc: '2026-11-28T05:00:00Z',
          },
        }),
      ),
      get: vi.fn(() =>
        of({
          id: 'user-1',
          email: 'nimal@example.lk',
          fullName: 'Nimal Perera',
          phoneNumber: null,
          isActive: true,
          createdAtUtc: '2026-11-28T05:00:00Z',
        }),
      ),
    };
    TestBed.configureTestingModule({ providers: [{ provide: ApiClient, useValue: api }] });

    const user = await firstValueFrom(
      TestBed.inject(LiveAuthApi).signIn({ email: ' nimal@example.lk ', password: 'secret123' }),
    );

    expect(api.post).toHaveBeenCalledWith(
      'api/v1/auth/login',
      { email: 'nimal@example.lk', password: 'secret123' },
      expect.anything(),
      expect.objectContaining({ suppressSessionExpiry: true }),
    );
    expect(user).toEqual({
      id: 'user-1',
      email: 'nimal@example.lk',
      displayName: 'Nimal Perera',
      roles: [],
      permissions: [],
    });
    expect(TestBed.inject(AuthTokenStore).accessToken()).toBe('access-1');
    expect(TestBed.inject(AuthTokenStore).refreshToken()).toBe('refresh-1');
  });

  it('does not make a logout request when the server issued no refresh token', async () => {
    const api = { post: vi.fn(), get: vi.fn() };
    TestBed.configureTestingModule({ providers: [{ provide: ApiClient, useValue: api }] });

    await firstValueFrom(TestBed.inject(LiveAuthApi).signOut());

    expect(api.post).not.toHaveBeenCalled();
  });
});
