import { TestBed } from '@angular/core/testing';
import { Subject, firstValueFrom, of } from 'rxjs';
import { LoadingService } from './loading.service';

describe('LoadingService', () => {
  let service: LoadingService;
  beforeEach(() => (service = TestBed.inject(LoadingService)));

  it('never goes below zero', () => {
    service.end();
    expect(service.pendingCount()).toBe(0);
    expect(service.isLoading()).toBe(false);
  });

  it('tracks an observable from subscribe to completion', async () => {
    const source = new Subject<number>();
    const result = firstValueFrom(service.track(source));
    expect(service.isLoading()).toBe(true);
    source.next(1);
    source.complete();
    await result;
    expect(service.isLoading()).toBe(false);
  });

  it('does not count until subscribed', async () => {
    const tracked = service.track(of(1));
    expect(service.isLoading()).toBe(false);
    await firstValueFrom(tracked);
    expect(service.isLoading()).toBe(false);
  });

  it('ends on unsubscribe', () => {
    const sub = service.track(new Subject<void>()).subscribe();
    expect(service.pendingCount()).toBe(1);
    sub.unsubscribe();
    expect(service.pendingCount()).toBe(0);
  });
});
