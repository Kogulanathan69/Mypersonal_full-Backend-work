import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import * as z from 'zod/mini';
import { ApiClient } from '../../core/api/api-client';
import { CUSTOMER_CONTRACTS } from './customer.contracts';

const projectSchema = z.object({
  id: z.string(),
  name: z.string(),
  projectType: z.nullable(z.string()),
  locationReference: z.nullable(z.string()),
  status: z.string(),
  revision: z.number(),
  createdAtUtc: z.string(),
  updatedAtUtc: z.nullable(z.string()),
});

const projectsSchema = z.array(projectSchema);
const unreadSchema = z.object({ unread: z.number() });
const profileSchema = z.object({
  id: z.string(),
  email: z.string(),
  fullName: z.nullable(z.unknown()),
  phoneNumber: z.nullable(z.unknown()),
  isActive: z.boolean(),
  createdAtUtc: z.string(),
});
const securitySchema = z.object({
  emailVerified: z.boolean(),
  emailVerifiedAtUtc: z.nullable(z.unknown()),
  verificationPending: z.boolean(),
});
const emptySchema = z.null();
const sessionSchema = z.object({
  sessionId: z.optional(z.nullable(z.string())),
  startedAtUtc: z.optional(z.nullable(z.string())),
  lastRefreshedAtUtc: z.optional(z.nullable(z.string())),
  expiresAtUtc: z.optional(z.nullable(z.string())),
  absoluteExpiresAtUtc: z.optional(z.nullable(z.string())),
  active: z.optional(z.nullable(z.boolean())),
});
const sessionListSchema = z.object({ items: z.optional(z.nullable(z.array(sessionSchema))) });
const securityEventSchema = z.object({
  eventId: z.string(),
  kind: z.string(),
  occurredAtUtc: z.string(),
  detailsJson: z.optional(z.nullable(z.string())),
});
const securityEventsPageSchema = z.object({
  items: z.array(securityEventSchema),
  page: z.number(),
  pageSize: z.number(),
  total: z.number(),
});
const lifecycleSchema = z.object({
  projectId: z.string(),
  stage: z.string(),
  milestones: z.array(z.object({ code: z.string(), required: z.boolean(), satisfied: z.boolean() })),
  nextActions: z.array(z.string()),
  stale: z.object({
    staleGenerationJobs: z.number(),
    stalePlans: z.number(),
    staleReports: z.number(),
    supersededConfirmations: z.number(),
  }),
});
const surveySummarySchema = z.object({
  surveyId: z.optional(z.string()),
  originalFileName: z.optional(z.nullable(z.string())),
  mediaType: z.optional(z.nullable(z.string())),
  byteLength: z.optional(z.number()),
  sha256Hash: z.optional(z.nullable(z.string())),
  status: z.optional(z.nullable(z.string())),
  uploadedAtUtc: z.optional(z.string()),
  completedAtUtc: z.optional(z.nullable(z.string())),
  failureCode: z.optional(z.nullable(z.string())),
  processingAttempts: z.optional(z.number()),
});
const surveyListSchema = z.object({ items: z.optional(z.nullable(z.array(surveySummarySchema))) });
const surveyUploadSchema = z.object({ status: z.string(), surveyId: z.string(), uploadedAtUtc: z.string() });
const surveyStatusSchema = z.object({
  surveyId: z.string(), status: z.string(), stateVersion: z.number(), uploadedAtUtc: z.string(),
  completedAtUtc: z.nullable(z.unknown()), failureCode: z.string(),
});
const evidenceSourceSchema = z.object({ file_id: z.string(), page_number: z.number(), reference_text: z.string() });
const extractedMeasurementSchema = z.object({
  measurement_name: z.string(), value: z.number(), unit: z.string(), confidence: z.nullable(z.unknown()),
  source: evidenceSourceSchema, review_status: z.string(),
});
const surveyExtractionSchema = z.object({
  extraction: z.nullable(z.object({
    createdAtUtc: z.string(), extractionId: z.string(), method: z.string(), outcome: z.string(),
    evidence: z.object({
      extraction_id: z.string(), file_id: z.string(), status: z.string(),
      measurements: z.array(extractedMeasurementSchema),
      lot_reference: z.object({ value: z.string(), source: evidenceSourceSchema, review_status: z.string() }),
      reported_area: z.object({ value: z.number(), unit: z.string(), source: evidenceSourceSchema, review_status: z.string() }),
      diagnostics: z.array(z.unknown()), completed_at_utc: z.nullable(z.unknown()),
    }),
  })),
  failureCode: z.nullable(z.unknown()), notice: z.string(), processingStatus: z.string(), surveyId: z.string(),
});
const surveyReviewSchema = z.object({
  notice: z.string(),
  review: z.object({
    reviewId: z.string(), extractionId: z.string(), surveyId: z.string(), version: z.number(), reviewedBy: z.string(),
    createdAtUtc: z.string(), measurements: z.array(z.object({
      measurementIndex: z.number(), measurementName: z.string(), action: z.string(),
      valueMetres: z.nullable(z.number()), basis: z.string(),
    })), note: z.nullable(z.string()),
  }),
});
const pointSchema = z.object({ x: z.number(), y: z.number() });
const coordinateSystemSchema = z.object({
  type: z.string(), unit: z.string(), origin: pointSchema, xDirection: z.string(), yDirection: z.string(),
});
const landSchema = z.object({
  landRevisionId: z.string(), projectId: z.string(), revisionNumber: z.number(), exterior: z.array(pointSchema),
  coordinateSystem: coordinateSystemSchema, areaM2: z.number(), edgeCount: z.number(), surveyId: z.nullable(z.string()),
  createdAtUtc: z.string(), geometryDigest: z.string(), provenance: z.string(),
});
const landValidationSchema = z.object({ areaM2: z.nullable(z.number()), code: z.string(), edgeCount: z.nullable(z.number()), persisted: z.boolean(), valid: z.boolean() });
const landEdgeSchema = z.object({
  edgeId: z.string(), start: z.array(z.number()), end: z.array(z.number()), lengthM: z.number(),
  gridBearingDeg: z.number(), parentEdgeId: z.nullable(z.string()), role: z.string(),
});
const landEdgesSchema = z.object({
  landRevisionId: z.optional(z.string()), revisionNumber: z.optional(z.number()), geometryDigest: z.optional(z.nullable(z.string())),
  edges: z.optional(z.nullable(z.array(landEdgeSchema))), roadFrontageEdges: z.optional(z.number()),
  unassignedEdges: z.optional(z.number()), reviewComplete: z.optional(z.boolean()), notice: z.optional(z.nullable(z.string())),
});
const requirementsSchema = z.object({
  revisionId: z.string(), projectId: z.string(), revisionNumber: z.number(), schemaVersion: z.string(), catalogueVersion: z.string(),
  floorCount: z.number(), groundFloorRooms: z.array(z.object({ count: z.number(), roomType: z.string() })),
  additionalSpaces: z.array(z.unknown()), relationships: z.array(z.unknown()), designMethod: z.nullable(z.unknown()), createdAtUtc: z.string(),
});
const planPointSchema = z.object({ x: z.number(), y: z.number() });
const planGeometrySchema = z.object({
  schemaVersion: z.string(), projectId: z.string(), planId: z.string(), landRevisionId: z.string(), requirementsRevisionId: z.string(),
  coordinateSystem: coordinateSystemSchema,
  floors: z.array(z.object({ id: z.string(), level: z.number(),
    rooms: z.array(z.object({ id: z.string(), name: z.string(), type: z.string(), polygon: z.array(planPointSchema), wallIds: z.array(z.string()) })),
    walls: z.array(z.object({ id: z.string(), start: planPointSchema, end: planPointSchema, thickness: z.number() })),
    openings: z.array(z.object({ id: z.string(), offset: z.number(), type: z.string(), wallId: z.string(), width: z.number() })),
  })),
});
const candidateSchema = z.object({
  candidateId: z.string(), diversitySignature: z.string(), geometry: planGeometrySchema, geometryHash: z.string(),
  metrics: z.object({ floorAreaM2: z.number(), footprintAreaM2: z.number(), roomCount: z.number() }), ordinal: z.number(), rank: z.number(),
  validation: z.object({ diagnostics: z.array(z.object({ code: z.string(), message: z.string(), severity: z.string() })), geometryValid: z.boolean(), hasBlockingIssues: z.boolean(), regulatoryEvidenceStatus: z.string(), requirementsMatch: z.boolean(), validationId: z.string(), validatorVersion: z.string() }),
});
const generationJobSchema = z.object({
  jobId: z.string(), projectId: z.string(), status: z.string(), outcome: z.string(), reasonCode: z.nullable(z.unknown()), algorithmVersion: z.string(), evidenceMode: z.string(), evidencePackId: z.nullable(z.unknown()), seed: z.number(), landRevisionId: z.string(), requirementsRevisionId: z.string(), createdAtUtc: z.string(), completedAtUtc: z.string(), diagnostics: z.array(z.object({ code: z.string(), severity: z.string(), message: z.string() })), notice: z.string(), candidates: z.array(candidateSchema),
});
const selectedPlanSchema = z.object({ planId: z.string(), projectId: z.string(), revisionId: z.string(), versionNumber: z.number(), geometryHash: z.string(), landRevisionId: z.string(), requirementsRevisionId: z.string(), validation: z.object({ validationId: z.string(), validatorVersion: z.string(), geometryHash: z.string(), geometryValid: z.boolean(), requirementsMatch: z.boolean(), regulatoryEvidenceStatus: z.string(), hasBlockingIssues: z.boolean(), validatedAtUtc: z.string() }), createdAtUtc: z.string(), changeSummary: z.string(), notice: z.string() });
const planSchema = z.object({
  planId:z.string(),projectId:z.string(),revisionId:z.string(),versionNumber:z.number(),geometryHash:z.string(),landRevisionId:z.string(),requirementsRevisionId:z.string(),geometry:planGeometrySchema,
  validation:z.object({validationId:z.string(),validatorVersion:z.string(),geometryHash:z.string(),geometryValid:z.boolean(),requirementsMatch:z.boolean(),regulatoryEvidenceStatus:z.string(),hasBlockingIssues:z.boolean(),validatedAtUtc:z.string()}),
  confirmation:z.nullable(z.unknown()),createdAtUtc:z.string(),changeSummary:z.nullable(z.string()),notice:z.string(),
});
const editCandidateSchema=z.object({baseRevisionId:z.string(),candidateGeometry:planGeometrySchema,candidateId:z.string(),createdAtUtc:z.string(),diagnostics:z.array(z.unknown()),expiresAtUtc:z.string(),geometryHash:z.string(),planId:z.string(),projectId:z.string(),validation:z.object({geometryHash:z.string(),geometryValid:z.boolean(),hasBlockingIssues:z.boolean(),regulatoryEvidenceStatus:z.string(),requirementsMatch:z.boolean(),validatedAtUtc:z.string(),validationId:z.string(),validatorVersion:z.string()})});
const candidateResultSchema=z.object({candidateId:z.string(),planId:z.string(),projectId:z.string(),status:z.string(),revision:z.nullable(z.unknown())});
const revisionSchema=z.object({changeSummary:z.nullable(z.string()),createdAtUtc:z.string(),createdBy:z.string(),geometryHash:z.string(),geometrySchemaVersion:z.string(),landRevisionId:z.string(),parentRevisionId:z.nullable(z.string()),requirementsRevisionId:z.string(),revisionId:z.string(),validationId:z.string(),versionNumber:z.number()});
const revisionListSchema=z.object({nextCursor:z.nullable(z.unknown()),revisions:z.array(revisionSchema)});
const confirmationSchema=z.object({confirmationId:z.string(),projectId:z.string(),planId:z.string(),confirmedRevisionId:z.string(),geometryHash:z.string(),validationId:z.string(),confirmedAtUtc:z.string(),warningAcknowledgement:z.boolean(),publication:z.string(),notice:z.string()});
const reportsSchema=z.object({reports:z.array(z.object({reportId:z.string(),status:z.string(),planId:z.string(),planRevisionId:z.string(),confirmationId:z.string(),requestedAtUtc:z.string(),completedAtUtc:z.string(),failureCode:z.nullable(z.unknown())}))});
const reportCreateSchema=z.object({reportId:z.string(),projectId:z.string(),status:z.string(),stateVersion:z.number(),existing:z.boolean(),notice:z.string()});
const architectsSchema=z.object({architects:z.array(z.object({displayName:z.string(),recipientId:z.string(),verificationStatus:z.string()})),notice:z.string()});
const architectRequestsSchema=z.object({requests:z.array(z.object({requestId:z.string(),projectId:z.string(),recipientId:z.string(),reportId:z.string(),documentIds:z.array(z.unknown()),policyVersion:z.string(),consentedAtUtc:z.string(),requestedAtUtc:z.string(),stateVersion:z.number(),status:z.string(),withdrawnAtUtc:z.nullable(z.unknown()),delivery:z.string()}))});
const aiEditProposalSchema=z.object({candidate:z.object({candidateId:z.string(),projectId:z.string(),planId:z.string(),baseRevisionId:z.string(),geometryJson:z.string(),geometryHash:z.string(),validation:z.object({validationId:z.string(),validatorVersion:z.string(),geometryHash:z.string(),geometryValid:z.boolean(),requirementsMatch:z.boolean(),regulatoryEvidenceStatus:z.string(),hasBlockingIssues:z.boolean(),validatedAtUtc:z.string()}),createdAtUtc:z.string(),expiresAtUtc:z.string()}),proposalSource:z.string(),applied:z.boolean(),proposedCommand:z.object({commandType:z.string(),targetId:z.string(),payload:z.object({scaleX:z.number(),scaleY:z.number(),anchorX:z.number(),anchorY:z.number()})}),notice:z.string()});

