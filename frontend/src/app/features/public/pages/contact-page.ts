import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { InfoPage } from './info-page';

/**
 * Contact / help. No support address, phone number or form endpoint has been provided yet, so
 * this page does not invent one; it says so plainly and points to the help that exists.
 */
@Component({
  selector: 'bm-contact-page',
  imports: [InfoPage, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <bm-info-page
      eyebrow="Contact"
      heading="Help and contact"
      lead="Answers to common questions are on the Home Page. Direct contact details will be listed here once the BuildMind support channel is confirmed."
    >
      <section>
        <h2>Common questions</h2>
        <ul>
          <li><a routerLink="/" fragment="how-it-works">How BuildMind works</a></li>
          <li><a routerLink="/about">What BuildMind does and does not do</a></li>
          <li><a routerLink="/privacy">How your information is handled</a></li>
        </ul>
      </section>
      <section>
        <h2>Account and sign-in help</h2>
        <ul>
          <li><a routerLink="/auth/forgot-password">Reset a forgotten password</a></li>
          <li><a routerLink="/auth/verify">Resend an email verification link</a></li>
        </ul>
      </section>
      <section>
        <h2>Support contact</h2>
        <p>
          The support email address and hours have not been published yet. This page will be updated
          when they are.
        </p>
      </section>
    </bm-info-page>
  `,
})
export class ContactPage {}
