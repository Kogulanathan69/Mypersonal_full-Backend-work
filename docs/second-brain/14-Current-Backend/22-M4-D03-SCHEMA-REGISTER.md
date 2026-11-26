# BE-M4-D03 — Schema, mapping and repository register (2026-11-18, UNCOMMITTED, awaiting diff review)

Branch `feature/m4-database-schema-repositories`, created from `b5208bc` (the PR #21 tip). **It depends on PR #21, which is not merged**; this is not merged `develop`. Status labels: **implemented** (code written), **compiled**, **executed** (run in CI), **verified** (passed).

## Approval evidence
- **Lead-reported (STEP 06-A, 2026-11-18):** "required team technical approvals obtained; D03 authorized". No written per-member decision, GitHub review or decision-record entry for the seven D03 decisions (doc 21) was found in the repository. **None is invented here.** Where a decision is needed but unspecified, the feature is isolated (see Blocked).
- G03 (provider commercial licensing / public repository): **open**. G20 (M3 port implementer): **no assignment recorded**. Independent reviews of PR #8/#10/#11/#18/#21: **none**.

## Tables (implemented, compiled; live schema test D03-R01 compiled, not yet executed)
| Table | Source contract | Columns (type) | Keys / constraints / indexes |
|---|---|---|---|
| `users` | M1 `User` | `id` char(36), `email` varchar(254), `normalized_email` varchar(254), `password_hash` varchar(512), `full_name` varchar(100)?, `phone_number` varchar(16)?, `is_active` bool, `created_at_utc` datetime(6) | `pk_users`; `ux_users_normalized_email` |
| `projects` | M1 `Project` | `id`, `owner_id`, `name` varchar(150), `project_type` varchar(100)?, `location_reference` varchar(500)?, `status` varchar(32), `revision` bigint (concurrency token), `created_at_utc`, `updated_at_utc`? | `pk_projects`; `fk_projects_users_owner_id` (RESTRICT); `ix_projects_owner_id_created_at_utc` |
| `requirement_revisions` | M5 `RequirementRevision` | `id`, `project_id`, `revision_number` bigint, `schema_version` varchar(64), `catalogue_version` varchar(64), `content_json` **json**, `created_at_utc` | `pk_requirement_revisions`; `fk_requirement_revisions_projects_project_id` (RESTRICT); `ux_requirement_revisions_project_id_revision_number`; `ak_requirement_revisions_project_id_id` |
| `project_requirement_heads` | M5 port "current pointer" | `project_id`, `current_revision_id`, `current_revision_number` | `pk_project_requirement_heads`; `fk_project_requirement_heads_projects_project_id`; composite `fk_project_requirement_heads_requirement_revisions_current` (`project_id`, `current_revision_id`) → (`project_id`, `id`), so the head can only point at a revision of the same project |

All GUIDs are `char(36)` (provider default, observed in D02b) and never database-generated. All timestamps are UTC `datetime(6)` through the M4 converters. Lengths come from the M1 validators. The version and status lengths are M4 storage limits, because no contract defines them.

## Persistence models and mapping
- `Persistence/Records/*`: Infrastructure-only row models. Domain and Application have no EF references (architecture tests).
- `RequirementRevisionMapper`: explicit `ToRecord`/`ToDomain`. Reconstruction uses the public validating constructors of `RequirementRevision`/`RequirementContent`, so invariants are re-checked on read; corrupt rows → `InvalidDataException` (content never echoed). The JSON document contract is fixed and independent of the domain type's shape. MySQL native `json` isn't byte-stable, so this content is **not** used for hashing.
- **M1 `User`/`Project` are NOT reconstructed:** their `Id`, `IsActive`, `Status`, `Revision` and `UpdatedAtUtc` have private setters and no factory, and reflection is not allowed. The tables exist only as foreign-key targets.

## Repositories
| Port | Status |
|---|---|
| `IRequirementRevisionRepository` (M5) | **Implemented** — `RequirementRevisionRepository` (scoped DI). `TransactionRunner`: project check → insert immutable revision → head insert or compare-and-set (`ExecuteUpdate … WHERE current_revision_id = @expected`). Stale expected head → `RevisionConflict`. After a duplicate key (1062) or deadlock (1213) the head is re-read: **conflict only if it verifiably moved**; reuse of an existing revision id (`requirement_revisions.PRIMARY`) → `ArgumentException`; otherwise rethrown. Only the project FKs → `ProjectNotFound`. 1205, unknown commit and unconfirmed rollback propagate. Project-scoped reads. No retries. **Project filtering is not user authorization**: callers must verify ownership first, and no endpoint or registered Application service exposes it yet |
| `IUserRepository`, `IProjectRepository` (M1) | **Blocked**: M1 must add rehydration factories (for example `User.Rehydrate(...)`, `Project.Rehydrate(...)`) and decide whether `IUserRepository.UpdateAsync` gets a concurrency token |
| `IReportRepository` (M5) | **Blocked**: atomic creation must verify persisted plan/confirmation/validation/land sources, and those tables don't exist (M2/M3/M6); `ReportSnapshot` storage needs the canonical-hash decision |
| `IArchitectRequestRepository` (M5) | **Blocked**: needs the G12 policy/eligibility sources and the consent/document storage decision |
| `IPlanRevisionStore` and M3 candidate/apply/expire (M3) | **Blocked**: no implementer assignment recorded (G20) |

## Migration register
| Migration | Status |
|---|---|
| Initial feature schema (`users`, `projects`, `requirement_revisions`, `project_requirement_heads`) | **CI generation step added (STEP 06-D, pending first run).** The `database-compatibility` job runs `dotnet-ef migrations add InitialFeatureSchema` in its disposable workspace while no migration is committed, then `has-pending-model-changes`, `migrations list --no-connect` and `migrations script 0` (**ranged**). It uploads the migration, designer, snapshot and SQL in the `mysql-compatibility-results` artifact (`buildmind-migration/`). Never committed by CI: the reviewed files are committed by M4. Live validation: M01–M04 (exists + no pending changes, schema/FK RESTRICT/unique/indexes/json/datetime(6), rollback + reapply, ranged-script deployment). History of this row: originally **NOT GENERATED.** `dotnet-ef migrations add` loads the freshly built, unsigned assemblies, which Smart App Control blocks on the lead's workstation (G17); no bypass. Proposal (needs approval): generate it in the isolated Linux CI job (as the D02b spike already does for a synthetic context) and publish it for review, or on an approved development machine. Then: reviewed **ranged** SQL, `HasPendingModelChanges() == false`, ranged-script deployment test. Until then the live tests create the schema in **disposable** databases with `EnsureCreated` (interim, test-only) |

## Tests
- Database-free (`BuildMind.Infrastructure.Tests`, compiled, not executed locally): mapper tests (7 methods / 10 cases), schema model tests (13 / 27), DI registration (1), plus the updated table-set assertion. Suite total: 150 methods / **259 cases** (was 221).
- Live (`BuildMind.Persistence.IntegrationTests`, compiled, **not executed**; suite total **38**, was 28): D03-R01 to R10 (schema/keys/indexes, first/next save, stale conflict, missing project, project scoping, 8-way concurrent first and next saves, cancellation, FK/unique translation without values).

## STEP 06-C audit (2026-11-18)

See EV-20261118-49. Additional notes:
- Losers of an 8-way concurrent save report either `RevisionConflict` (head moved) or a fully rolled-back deadlock exception (contention before the winner committed). The tests assert exactly one winner and nothing else written.
- **Follow-up once the reviewed migration is committed:** `PersistenceRegistrationTests` and `DesignTimeFactoryTests` currently assert that the migration list is empty (true while none is committed). They must then assert the committed migration set instead; the application still never migrates at start-up.
- Test inventory: infrastructure 161 methods / 279 cases; MySQL 43 (T01–T25, T19D, LP, SQL, D03-R01–R11, M01–M04).

## STEP 06-E3 — initial migration committed (2026-11-18)

- `20261118133137_InitialFeatureSchema` (generated in CI run 44314331460, reviewed, committed byte-identical). CI now **verifies** it and never regenerates. Repository tests run on the migration only.
- **Live verification:** 43/43 on MySQL 8.4.13 (EV-20261118-51).
- **Open schema decisions (no change made):**
  1. *Email uniqueness collation:* the table inherits the database default; `utf8mb4_0900_ai_ci` is case- and **accent-insensitive**. With M1 already normalizing (upper-casing), a binary collation (`utf8mb4_bin`) on `normalized_email` would make uniqueness exact. Needs M1 + M4 agreement and a follow-up migration.
  2. *Head number consistency:* `current_revision_number` is guaranteed only by the repository. A database guarantee would need a 3-column FK `(project_id, current_revision_id, current_revision_number)` → a 3-column alternate key on revisions. Optional; follow-up migration if wanted.
- Deployment: ranged scripts only, explicit M4-lead authorization per database, backup first, destructive rollback warning (`tools/database/mysql/README.md`). Nothing has been deployed beyond disposable CI databases.

## STEP 06-F — reassessment and proposed M1 contract change (2026-11-18)

**Result: no repository unblocked; no code written.** Domain and Application are unchanged since `fdf7baa` (EV-20261118-54).

### Proposal for M1 (owner decision; M4 will not edit M1 files)
Add validating rehydration factories, so persisted rows can be rebuilt without reflection, mirroring M5's `ArchitectRequest.Rehydrate`:

```csharp
// BuildMind.Domain.Entities.User
public static User Rehydrate(Guid id, string email, string normalizedEmail, string passwordHash,
    string? fullName, string? phoneNumber, bool isActive, DateTimeOffset createdAtUtc);
// BuildMind.Domain.Entities.Project
public static Project Rehydrate(Guid id, Guid ownerId, string name, string? projectType,
    string? locationReference, string status, long revision, DateTimeOffset createdAtUtc, DateTimeOffset? updatedAtUtc);
```

Expected validation: non-empty ids; required strings non-blank; `revision >= 1`; `updatedAtUtc` not before `createdAtUtc`; timestamps kept as UTC.

### Further M1 decisions needed before the M1 repositories
1. **`IProjectRepository.UpdateAsync(project, ownerId, expectedRevision)`**: who produces the next revision? The domain `Project` cannot change `Revision`/`UpdatedAtUtc`. M4 can increment `revision` in the database compare-and-set (`WHERE id = @id AND owner_id = @owner AND revision = @expected`), but the caller's object would then be stale. Options: return the updated project, or add a domain method that yields the next state.
2. **`IUserRepository.UpdateAsync(user)` has no concurrency token**: concurrent profile and password updates would silently overwrite each other (last writer wins). Options: accept it explicitly, or add an expected-revision parameter (needs a `users.revision` column, so a new migration).
3. **`normalized_email` collation** (open since STEP 06-E3): binary versus inherited `ai_ci`.

### Ready on the M4 side once these exist
Tables `users`/`projects` and their indexes are already migrated. The repositories would use the same patterns as `RequirementRevisionRepository`: explicit mapper, owner-filtered queries (`GetByIdAndOwnerAsync`, `GetByOwnerIdAsync` ordered by `created_at_utc`, `id`), CAS update, duplicate email → safe translation without the value, `TransactionRunner`, no reflection.

### Unchanged blockers
Report (`IReportRepository`) needs persisted plan/confirmation/validation/land sources (M2/M3/M6) and the snapshot hashing decision. ArchitectRequest needs G12. M3 `IPlanRevisionStore` and candidate operations need G20.
