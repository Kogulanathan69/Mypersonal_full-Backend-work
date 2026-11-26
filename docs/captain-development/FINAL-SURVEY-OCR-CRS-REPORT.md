# Final survey, OCR, land and CRS report

Details: CRS-GEOMETRY-INTEGRATION-REPORT.md and OCR-SANDBOX-AND-ACCURACY-REPORT.md.

| Capability | Status | Evidence |
|---|---|---|
| Secure PDF/image upload (size and content validation, private GUID storage outside the web root) | IMPLEMENTED, LIVE_VERIFIED | journey 2/8; survey tests |
| Survey listing (`GET /projects/{projectId}/surveys`, owner-scoped) | IMPLEMENTED, LIVE_VERIFIED | journey 11 |
| Background extraction (lease, heartbeat, bounded attempts), PDF text + local OCR (Tesseract eng/sin/tam) in a sandbox with timeouts and resource limits | IMPLEMENTED, LIVE_VERIFIED | journey 8; live DB tests 12/12; python suite |
| Extraction review/correction by the customer (`…/extraction/reviews`, versioned); values never auto-promoted to verified | IMPLEMENTED, LIVE_VERIFIED | journey 8 |
| Coordinates are **never** inferred from survey lengths/bearings | by rule | — |
| Land polygon validation (simple polygon, area, self-intersection) | IMPLEMENTED, LIVE_VERIFIED | journey 3 |
| CRS identification + transformation (PROJ via M2 `normalize_land_crs`; auto SLD99/EPSG:5235 for geographic sources in Sri Lanka; refuses unknown or ambiguous CRSs) | IMPLEMENTED, LIVE_VERIFIED | 13 py tests, journey 10 |
| Local Cartesian frame with origin (floored anchor), axes, unit, provenance; canonical digest with .NET parity | IMPLEMENTED, LIVE_VERIFIED | 3 .NET parity tests |
| Stable physical edge identity (UUIDv5 of sorted endpoints) + split lineage (`parentEdgeId`) | IMPLEMENTED, LIVE_VERIFIED | journey 10 |
| Land revision persistence + history (`GET land/revisions`, `land/revisions/{id}`) | IMPLEMENTED, LIVE_VERIFIED | journey 11 |
| Confirmed land state (`POST land/confirm`) | IMPLEMENTED, LIVE_VERIFIED | journey 8 |
| Holes/exclusions | NOT SUPPORTED: the land contract is a single exterior ring; rejected, not silently dropped | — |
| **Boundary role classification** (road frontage / side / rear) with evidence, human review and confirmation | **MISSING (actionable)**: every edge is `UNASSIGNED`. Roles are not guessed. A review/confirmation workflow (edge-role assignment table + endpoints) is the next implementation item. | FINAL-REMAINING-BLOCKERS-REGISTER R-01 |
| Setback evidence per edge / buildable-area calculation from **reviewed** setbacks | BLOCKED on R-01 + E-5 (reviewed constraint values). Generation uses labelled synthetic setbacks. | — |
| Survey original-file download | **MISSING (actionable)**: the storage port has no read operation yet | R-02 |
| OCR accuracy on real surveys | needs authorised representative surveys (E-7) | — |
