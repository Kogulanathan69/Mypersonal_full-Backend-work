import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { CustomerApi } from '../customer-api';
import { ReviewPage } from './review-page';

describe('ReviewPage', () => {
  it('shows consent evidence without offering an unsafe request', async () => {
    TestBed.configureTestingModule({
      imports: [ReviewPage],
      providers: [
        provideRouter([]),
        {
          provide: CustomerApi,
          useValue: {
            listArchitects: () =>
              of([
                { recipientId: 'a1', displayName: 'Asha Perera', verificationStatus: 'Verified' },
              ]),
            listArchitectRequests: () =>
              of([
                {
                  requestId: 'r1', projectId: 'p1', recipientId: 'a1', reportId: 'report-1',
                  documentIds: [], policyVersion: 'policy-7', consentedAtUtc: '2026-11-28T00:00:00Z',
                  requestedAtUtc: '2026-11-28T00:00:00Z', stateVersion: 1, status: 'Accepted',
                  withdrawnAtUtc: null, delivery: 'Delivered',
                },
              ]),
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(ReviewPage);
    fixture.componentRef.setInput('id', 'p1');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Asha Perera');
    expect(fixture.nativeElement.textContent).toContain('Policy policy-7');
    expect(fixture.nativeElement.textContent).toContain('will not guess or hard-code a version');
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
  });
});
