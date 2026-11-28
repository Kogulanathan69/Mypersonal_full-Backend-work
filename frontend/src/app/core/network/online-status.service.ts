import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { DOCUMENT } from '@angular/common';

/** Tracks the browser's online/offline state for the offline banner and request handling. */
@Injectable({ providedIn: 'root' })
export class OnlineStatusService {
  private readonly window = inject(DOCUMENT).defaultView;
  private readonly _online = signal(this.window?.navigator.onLine ?? true);
  readonly online = this._online.asReadonly();

  constructor() {
    const win = this.window;
    if (!win) return;
    const up = () => this._online.set(true);
    const down = () => this._online.set(false);
    win.addEventListener('online', up);
    win.addEventListener('offline', down);
    inject(DestroyRef).onDestroy(() => {
      win.removeEventListener('online', up);
      win.removeEventListener('offline', down);
    });
  }
}
