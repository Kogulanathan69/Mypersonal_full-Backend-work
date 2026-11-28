import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { AppError } from '../../../core/api/app-error';
import { Button } from '../../../shared/ui/button/button';
import { FormAlert } from '../../../shared/ui/form-alert/form-alert';
import { ErrorState } from '../../../shared/ui/states/error-state';
import { Skeleton } from '../../../shared/ui/skeleton/skeleton';
import { CustomerApi, GenerationJob, LandGeometry, LayoutCandidate, RequirementsSnapshot } from '../customer-api';

@Component({
 selector:'bm-generation-page',imports:[RouterLink,Button,FormAlert,ErrorState,Skeleton],changeDetection:ChangeDetectionStrategy.OnPush,
 template:`
 <a [routerLink]="['/app/projects',id()]" class="text-sm">Back to project</a>
 <header class="mt-5 border-b border-border pb-6"><h1 class="text-3xl text-text">Generate planning layouts</h1><p class="mt-2 max-w-3xl text-muted">Generate alternatives from the current confirmed land and requirements revisions. Results remain preliminary planning layouts.</p></header>
 @if(loading()){<div class="mt-8"><bm-skeleton height="24rem"/></div>}@else if(error();as failure){<div class="mt-8"><bm-error-state [error]="failure" (retry)="load()"/></div>}@else{
 <section class="mt-8 grid gap-6 border-y border-border py-6 sm:grid-cols-2"><div><h2 class="text-lg text-text">Land prerequisite</h2><p class="mt-2 text-sm text-muted">Revision {{land()?.revisionNumber}} · {{land()?.areaM2}} m² · {{land()?.edgeCount}} edges</p></div><div><h2 class="text-lg text-text">Requirements prerequisite</h2><p class="mt-2 text-sm text-muted">Revision {{requirements()?.revisionNumber}} · {{roomTotal()}} requested rooms · {{requirements()?.floorCount}} floor(s)</p></div></section>
 @if(actionError();as message){<bm-form-alert class="mt-6" tone="error" heading="Generation unavailable" [message]="message"/>}
 @if(!job()){<button class="mt-6" bmButton type="button" [loading]="generating()" (click)="generate()">Generate layout alternatives</button>}
 @if(job();as result){<section class="mt-10"><div class="flex flex-wrap items-end justify-between gap-4"><div><h2 class="text-2xl text-text">Generated alternatives</h2><p class="mt-2 text-sm text-muted">{{result.status}} · {{result.outcome}} · {{result.notice}}</p></div><p class="text-sm text-muted">Algorithm {{result.algorithmVersion}} · seed {{result.seed}}</p></div>
 @if(result.candidates.length===0){<p class="mt-6 border-y border-border py-6 text-sm text-muted">No layout candidates were returned. @if(result.reasonCode){Reason: {{result.reasonCode}}}</p>}@else{
 <div class="mt-7 grid gap-8 xl:grid-cols-2">@for(candidate of result.candidates;track candidate.candidateId){<article class="border-t border-border pt-5"><div class="flex justify-between gap-4"><div><h3 class="text-xl text-text">Alternative {{candidate.ordinal}}</h3><p class="mt-1 text-sm text-muted">Rank {{candidate.rank}} · {{candidate.metrics.floorAreaM2}} m² floor area · {{candidate.metrics.roomCount}} rooms</p></div><span [class]="candidate.validation.hasBlockingIssues?'text-danger':'text-accent'">{{candidate.validation.hasBlockingIssues?'Review required':'Validated'}}</span></div>
 <div class="mt-4 overflow-auto border border-border bg-paper p-4"><svg viewBox="-5 -5 60 50" class="h-80 min-w-[30rem]" role="img" [attr.aria-label]="'Floor plan for alternative '+candidate.ordinal">@for(wall of firstFloor(candidate)?.walls??[];track wall.id){<line [attr.x1]="wall.start.x" [attr.y1]="wall.start.y" [attr.x2]="wall.end.x" [attr.y2]="wall.end.y" stroke="var(--color-ink)" [attr.stroke-width]="Math.max(.35,wall.thickness)"/>}@for(room of firstFloor(candidate)?.rooms??[];track room.id){<polygon [attr.points]="points(room.polygon)" fill="color-mix(in srgb, var(--color-accent) 12%, transparent)" stroke="var(--color-accent-strong)" stroke-width=".2"/><text [attr.x]="centre(room.polygon).x" [attr.y]="centre(room.polygon).y" text-anchor="middle" font-size="1.8" fill="var(--color-ink)">{{room.name}}</text>}</svg></div>
 @if(candidate.validation.diagnostics.length){<ul class="mt-3 space-y-1 text-sm text-muted">@for(diagnostic of candidate.validation.diagnostics;track diagnostic.code){<li>{{diagnostic.severity}} · {{diagnostic.message}}</li>}</ul>}
 <button class="mt-4" bmButton type="button" [disabled]="candidate.validation.hasBlockingIssues" [loading]="selecting()===candidate.candidateId" (click)="select(result,candidate)">Select this layout</button></article>}</div>}
 </section>}}
 `
})
export class GenerationPage{
 readonly id=input.required<string>();protected readonly Math=Math;private readonly api=inject(CustomerApi);private readonly destroyRef=inject(DestroyRef);private readonly router=inject(Router);
 protected readonly land=signal<LandGeometry|null>(null);protected readonly requirements=signal<RequirementsSnapshot|null>(null);protected readonly job=signal<GenerationJob|null>(null);protected readonly loading=signal(true);protected readonly error=signal<AppError|null>(null);protected readonly actionError=signal<string|null>(null);protected readonly generating=signal(false);protected readonly selecting=signal<string|null>(null);
 constructor(){queueMicrotask(()=>this.load());}
 protected load():void{this.loading.set(true);this.error.set(null);forkJoin({land:this.api.getLand(this.id()),requirements:this.api.getRequirements(this.id())}).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:value=>{this.land.set(value.land);this.requirements.set(value.requirements);this.loading.set(false);},error:(error:AppError)=>{this.error.set(error);this.loading.set(false);}});}
 protected roomTotal():number{return this.requirements()?.groundFloorRooms.reduce((n,r)=>n+r.count,0)??0;}
 protected generate():void{const land=this.land(),requirements=this.requirements();if(!land||!requirements)return;this.generating.set(true);this.actionError.set(null);const key=crypto.randomUUID();this.api.startGeneration(this.id(),land.landRevisionId,requirements.revisionId,Math.floor(Math.random()*2147483647),key).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:job=>{this.job.set(job);this.generating.set(false);},error:(error:AppError)=>{this.actionError.set(error.message);this.generating.set(false);}});}
 protected firstFloor(candidate:LayoutCandidate){return candidate.geometry.floors[0];}protected points(points:readonly{x:number;y:number}[]):string{return points.map(p=>`${p.x},${p.y}`).join(' ');}protected centre(points:readonly{x:number;y:number}[]){return points.length?{x:points.reduce((n,p)=>n+p.x,0)/points.length,y:points.reduce((n,p)=>n+p.y,0)/points.length}:{x:0,y:0};}
 protected select(job:GenerationJob,candidate:LayoutCandidate):void{this.selecting.set(candidate.candidateId);this.actionError.set(null);this.api.selectLayout(this.id(),job.jobId,candidate.candidateId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:plan=>void this.router.navigate(['/app/projects',this.id(),'plans',plan.planId]),error:(error:AppError)=>{this.actionError.set(error.message);this.selecting.set(null);}});}
}