export type ProjectSummary = z.infer<typeof projectSchema>;
export type CustomerProfile = z.infer<typeof profileSchema>;
export type SecurityStatus = z.infer<typeof securitySchema>;
export type AccountSession = z.infer<typeof sessionSchema>;
export type SecurityEvent = z.infer<typeof securityEventSchema>;
export type ProjectLifecycle = z.infer<typeof lifecycleSchema>;
export type SurveySummary = z.infer<typeof surveySummarySchema>;
export type SurveyStatus = z.infer<typeof surveyStatusSchema>;
export type SurveyUpload = z.infer<typeof surveyUploadSchema>;
export type SurveyExtraction = z.infer<typeof surveyExtractionSchema>;
export interface SurveyReviewDecision { readonly measurementIndex: number; readonly action: 'CONFIRM' | 'CORRECT' | 'REJECT'; readonly correctedValue?: number; readonly correctedUnit?: 'm' | 'ft'; }
export type LandGeometry = z.infer<typeof landSchema>;
export type LandEdge = z.infer<typeof landEdgeSchema>;
export type RequirementsSnapshot = z.infer<typeof requirementsSchema>;
export interface SaveRequirementsInput { readonly expectedRevisionId: string | null; readonly catalogueVersion: string; readonly floorCount: number; readonly groundFloorRooms: readonly { count: number; roomType: string }[]; readonly additionalSpaces: readonly unknown[]; readonly relationships: readonly { kind: string; sourceRoomType: string; targetRoomType: string }[]; readonly designMethod: string | null; }
export type GenerationJob = z.infer<typeof generationJobSchema>;
export type LayoutCandidate = z.infer<typeof candidateSchema>;
export type ProjectPlan = z.infer<typeof planSchema>;
export type EditCandidate=z.infer<typeof editCandidateSchema>;
export type PlanRevision=z.infer<typeof revisionSchema>;
export type ProjectReport=z.infer<typeof reportsSchema>['reports'][number];
export type ArchitectDirectoryEntry=z.infer<typeof architectsSchema>['architects'][number];
export type ArchitectRequest=z.infer<typeof architectRequestsSchema>['requests'][number];
export type AiEditProposal=z.infer<typeof aiEditProposalSchema>;

