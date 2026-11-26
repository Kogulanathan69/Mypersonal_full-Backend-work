> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Boundary contracts — Angular ↔ C# ↔ Python ↔ RAG

Proposed paths are illustrative until API approvals. Maintain machine-readable OpenAPI and JSON Schema with fixtures.

## Stable contract principles
- C# public API is `/api/v1` with explicit schema versions; Python routes are **private internal**.
- Every request carries correlation ID; authenticated C# extracts user ID from identity; never trust client owner ID.
- JSON response convention (`data`, `error`, `traceId`, `revisionId` where relevant) decided by M4 and documented in canonical OpenAPI.
- Units explicit: coordinates CRS (e.g., EPSG:4326 for lat/lon), plan dimensions in **meters in canonical geometry**, source units preserved; no guessing ft↔m.
- `planRevisionId` and stable geometry hash accompany validation and confirmation.
- Geometry has stable room/wall/opening IDs, polygon topology, boundary constraints and schema version.
- Separate `geometry_valid`, `requirements_match`, `regulatory_evidence_status`, `confirmed` values; none is proxy for another.

## Sample plan generation request/response (design sketch, not deployed)
```json
{
  "projectId": "project-id",
  "requirementsRevisionId": "req-v3",
  "landRevisionId": "land-v2",
  "geometrySchemaVersion": "1.0",
  "correlationId": "uuid"
}
```
```json
{
  "status": "SUCCEEDED",
  "alternatives": [
    {"id":"A", "geometry":{}, "validation":{}},
    {"id":"B", "geometry":{}, "validation":{}},
    {"id":"C", "geometry":{}, "validation":{}}
  ],
  "schemaVersion": "1.0",
  "warnings": []
}
```
The placeholder `{}` values are not valid real geometry; test fixtures must use canonical schema. If cannot produce three valid/distinct results, status `INFEASIBLE` + diagnostics; not a fabricated success.

## RAG evidence sample (not a government certification)
```json
{
 "sourceDocumentId":"id", "sourceVersion":"2026-revision-id",
 "page":12, "authority":"authority-label", "jurisdiction":"district-or-rule-scope",
 "quotedRuleText":"source excerpt", "retrievalConfidence":0.83,
 "applicability":"NOT_VERIFIED", "reviewedBy":null,
 "limitations":["Site jurisdiction not established"]
}
```
`retrievalConfidence` is search relevance, not regulation validity. Only a separately reviewed rule applicability decision can support a deterministic compliance check. M7 owns evidence schema, M6 uses evidence without recasting uncertain rules as approved, M5 displays correctly through C#.

## Contract ownership and rollout
| Contract | Producer | Consumer | Owner / Approver |
|---|---|---|---|
| Auth/project API | M4 | M1 | M4 + M1 review |
| Survey upload/land API | M5 | M2 | M5 + M2/M6 review |
| Requirements API | M5 | M1/M2 | M5 + FE review |
| Generate/preview/edit API | M5 | M3 | M5 + M3/M6 review |
| Python plan/geometry result | M6 | M5 | M6 + M5 review |
| RAG evidence | **M7** | M6/M5 | M7 + M6/M5 review |
| Confirm/report/architect API | M5 | M1/M3 | M5 + FE/M4 review |

## Versioning & breaking changes
Additive fields may be permitted with tests. Removing/renaming fields, changing units, changing geometry topology assumptions, changing validation meaning or auth requires ADR + major schema version + consumer signoff. Old consumers remain supported until agreed cutover. Tests must check current and prior supported fixtures.
