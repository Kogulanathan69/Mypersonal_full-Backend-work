import { Injectable, signal } from '@angular/core';

export type ToastKind = 'success' | 'info' | 'warning' | 'error';

export interface Toast {
  readonly id: number;
  readonly kind: ToastKind;
  readonly title: string;
  readonly message?: string;
  /** 0 = stays until dismissed (used for errors). */
  readonly timeoutMs: number;
}

const DEFAULT_TIMEOUT: Record<ToastKind, number> = {
  success: 5000,
  info: 6000,
  warning: 8000,
  error: 0,
};
const MAX_VISIBLE = 4;

export interface Announcement {
  readonly id: number;
  readonly text: string;
  readonly politeness: 'polite' | 'assertive';
}

/**
 * Non-blocking notifications. Every toast is also announced to screen readers through the
 * aria-live regions rendered by bm-toast-host (no CDK LiveAnnouncer, to keep the initial bundle small).
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly _toasts = signal<readonly Toast[]>([]);
  private nextId = 1;
  private readonly timers = new Map<number, ReturnType<typeof setTimeout>>();

  readonly toasts = this._toasts.asReadonly();
  private readonly _announcement = signal<Announcement | null>(null);
  /** Latest text for screen readers; rendered into a polite or assertive live region. */
  readonly announcement = this._announcement.asReadonly();

  show(
    kind: ToastKind,
    title: string,
    message?: string,
    timeoutMs = DEFAULT_TIMEOUT[kind],
  ): number {
    const toast: Toast = { id: this.nextId++, kind, title, message, timeoutMs };
    this._toasts.update((list) => {
      const next = [...list, toast];
      const overflow = next.slice(0, Math.max(0, next.length - MAX_VISIBLE));
      overflow.forEach((t) => this.clearTimer(t.id));
      return next.slice(-MAX_VISIBLE);
    });
    this._announcement.set({
      id: toast.id,
      text: [title, message].filter(Boolean).join('. '),
      politeness: kind === 'error' ? 'assertive' : 'polite',
    });
    if (timeoutMs > 0) {
      this.timers.set(
        toast.id,
        setTimeout(() => this.dismiss(toast.id), timeoutMs),
      );
    }
    return toast.id;
  }

  success(title: string, message?: string): number {
    return this.show('success', title, message);
  }
  info(title: string, message?: string): number {
    return this.show('info', title, message);
  }
  warning(title: string, message?: string): number {
    return this.show('warning', title, message);
  }
  error(title: string, message?: string): number {
    return this.show('error', title, message);
  }

  dismiss(id: number): void {
    this.clearTimer(id);
    this._toasts.update((list) => list.filter((t) => t.id !== id));
  }

  clear(): void {
    this.timers.forEach((t) => clearTimeout(t));
    this.timers.clear();
    this._toasts.set([]);
  }

  private clearTimer(id: number): void {
    const timer = this.timers.get(id);
    if (timer) clearTimeout(timer);
    this.timers.delete(id);
  }
}
