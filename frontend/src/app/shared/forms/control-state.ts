import { Signal, computed } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl } from '@angular/forms';
import { of, startWith, switchMap } from 'rxjs';
import { formErrorMessage } from './form-errors';

export interface ControlView {
  /** Message to show, or null. Appears once the control is touched (or the form was submitted). */
  readonly message: Signal<string | null>;
  readonly invalid: Signal<boolean>;
}

/** Turns a (signal of a) reactive-forms control into signals the zoneless UI can react to. */
export function controlView(
  control: Signal<AbstractControl | null | undefined>,
  label: Signal<string>,
): ControlView {
  const tick = toSignal(
    toObservable(control).pipe(switchMap((c) => (c ? c.events.pipe(startWith(null)) : of(null)))),
  );
  const invalid = computed(() => {
    tick();
    const c = control();
    return !!c && c.invalid && c.touched;
  });
  const message = computed(() => {
    tick();
    const c = control();
    return c && c.invalid && c.touched ? formErrorMessage(c.errors, label()) : null;
  });
  return { message, invalid };
}
