import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { InfoPage } from './info-page';

@Component({
  selector: 'bm-about-page',
  imports: [InfoPage, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <bm-info-page
      eyebrow="About"
      heading="About BuildMind"
      lead="BuildMind helps homeowners in Sri Lanka turn their land survey into a preliminary 2D house plan they can understand, refine and discuss with professionals."
    >
      <section>
        <h2>What BuildMind does</h2>
        <ul>
          <li>
            Reads your survey plan and helps you confirm your land boundary and coordinate system.
          </li>
          <li>
            Collects your room requirements and shows planning evidence that may apply to your site,
            with its sources.
          </li>
          <li>Generates alternative 2D layouts that you compare, choose from and edit.</li>
          <li>
            Lets you request changes in plain language; every AI suggestion is a proposal you accept
            or reject.
          </li>
          <li>
            Produces a preliminary report and lets you share a specific plan with an architect, with
            your consent.
          </li>
        </ul>
      </section>
      <section>
        <h2>What BuildMind does not do</h2>
        <ul>
          <li>
            It does not grant planning or building approval. Only your local authority can do that.
          </li>
          <li>It does not estimate construction costs or prepare bills of quantities.</li>
          <li>
            It does not generate 3D models of your home. The 3D house on the Home Page is an
            illustration.
          </li>
          <li>It does not take payments.</li>
        </ul>
      </section>
      <section>
        <h2>Who it is for</h2>
        <p>
          Homeowners planning a new residential house, architects who review plans that customers
          choose to share, and BuildMind staff who support the service.
        </p>
      </section>
      <p><a routerLink="/" fragment="how-it-works">See how it works →</a></p>
    </bm-info-page>
  `,
})
export class AboutPage {}
