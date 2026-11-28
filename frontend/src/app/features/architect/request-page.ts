import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { Button } from '../../shared/ui/button/button';
import { ArchitectApi, ArchitectRequest } from './architect-api';

@Component({
  selector: 'bm-architect-request', imports: [RouterLink, Button], changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a routerLink="/architect/requests" class="text-sm">Back to requests</a>
    @if (request(); as r) {
      <header class="mt-5 border-b border-border pb-6"><h1 class="text-3xl text-text">Professional review</h1><p class="mt-2 text-muted">Project {{r.projectId}} · {{r.requestStatus}}</p></header>
      <div class="mt-8 grid gap-10 lg:grid-cols-[2fr_1fr]">
        <section>
          <h2 class="text-xl text-text">Consented preliminary report</h2>
          <p class="mt-2 text-sm text-muted">This report is preliminary planning material, not professional approval. Access is consent-bound and audited.</p>
          <button bmButton variant="secondary" class="mt-5" type="button" [loading]="busy()" (click)="download()">Download exact shared report</button>
          @if (!r.decision) {
            <div class="mt-10 border-t border-border pt-6"><h2 class="text-xl text-text">Respond once</h2><textarea aria-label="Response note" maxlength="4000" class="mt-4 min-h-28 w-full rounded-md border border-border bg-surface p-3 text-text" [value]="note()" (input)="note.set($any($event.target).value)"></textarea><div class="mt-4 flex gap-3"><button bmButton type="button" [loading]="busy()" (click)="respond('ACCEPTED')">Accept</button><button bmButton variant="secondary" type="button" [loading]="busy()" (click)="respond('DECLINED')">Decline</button></div></div>
          } @else if (r.decision === 'ACCEPTED') {
            <div class="mt-10 border-t border-border pt-6"><h2 class="text-xl text-text">Professional feedback</h2><select aria-label="Feedback kind" class="mt-4 h-11 border border-border bg-surface px-3 text-text" [value]="kind()" (change)="kind.set($any($event.target).value)"><option value="FEEDBACK">Feedback</option><option value="REVISION_REQUEST">Revision request</option></select><textarea aria-label="Feedback" maxlength="4000" class="mt-3 min-h-32 w-full rounded-md border border-border bg-surface p-3 text-text" [value]="body()" (input)="body.set($any($event.target).value)"></textarea><div class="mt-4 flex gap-3"><button bmButton type="button" [disabled]="!body().trim()" [loading]="busy()" (click)="sendFeedback()">Send feedback</button><button bmButton variant="secondary" type="button" [loading]="busy()" (click)="complete()">Complete review</button></div></div>
          }
        </section>
        <aside class="border-l border-border pl-6"><h2 class="text-sm font-semibold uppercase tracking-[.16em] text-accent">Audit state</h2><dl class="mt-5 space-y-4 text-sm"><div><dt class="text-muted">Requested</dt><dd class="text-text">{{r.requestedAtUtc}}</dd></div><div><dt class="text-muted">Decision</dt><dd class="text-text">{{r.decision||'Awaiting response'}}</dd></div></dl></aside>
      </div>
      @if (message()) { <p role="status" class="mt-6 text-sm text-accent">{{message()}}</p> }
    } @else { <p class="mt-8 text-muted">Loading consent record…</p> }
  `,
})
export class ArchitectRequestPage {
  readonly requestId=input.required<string>(); private readonly api=inject(ArchitectApi); private readonly destroyRef=inject(DestroyRef);
  protected readonly request=signal<ArchitectRequest|null>(null); protected readonly note=signal(''); protected readonly body=signal(''); protected readonly kind=signal<'FEEDBACK'|'REVISION_REQUEST'>('FEEDBACK'); protected readonly busy=signal(false); protected readonly message=signal('');
  constructor(){queueMicrotask(()=>this.load());}
  private load(){this.api.request(this.requestId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(v=>this.request.set(v));}
  protected respond(decision:'ACCEPTED'|'DECLINED'){this.busy.set(true);this.api.respond(this.requestId(),decision,this.note().trim()||null).pipe(finalize(()=>this.busy.set(false)),takeUntilDestroyed(this.destroyRef)).subscribe(v=>{this.request.set(v);this.message.set('Response recorded.');});}
  protected sendFeedback(){this.busy.set(true);this.api.feedback(this.requestId(),this.kind(),this.body().trim()).pipe(finalize(()=>this.busy.set(false)),takeUntilDestroyed(this.destroyRef)).subscribe(()=>{this.body.set('');this.message.set('Feedback sent to the customer.');});}
  protected complete(){this.busy.set(true);this.api.complete(this.requestId()).pipe(finalize(()=>this.busy.set(false)),takeUntilDestroyed(this.destroyRef)).subscribe(v=>{this.request.set(v);this.message.set('Review completed; consent-bound access has ended.');});}
  protected download(){this.busy.set(true);this.api.report(this.requestId()).pipe(finalize(()=>this.busy.set(false)),takeUntilDestroyed(this.destroyRef)).subscribe(blob=>{const url=URL.createObjectURL(blob);const link=document.createElement('a');link.href=url;link.download=`buildmind-review-${this.requestId()}.pdf`;link.click();URL.revokeObjectURL(url);});}
}
