# Final plan editor and atomic mutation report

| Capability | Status | Evidence |
|---|---|---|
| Edit commands: resizeRoom, moveRoom, addRoom/removeRoom (M3 engine), updateWall, addOpening / moveOpening / removeOpening | IMPLEMENTED (M3 engine), LIVE_VERIFIED for the journey-exercised commands | journey 4/8; python plan-editor suite |
| Edit validation without persistence (`edits/validate`) | IMPLEMENTED, LIVE_VERIFIED | journey 8 |
| Candidate preview → stored **unapplied** candidate; apply (acknowledged warnings); reject; expiry (`expiresAtUtc`, reported `expired`) | IMPLEMENTED, LIVE_VERIFIED | journey 4/11 |
| Candidate listing/detail (`GET plans/{planId}/candidates[/{id}]`) | IMPLEMENTED, LIVE_VERIFIED | journey 11 |
| Immutable revisions + history + by-id | IMPLEMENTED, LIVE_VERIFIED | journey 3/4/11 |
| Head-revision concurrency: stale base → 409; duplicate apply → 409 | IMPLEMENTED, LIVE_VERIFIED | journey 4/11 |
| **Undo/redo** (`POST plans/{planId}/revert`): new head revision equal to a target revision, through the validated save-version path; history never rewritten | IMPLEMENTED, LIVE_VERIFIED | journey 11: revert hash equals v1, stale revert 409, redo hash equals the later head |
| Final confirmation (exact revision + geometry hash + acknowledgement) and read-back (`GET plans/{planId}/confirmation`) | IMPLEMENTED, LIVE_VERIFIED | journey 4/11 |

## Atomicity and authorization

- Every protected mutation runs in `ProtectedPlanningWriter`: one short transaction that
  - locks the project row;
  - re-checks the trusted actor, the current owner and the **AuthorizationVersion**;
  - applies a CAS update on the expected state/version;
  - commits.
  Any mismatch writes nothing: no partial writes.
- Candidate apply re-checks:
  - exact plan, base revision and expected head;
  - candidate lifecycle state;
  - geometry hash;
  - validation evidence id.
  The appended revision carries its validation evidence.
- **Unknown commit outcome:** the M3/M4 path classifies it as `TRANSACTION_OUTCOME_UNKNOWN` / `PERSISTENCE_OUTCOME_UNKNOWN`, which is never reported as success. A replay with the same command id is resolved by the command-id uniqueness.
- **Live concurrency evidence:** the persistence suite's concurrent protected-write, authorization-version and CAS tests (115/115). The journey covers duplicate and stale operations.
- **AI proposals** never write geometry: they produce a validated, unapplied candidate that only the customer can apply (FINAL-AI-LLM-BENCHMARK-REPORT).
