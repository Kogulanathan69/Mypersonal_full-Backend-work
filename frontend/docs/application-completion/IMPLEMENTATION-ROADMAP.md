# BuildMind application implementation roadmap

## Evidence-led sequence

1. Foundation integration: typed auth adapter, in-memory session refresh, guarded role branches,
   runtime configuration and errors. Unit/API fixture tests first.
2. Customer core: dashboard, profile/security, project list/create/detail and lifecycle.
3. Planning intake: survey upload/extraction review, land workspace and requirements.
4. Planning output: regulation boundary, generation jobs, alternatives, editor, instruction proposals,
   confirmation, revisions and reports.
5. Collaboration: customer consent/request flow, architect portal.
6. Operations: staff and admin portals using their distinct server-authorized contracts.
7. System hardening: loading/empty/error/conflict states, responsive/accessibility/browser evidence,
   live-local workflows when the API and isolated data are available.
8. Final handover documents, measured build/test results and evidence-based status.

## Design system direction

Authenticated surfaces run in **Operate** mode. The accepted charcoal/champagne palette is retained,
but technical drawing sheets may use the existing warm chalk surface. Application hierarchy comes
from navigation, typography, dividers and state—not decorative gradients or repeated dashboard cards.
Desktop editors use bounded workspaces; mobile prioritizes inspection and safe supported actions.

## Milestone commit policy

- Foundation/authentication.
- Customer projects/profile/dashboard.
- Survey/land/requirements.
- Generation/editor/reports.
- Collaboration portals.
- Admin/staff portals.
- Verification and final handover.

Each milestone requires typecheck, lint, relevant unit tests, production build and inspected browser
evidence before commit. Live integration is recorded separately from fixture-backed frontend tests.

## Current blockers that do not stop independent work

- Local backend API and Mailpit are stopped.
- `public/config.json` has no API base URL.
- Regulation corpus approval/configuration is not evidenced.
- No authorized disposable live-test credentials or database session is currently available.

These block live end-to-end status, not typed frontend implementation or fixture-backed verification.
