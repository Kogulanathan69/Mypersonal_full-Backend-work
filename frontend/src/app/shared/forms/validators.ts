import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/**
 * Shared validators. Client checks are for fast feedback only; the backend validates again.
 * The password policy below is a PROPOSAL until the backend policy is confirmed.
 */
export interface PasswordPolicy {
  readonly minLength: number;
  readonly requireLetter: boolean;
  readonly requireDigit: boolean;
}

export const DEFAULT_PASSWORD_POLICY: PasswordPolicy = {
  minLength: 8,
  requireLetter: true,
  requireDigit: true,
};

export function passwordPolicy(policy: PasswordPolicy = DEFAULT_PASSWORD_POLICY): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value = String(control.value ?? '');
    if (!value) return null; // `required` handles empty
    const failed: string[] = [];
    if (value.length < policy.minLength) failed.push(`at least ${policy.minLength} characters`);
    if (policy.requireLetter && !/[A-Za-z]/.test(value)) failed.push('a letter');
    if (policy.requireDigit && !/\d/.test(value)) failed.push('a number');
    return failed.length ? { passwordPolicy: { missing: failed } } : null;
  };
}

/**
 * Sri Lankan phone numbers: 0XXXXXXXXX (10 digits) or +94XXXXXXXXX (9 digits after +94).
 * Spaces and dashes are ignored.
 */
export const sriLankaPhone: ValidatorFn = (control) => {
  const raw = String(control.value ?? '').replace(/[\s-]/g, '');
  if (!raw) return null;
  return /^(?:0\d{9}|\+94\d{9})$/.test(raw) ? null : { sriLankaPhone: true };
};

/** Rejects values that are only whitespace (Validators.required accepts "   "). */
export const notBlank: ValidatorFn = (control) =>
  typeof control.value === 'string' && control.value.length > 0 && control.value.trim().length === 0
    ? { notBlank: true }
    : null;

/** Group validator: `field` must equal `matchField` (e.g. confirm password). Error set on the group. */
export function fieldsMatch(field: string, matchField: string): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const a = group.get(field)?.value;
    const b = group.get(matchField)?.value;
    return a && b && a !== b ? { fieldsMatch: { field, matchField } } : null;
  };
}

/** Very small strength hint for the password field (not a security control). */
export function passwordStrength(value: string): 0 | 1 | 2 | 3 | 4 {
  if (!value) return 0;
  let score = 0;
  if (value.length >= 8) score++;
  if (value.length >= 12) score++;
  if (/[a-z]/.test(value) && /[A-Z]/.test(value)) score++;
  if (/\d/.test(value) && /[^A-Za-z0-9]/.test(value)) score++;
  return Math.min(4, score) as 0 | 1 | 2 | 3 | 4;
}
