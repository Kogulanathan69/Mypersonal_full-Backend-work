import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Icon } from '../../../shared/ui/icon/icon';
import type { IconName } from '../../../shared/ui/icon/icon-paths';

interface Highlight {
  readonly icon: IconName;
  readonly title: string;
  readonly detail: string;
}

interface JourneyStage {
  readonly icon: IconName;
  readonly title: string;
}

@Component({
  selector: 'bm-static-home-hero',
  imports: [RouterLink, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block' },
  template: `
    <section class="bm-static-hero" aria-labelledby="home-hero-heading">
      <img
        src="assets/marketing/buildmind-villa-pexels-28915352.jpg"
        alt="Modern two-storey tropical residence with warm lighting and a swimming pool"
        class="bm-static-hero__image"
        fetchpriority="high"
      />
      <div class="bm-static-hero__shade" aria-hidden="true"></div>
      <div class="bm-static-hero__content">
        <p class="bm-static-hero__kicker">Land · Planning · Regulation · Design</p>
        <h1 id="home-hero-heading" class="bm-static-hero__title">
          <span>Plan your home </span>
          <em>with confidence.</em>
        </h1>
        <p class="bm-static-hero__copy">
          Turn survey information into regulation-aware layout options, refine them with AI and
          collaborate with a qualified professional.
        </p>
        <div class="bm-static-hero__actions">
          <a routerLink="/auth/register" class="bm-static-hero__primary">
            Start the Planning Experience <bm-icon name="arrow-right" [size]="18" />
          </a>
          <a routerLink="/" fragment="how-it-works" class="bm-static-hero__secondary">
            <span class="bm-static-hero__play" aria-hidden="true">▶</span>
            See How It Works
          </a>
        </div>

        <ul class="bm-static-hero__highlights" aria-label="BuildMind planning highlights">
          @for (item of highlights; track item.title) {
            <li>
              <bm-icon [name]="item.icon" [size]="23" />
              <span
                ><strong>{{ item.title }}</strong
                >{{ item.detail }}</span
              >
            </li>
          }
        </ul>
        <p class="bm-static-hero__credit">
          Architectural photograph via Pexels. Product plans and visual stages are illustrative.
        </p>
      </div>
    </section>

    <section class="bm-planning-journey" aria-labelledby="planning-journey-heading">
      <div class="bm-planning-journey__inner">
        <p class="bm-planning-journey__label">The planning journey</p>
        <h2 id="planning-journey-heading">From surveyed land to a plan you can discuss</h2>
        <ol aria-label="Static planning journey">
          @for (stage of journey; track stage.title; let index = $index) {
            <li>
              <span class="bm-planning-journey__icon">
                <bm-icon [name]="stage.icon" [size]="27" />
              </span>
              <span class="bm-planning-journey__number">0{{ index + 1 }}</span>
              <strong>{{ stage.title }}</strong>
              @if (index < journey.length - 1) {
                <span class="bm-planning-journey__arrow" aria-hidden="true">→</span>
              }
            </li>
          }
        </ol>
        <p class="bm-planning-journey__note">
          Architectural visualisation stages are illustrative marketing content, not
          customer-specific 3D generation.
        </p>
      </div>
    </section>
  `,
})
export class StaticHomeHero {
  protected readonly highlights: readonly Highlight[] = [
    { icon: 'file-text', title: 'Survey to Plan', detail: 'Extract & interpret' },
    { icon: 'shield', title: 'Regulation Aware', detail: 'Sri Lanka context' },
    { icon: 'sparkles', title: 'AI Assisted', detail: 'Refine with natural language' },
    { icon: 'users', title: 'Professional Review', detail: 'Collaborate & consent' },
  ];

  protected readonly journey: readonly JourneyStage[] = [
    { icon: 'map-pin', title: 'Land Intelligence' },
    { icon: 'file-text', title: 'Blueprint Formation' },
    { icon: 'layers', title: '3D Structure' },
    { icon: 'sparkles', title: 'Material Transformation' },
    { icon: 'check-circle', title: 'Final Experience' },
  ];
}
