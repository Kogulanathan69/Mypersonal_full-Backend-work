import { ChangeDetectionStrategy, Component } from '@angular/core';
import { DraftNotice, InfoPage } from './info-page';

@Component({
  selector: 'bm-privacy-page',
  imports: [InfoPage, DraftNotice],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <bm-info-page eyebrow="Privacy" heading="Privacy information">
      <bm-draft-notice document="privacy policy" />
      <section>
        <h2>Information BuildMind uses</h2>
        <ul>
          <li>Account details you provide, such as your name, email and optional phone number.</li>
          <li>
            Project information: survey files, land geometry, room requirements, plans and reports.
          </li>
          <li>Technical records needed to run and secure the service, such as sign-in events.</li>
        </ul>
      </section>
      <section>
        <h2>Design principles</h2>
        <ul>
          <li>
            Your projects are visible to you. Staff access is limited to assigned support and review
            tasks.
          </li>
          <li>
            An architect sees a plan or report only after you share that specific item, and loses
            access if you withdraw consent.
          </li>
          <li>Passwords and sign-in tokens are never shown in the app or written to logs.</li>
        </ul>
      </section>
      <section>
        <h2>Still to be confirmed</h2>
        <p>
          Data retention periods, the legal entity responsible, contact details for privacy requests
          and the governing law will be stated in the final policy.
        </p>
      </section>
    </bm-info-page>
  `,
})
export class PrivacyPage {}

@Component({
  selector: 'bm-terms-page',
  imports: [InfoPage, DraftNotice],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <bm-info-page eyebrow="Terms" heading="Terms of use">
      <bm-draft-notice document="terms of use" />
      <section>
        <h2>Preliminary plans only</h2>
        <p>
          Plans, reports and evidence in BuildMind are preliminary and for planning and discussion.
          They are not approved building drawings and are not legal or professional advice. Obtain
          approval from your local authority and have plans reviewed by a qualified professional
          before building.
        </p>
      </section>
      <section>
        <h2>AI suggestions</h2>
        <p>
          AI-generated layouts and edits are proposals. You decide whether to accept them, and you
          remain responsible for the decisions you make with them.
        </p>
      </section>
      <section>
        <h2>Still to be confirmed</h2>
        <p>
          Account terms, acceptable use, liability, termination and governing law will be stated in
          the final terms.
        </p>
      </section>
    </bm-info-page>
  `,
})
export class TermsPage {}
