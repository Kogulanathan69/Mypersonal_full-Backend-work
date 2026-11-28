import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { CustomerApi } from '../customer-api';
import { SurveyEvidencePage } from './survey-evidence-page';

describe('SurveyEvidencePage', () => {
  it('shows the actual backend limitation when no extraction exists', async () => {
    TestBed.configureTestingModule({
      imports: [SurveyEvidencePage],
      providers: [provideRouter([]), { provide: CustomerApi, useValue: {
        getSurveyExtraction: () => of({
          extraction: null, failureCode: 'OCR_FAILED', notice: 'Extraction could not be completed.',
          processingStatus: 'Failed', surveyId: 'survey-1',
        }),
        getSurveyFile: () => of(new Blob(['survey'], { type: 'application/pdf' })),
      } }],
    });
    const fixture = TestBed.createComponent(SurveyEvidencePage);
    fixture.componentRef.setInput('id', 'project-1');
    fixture.componentRef.setInput('surveyId', 'survey-1');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Extraction could not be completed.');
    expect(fixture.nativeElement.textContent).not.toContain('0 m');
  });
});
