import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import * as z from 'zod/mini';
import { ApiClient } from '../../core/api/api-client';

const profileSchema = z.object({ recipientId:z.string(), userId:z.string(), displayName:z.string(), registrationNumber:z.nullable(z.string()), verificationStatus:z.string(), verificationReference:z.nullable(z.string()), isListed:z.boolean(), acceptingRequests:z.boolean(), rowVersion:z.number() });
const requestSchema = z.object({ requestId:z.string(), projectId:z.string(), requestStatus:z.string(), reportId:z.nullable(z.string()), requestedAtUtc:z.string(), decision:z.nullable(z.string()), decisionNote:z.nullable(z.string()), respondedAtUtc:z.nullable(z.string()) });
const dashboardSchema = z.object({ profile:profileSchema, pending:z.number(), accepted:z.number() });
const requestsSchema = z.object({ requests:z.array(requestSchema) });
const feedbackSchema = z.object({ feedbackId:z.string(), requestId:z.string(), authorId:z.string(), kind:z.string(), body:z.string(), createdAtUtc:z.string() });

export type ArchitectDashboard = z.infer<typeof dashboardSchema>;
export type ArchitectRequest = z.infer<typeof requestSchema>;

@Injectable({ providedIn: 'root' })
export class ArchitectApi {
  private readonly api=inject(ApiClient);
  dashboard():Observable<ArchitectDashboard>{return this.api.get('api/v1/architect/dashboard',dashboardSchema,{contractId:'architect.dashboard'});}
  requests():Observable<readonly ArchitectRequest[]>{return this.api.get('api/v1/architect/requests',requestsSchema,{contractId:'architect.requests.list'}).pipe(map(v=>v.requests));}
  request(id:string):Observable<ArchitectRequest>{return this.api.get(`api/v1/architect/requests/${encodeURIComponent(id)}`,requestSchema,{contractId:'architect.requests.get'});}
  respond(id:string,decision:'ACCEPTED'|'DECLINED',note:string|null):Observable<ArchitectRequest>{return this.api.post(`api/v1/architect/requests/${encodeURIComponent(id)}/respond`,{decision,note},requestSchema,{contractId:'architect.requests.respond'});}
  feedback(id:string,kind:'FEEDBACK'|'REVISION_REQUEST',body:string){return this.api.post(`api/v1/architect/requests/${encodeURIComponent(id)}/feedback`,{kind,body},feedbackSchema,{contractId:'architect.requests.feedback'});}
  complete(id:string):Observable<ArchitectRequest>{return this.api.post(`api/v1/architect/requests/${encodeURIComponent(id)}/complete`,{},requestSchema,{contractId:'architect.requests.complete'});}
  report(id:string):Observable<Blob>{return this.api.getBlob(`api/v1/architect/requests/${encodeURIComponent(id)}/report`,{contractId:'architect.requests.report'});}
}
