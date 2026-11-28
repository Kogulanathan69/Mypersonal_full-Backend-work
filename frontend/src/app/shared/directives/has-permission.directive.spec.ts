import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { PERMISSION_CHECKER } from '../../core/auth/permission';
import { HasPermission } from './has-permission.directive';

@Component({
  imports: [HasPermission],
  template: `<button *bmHasPermission="'admin.users.suspend'">Suspend</button>`,
})
class Host {}

describe('HasPermission', () => {
  it('hides content by default (deny-all until the session exists)', async () => {
    const f = TestBed.createComponent(Host);
    await f.whenStable();
    expect(f.nativeElement.querySelector('button')).toBeNull();
  });

  it('shows and hides content as the checker result changes', async () => {
    const granted = signal(true);
    TestBed.configureTestingModule({
      providers: [{ provide: PERMISSION_CHECKER, useValue: () => granted() }],
    });
    const f = TestBed.createComponent(Host);
    await f.whenStable();
    expect(f.nativeElement.querySelector('button')?.textContent).toContain('Suspend');
    granted.set(false);
    await f.whenStable();
    expect(f.nativeElement.querySelector('button')).toBeNull();
  });
});
