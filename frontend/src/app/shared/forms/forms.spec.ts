import { FormControl, FormGroup, Validators } from '@angular/forms';
import { Subject, of, throwError } from 'rxjs';
import { AppError } from '../../core/api/app-error';
import { applyServerErrors, formErrorMessage } from './form-errors';
import { createSubmitState } from './submit-state';
import {
  fieldsMatch,
  notBlank,
  passwordPolicy,
  passwordStrength,
  sriLankaPhone,
} from './validators';

describe('validators', () => {
  it('passwordPolicy lists what is missing and ignores empty values', () => {
    const v = passwordPolicy();
    expect(v(new FormControl(''))).toBeNull();
    expect(v(new FormControl('abc'))).toEqual({
      passwordPolicy: { missing: ['at least 8 characters', 'a number'] },
    });
    expect(v(new FormControl('abcdefg1'))).toBeNull();
  });

  it.each([
    ['0712345678', true],
    ['071 234 5678', true],
    ['+94 71 234 5678', true],
    ['+94712345678', true],
    ['712345678', false],
    ['07123456789', false],
    ['+9471234567', false],
  ])('sriLankaPhone(%s) valid=%s', (value, valid) => {
    expect(sriLankaPhone(new FormControl(value)) === null).toBe(valid);
  });

  it('notBlank rejects whitespace-only strings', () => {
    expect(notBlank(new FormControl('   '))).toEqual({ notBlank: true });
    expect(notBlank(new FormControl(' a '))).toBeNull();
    expect(notBlank(new FormControl(''))).toBeNull();
  });

  it('fieldsMatch flags different values on the group', () => {
    const g = new FormGroup(
      { a: new FormControl('x1'), b: new FormControl('x2') },
      { validators: fieldsMatch('a', 'b') },
    );
    expect(g.errors).toEqual({ fieldsMatch: { field: 'a', matchField: 'b' } });
    g.controls.b.setValue('x1');
    expect(g.errors).toBeNull();
  });

  it('passwordStrength grows with length and variety', () => {
    expect(passwordStrength('')).toBe(0);
    expect(passwordStrength('abcdefgh')).toBe(1);
    expect(passwordStrength('Abcdefghijk1!x')).toBe(4);
  });
});

describe('form errors', () => {
  it('produces one readable message per error', () => {
    expect(formErrorMessage({ required: true }, 'Email')).toBe('Email is required.');
    expect(formErrorMessage({ minlength: { requiredLength: 3 } }, 'Name')).toBe(
      'Name must be at least 3 characters.',
    );
    expect(formErrorMessage({ custom: true }, 'Code')).toBe('Code is invalid.');
    expect(formErrorMessage(null, 'X')).toBeNull();
  });

  it('applies server field errors to controls and returns unmatched ones', () => {
    const form = new FormGroup({ email: new FormControl('a@b.lk', Validators.email) });
    const unmatched = applyServerErrors(
      form,
      new AppError({
        kind: 'validation',
        message: 'x',
        fieldErrors: { email: ['Taken.'], phone: ['Bad phone.'] },
      }),
    );
    expect(form.controls.email.errors).toEqual({ server: ['Taken.'] });
    expect(form.controls.email.touched).toBe(true);
    expect(formErrorMessage(form.controls.email.errors, 'Email')).toBe('Taken.');
    expect(unmatched).toEqual(['Bad phone.']);
  });
});

describe('createSubmitState', () => {
  it('ignores a second run while the first is pending', () => {
    const state = createSubmitState();
    const pending = new Subject<number>();
    const action = vi.fn(() => pending);
    expect(state.run(action)).toBe(true);
    expect(state.run(action)).toBe(false);
    expect(action).toHaveBeenCalledTimes(1);
    expect(state.pending()).toBe(true);
    pending.next(1);
    expect(state.pending()).toBe(false);
  });

  it('calls onSuccess and records errors as AppError', () => {
    const ok = createSubmitState();
    const done = vi.fn();
    ok.run(() => of('saved'), done);
    expect(done).toHaveBeenCalledWith('saved');

    const failing = createSubmitState();
    failing.run(() => throwError(() => new AppError({ kind: 'conflict', message: 'Changed.' })));
    expect(failing.error()?.kind).toBe('conflict');
    expect(failing.pending()).toBe(false);
    failing.reset();
    expect(failing.error()).toBeNull();
  });
});