export interface CreateProjectInput {
  readonly name: string;
  readonly projectType: string;
  readonly locationReference: string | null;
}

@Injectable({ providedIn: 'root' })
export class CustomerApi {
  private readonly api = inject(ApiClient);

  listProjects(): Observable<readonly ProjectSummary[]> {
    return this.api.get('api/v1/projects', projectsSchema, {
      contractId: CUSTOMER_CONTRACTS.projectsList.id,
    });
  }

  createProject(input: CreateProjectInput): Observable<ProjectSummary> {
    return this.api.post('api/v1/projects', input, projectSchema, {
      contractId: CUSTOMER_CONTRACTS.projectsCreate.id,
    });
  }

  getProject(id: string): Observable<ProjectSummary> {
    return this.api.get(`api/v1/projects/${encodeURIComponent(id)}`, projectSchema, {
      contractId: CUSTOMER_CONTRACTS.projectDetail.id,
    });
  }

  updateProject(id: string, input: CreateProjectInput, revision: number): Observable<ProjectSummary> {
    return this.api.patch(
      `api/v1/projects/${encodeURIComponent(id)}`,
      { ...input, revision },
      projectSchema,
      { contractId: CUSTOMER_CONTRACTS.projectUpdate.id },
    );
  }

  getProjectLifecycle(projectId: string): Observable<ProjectLifecycle> {
    return this.api.get(`api/v1/projects/${encodeURIComponent(projectId)}/lifecycle`, lifecycleSchema, {
      contractId: CUSTOMER_CONTRACTS.projectLifecycle.id,
    });
  }

