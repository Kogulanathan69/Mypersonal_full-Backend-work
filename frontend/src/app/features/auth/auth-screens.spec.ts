import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { AppError } from '../../core/api/app-error';
import { AUTH_API, AuthApi, SessionUser } from '../../core/auth/auth-api';
import { SessionStore } from '../../core/auth/session.store';
import { ForgotPassword } from './forgot-password';
import { Register } from './register';
import { ResetPassword } from './reset-password';
import { SignIn } from './sign-in';
import { VerifyEmail } from './verify-email';

type Fake = { [K in keyof AuthApi]: ReturnType<typeof vi.fn> };
const fakeApi = (overrides: Partial<Fake> = {}): Fake => ({
  signIn: vi.fn(() =>
    of<SessionUser>({
      id: 'user-1',
      email: 'nimal@example.invalid',
      displayName: 'Nimal',
      roles: ['customer'],
      permissions: [],
    }),
  ),
  register: vi.fn(() => of(undefined)),
  verifyEmail: vi.fn(() => of(undefined)),
  resendVerification: vi.fn(() => of(undefined)),
  forgotPassword: vi.fn(() => of(undefined)),
  resetPassword: vi.fn(() => of(undefined)),
  signOut: vi.fn(() => of(undefined)),
  currentUser: vi.fn(() => of(null)),
  ...overrides,
});

async function render<T>(cmp: Type<T>, url: string, api?: Fake) {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([{ path: '**', component: cmp }]),
      ...(api ? [{ provide: AUTH_API, useValue: api }] : []),
    ],
  });
  const router = TestBed.inject(Router);
  await router.navigateByUrl(url);
  const f = TestBed.createComponent(cmp);
  await f.whenStable();
  const el = f.nativeElement as HTMLElement;
  const type = async (selector: string, value: string) => {
    const input = el.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await f.whenStable();
  };
  const submit = async () => {
    el.querySelector<HTMLFormElement>('form')!.dispatchEvent(new Event('submit'));
    await f.whenStable();
  };
  return { f, el, router, type, submit };
}

describe('Sign in', () => {
  it('validates before calling the API', async () => {
    const api = fakeApi();
    const { el, submit } = await render(SignIn, '/auth/sign-in', api);
    await submit();
    expect(api.signIn).not.toHaveBeenCalled();
    expect(el.textContent).toContain('Email is required.');
  });

  it('shows the truthful not-connected state with the real (blocked) API', async () => {
    const { el, type, submit } = await render(SignIn, '/auth/sign-in');
    await type('input[type=email]', 'nimal@example.lk');
    await type('bm-password-input input', 'secret123');
    await submit();
    expect(el.querySelector('[role="status"]')?.textContent).toContain('Not connected yet');
  });

  it('on success stores the session and goes to a safe return URL only', async () => {
    const api = fakeApi();
    const { router, type, submit } = await render(
      SignIn,
      '/auth/sign-in?returnUrl=%2F%2Fevil.example',
      api,
    );
    const nav = vi.spyOn(router, 'navigateByUrl');
    await type('input[type=email]', 'nimal@example.lk');
    await type('bm-password-input input', 'secret123');
    await submit();
    expect(api.signIn).toHaveBeenCalledWith({ email: 'nimal@example.lk', password: 'secret123' });
    expect(TestBed.inject(SessionStore).user()?.displayName).toBe('Nimal');
    expect(nav).toHaveBeenCalledWith('/app/dashboard');
  });

  it('maps a 401 to an incorrect-credentials message and blocks double submission', async () => {
    const pending = new Subject<SessionUser>();
    const api = fakeApi({ signIn: vi.fn(() => pending as Observable<SessionUser>) });
    const { el, f, type, submit } = await render(SignIn, '/auth/sign-in', api);
    await type('input[type=email]', 'nimal@example.lk');
    await type('bm-password-input input', 'wrong-pass1');
    await submit();
    await submit();
    expect(api.signIn).toHaveBeenCalledTimes(1);
    pending.error(new AppError({ kind: 'unauthorized', message: 'x' }));
    await f.whenStable();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain(
      'The email or password is incorrect.',
    );
  });
});

