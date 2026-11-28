import { AppErrorKind } from './app-error';

/** Default user-facing wording per error kind. Screens may override with more specific text. */
export const USER_MESSAGES: Readonly<Record<AppErrorKind, string>> = {
  network: 'We could not reach BuildMind. Check your connection and try again.',
  offline: 'You are offline. Your changes have not been sent.',
  timeout: 'The server took too long to respond. Please try again.',
  unauthorized: 'Your session has ended. Please sign in again.',
  forbidden: 'You do not have permission to do this.',
  'not-found': 'We could not find what you were looking for.',
  conflict: 'This item changed since you opened it. Reload to see the latest version.',
  validation: 'Some details need your attention.',
  'rate-limited': 'Too many attempts. Please wait a moment and try again.',
  server: 'Something went wrong on our side. Please try again shortly.',
  contract: 'BuildMind received an unexpected response. The team has been notified.',
  config: 'This feature is not configured yet.',
  unknown: 'Something went wrong. Please try again.',
};