  unreadNotifications(): Observable<number> {
    return this.api
      .get('api/v1/notifications/unread-count', unreadSchema, {
        contractId: CUSTOMER_CONTRACTS.notificationsUnread.id,
        skipGlobalLoading: true,
      })
      .pipe((source) => new Observable<number>((subscriber) => source.subscribe({
        next: (value) => subscriber.next(value.unread),
        error: (error: unknown) => subscriber.error(error),
        complete: () => subscriber.complete(),
      })));
  }

  getProfile(): Observable<CustomerProfile> {
    return this.api.get('api/v1/users/me', profileSchema, {
      contractId: CUSTOMER_CONTRACTS.profileGet.id,
    });
  }

  updateProfile(fullName: string, phoneNumber: string | null): Observable<CustomerProfile> {
    return this.api.put('api/v1/users/me', { fullName, phoneNumber }, profileSchema, {
      contractId: CUSTOMER_CONTRACTS.profileUpdate.id,
    });
  }

  getSecurity(): Observable<SecurityStatus> {
    return this.api.get('api/v1/users/me/security', securitySchema, {
      contractId: CUSTOMER_CONTRACTS.securityGet.id,
    });
  }

  changePassword(currentPassword: string, newPassword: string): Observable<void> {
    return this.api
      .post(
        'api/v1/users/me/password',
        { currentPassword, newPassword, confirmPassword: newPassword },
        emptySchema,
        { contractId: CUSTOMER_CONTRACTS.passwordChange.id },
      )
      .pipe((source) =>
        new Observable<void>((subscriber) =>
          source.subscribe({
            next: () => subscriber.next(),
            error: (error: unknown) => subscriber.error(error),
            complete: () => subscriber.complete(),
          }),
        ),
      );
  }