describe('Register', () => {
  it('requires consent and matching passwords', async () => {
    const api = fakeApi();
    const { el, type, submit } = await render(Register, '/auth/register', api);
    await type('input[type=email]', 'nimal@example.lk');
    const pw = el.querySelectorAll<HTMLInputElement>('bm-password-input input');
    pw[0]!.value = 'abcdefg1';
    pw[0]!.dispatchEvent(new Event('input'));
    pw[1]!.value = 'abcdefg2';
    pw[1]!.dispatchEvent(new Event('input'));
    await submit();
    expect(api.register).not.toHaveBeenCalled();
    expect(el.textContent).toContain('The passwords do not match.');
    expect(el.textContent).toContain('You need to accept the terms');
  });

  it('submits the verified registration fields, then offers sign in', async () => {
    const api = fakeApi();
    const { el, f, type, submit } = await render(Register, '/auth/register', api);
    await type('input[type=email]', 'nimal@example.lk ');
    const pw = el.querySelectorAll<HTMLInputElement>('bm-password-input input');
    for (const p of [pw[0]!, pw[1]!]) {
      p.value = 'abcdefg1';
      p.dispatchEvent(new Event('input'));
    }
    el.querySelector<HTMLInputElement>('input[type=checkbox]')!.click();
    await f.whenStable();
    await submit();
    expect(api.register).toHaveBeenCalledWith({
      fullName: '',
      email: 'nimal@example.lk',
      phone: null,
      password: 'abcdefg1',
      acceptedTerms: true,
    });
    expect(el.querySelector('h1')?.textContent).toBe('Account created');
  });
});

describe('Forgot password', () => {
  it('confirms neutrally without revealing whether the account exists', async () => {
    const api = fakeApi();
    const { el, type, submit } = await render(ForgotPassword, '/auth/forgot-password', api);
    await type('input[type=email]', 'someone@example.lk');
    await submit();
    expect(el.textContent).toContain('If an account exists for that email');
  });
});

describe('Reset password', () => {
  it('reads the token once and removes it from the address bar', async () => {
    const api = fakeApi();
    const { el, f, router, submit } = await render(
      ResetPassword,
      '/auth/reset-password?token=abc.def&email=nimal%40example.lk',
      api,
    );
    await f.whenStable();
    expect(router.url).not.toContain('token');
    const pw = el.querySelectorAll<HTMLInputElement>('bm-password-input input');
    for (const p of [pw[0]!, pw[1]!]) {
      p.value = 'newpass123';
      p.dispatchEvent(new Event('input'));
    }
    await submit();
    expect(api.resetPassword).toHaveBeenCalledWith({
      email: 'nimal@example.lk',
      token: 'abc.def',
      password: 'newpass123',
    });
    expect(el.querySelector('h1')?.textContent).toBe('Password updated');
  });

  it('shows the invalid-link state without a token, and when the server rejects it', async () => {
    const noToken = await render(ResetPassword, '/auth/reset-password');
    expect(noToken.el.querySelector('h1')?.textContent).toContain('expired or is invalid');
    TestBed.resetTestingModule();
    const api = fakeApi({
      resetPassword: vi.fn(() =>
        throwError(() => new AppError({ kind: 'not-found', message: 'x' })),
      ),
    });
    const { el, f, submit } = await render(
      ResetPassword,
      '/auth/reset-password?token=old&email=nimal%40example.lk',
      api,
    );
    const pw = el.querySelectorAll<HTMLInputElement>('bm-password-input input');
    for (const p of [pw[0]!, pw[1]!]) {
      p.value = 'newpass123';
      p.dispatchEvent(new Event('input'));
    }
    await submit();
    await f.whenStable();
    expect(el.querySelector('h1')?.textContent).toContain('expired or is invalid');
  });
});

describe('Verify email', () => {
  it('verifies a link token and offers sign-in', async () => {
    const api = fakeApi();
    const { el, router } = await render(VerifyEmail, '/auth/verify?token=t1', api);
    expect(api.verifyEmail).toHaveBeenCalledWith('t1');
    expect(el.querySelector('h1')?.textContent).toBe('Email verified');
    expect(router.url).not.toContain('t1');
  });

  it('shows the expired state and resend form when the token is rejected', async () => {
    const api = fakeApi({
      verifyEmail: vi.fn(() =>
        throwError(() => new AppError({ kind: 'validation', message: 'x' })),
      ),
    });
    const { el, f } = await render(VerifyEmail, '/auth/verify?token=old&email=a%40b.lk', api);
    await f.whenStable();
    expect(el.querySelector('h1')?.textContent).toContain('expired or is invalid');
    expect(el.querySelector<HTMLInputElement>('input[type=email]')?.value).toBe('a@b.lk');
  });
});
