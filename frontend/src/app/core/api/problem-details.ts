import * as z from 'zod/mini';

/**
 * RFC 9457 ProblemDetails, as produced by ASP.NET Core (`ValidationProblemDetails` adds `errors`).
 * ASSUMPTION — pending verification with the backend team (see docs/contract-register.md).
 * Parsing is lenient: unknown or missing fields never break error handling.
 */
export const problemDetailsSchema = z.looseObject({
  type: z.optional(z.string()),
  title: z.optional(z.string()),
  status: z.optional(z.number()),
  detail: z.optional(z.string()),
  instance: z.optional(z.string()),
  traceId: z.optional(z.string()),
  errors: z.optional(z.record(z.string(), z.array(z.string()))),
});

export type ProblemDetails = z.infer<typeof problemDetailsSchema>;

export function parseProblemDetails(body: unknown): ProblemDetails | null {
  const result = problemDetailsSchema.safeParse(body);
  return result.success ? result.data : null;
}

/** ASP.NET uses PascalCase keys ("Email"); forms use camelCase ("email"). */
export function toControlName(key: string): string {
  const last = key.split('.').pop() ?? key;
  return last.length ? last.charAt(0).toLowerCase() + last.slice(1) : last;
}