  listSessions(): Observable<readonly AccountSession[]> {
    return this.api
      .get('api/v1/users/me/sessions', sessionListSchema, {
        contractId: CUSTOMER_CONTRACTS.sessionsList.id,
      })
      .pipe(map((result) => result.items ?? []));
  }

  revokeSession(sessionId: string): Observable<void> {
    return this.api
      .delete(`api/v1/users/me/sessions/${encodeURIComponent(sessionId)}`, emptySchema, {
        contractId: CUSTOMER_CONTRACTS.sessionRevoke.id,
      })
      .pipe(map(() => undefined));
  }

  listSecurityEvents(): Observable<readonly SecurityEvent[]> {
    return this.api
      .get('api/v1/users/me/security-events', securityEventsPageSchema, {
        contractId: CUSTOMER_CONTRACTS.securityEvents.id,
        params: { page: 1, pageSize: 20 },
      })
      .pipe(map((page) => page.items));
  }

  listSurveys(projectId: string): Observable<readonly SurveySummary[]> {
    return this.api
      .get(`api/v1/projects/${encodeURIComponent(projectId)}/surveys`, surveyListSchema, {
        contractId: CUSTOMER_CONTRACTS.surveysList.id,
      })
      .pipe(map((result) => result.items ?? []));
  }

