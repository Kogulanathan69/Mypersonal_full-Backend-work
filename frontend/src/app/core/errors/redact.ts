/**
 * Removes secrets and personal data from text before it is logged.
 * Applied to every log line written by the global error handler.
 */
const RULES: readonly [RegExp, string][] = [
  [/Bearer\s+[A-Za-z0-9\-._~+/]+=*/gi, 'Bearer [REDACTED]'],
  [/\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\b/g, '[REDACTED_JWT]'],
  [
    /([?&](?:token|access_token|refresh_token|code|password|key|signature|sig)=)[^&#\s]+/gi,
    '$1[REDACTED]',
  ],
  [/[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/g, '[REDACTED_EMAIL]'],
  [/("?(?:password|token|secret|apiKey|api_key)"?\s*[:=]\s*)"?[^",\s}&]+"?/gi, '$1[REDACTED]'],
];

export function redact(text: string): string {
  return RULES.reduce((acc, [re, replacement]) => acc.replace(re, replacement), text);
}
