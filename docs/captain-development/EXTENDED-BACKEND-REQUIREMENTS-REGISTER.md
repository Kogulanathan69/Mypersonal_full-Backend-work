# Extended backend requirements register (extended-order Phase 0)

- **Audit:** 2026-11-24, HEAD `831b829`, branch `feature/captain-full-backend-completion`.
- **Scope correction (Captain, same day):** cost estimation, BOQ, 3D generation and payments are **EXCLUDED**. No code, APIs, tables or counts for them.

## Sources reconciled

- **V3:** `14-Current-Backend/02-BUSINESS-AND-FUNCTIONAL.md` (FR01–FR14), `06-WORKFLOWS.md`, `09-ADMIN.md`, `15-OPEN-DECISIONS.md` (G01–G20), `05-API-CONTRACTS.md`.
- **Captain:** the documents in `docs/captain-development/`.
- **Source scan:**
  - no `NotImplementedException`, TODO or FIXME in `src/` or `python-services/app`;
  - no role or permission model (`ICurrentUser.Roles` exists, but the issuer never emits roles);
  - `NonDeliveringPasswordResetEmailSender` is the only email path;
  - the architect directory is a JSON file;
  - the requirement catalogue is a JSON file;
  - the AI proposal port is "unconfigured".

## Authoritative admin model (09-ADMIN, FR13): overrides the generic role list in the order where they differ

1. There are two primary categories, **Customer** and **Admin**. **Staff** are delegated, admin-created accounts with **module scope**; they are not a third primary category.
2. Staff may **view** their assigned modules. **Every staff add/edit/delete is a pending proposal** until the main admin approves it. Rejection leaves live state unchanged. Approval is checked against the base version, applied atomically, and returns a stale-proposal conflict when the base changed.
3. Credentials are hashed. Initial credentials are delivered securely, never stored or logged in plaintext. A temporary password must be changed.
4. No privilege escalation; no access to unrelated modules; a backend security audit record is mandatory.
5. Admin MVP direction explicitly removes construction rates, 3D, the design knowledge base and support/communication. Payment, subscriptions and the architect company portal are unresolved (G09).

## Register

| # | Requirement | Class | Source | State at start | Plan |
|---|---|---|---|---|---|
| X-01 | RBAC: Admin and delegated Staff (module scopes), server-side fresh authorization, deny-by-default | 2 (Captain-authorized) | 09-ADMIN, FR13, order §7 | MISSING | Implement |
| X-02 | Secure admin bootstrap (operator-controlled, no hardcoded credentials) | 2 | order §7 | MISSING | Implement |
| X-03 | Staff provisioning by invitation (set-password link; must change password) | 2 | 09-ADMIN | MISSING | Implement |
| X-04 | Staff change-proposal queue (pending → approve/reject/stale) | 1 (FR13) | 09-ADMIN | MISSING | Implement |
| X-05 | Admin audit trail (append-only, tamper-evident) | 1 | 09-ADMIN | MISSING | Implement |
| X-06 | Admin customer management (list, search, detail, suspend/activate with session revocation) | 2 | order §7 | MISSING | Implement (metadata only; no document content) |
| X-07 | Operational monitoring (projects, surveys, jobs, reports, architect requests, health) | 2 | order §7 | MISSING | Implement (aggregate + metadata) |
| X-08 | Staff tasks, assignment, notes, escalation, purpose-bound audited project review | 2 | order §7 | MISSING | Implement |
| X-09 | Email abstraction + SMTP adapter + local capture (Mailpit) for recovery, verification and invitations | 1 (FR01) + external provider | FR01, order §8 | PARTIAL (non-delivering) | Implement locally; real provider needs approval |
| X-10 | Email verification, password change, security event history | 2 | order §8 | MISSING | Implement |
| X-11 | Project lifecycle (status derived from prerequisites, transition history, archive) | 1 (06-WORKFLOWS: "derived, not client flag") | 06-WORKFLOWS | MISSING | Implement |
| X-12 | Survey upload uncertain-commit reconciliation | 1 (FR03 hardening) | KNOWN-BLOCKERS | OPEN defect | Implement |
| X-13 | Worker lease renewal (lease 120 s < timeout 150 s inconsistency) | 1 | order §10 | DEFECT | Fix |
| X-14 | OCR sandbox limits (pixels, pages, CPU, memory, time, network) | 2 | SECURITY-REVIEW SR-5 | PARTIAL | Implement |
| X-15 | Survey extraction correction workflow and history | 2 | order §10 | MISSING | Implement |
| X-16 | Land CRS preservation / projected frame / edge identity (JC-01) | 1 (FR04 "explicit CRS") + 3 (JC-01 unratified) | FR04, order §11 | PARTIAL (local metric frame only) | Implement the local versioned slice |
| X-17 | Persistent, versioned requirement catalogue + admin publication | 1 (FR05) + 3 (values need product data) | FR05, 09-ADMIN | FILE-BASED | Implement; values stay labelled development |
| X-18 | Real regulation corpus activation | 5 (STEP 8B ON HOLD) + 4 | FR12, G06/G10 | BLOCKED | Document; no change to protected scope |
| X-19 | Reviewed-constraint administration (proposal/review/publish) | 1 (09-ADMIN rule publication) | 09-ADMIN | MISSING | Implement via the staff proposal workflow (Captain side only) |
| X-20 | Generation: openings, circulation, adjacency scoring, diversity | 1 (FR07) | FR07, order §14 | PARTIAL | Audit; extend where the D02/D04 code allows |
| X-21 | AI edit proposals: provider-independent adapter, persistence, accept/reject | 1 (FR08) + 4 (real LLM) | FR08, G20 | BLOCKED (503) | Implement the rule-based local adapter, clearly labelled; LLM adapter disabled |
| X-22 | Confirmation supersession (a new revision invalidates confirmation) | 1 (06-WORKFLOWS §5) | 06-WORKFLOWS | Audit | Verify / implement |
| X-23 | Report history, regeneration, access history, retention framework | 2 | order §17, G12 | PARTIAL | Implement (retention cleanup disabled) |
| X-24 | Persistent architect directory, verification, architect portal, feedback | 2 + 3 (G09 portal scope) | order §18 | FILE-BASED | Implement locally; deny-by-default |
| X-25 | Durable outbox + notification adapter + in-app notifications | 2 | order §18–19 | MISSING | Implement |
| X-26 | RH09 provider timeout hang | 1 (FR14) | KNOWN-BLOCKERS | DEFERRED | Investigate in isolation; report honestly |
| X-27 | Internal mTLS, certificate tests | 4 (G11) | order §21 | PARTIAL (token only) | Implement locally |
| X-28 | Containers, compose, backup/restore, metrics | 2 | order §22 | MISSING | Implement locally |
| X-29 | Frontend guides per role, TypeScript contracts | 2 | order §23 | PARTIAL (customer guide) | Implement |
| X-30 | Upper-floor / manual path | 3 (G08) | G08 | OPEN decision | Not expanded |
| — | Cost, BOQ, 3D, payments | **EXCLUDED** (scope correction) | — | — | None |
| — | Subscriptions, advanced analytics, data export | 6 (future, unapproved) | 09-ADMIN G09 | — | Documented as future only |

Class key:
1. original approved;
2. newly authorized Captain extended development;
3. proposed business policy requiring approval;
4. external-provider dependency;
5. protected or held;
6. optional future.