  uploadSurvey(projectId: string, file: File): Observable<SurveyUpload> {
    const body = new FormData();
    body.append('file', file, file.name);
    return this.api.post(
      `api/v1/projects/${encodeURIComponent(projectId)}/surveys`, body, surveyUploadSchema,
      { contractId: CUSTOMER_CONTRACTS.surveyUpload.id, timeoutMs: 30_000 },
    );
  }

  getSurveyStatus(projectId: string, surveyId: string): Observable<SurveyStatus> {
    return this.api.get(
      `api/v1/projects/${encodeURIComponent(projectId)}/surveys/${encodeURIComponent(surveyId)}`,
      surveyStatusSchema, { contractId: CUSTOMER_CONTRACTS.surveyStatus.id },
    );
  }

  getSurveyExtraction(projectId: string, surveyId: string): Observable<SurveyExtraction> {
    return this.api.get(
      `api/v1/projects/${encodeURIComponent(projectId)}/surveys/${encodeURIComponent(surveyId)}/extraction`,
      surveyExtractionSchema, { contractId: CUSTOMER_CONTRACTS.surveyExtraction.id },
    );
  }

  getSurveyFile(projectId: string, surveyId: string): Observable<Blob> {
    return this.api.getBlob(
      `api/v1/projects/${encodeURIComponent(projectId)}/surveys/${encodeURIComponent(surveyId)}/file`,
      { contractId: CUSTOMER_CONTRACTS.surveyFile.id, timeoutMs: 30_000 },
    );
  }

  submitSurveyReview(projectId: string, surveyId: string, extractionId: string, expectedVersion: number, decisions: readonly SurveyReviewDecision[], note: string | null) {
    return this.api.post(
      `api/v1/projects/${encodeURIComponent(projectId)}/surveys/${encodeURIComponent(surveyId)}/extraction/reviews`,
      { extractionId, expectedVersion, decisions, note }, surveyReviewSchema,
      { contractId: CUSTOMER_CONTRACTS.surveyReviewCreate.id },
    );
  }

  getLand(projectId: string): Observable<LandGeometry> {
    return this.api.get(`api/v1/projects/${encodeURIComponent(projectId)}/land`, landSchema, { contractId: CUSTOMER_CONTRACTS.landGet.id });
  }

  getLandEdges(projectId: string): Observable<readonly LandEdge[]> {
    return this.api.get(`api/v1/projects/${encodeURIComponent(projectId)}/land/edges`, landEdgesSchema, { contractId: CUSTOMER_CONTRACTS.landEdges.id })
      .pipe(map((result) => result.edges ?? []));
  }

