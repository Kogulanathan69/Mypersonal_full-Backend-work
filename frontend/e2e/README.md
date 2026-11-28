# End-to-end tests

Playwright suites live here, split by owner:

| Folder | Owner module |
| --- | --- |
| `smoke/`, `a11y/`, `support/` | F — Foundation |
| `customer-onboarding/` | A — Customer Portal |
| `customer-planning/` | B — 2D Planning |
| `admin/` | C — Admin Portal |

Suites are added from Phase 01 step 12 onward. Run with `npm run e2e` after
`npx playwright install chromium`. Tests run against the **mock** build only.
