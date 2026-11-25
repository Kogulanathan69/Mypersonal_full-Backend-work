# Admin, staff and architect permission matrix (Captain-local proposal, not ratified)

## Basis and enforcement

- **Basis:** V3 `09-ADMIN.md` / FR13.
  - Two primary categories: **Customer** (no role row) and **Admin**.
  - **Staff** are delegated, admin-created accounts with module scopes.
  - **Architect** is a portal role.
- **Implementation:** `principal_roles`, `staff_profiles`; enforced by `AdministrationEndpointModule.AsAdmin` / `AsStaff` and `PortalEndpointModule.AsArchitect`.
- **Fresh privileges:** they are read from MySQL on every request and are **never** carried in the JWT.
- **Session revocation:** role, scope and status changes increment `users.token_version` and revoke refresh tokens, so existing sessions end immediately.

## Matrix

| Capability | Customer | Staff (module M) | Admin | Architect |
|---|---|---|---|---|
| Own projects, surveys, land, requirements, plans, reports, requests | ✔ (owner only) | ✖ (no ownership bypass) | ✖ (no ownership bypass) | ✖ |
| `/admin/*` (dashboard, accounts, staff, roles, proposals review, tasks, policies, catalogues, architects, monitoring, audit) | ✖ 403 | ✖ 403 | ✔ | ✖ 403 |
| View module M monitoring (`/staff/monitoring/{kind}`) | ✖ | ✔ only for M | ✔ (via `/admin`) | ✖ |
| Submit change proposal in module M | ✖ | ✔ (PENDING, no live effect) | — | ✖ |
| Approve/reject proposals | ✖ | ✖ | ✔ (not own; base-version checked; stale → STALE) | ✖ |
| Assigned tasks: read, status (not cancel), notes, escalate | ✖ | ✔ only own tasks in a held module | ✔ all (incl. cancel) | ✖ |
| Project review | ✖ | ✔ only the project of an active task assigned to them, with a stated purpose (10–500 chars), metadata only, audited `PROJECT_REVIEW_ACCESS` | — | ✖ |
| Customer documents (survey files, plans, PDFs) | owner | ✖ | ✖ (no endpoint) | consented report of an addressed, non-withdrawn request only (audited `ARCHITECT_REPORT_ACCESS`) |
| Assign roles | ✖ | ✖ | ✔ ADMIN/ARCHITECT, not self, never the last admin; STAFF only via invitation | ✖ |
| Suspend/activate accounts | ✖ | ✖ | ✔ (reason required, not self, never the last admin) | ✖ |

## Guards

| Guard | Rule |
|---|---|
| Bootstrap | Operator setting `CaptainIntegration:AdminBootstrap:Email`; grants ADMIN to an existing active account only while no active admin exists; audited `BOOTSTRAP_ADMIN`; no hardcoded credentials |
| Self-escalation | Impossible: no self-assignable role route exists; mass-assignment fields are rejected (strict JSON) |
| Staff credentials | Invitation by single-use, hashed, 24 h set-password code (email); no temporary passwords ever exist |
| Audit | Append-only SHA-256 hash chain (`admin_audit_events` + head row); `GET /admin/audit/verify` detects tampering |

**Modules:** `CUSTOMERS`, `PROJECTS`, `SURVEYS`, `REPORTS`, `ARCHITECT_REQUESTS`, `ARCHITECT_DIRECTORY`, `REQUIREMENT_CATALOGUE`, `REGULATORY_CONSTRAINTS`, `SYSTEM_POLICIES`.

**Proposal appliers implemented:**
- `SYSTEM_POLICY` (SYSTEM_POLICIES);
- `REQUIREMENT_CATALOGUE_DRAFT` (REQUIREMENT_CATALOGUE; creates a draft only, and publication stays admin-only).

The other modules are view-only for staff until appliers are approved.

**Evidence:**
- live tests `AdministrationLiveTests` A01–A09;
- `smoke_admin.py` 65/65;
- `smoke_journey.py` phase 9.