  validateLand(projectId: string, exterior: readonly { x: number; y: number }[]) {
    return this.api.post(`api/v1/projects/${encodeURIComponent(projectId)}/land/validate`, { exterior }, landValidationSchema, { contractId: CUSTOMER_CONTRACTS.landValidate.id });
  }

  confirmLand(projectId: string, land: LandGeometry): Observable<LandGeometry> {
    return this.api.post(`api/v1/projects/${encodeURIComponent(projectId)}/land/confirm`, {
      expectedCurrentLandRevisionId: land.landRevisionId, exterior: land.exterior, surveyId: land.surveyId ?? undefined,
    }, landSchema, { contractId: CUSTOMER_CONTRACTS.landConfirm.id });
  }

  getRequirements(projectId: string): Observable<RequirementsSnapshot> {
    return this.api.get(`api/v1/projects/${encodeURIComponent(projectId)}/requirements`, requirementsSchema, { contractId: CUSTOMER_CONTRACTS.requirementsGet.id });
  }

  saveRequirements(projectId: string, input: SaveRequirementsInput): Observable<RequirementsSnapshot> {
    return this.api.put(`api/v1/projects/${encodeURIComponent(projectId)}/requirements`, input, requirementsSchema, { contractId: CUSTOMER_CONTRACTS.requirementsSave.id });
  }

  startGeneration(projectId: string, landRevisionId: string, requirementsRevisionId: string, seed: number, idempotencyKey: string): Observable<GenerationJob> {
    return this.api.post(`api/v1/projects/${encodeURIComponent(projectId)}/generation-jobs`, { expectedLandRevisionId: landRevisionId, expectedRequirementsRevisionId: requirementsRevisionId, idempotencyKey, seed }, generationJobSchema, { contractId: CUSTOMER_CONTRACTS.generationCreate.id, idempotencyKey, timeoutMs: 60_000 });
  }

  getGeneration(projectId: string, jobId: string): Observable<GenerationJob> {
    return this.api.get(`api/v1/projects/${encodeURIComponent(projectId)}/generation-jobs/${encodeURIComponent(jobId)}`, generationJobSchema, { contractId: CUSTOMER_CONTRACTS.generationGet.id });
  }

  selectLayout(projectId: string, jobId: string, candidateId: string) {
    return this.api.post(`api/v1/projects/${encodeURIComponent(projectId)}/generation-jobs/${encodeURIComponent(jobId)}/select`, { alternativeId: candidateId }, selectedPlanSchema, { contractId: CUSTOMER_CONTRACTS.generationSelect.id });
  }

  getPlan(projectId:string,planId:string):Observable<ProjectPlan>{
    return this.api.get(`api/v1/projects/${encodeURIComponent(projectId)}/plans/${encodeURIComponent(planId)}`,planSchema,{contractId:CUSTOMER_CONTRACTS.planGet.id});
  }

