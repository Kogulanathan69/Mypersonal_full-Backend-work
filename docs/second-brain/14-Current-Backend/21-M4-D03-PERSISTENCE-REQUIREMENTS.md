# BE-M4-D03 — Feature schema and repository implementation requirements (PROPOSAL, 2026-11-18)

Status: **requirements only.** No schema, mapping, migration or repository has been created. D03 starts only after D02 review and approval, and with the feature owners' agreement on each entity. G03 (commercial licensing) remains open; work stays within the internal-development authorization.

## Inputs already on `develop`
| Port | Owner | Key semantics M4 must implement |
|---|---|---|
| `IUserRepository` | M1 | Unique normalized email; `UpdateAsync` has no concurrency token (decision needed) |
| `IProjectRepository` | M1 | Owner-scoped reads; `UpdateAsync(project, ownerId, expectedRevision)` compare-and-set |
| `IRequirementRevisionRepository` | M5 | Atomic insert of an immutable revision plus a current-pointer CAS; consecutive numbering; no overwrite |
| `IReportRepository` | M5 | Atomic source-verified creation; `StateVersion` CAS transitions Pending → Ready/Failed |
| `IArchitectRequestRepository` | M5 | Atomic request + consent creation; versioned withdrawal |
| `IPlanRevisionStore` | M3 (implementer unassigned, G20) | Append-only revisions; CAS on the expected base revision; duplicate-command handling |

## Decisions needed from owners before any migration
1. **Mapping strategy** for immutable domain types (`Report`, `ArchitectRequest`, `RequirementRevision`, `ReportSnapshot`): M4 recommends persistence row models in Infrastructure plus `Rehydrate`, rather than EF mapping of domain types directly.
2. **Keys and types:** GUID storage (provider default `char(36)` observed; `binary(16)` is an option), owner/project FK graph, nullability, `decimal` precision per field.
3. **JSON:** native `json` columns are semantically equal but **not byte-identical** (T20). Hashes (geometry, snapshots) must be computed over a canonical serialization, never over stored bytes.
4. **Timestamps:** UTC only (`datetime(6)` + M4 converters); original offsets are not stored. Owners confirm nothing needs the local offset.
5. **Concurrency tokens:** `Revision`/`StateVersion` as compare-and-set columns (`ExecuteUpdate … WHERE version = @expected`); `IUserRepository.UpdateAsync` needs a decision.
6. **Idempotency keys** for create operations that callers may retry (G12 / M5 / M6).
7. **Deletion and retention:** soft versus hard delete; FK `ON DELETE` behaviour (default `RESTRICT`).

## Implementation requirements (M4)
- One migration coordinator (M4). Each feature's first migration needs owner sign-off. Migrations are deployed only by the Option B procedure (ranged scripts, `bm_migrator`, backup, verification).
- Configurations go in `Persistence/Configurations/<Feature>/` (discovered automatically). Repositories go in `Persistence/Repositories/<Feature>/`. No generic repository.
- Every atomic port method runs in `TransactionRunner`, filters by owner/project, honours cancellation, never retries implicitly, and maps `PersistenceFailure` to the port's own result statuses (for example a duplicate key → `AlreadyExists`/`DuplicateCommand`; a CAS miss → `RevisionConflict`/`StateConflict`).
- No external calls inside a transaction (storage/rendering outside, per the ports).
- Indexes: every owner/project-scoped query uses a leading scoping-key index (verified with `EXPLAIN`, as in the spike SQL test).

## Acceptance tests (from the M4 plan, made concrete)
- A fresh migration applies on a disposable MySQL 8.4 database in the `database-compatibility` CI job, with ranged-script deployment verified.
- Invalid FKs are rejected; unique keys are translated without values; rollback is tested on disposable databases only.
- For each repository: owner isolation (one owner can't read another's records), CAS conflict, duplicate handling, cancellation without partial writes, and no stale tracked state.
- Generated migration plus snapshot with `HasPendingModelChanges() == false`.
- Evidence: migration files, schema diagram, CI TRX, PR. Independent review is required.

## Known risks carried from D02
Two provider limitations (idempotent scripts; unconverted `DateTimeOffset` reads) are mitigated by the Option B procedure and the UTC converter, and must be reassessed on any provider upgrade. The T06 version guard currently reports the EF Core assembly, not the provider (reviewer note). The G03 licence decision may still change the provider.
