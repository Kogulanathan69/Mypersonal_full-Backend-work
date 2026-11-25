# Project lifecycle (derived; Captain-local stage names, not ratified enums)

V3 `06-WORKFLOWS.md`: *"Project state derived from valid prerequisites, not a client-controlled flag."*

- **Endpoint:** `GET /projects/{id}/lifecycle` (owner only) computes the stage from persisted, revision-bound facts. It writes nothing.
- **Implementation:** `MySqlProjectLifecycleReader`.

## Milestones

A milestone counts only for **current** inputs.

| Milestone | Satisfied when | Required for later stages |
|---|---|---|
| SURVEY_UPLOADED | ≥1 survey | no (boundary may be entered manually) |
| SURVEY_REVIEWED | ≥1 extraction review revision | no |
| LAND_CONFIRMED | a land revision exists (customer-confirmed) | yes |
| REQUIREMENTS_COMPLETED | a requirements head exists | yes |
| PLAN_GENERATED | a Succeeded generation job for the current land + requirements revisions | yes |
| PLAN_SELECTED | a plan whose head revision is bound to the current land + requirements | yes |
| PLAN_CONFIRMED | a confirmation whose revision is still that plan's head, on current inputs | yes |
| REPORT_READY | a Ready report from a live confirmation | yes |
| ARCHITECT_REVIEW_REQUESTED | a Pending request for such a report | no |

## Rules

| Rule | Behaviour |
|---|---|
| Stage | The furthest milestone reached with every *required* predecessor satisfied; `GENERATION_READY` when land + requirements exist but nothing current was generated; `DRAFT` otherwise. |
| `stale` | Counts of generation jobs, plans, confirmations and reports that refer to superseded inputs or heads. They are kept as history, never counted as current (06-WORKFLOWS: "re-running generation makes previous selection stale … don't delete prior history"). |
| `nextActions` | Server-suggested next steps (for example `START_GENERATION`). |

**Verified live** (journey phase 8):
- `GENERATION_READY` → `PLAN_SELECTED` → `PLAN_CONFIRMED`;
- a new land revision returns the project to `GENERATION_READY`, with `stalePlans = 2` and `supersededConfirmations = 1`.

## Not implemented (policy required)

- **Archival/restore, ownership transfer, deletion:** need an approved retention and authorization policy, plus an M1 lifecycle writer. Destructive deletion is not enabled.
- **Confirmation supersession:** a new revision already makes the earlier confirmation non-current (the reader treats it as superseded). An explicit invalidation record is not written.