  createResizeCandidate(projectId:string,plan:ProjectPlan,floorId:string,roomId:string,scaleX:number,scaleY:number):Observable<EditCandidate>{
    const commandId=crypto.randomUUID();return this.api.post(`api/v1/projects/${encodeURIComponent(projectId)}/plans/${encodeURIComponent(plan.planId)}/candidates`,{schemaVersion:plan.geometry.schemaVersion,projectId,planId:plan.planId,baseRevisionId:plan.revisionId,command:{commandId,commandType:'resizeRoom',floorId,targetId:roomId,payload:{scaleX,scaleY,anchorX:0.5,anchorY:0.5}}},editCandidateSchema,{contractId:CUSTOMER_CONTRACTS.editCandidateCreate.id});
  }
  applyEditCandidate(projectId:string,planId:string,candidateId:string,acknowledgedWarnings:boolean){return this.api.post(`api/v1/projects/${encodeURIComponent(projectId)}/plans/${encodeURIComponent(planId)}/candidates/${encodeURIComponent(candidateId)}/apply`,{acknowledgedWarnings},candidateResultSchema,{contractId:CUSTOMER_CONTRACTS.editCandidateApply.id});}
  rejectEditCandidate(projectId:string,planId:string,candidateId:string){return this.api.post(`api/v1/projects/${encodeURIComponent(projectId)}/plans/${encodeURIComponent(planId)}/candidates/${encodeURIComponent(candidateId)}/reject`,null,candidateResultSchema,{contractId:CUSTOMER_CONTRACTS.editCandidateReject.id});}
  listPlanVersions(projectId:string,planId:string):Observable<readonly PlanRevision[]>{return this.api.get(`api/v1/projects/${encodeURIComponent(projectId)}/plans/${encodeURIComponent(planId)}/versions`,revisionListSchema,{contractId:CUSTOMER_CONTRACTS.planVersions.id,params:{pageSize:50}}).pipe(map(x=>x.revisions));}
  confirmPlan(projectId:string,plan:ProjectPlan,acknowledgedWarnings:boolean){return this.api.post(`api/v1/projects/${encodeURIComponent(projectId)}/plans/${encodeURIComponent(plan.planId)}/confirm`,{revisionId:plan.revisionId,expectedGeometryHash:plan.geometryHash,acknowledgedWarnings},confirmationSchema,{contractId:CUSTOMER_CONTRACTS.planConfirm.id});}
  revertPlan(projectId:string,planId:string,currentRevisionId:string,targetRevisionId:string){return this.api.post(`api/v1/projects/${encodeURIComponent(projectId)}/plans/${encodeURIComponent(planId)}/revert`,{expectedBaseRevisionId:currentRevisionId,targetRevisionId},selectedPlanSchema,{contractId:CUSTOMER_CONTRACTS.planRevert.id});}
  listReports(projectId:string):Observable<readonly ProjectReport[]>{return this.api.get(`api/v1/projects/${encodeURIComponent(projectId)}/reports`,reportsSchema,{contractId:CUSTOMER_CONTRACTS.reportsList.id}).pipe(map(x=>x.reports));}
  createReport(projectId:string,plan:ProjectPlan,confirmationId:string){const reportId=crypto.randomUUID();return this.api.post(`api/v1/projects/${encodeURIComponent(projectId)}/reports`,{confirmationId,geometryHash:plan.geometryHash,planId:plan.planId,planRevisionId:plan.revisionId,reportId},reportCreateSchema,{contractId:CUSTOMER_CONTRACTS.reportsCreate.id,idempotencyKey:reportId});}
  downloadReport(projectId:string,reportId:string){return this.api.getBlob(`api/v1/projects/${encodeURIComponent(projectId)}/reports/${encodeURIComponent(reportId)}/download`,{contractId:CUSTOMER_CONTRACTS.reportDownload.id});}
  listArchitects():Observable<readonly ArchitectDirectoryEntry[]>{return this.api.get('api/v1/architects',architectsSchema,{contractId:CUSTOMER_CONTRACTS.architectsList.id}).pipe(map(x=>x.architects));}
  listArchitectRequests(projectId:string):Observable<readonly ArchitectRequest[]>{return this.api.get(`api/v1/projects/${encodeURIComponent(projectId)}/architect-requests`,architectRequestsSchema,{contractId:CUSTOMER_CONTRACTS.architectRequestsList.id}).pipe(map(x=>x.requests));}
  proposeAiEdit(projectId:string,planId:string,expectedBaseRevisionId:string,instruction:string):Observable<AiEditProposal>{return this.api.post(`api/v1/projects/${encodeURIComponent(projectId)}/plans/${encodeURIComponent(planId)}/ai-edit/proposals`,{expectedBaseRevisionId,instruction},aiEditProposalSchema,{contractId:CUSTOMER_CONTRACTS.aiEditProposal.id,timeoutMs:60_000});}
}
