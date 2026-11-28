import { declareContract } from '../../core/api/contract-status';

const EVIDENCE =
  'Captain docker-verified buildmind-api.d.ts and ProjectResponse.cs at backend HEAD 25309cd82417fb1e821a09f4e9fbc6084dfc0b2f';
const verified = { owner: 'A' as const, status: 'VERIFIED' as const, evidence: EVIDENCE };

export const CUSTOMER_CONTRACTS = {
  projectsList: declareContract({
    id: 'customer.projects.list',
    ...verified,
    method: 'GET',
    path: '/api/v1/projects',
  }),
  projectsCreate: declareContract({
    id: 'customer.projects.create',
    ...verified,
    method: 'POST',
    path: '/api/v1/projects',
  }),
  projectDetail: declareContract({
    id: 'customer.projects.detail',
    ...verified,
    method: 'GET',
    path: '/api/v1/projects/{id}',
  }),
  projectUpdate: declareContract({
    id: 'customer.projects.update',
    ...verified,
    method: 'PATCH',
    path: '/api/v1/projects/{id}',
  }),
  projectLifecycle: declareContract({
    id: 'customer.projects.lifecycle',
    ...verified,
    method: 'GET',
    path: '/api/v1/projects/{projectId}/lifecycle',
  }),
  notificationsUnread: declareContract({
    id: 'customer.notifications.unread',
    ...verified,
    method: 'GET',
    path: '/api/v1/notifications/unread-count',
  }),
  profileGet: declareContract({
    id: 'customer.profile.get',
    ...verified,
    method: 'GET',
    path: '/api/v1/users/me',
  }),
  profileUpdate: declareContract({
    id: 'customer.profile.update',
    ...verified,
    method: 'PUT',
    path: '/api/v1/users/me',
  }),
  securityGet: declareContract({
    id: 'customer.security.get',
    ...verified,
    method: 'GET',
    path: '/api/v1/users/me/security',
  }),
  passwordChange: declareContract({
    id: 'customer.password.change',
    ...verified,
    method: 'POST',
    path: '/api/v1/users/me/password',
  }),
  sessionsList: declareContract({
    id: 'customer.sessions.list',
    ...verified,
    method: 'GET',
    path: '/api/v1/users/me/sessions',
  }),
  sessionRevoke: declareContract({
    id: 'customer.sessions.revoke',
    ...verified,
    method: 'DELETE',
    path: '/api/v1/users/me/sessions/{sessionId}',
  }),
  securityEvents: declareContract({
    id: 'customer.security.events',
    ...verified,
    method: 'GET',
    path: '/api/v1/users/me/security-events',
  }),
  surveysList: declareContract({
    id: 'customer.surveys.list',
    ...verified,
    method: 'GET',
    path: '/api/v1/projects/{projectId}/surveys',
  }),
  surveyUpload: declareContract({
    id: 'customer.surveys.upload',
    ...verified,
    method: 'POST',
    path: '/api/v1/projects/{id}/surveys',
  }),
  surveyStatus: declareContract({
    id: 'customer.surveys.status',
    ...verified,
    method: 'GET',
    path: '/api/v1/projects/{id}/surveys/{surveyId}',
  }),
  surveyExtraction: declareContract({
    id: 'customer.surveys.extraction',
    ...verified,
    method: 'GET',
    path: '/api/v1/projects/{projectId}/surveys/{surveyId}/extraction',
  }),
  surveyFile: declareContract({
    id: 'customer.surveys.file', ...verified, method: 'GET',
    path: '/api/v1/projects/{projectId}/surveys/{surveyId}/file',
  }),
  surveyReviewCreate: declareContract({
    id: 'customer.surveys.reviews.create', ...verified, method: 'POST',
    path: '/api/v1/projects/{projectId}/surveys/{surveyId}/extraction/reviews',
  }),
  landGet: declareContract({
    id: 'customer.land.get', ...verified, method: 'GET', path: '/api/v1/projects/{projectId}/land',
  }),
  landEdges: declareContract({
    id: 'customer.land.edges', ...verified, method: 'GET', path: '/api/v1/projects/{projectId}/land/edges',
  }),
  landFrame: declareContract({
    id: 'customer.land.frame', ...verified, method: 'GET', path: '/api/v1/projects/{projectId}/land/frame',
  }),
  landValidate: declareContract({
    id: 'customer.land.validate', ...verified, method: 'POST', path: '/api/v1/projects/{projectId}/land/validate',
  }),
  landConfirm: declareContract({
    id: 'customer.land.confirm', ...verified, method: 'POST', path: '/api/v1/projects/{projectId}/land/confirm',
  }),
  requirementsGet: declareContract({ id: 'customer.requirements.get', ...verified, method: 'GET', path: '/api/v1/projects/{id}/requirements' }),
  requirementsSave: declareContract({ id: 'customer.requirements.save', ...verified, method: 'PUT', path: '/api/v1/projects/{id}/requirements' }),
  generationCreate: declareContract({ id: 'customer.generation.create', ...verified, method: 'POST', path: '/api/v1/projects/{projectId}/generation-jobs' }),
  generationGet: declareContract({ id: 'customer.generation.get', ...verified, method: 'GET', path: '/api/v1/projects/{projectId}/generation-jobs/{jobId}' }),
  generationSelect: declareContract({ id: 'customer.generation.select', ...verified, method: 'POST', path: '/api/v1/projects/{projectId}/generation-jobs/{jobId}/select' }),
  planGet: declareContract({ id: 'customer.plans.get', ...verified, method: 'GET', path: '/api/v1/projects/{projectId}/plans/{planId}' }),
  editCandidateCreate: declareContract({ id:'customer.plans.candidates.create',...verified,method:'POST',path:'/api/v1/projects/{projectId}/plans/{planId}/candidates' }),
  editCandidateApply: declareContract({ id:'customer.plans.candidates.apply',...verified,method:'POST',path:'/api/v1/projects/{projectId}/plans/{planId}/candidates/{candidateId}/apply' }),
  editCandidateReject: declareContract({ id:'customer.plans.candidates.reject',...verified,method:'POST',path:'/api/v1/projects/{projectId}/plans/{planId}/candidates/{candidateId}/reject' }),
  planVersions: declareContract({id:'customer.plans.versions',...verified,method:'GET',path:'/api/v1/projects/{projectId}/plans/{planId}/versions'}),
  planRevert: declareContract({id:'customer.plans.revert',...verified,method:'POST',path:'/api/v1/projects/{projectId}/plans/{planId}/revert'}),
  planConfirm: declareContract({id:'customer.plans.confirm',...verified,method:'POST',path:'/api/v1/projects/{projectId}/plans/{planId}/confirm'}),
  reportsList: declareContract({id:'customer.reports.list',...verified,method:'GET',path:'/api/v1/projects/{projectId}/reports'}),
  reportsCreate: declareContract({id:'customer.reports.create',...verified,method:'POST',path:'/api/v1/projects/{projectId}/reports'}),
  reportDownload: declareContract({id:'customer.reports.download',...verified,method:'GET',path:'/api/v1/projects/{projectId}/reports/{reportId}/download'}),
  architectsList: declareContract({id:'customer.architects.list',...verified,method:'GET',path:'/api/v1/architects'}),
  architectRequestsList: declareContract({id:'customer.architectRequests.list',...verified,method:'GET',path:'/api/v1/projects/{projectId}/architect-requests'}),
  aiEditProposal: declareContract({id:'customer.plans.aiEditProposal',...verified,method:'POST',path:'/api/v1/projects/{projectId}/plans/{planId}/ai-edit/proposals'}),
} as const;
