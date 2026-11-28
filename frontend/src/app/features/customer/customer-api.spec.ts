import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { ApiClient } from '../../core/api/api-client';
import { CustomerApi } from './customer-api';

describe('CustomerApi', () => {
  it('sends the server revision when updating a project', async () => {
    const project = {
      id: 'project-1', name: 'Courtyard home', projectType: 'NewBuild', locationReference: 'Kandy',
      status: 'Draft', revision: 8, createdAtUtc: '2026-11-28T05:00:00Z', updatedAtUtc: null,
    };
    const api = { patch: vi.fn(() => of(project)) };
    TestBed.configureTestingModule({ providers: [{ provide: ApiClient, useValue: api }] });

    await firstValueFrom(TestBed.inject(CustomerApi).updateProject('project-1', {
      name: 'Courtyard home', projectType: 'NewBuild', locationReference: 'Kandy',
    }, 7));

    expect(api.patch).toHaveBeenCalledWith(
      'api/v1/projects/project-1',
      { name: 'Courtyard home', projectType: 'NewBuild', locationReference: 'Kandy', revision: 7 },
      expect.anything(),
      expect.objectContaining({ contractId: 'customer.projects.update' }),
    );
  });

  it('returns only the server-owned security event items', async () => {
    const items = [{ eventId: 'event-1', kind: 'LoginSucceeded', occurredAtUtc: '2026-11-28T05:00:00Z', detailsJson: null }];
    const api = { get: vi.fn(() => of({ items, page: 1, pageSize: 20, total: 1 })) };
    TestBed.configureTestingModule({ providers: [{ provide: ApiClient, useValue: api }] });

    await expect(firstValueFrom(TestBed.inject(CustomerApi).listSecurityEvents())).resolves.toEqual(items);
    expect(api.get).toHaveBeenCalledWith(
      'api/v1/users/me/security-events', expect.anything(),
      expect.objectContaining({ params: { page: 1, pageSize: 20 } }),
    );
  });

  it('unwraps the verified session-list envelope', async () => {
    const items = [{ sessionId: 'session-1', active: true }];
    const api = { get: vi.fn(() => of({ items })) };
    TestBed.configureTestingModule({ providers: [{ provide: ApiClient, useValue: api }] });

    await expect(firstValueFrom(TestBed.inject(CustomerApi).listSessions())).resolves.toEqual(items);
  });

  it('uploads a survey as multipart form data under the verified file field', async () => {
    let capturedBody: unknown;
    const api = { post: vi.fn((...args: unknown[]) => {
      capturedBody = args[1];
      return of({ status: 'Queued', surveyId: 'survey-1', uploadedAtUtc: '2026-11-28T05:00:00Z' });
    }) };
    TestBed.configureTestingModule({ providers: [{ provide: ApiClient, useValue: api }] });
    const file = new File(['survey'], 'site.pdf', { type: 'application/pdf' });

    await firstValueFrom(TestBed.inject(CustomerApi).uploadSurvey('project-1', file));

    const body = capturedBody as FormData;
    expect((body.get('file') as File).name).toBe('site.pdf');
    expect(api.post).toHaveBeenCalledWith(
      'api/v1/projects/project-1/surveys', expect.any(FormData), expect.anything(),
      expect.objectContaining({ contractId: 'customer.surveys.upload', timeoutMs: 30_000 }),
    );
  });

  it('loads server-owned survey extraction evidence without synthesizing measurements', async () => {
    const extraction = {
      extraction: null, failureCode: 'EXTRACTION_UNAVAILABLE',
      notice: 'Extraction is unavailable.', processingStatus: 'Failed', surveyId: 'survey-1',
    };
    const api = { get: vi.fn(() => of(extraction)) };
    TestBed.configureTestingModule({ providers: [{ provide: ApiClient, useValue: api }] });

    await expect(firstValueFrom(TestBed.inject(CustomerApi).getSurveyExtraction('project-1', 'survey-1'))).resolves.toEqual(extraction);
    expect(api.get).toHaveBeenCalledWith(
      'api/v1/projects/project-1/surveys/survey-1/extraction', expect.anything(),
      expect.objectContaining({ contractId: 'customer.surveys.extraction' }),
    );
  });

  it('downloads survey source through the declared private-file contract', async () => {
    const blob = new Blob(['survey'], { type: 'application/pdf' });
    const api = { getBlob: vi.fn(() => of(blob)) };
    TestBed.configureTestingModule({ providers: [{ provide: ApiClient, useValue: api }] });
    await expect(firstValueFrom(TestBed.inject(CustomerApi).getSurveyFile('project-1', 'survey-1'))).resolves.toBe(blob);
    expect(api.getBlob).toHaveBeenCalledWith(
      'api/v1/projects/project-1/surveys/survey-1/file',
      expect.objectContaining({ contractId: 'customer.surveys.file' }),
    );
  });

  it('submits review decisions with the current immutable review version', async () => {
    const api = { post: vi.fn(() => of({ notice: 'Recorded', review: { reviewId: 'r1', extractionId: 'e1', surveyId: 's1', version: 3, reviewedBy: 'u1', createdAtUtc: '2026-11-28T00:00:00Z', measurements: [], note: null } })) };
    TestBed.configureTestingModule({ providers: [{ provide: ApiClient, useValue: api }] });
    await firstValueFrom(TestBed.inject(CustomerApi).submitSurveyReview('p1', 's1', 'e1', 2, [{ measurementIndex: 0, action: 'CONFIRM' }], null));
    expect(api.post).toHaveBeenCalledWith(
      'api/v1/projects/p1/surveys/s1/extraction/reviews',
      { extractionId: 'e1', expectedVersion: 2, decisions: [{ measurementIndex: 0, action: 'CONFIRM' }], note: null },
      expect.anything(), expect.objectContaining({ contractId: 'customer.surveys.reviews.create' }),
    );
  });

  it('loads authoritative land geometry for the project workspace', async () => {
    const land = { landRevisionId: 'l1', projectId: 'p1', revisionNumber: 1, exterior: [{ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 0, y: 10 }], coordinateSystem: { type: 'Local', unit: 'm', origin: { x: 0, y: 0 }, xDirection: 'East', yDirection: 'North' }, areaM2: 50, edgeCount: 3, surveyId: 's1', createdAtUtc: '2026-11-28T00:00:00Z', geometryDigest: 'g1', provenance: 'Customer' };
    const api = { get: vi.fn(() => of(land)) };
    TestBed.configureTestingModule({ providers: [{ provide: ApiClient, useValue: api }] });
    await expect(firstValueFrom(TestBed.inject(CustomerApi).getLand('p1'))).resolves.toEqual(land);
  });

  it('persists requirements against the current revision', async () => {
    const saved = { revisionId: 'r2', projectId: 'p1', revisionNumber: 2, schemaVersion: '1', catalogueVersion: 'cat1', floorCount: 1, groundFloorRooms: [{ count: 2, roomType: 'Bedroom' }], additionalSpaces: [], relationships: [], designMethod: null, createdAtUtc: '2026-11-28T00:00:00Z' };
    const api = { put: vi.fn(() => of(saved)) };
    TestBed.configureTestingModule({ providers: [{ provide: ApiClient, useValue: api }] });
    await firstValueFrom(TestBed.inject(CustomerApi).saveRequirements('p1', { ...saved, expectedRevisionId: 'r1' }));
    expect(api.put).toHaveBeenCalledWith('api/v1/projects/p1/requirements', expect.objectContaining({ expectedRevisionId: 'r1' }), expect.anything(), expect.objectContaining({ contractId: 'customer.requirements.save' }));
  });

  it('starts generation with exact prerequisite revisions and an idempotency key', async () => {
    const job = { jobId: 'j1', projectId: 'p1', status: 'Succeeded', outcome: 'Completed', reasonCode: null, algorithmVersion: '1', evidenceMode: 'none', evidencePackId: null, seed: 7, landRevisionId: 'l1', requirementsRevisionId: 'r1', createdAtUtc: '2026-11-28T00:00:00Z', completedAtUtc: '2026-11-28T00:00:01Z', diagnostics: [], notice: '', candidates: [] };
    const api = { post: vi.fn(() => of(job)) };
    TestBed.configureTestingModule({ providers: [{ provide: ApiClient, useValue: api }] });
    await firstValueFrom(TestBed.inject(CustomerApi).startGeneration('p1', 'l1', 'r1', 7, 'key-1'));
    expect(api.post).toHaveBeenCalledWith('api/v1/projects/p1/generation-jobs', { expectedLandRevisionId: 'l1', expectedRequirementsRevisionId: 'r1', idempotencyKey: 'key-1', seed: 7 }, expect.anything(), expect.objectContaining({ contractId: 'customer.generation.create', idempotencyKey: 'key-1' }));
  });

  it('loads a selected plan from the owner-scoped plan contract', async () => {
    const api = { get: vi.fn(() => of({ planId: 'pl1' })) };
    TestBed.configureTestingModule({ providers: [{ provide: ApiClient, useValue: api }] });
    await firstValueFrom(TestBed.inject(CustomerApi).getPlan('p1','pl1'));
    expect(api.get).toHaveBeenCalledWith('api/v1/projects/p1/plans/pl1',expect.anything(),expect.objectContaining({contractId:'customer.plans.get'}));
  });

  it('loads immutable plan revision history', async () => {
    const revisions=[{revisionId:'r1',versionNumber:1,changeSummary:'Generated'}];
    const api={get:vi.fn(()=>of({revisions,nextCursor:null}))};TestBed.configureTestingModule({providers:[{provide:ApiClient,useValue:api}]});
    await expect(firstValueFrom(TestBed.inject(CustomerApi).listPlanVersions('p1','pl1'))).resolves.toEqual(revisions);
  });

  it('requests an instruction proposal against the exact base revision', async () => {
    const response={candidate:{candidateId:'c1',projectId:'p1',planId:'pl1',baseRevisionId:'r1',geometryJson:'{}',geometryHash:'g',validation:{validationId:'v1',validatorVersion:'1',geometryHash:'g',geometryValid:true,requirementsMatch:true,regulatoryEvidenceStatus:'Unavailable',hasBlockingIssues:false,validatedAtUtc:'2026-11-28T00:00:00Z'},createdAtUtc:'2026-11-28T00:00:00Z',expiresAtUtc:'2026-11-28T01:00:00Z'},proposalSource:'RULE_BASED',applied:false,proposedCommand:{commandType:'resizeRoom',targetId:'room1',payload:{scaleX:1.1,scaleY:1,anchorX:.5,anchorY:.5}},notice:'Review before applying.'};
    const api={post:vi.fn(()=>of(response))};TestBed.configureTestingModule({providers:[{provide:ApiClient,useValue:api}]});
    await firstValueFrom(TestBed.inject(CustomerApi).proposeAiEdit('p1','pl1','r1','make bedroom wider'));
    expect(api.post).toHaveBeenCalledWith('api/v1/projects/p1/plans/pl1/ai-edit/proposals',{expectedBaseRevisionId:'r1',instruction:'make bedroom wider'},expect.anything(),expect.objectContaining({contractId:'customer.plans.aiEditProposal'}));
  });
});
