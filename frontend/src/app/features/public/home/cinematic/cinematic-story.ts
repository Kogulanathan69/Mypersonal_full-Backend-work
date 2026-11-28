import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  inject,
  signal,
  viewChild,
  viewChildren,
} from '@angular/core';
import { DOCUMENT, NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { BUILD_ENV } from '../../../../core/config/build-env.token';
import { Icon } from '../../../../shared/ui/icon/icon';
import { Spinner } from '../../../../shared/ui/spinner/spinner';
import type { CinematicEngine } from './engine/cinematic-engine';
import {
  Capability,
  STATIC_REASON_TEXT,
  StaticReason,
  decideCapability,
  detectWebgl,
  readPreference,
  writePreference,
} from './capability';
import { StaticHouseIllustration } from './static-house-illustration';
import { CINEMATIC_SCENES, sceneAt, sceneLocalProgress } from './eight-scene-timeline';

/**
 * Hero + scroll-driven hybrid story (scenes 01–08). Owner: A — Customer Portal.
 * WebGL mode: a tall section with a sticky full-screen canvas; scroll progress drives the scene.
 * Static mode: the same hero and five chapters as still SVG illustrations.
 * Three.js and GSAP are imported only after the browser is known to support WebGL.
 */
@Component({
  selector: 'bm-cinematic-story',
  imports: [RouterLink, NgTemplateOutlet, Icon, Spinner, StaticHouseIllustration],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <ng-template #heroContent>
      <p class="text-xs font-medium tracking-[0.28em] text-mk-ink-muted uppercase">
        Land &nbsp;·&nbsp; Planning &nbsp;·&nbsp; Regulation &nbsp;·&nbsp; Design
      </p>
      <h1
        class="mt-5 max-w-none font-mk-display text-[clamp(3.1rem,4.65vw,4.85rem)] leading-[0.9] font-normal tracking-[-0.035em] text-mk-ink"
      >
        <span class="block">Plan your home </span>
        <em class="mt-2 block font-normal text-mk-ink">with confidence.</em>
      </h1>
      <p class="mt-7 max-w-md text-sm leading-6 text-mk-ink-muted sm:text-base">
        Turn your survey into preliminary layouts, review the planning evidence and discuss the next
        step with a qualified professional.
      </p>
      <div class="mt-8 flex flex-wrap gap-3 text-sm">
        <a
          routerLink="/"
          fragment="planning-experience"
          class="inline-flex min-h-11 items-center gap-2 rounded-pill bg-mk-gold px-6 font-semibold text-mk-ink no-underline transition-colors hover:bg-mk-ink hover:text-mk-paper focus-visible:outline-offset-4"
        >
          Explore the planning experience <bm-icon name="arrow-right" [size]="18" />
        </a>
        <a
          routerLink="/"
          fragment="how-it-works"
          class="inline-flex min-h-11 items-center rounded-pill border border-mk-ink/45 px-6 font-medium text-mk-ink no-underline transition-colors hover:border-mk-gold focus-visible:outline-offset-4"
        >
          See how BuildMind works
        </a>
      </div>
      <ul class="mt-9 flex gap-7 text-xs text-mk-ink-muted" aria-label="Planning features">
        <li class="flex items-center gap-2">
          <span class="mk-architectural-icon"><bm-icon name="map-pin" [size]="16" /></span>
          Your land
        </li>
        <li class="flex items-center gap-2">
          <span class="mk-architectural-icon"><bm-icon name="layers" [size]="16" /></span>
          Plan options
        </li>
        <li class="hidden items-center gap-2 sm:flex">
          <span class="mk-architectural-icon"><bm-icon name="shield" [size]="16" /></span>
          Evidence
        </li>
      </ul>
    </ng-template>

    @if (mode() === 'webgl') {
      <section
        #story
        aria-label="The BuildMind story"
        class="mk-cinematic-warm relative h-[760vh] bg-mk-paper"
      >
        <div
          class="mk-cinematic-stage sticky top-16 isolate h-[calc(100dvh-4rem)] w-full overflow-hidden bg-mk-paper lg:top-18 lg:h-[calc(100dvh-4.5rem)]"
        >
          <canvas
            #canvas
            class="absolute inset-0 z-0 h-full w-full transition-opacity duration-700"
            [class.opacity-0]="active() > 3"
            aria-hidden="true"
          ></canvas>

          @if (active() === 4) {
            <picture
              class="mk-cinematic-media absolute inset-0 left-[32%] z-0 block overflow-hidden"
              aria-hidden="true"
            >
              <img
                src="assets/cinematic/residence-exterior-desktop.png"
                alt=""
                class="size-full object-cover"
                decoding="async"
              />
            </picture>
          }
          @if (active() === 5) {
            <picture
              class="mk-cinematic-media absolute inset-0 left-[32%] z-0 block overflow-hidden"
              aria-hidden="true"
            >
              <img
                src="assets/cinematic/residence-interior-journey.png"
                alt=""
                class="size-full object-cover"
                decoding="async"
              />
            </picture>
          }
          @if (active() >= 6) {
            <div
              class="absolute inset-0 left-[38%] z-0 bg-mk-paper/86 transition-opacity duration-700"
              aria-hidden="true"
            ></div>
          }

          @if (loading()) {
            <div
              class="absolute inset-0 left-[42%] z-0 flex items-center justify-center overflow-hidden"
            >
              <bm-static-house-illustration
                [stage]="0"
                class="w-[min(48vw,48rem)] opacity-40"
                label=""
                aria-hidden="true"
              />
              <p
                class="absolute bottom-10 flex items-center gap-3 text-sm text-mk-ink-muted"
                role="status"
              >
                <bm-spinner [size]="18" /> Preparing the 3D scene…
              </p>
            </div>
          }

          <div
            class="pointer-events-none absolute inset-0 z-[1] bg-linear-to-r from-mk-paper via-mk-paper/40 to-transparent transition-opacity duration-700"
            [class.opacity-0]="active() >= 6"
            aria-hidden="true"
          ></div>
          <div
            class="pointer-events-none absolute right-0 bottom-0 left-[45%] z-[1] h-40 bg-linear-to-t from-mk-paper/75 to-transparent transition-opacity duration-700"
            [class.opacity-0]="active() >= 6"
            aria-hidden="true"
          ></div>

          <div #hero class="absolute inset-y-0 left-0 z-20 flex w-[47%] items-center">
            <div class="w-full px-6 lg:px-12 xl:pl-[max(3rem,calc((100vw-88rem)/2))]">
              <ng-container *ngTemplateOutlet="heroContent" />
              <button
                type="button"
                class="mt-10 flex items-center gap-3 text-xs text-mk-ink-muted hover:text-mk-ink"
                (click)="goToChapter(0)"
              >
                <span
                  class="flex h-8 w-5 justify-center rounded-pill border border-mk-ink/30 pt-1.5"
                  aria-hidden="true"
                >
                  <span
                    class="h-2 w-1 animate-bounce rounded-pill bg-mk-gold motion-reduce:animate-none"
                  ></span>
                </span>
                Scroll to explore the story
              </button>
            </div>
          </div>

          <ol class="absolute top-10 right-20 left-[58%] z-30" aria-label="Story chapters">
            @for (scene of scenes; track scene.id; let i = $index) {
              <li
                #caption
                class="absolute top-0 right-0 max-w-sm border-r-2 border-mk-gold bg-mk-paper/82 py-3 pr-4 pl-5 text-right opacity-0 backdrop-blur-sm"
                [attr.aria-current]="i === active() ? 'step' : null"
              >
                <p class="font-mono text-sm" class="text-mk-ink-muted">{{ scene.number }} / 08</p>
                <h2
                  class="mt-2 text-3xl font-semibold sm:text-4xl"
                  class="font-mk-display text-mk-ink"
                >
                  {{ scene.title }}
                </h2>
                <p class="mt-2 text-sm text-mk-ink-muted">
                  {{ scene.text }}
                </p>
              </li>
            }
          </ol>

          <nav
            aria-label="Jump to story chapter"
            class="absolute top-1/2 right-6 z-30 hidden -translate-y-1/2 flex-col gap-3 sm:flex"
          >
            @for (scene of scenes; track scene.id; let i = $index) {
              <button
                type="button"
                class="group flex items-center justify-end gap-3"
                [attr.aria-label]="'Chapter ' + scene.number + ': ' + scene.title"
                [attr.aria-current]="i === active() ? 'step' : null"
                (click)="goToChapter(i)"
              >
                <span
                  class="text-xs text-mk-ink opacity-0 transition-opacity group-hover:opacity-100 group-focus-visible:opacity-100"
                  >{{ scene.title }}</span
                >
                <span
                  class="block size-2.5 rounded-pill border"
                  [class]="
                    i === active() ? 'border-mk-gold bg-mk-gold' : 'border-mk-ink/45 bg-mk-paper'
                  "
                ></span>
              </button>
            }
          </nav>

          <div
            class="absolute right-8 bottom-6 z-30 flex items-center gap-3 text-xs text-mk-ink-muted"
          >
            <span class="hidden md:inline"
              >Illustrative model — not generated from a customer plan.</span
            >
            <button
              type="button"
              class="rounded-pill border border-mk-ink/25 bg-mk-paper/75 px-3 py-1.5 text-mk-ink backdrop-blur hover:border-mk-gold"
              (click)="setAnimation(false)"
            >
              Turn animation off
            </button>
          </div>
        </div>
      </section>
    } @else {
      <section aria-label="The BuildMind story" class="mk-cinematic-warm relative bg-mk-paper">
        <div
          class="mx-auto grid min-h-[calc(100dvh-4rem)] max-w-7xl items-center gap-10 px-5 py-12 sm:px-8 lg:grid-cols-[0.85fr_1.15fr]"
        >
          <div><ng-container *ngTemplateOutlet="heroContent" /></div>
          <div class="relative overflow-hidden">
            <bm-static-house-illustration
              [stage]="2"
              label="Technical wireframe of the illustrative residence"
            />
          </div>
        </div>
        <div class="mx-auto max-w-7xl px-5 pb-16 sm:px-8">
          <p class="flex flex-wrap items-center gap-3 text-sm text-mk-ink-muted" role="status">
            {{ reasonText() }}
            @if (
              capability()?.webglAvailable && !capability()?.mobile && reason() !== 'context-lost'
            ) {
              <button
                type="button"
                class="rounded-pill border border-mk-ink/30 px-3 py-1 text-mk-ink hover:border-mk-gold"
                (click)="setAnimation(true)"
              >
                Turn 3D animation on
              </button>
            }
          </p>
          <ol class="mt-8 grid gap-6 md:grid-cols-2 lg:grid-cols-3" aria-label="Story chapters">
            @for (scene of scenes; track scene.id; let i = $index) {
              <li class="border-t border-mk-paper-line py-6">
                <bm-static-house-illustration
                  [stage]="illustrationStage(i)"
                  [label]="'Illustration of chapter ' + scene.number + ': ' + scene.title"
                />
                <p class="mt-4 font-mono text-sm text-mk-cyan">{{ scene.number }} / 08</p>
                <h2 class="mt-1 text-2xl font-semibold text-mk-ink">{{ scene.title }}</h2>
                <p class="mt-2 text-sm text-mk-ink-muted">{{ scene.text }}</p>
              </li>
            }
          </ol>
          <p class="mt-6 text-xs text-mk-ink-muted">
            Illustrative model — not generated from a customer plan.
          </p>
        </div>
      </section>
    }
  `,
})
export class CinematicStory {
  private readonly document = inject(DOCUMENT);
  private readonly env = inject(BUILD_ENV);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly scenes = CINEMATIC_SCENES;
  protected readonly mode = signal<'webgl' | 'static' | 'pending'>('pending');
  protected readonly loading = signal(true);
  protected readonly active = signal(0);
  protected readonly capability = signal<Capability | null>(null);
  protected readonly reason = signal<StaticReason | null>(null);
  protected readonly reasonText = () => STATIC_REASON_TEXT[this.reason() ?? 'user-off'];

  private readonly story = viewChild<ElementRef<HTMLElement>>('story');
  private readonly canvas = viewChild<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly captions = viewChildren<ElementRef<HTMLElement>>('caption');

  private engine: CinematicEngine | null = null;
  private cleanups: (() => void)[] = [];

  constructor() {
    afterNextRender(() => this.init());
    this.destroyRef.onDestroy(() => this.teardown());
  }

  protected setAnimation(on: boolean): void {
    writePreference(this.document.defaultView?.localStorage ?? null, on ? 'on' : 'off');
    this.teardown();
    this.init();
  }

  protected goToChapter(index: number): void {
    const win = this.document.defaultView;
    const section = this.story()?.nativeElement;
    const scene = CINEMATIC_SCENES[index];
    if (!win || !section || !scene) return;
    const top = section.getBoundingClientRect().top + win.scrollY;
    const travel = section.offsetHeight - win.innerHeight;
    const target =
      index === 0
        ? Math.max(scene.start + 0.08, 0.08)
        : scene.start + (scene.end - scene.start) * 0.45;
    const reduce = win.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
    win.scrollTo({ top: top + travel * target, behavior: reduce ? 'auto' : 'smooth' });
  }

  protected illustrationStage(index: number): number {
    return Math.min(index, 3);
  }

  private init(): void {
    const win = this.document.defaultView;
    if (!win) return;
    const cap = decideCapability({
      reducedMotion: win.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false,
      webgl: detectWebgl(this.document),
      nav: win.navigator as unknown as Parameters<typeof decideCapability>[0]['nav'],
      viewportWidth: win.innerWidth,
      preference: readPreference(win.localStorage),
    });
    this.capability.set(cap);
    if (cap.mode === 'static') {
      this.useStatic(cap.reason ?? 'user-off');
      return;
    }
    this.loading.set(true);
    this.mode.set('webgl');
    // Wait one frame so the WebGL template (canvas, captions) exists.
    win.requestAnimationFrame(() => void this.startWebgl(cap));
  }

  private async startWebgl(cap: Capability): Promise<void> {
    const canvas = this.canvas()?.nativeElement;
    const section = this.story()?.nativeElement;
    if (!canvas || !section) return;
    try {
      const mod = await import('./engine/cinematic-engine');
      if (this.mode() !== 'webgl') return;
      const engine = await mod.createCinematicEngine(canvas, {
        mobile: cap.mobile,
        lowPower: cap.lowPower,
        debug: !this.env.production,
        onContextLost: () => this.useStatic('context-lost'),
      });
      this.engine = engine;
      engine.resize(canvas.clientWidth, canvas.clientHeight);

      const resize = new ResizeObserver(() =>
        engine.resize(canvas.clientWidth, canvas.clientHeight),
      );
      resize.observe(canvas);
      const visibility = new IntersectionObserver(([entry]) =>
        entry?.isIntersecting ? engine.start() : engine.stop(),
      );
      visibility.observe(section);
      const onHidden = (): void => (this.document.hidden ? engine.stop() : engine.start());
      this.document.addEventListener('visibilitychange', onHidden);
      this.cleanups.push(
        () => resize.disconnect(),
        () => visibility.disconnect(),
        () => this.document.removeEventListener('visibilitychange', onHidden),
      );

      const unbind = await mod.bindScrollProgress(section, (p) => this.onProgress(p));
      this.cleanups.push(unbind);
      this.onProgress(0);
      this.loading.set(false);
    } catch (error) {
      console.error(
        '[BuildMind] 3D scene unavailable:',
        error instanceof Error ? error.message : 'unknown error',
      );
      this.useStatic('error');
    }
  }

  private onProgress(p: number): void {
    this.engine?.setProgress(p <= 0.57 ? (p / 0.57) * 0.82 : 1);
    this.captions().forEach((c, i) => {
      const scene = CINEMATIC_SCENES[i];
      if (!scene) return;
      const local = sceneLocalProgress(scene, p);
      const enter = Math.min(1, local / 0.2);
      const leave = i === CINEMATIC_SCENES.length - 1 ? 1 : Math.min(1, (1 - local) / 0.18);
      const o = p < 0.025 && i === 0 ? 0 : enter * leave;
      c.nativeElement.style.opacity = String(o);
      c.nativeElement.style.transform = `translateY(${(1 - o) * 16}px)`;
    });
    const index = CINEMATIC_SCENES.indexOf(sceneAt(p));
    if (index !== this.active()) this.active.set(index);
  }

  private useStatic(reason: StaticReason): void {
    this.teardown();
    this.reason.set(reason);
    this.loading.set(false);
    this.mode.set('static');
  }

  private teardown(): void {
    this.cleanups.forEach((fn) => fn());
    this.cleanups = [];
    this.engine?.dispose();
    this.engine = null;
  }
}
