import { redact } from './redact';

describe('redact', () => {
  it('removes bearer tokens', () => {
    expect(redact('Authorization: Bearer abc.def-123')).toBe('Authorization: Bearer [REDACTED]');
  });

  it('removes JWT-looking strings', () => {
    expect(redact('token eyJhbGciOiJIUzI1.eyJzdWIiOiIxMjM0.SflKxwRJSMeKKF2QT4')).toBe(
      'token [REDACTED_JWT]',
    );
  });

  it('removes secret query parameters but keeps others', () => {
    expect(redact('/reset?token=abc123&lang=en')).toBe('/reset?token=[REDACTED]&lang=en');
  });

  it('removes email addresses', () => {
    expect(redact('user nimal.perera@example.lk failed')).toBe('user [REDACTED_EMAIL] failed');
  });

  it('removes password and secret fields in JSON-like text', () => {
    expect(redact('{"password":"hunter2","name":"x"}')).toBe('{"password":[REDACTED],"name":"x"}');
  });

  it('leaves ordinary text alone', () => {
    expect(redact('validation failed for room 3')).toBe('validation failed for room 3');
  });
});
