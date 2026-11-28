import { AppError } from '../../core/api/app-error';

export interface AlertView {
  readonly heading: string;
  readonly message: string;
  readonly tone: 'error' | 'warning';
  readonly reference?: string;
}

/**
 * Turns an AppError from an auth call into wording for that screen. Specific backend reasons
 * (e.g. suspended or unverified account) will be mapped from the verified error codes once the
 * contract is supplied; until then generic, non-revealing wording is used.
 */
export function authAlert(
  error: AppError,
  context: 'sign-in' | 'register' | 'verify' | 'forgot' | 'reset',
): AlertView {
  const reference = error.correlationId;
  switch (error.kind) {
    case 'config':
      return { heading: 'Not connected yet', message: error.message, tone: 'warning', reference };
    case 'unauthorized':
      return context === 'sign-in'
        ? {
            heading: 'Sign-in failed',
            message: 'The email or password is incorrect.',
            tone: 'error',
            reference,
          }
        : { heading: 'Not allowed', message: error.message, tone: 'error', reference };
    case 'forbidden':
      return {
        heading: 'This account cannot continue',
        message:
          'The account may be suspended or not yet verified. Contact BuildMind support if this continues.',
        tone: 'error',
        reference,
      };
    case 'rate-limited':
      return { heading: 'Too many attempts', message: error.message, tone: 'error', reference };
    case 'validation':
      return { heading: 'Please check the form', message: error.message, tone: 'error', reference };
    case 'conflict':
      return context === 'register'
        ? {
            heading: 'Account already exists',
            message:
              'An account with this email may already exist. Try signing in or resetting your password.',
            tone: 'error',
            reference,
          }
        : { heading: 'Please try again', message: error.message, tone: 'error', reference };
    default:
      return { heading: 'Something went wrong', message: error.message, tone: 'error', reference };
  }
}

/** Link tokens are invalid or expired when the server rejects them with these kinds. */
export const isInvalidTokenError = (error: AppError): boolean =>
  ['validation', 'not-found', 'conflict', 'unauthorized', 'forbidden'].includes(error.kind);
