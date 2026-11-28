/**
 * Accepts only same-app paths as a post-login destination, so a crafted link such as
 * `?returnUrl=https://evil.example` or `//evil.example` cannot send users off-site.
 * Auth pages themselves are not valid destinations (avoids loops).
 */
export function safeReturnUrl(raw: unknown, fallback = '/'): string {
  if (typeof raw !== 'string' || raw.length === 0 || raw.length > 2048) return fallback;
  let value: string;
  try {
    value = decodeURIComponent(raw);
  } catch {
    return fallback;
  }
  if (!value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) return fallback;
  if ([...value].some((ch) => ch.charCodeAt(0) < 0x20) || /^\/+[a-z][a-z0-9+.-]*:/i.test(value))
    return fallback;
  if (value === '/auth' || value.startsWith('/auth/') || value.startsWith('/auth?'))
    return fallback;
  return value;
}
