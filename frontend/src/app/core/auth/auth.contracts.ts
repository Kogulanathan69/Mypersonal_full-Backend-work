import { declareContract } from '../api/contract-status';

const EVIDENCE =
  'Captain docker-verified buildmind-api.d.ts at backend HEAD 25309cd82417fb1e821a09f4e9fbc6084dfc0b2f';
const VERIFIED = { owner: 'F' as const, status: 'VERIFIED' as const, evidence: EVIDENCE };
const VERIFICATION_NOTE =
  'Backend exposes authenticated code-based /users/me/email-verification operations; this pre-login token flow is not contract-compatible.';

export const AUTH_CONTRACTS = {
  signIn: declareContract({
    id: 'auth.signIn',
    ...VERIFIED,
    method: 'POST',
    path: '/api/v1/auth/login',
  }),
  register: declareContract({
    id: 'auth.register',
    ...VERIFIED,
    method: 'POST',
    path: '/api/v1/auth/register',
  }),
  verifyEmail: declareContract({
    id: 'auth.verifyEmail',
    owner: 'F',
    status: 'BLOCKED',
    notes: VERIFICATION_NOTE,
  }),
  resendVerification: declareContract({
    id: 'auth.resendVerification',
    owner: 'F',
    status: 'BLOCKED',
    notes: VERIFICATION_NOTE,
  }),
  forgotPassword: declareContract({
    id: 'auth.forgotPassword',
    owner: 'F',
    status: 'VERIFIED',
    method: 'POST',
    path: '/api/v1/auth/forgot-password',
    evidence: EVIDENCE,
  }),
  resetPassword: declareContract({
    id: 'auth.resetPassword',
    owner: 'F',
    status: 'VERIFIED',
    method: 'POST',
    path: '/api/v1/auth/reset-password',
    evidence: EVIDENCE,
  }),
  refresh: declareContract({
    id: 'auth.refresh',
    ...VERIFIED,
    method: 'POST',
    path: '/api/v1/auth/refresh',
  }),
  signOut: declareContract({
    id: 'auth.signOut',
    ...VERIFIED,
    method: 'POST',
    path: '/api/v1/auth/logout',
  }),
  currentUser: declareContract({
    id: 'auth.currentUser',
    owner: 'F',
    status: 'VERIFIED',
    method: 'GET',
    path: '/api/v1/users/me',
    evidence: EVIDENCE,
  }),
} as const;
