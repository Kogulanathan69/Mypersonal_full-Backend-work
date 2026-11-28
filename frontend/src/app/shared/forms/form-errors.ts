import { AbstractControl, ValidationErrors } from '@angular/forms';
import { AppError } from '../../core/api/app-error';

type MessageFn = (error: unknown, label: string) => string;

/** One wording per validator key. Server messages (key "server") are shown as given. */
export const FORM_ERROR_MESSAGES: Readonly<Record<string, MessageFn>> = {
  required: (_e, label) => `${label} is required.`,
  notBlank: (_e, label) => `${label} cannot be only spaces.`,
  email: () => 'Enter a valid email address, like name@example.com.',
  minlength: (e, label) =>
    `${label} must be at least ${(e as { requiredLength: number }).requiredLength} characters.`,
  maxlength: (e, label) =>
    `${label} must be ${(e as { requiredLength: number }).requiredLength} characters or fewer.`,
  min: (e, label) => `${label} must be at least ${(e as { min: number }).min}.`,
  max: (e, label) => `${label} must be ${(e as { max: number }).max} or less.`,
  pattern: (_e, label) => `${label} is not in the expected format.`,
  sriLankaPhone: () => 'Enter a Sri Lankan number, like 071 234 5678 or +94 71 234 5678.',
  passwordPolicy: (e) => `Password needs ${(e as { missing: string[] }).missing.join(', ')}.`,
  fieldsMatch: () => 'The passwords do not match.',
  server: (e) => (Array.isArray(e) ? e.join(' ') : String(e)),
};

/** First human-readable message for a control's errors, or null. */
export function formErrorMessage(errors: ValidationErrors | null, label: string): string | null {
  if (!errors) return null;
  for (const [key, value] of Object.entries(errors)) {
    const fn = FORM_ERROR_MESSAGES[key];
    return fn ? fn(value, label) : `${label} is invalid.`;
  }
  return null;
}

/**
 * Copies server field errors (from ProblemDetails, via AppError.fieldErrors) onto matching
 * controls as `{ server: [...] }` and marks them touched so they show immediately.
 * Returns field names that had no matching control, so the caller can show them in a summary.
 */
export function applyServerErrors(form: AbstractControl, error: AppError): string[] {
  const unmatched: string[] = [];
  for (const [field, messages] of Object.entries(error.fieldErrors)) {
    const control = form.get(field);
    if (!control) {
      unmatched.push(...messages);
      continue;
    }
    control.setErrors({ ...(control.errors ?? {}), server: messages });
    control.markAsTouched();
  }
  return unmatched;
}
