import { TestBed } from '@angular/core/testing';
import { AUTH_TOKEN_PROVIDER } from './auth-token';
import { PERMISSION_CHECKER } from './permission';
import { SessionEventsService } from './session-events.service';

describe('auth placeholders (until step 7)', () => {
  it('session expiry can be raised and acknowledged', () => {
    const s = TestBed.inject(SessionEventsService);
    s.markExpired();
    expect(s.expired()).toBe(true);
    s.acknowledge();
    expect(s.expired()).toBe(false);
  });

  it('defaults to no bearer token and deny-all permissions', () => {
    expect(TestBed.inject(AUTH_TOKEN_PROVIDER)()).toBeNull();
    expect(TestBed.inject(PERMISSION_CHECKER)('admin.users.suspend')).toBe(false);
  });
});
