import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AppError } from '../../../core/api/app-error';
import { Button } from '../../../shared/ui/button/button';
import { FormAlert } from '../../../shared/ui/form-alert/form-alert';
import { ErrorState } from '../../../shared/ui/states/error-state';
import { Skeleton } from '../../../shared/ui/skeleton/skeleton';
import { CustomerApi, RequirementsSnapshot } from '../customer-api';

@Component({
  selector: 'bm-requirements-page', imports: [FormsModule, RouterLink, Button, FormAlert, ErrorState, Skeleton], changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a [routerLink]="['/app/projects', id()]" class="text-sm">Back to project</a>
    <header class="mt-5 border-b border-border pb-6"><h1 class="text-3xl text-text">Home requirements</h1><p class="mt-2 max-w-3xl text-muted">Review the room programme stored for this project. Only catalogue-backed room types already returned by BuildMind are editable here.</p></header>
    @if (loading()) { <div class="mt-8"><bm-skeleton height="24rem" /></div> }
    @else if (error(); as failure) { <div class="mt-8"><bm-error-state [error]="failure" (retry)="load()" /></div> }
    @else if (snapshot(); as data) {
      <div class="mt-8 grid gap-10 lg:grid-cols-[minmax(0,1fr)_18rem]">
        <section><h2 class="text-xl text-text">Ground-floor rooms</h2>
          <div class="mt-5 divide-y divide-border border-y border-border">@for (room of rooms(); track room.roomType; let i = $index) { <label class="flex items-center justify-between gap-5 py-4"><span class="text-text">{{ room.roomType }}</span><input class="h-11 w-24 rounded-md border border-border bg-surface px-3 text-right tabular-nums text-text" type="number" min="0" max="20" [ngModel]="room.count" (ngModelChange)="setCount(i, $event)" /></label> }</div>
          @if (rooms().length === 0) { <p class="mt-5 text-sm text-muted">No catalogue-backed room requirements are present in this revision.</p> }
          @if (saveError(); as message) { <bm-form-alert class="mt-5" tone="error" heading="Requirements not saved" [message]="message" /> }
          @if (saved()) { <bm-form-alert class="mt-5" tone="success" message="Requirements saved as a new revision." /> }
          <button class="mt-6" bmButton type="button" [disabled]="rooms().length === 0" [loading]="saving()" (click)="save(data)">Save requirements</button>
        </section>
        <aside class="border-l border-border pl-6"><h2 class="text-xl text-text">Revision summary</h2><dl class="mt-5 divide-y divide-border text-sm"><div class="pb-3"><dt class="text-muted">Floors</dt><dd class="mt-1 text-text">{{ data.floorCount }}</dd></div><div class="py-3"><dt class="text-muted">Catalogue</dt><dd class="mt-1 text-text">{{ data.catalogueVersion }}</dd></div><div class="py-3"><dt class="text-muted">Revision</dt><dd class="mt-1 text-text">{{ data.revisionNumber }}</dd></div><div class="py-3"><dt class="text-muted">Relationships</dt><dd class="mt-1 text-text">{{ data.relationships.length }}</dd></div></dl><p class="mt-6 text-sm text-muted">Additional-space and relationship records are preserved unchanged because their generated contract does not expose typed editable fields.</p></aside>
      </div>
    }
  `,
})
export class RequirementsPage {
  readonly id=input.required<string>(); private readonly api=inject(CustomerApi); private readonly destroyRef=inject(DestroyRef);
  protected readonly snapshot=signal<RequirementsSnapshot|null>(null); protected readonly rooms=signal<{count:number;roomType:string}[]>([]); protected readonly loading=signal(true); protected readonly error=signal<AppError|null>(null); protected readonly saving=signal(false); protected readonly saveError=signal<string|null>(null); protected readonly saved=signal(false);
  constructor(){queueMicrotask(()=>this.load());}
  protected load():void{this.loading.set(true);this.error.set(null);this.api.getRequirements(this.id()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:data=>{this.snapshot.set(data);this.rooms.set(data.groundFloorRooms.map(room=>({...room})));this.loading.set(false);},error:(error:AppError)=>{this.error.set(error);this.loading.set(false);}});}
  protected setCount(index:number,count:number):void{this.rooms.update(items=>items.map((item,i)=>i===index?{...item,count:Math.max(0,Math.min(20,Number(count)||0))}:item));}
  protected save(data:RequirementsSnapshot):void{this.saving.set(true);this.saved.set(false);this.saveError.set(null);this.api.saveRequirements(this.id(),{expectedRevisionId:data.revisionId,catalogueVersion:data.catalogueVersion,floorCount:data.floorCount,groundFloorRooms:this.rooms(),additionalSpaces:data.additionalSpaces,relationships:data.relationships.filter((value):value is {kind:string;sourceRoomType:string;targetRoomType:string}=>typeof value==='object'&&value!==null&&'kind'in value&&'sourceRoomType'in value&&'targetRoomType'in value),designMethod:typeof data.designMethod==='string'?data.designMethod:null}).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({next:value=>{this.snapshot.set(value);this.rooms.set(value.groundFloorRooms.map(room=>({...room})));this.saving.set(false);this.saved.set(true);},error:(error:AppError)=>{this.saving.set(false);this.saveError.set(error.message);}});}
}
