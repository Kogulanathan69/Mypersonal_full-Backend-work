import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { take } from 'rxjs';
import { AUTH_API } from '../../core/auth/auth-api';
import { BrandLogo } from '../../layouts/parts/brand-logo';
import { SkipLink } from '../../layouts/parts/skip-link';
import { Button } from '../../shared/ui/button/button';

@Component({selector:'bm-architect-shell',imports:[RouterOutlet,RouterLink,RouterLinkActive,BrandLogo,SkipLink,Button],changeDetection:ChangeDetectionStrategy.OnPush,template:`
<bm-skip-link/><header class="sticky top-0 z-30 border-b border-border bg-bg-deep/95"><div class="mx-auto flex h-16 max-w-7xl items-center gap-8 px-4 sm:px-6"><bm-brand-logo link="/architect" [showTagline]="false"/><span class="border-l border-border pl-4 text-xs font-semibold uppercase tracking-[.18em] text-accent">Architect portal</span><nav aria-label="Architect" class="ml-auto flex items-center gap-2"><a routerLink="/architect" routerLinkActive="text-text" [routerLinkActiveOptions]="{exact:true}" class="px-3 py-2 text-sm text-muted">Dashboard</a><a routerLink="/architect/requests" routerLinkActive="text-text" class="px-3 py-2 text-sm text-muted">Requests</a><button bmButton variant="ghost" size="sm" type="button" (click)="signOut()">Sign out</button></nav></div></header><main id="main-content" tabindex="-1" class="mx-auto w-full max-w-7xl flex-1 px-4 py-8 sm:px-6"><router-outlet/></main>`})
export class ArchitectShell{private readonly auth=inject(AUTH_API);private readonly router=inject(Router);protected signOut(){this.auth.signOut().pipe(take(1)).subscribe({next:()=>void this.router.navigate(['/auth/sign-in']),error:()=>void this.router.navigate(['/auth/sign-in'])});}}
