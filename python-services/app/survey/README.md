# Survey module

**Owner M2** (survey, land, location, geometry normalization) per [V3 ownership](../../../docs/second-brain/14-Current-Backend/10-OWNERSHIP.md). Reconciled 2026-11-17 (BE-M7-D01); this file previously named M6, which is superseded. M6 is the proposed custodian of `app/main.py` and shared Python dependencies only; register routers through that custodian.

Interface and implementation pending. Proposed internal route (not frozen): `POST /survey/extract` — see [05-API-CONTRACTS](../../../docs/second-brain/14-Current-Backend/05-API-CONTRACTS.md). Internal routes are never public frontend endpoints. Supported formats, OCR, limits and storage remain open (G05). Use synthetic fixtures only; never commit real survey documents.


## Provisional survey and land contracts (BE-M2-D01)

Implemented in `contracts.py`:

- `SurveyDocument`: private document metadata, file type, size and SHA-256.
- `SourceReference`: source file and optional page/reference text.
- `SurveyExtraction`: extraction status, measurements and diagnostics.
- `ExtractedMeasurement`: source-reported measurement, confidence and review status.
- `BoundaryMeasurement`: source-reported boundary segment length and bearing.
- `LandBoundaryRevision`: versioned draft boundary measurements and reported area.

### Validation rules

- Unknown or missing measurements remain `None`; values are never invented.
- Measurement units: `m`, `ft`, `unknown`.
- Coordinate units: `m`, `ft`, `degree`, `unknown`.
- Area units: `m2`, `ft2`, `unknown`.
- Non-finite measurement values (NaN and Infinity) are rejected.
- Confidence values must be between 0 and 1.
- Extra contract fields are rejected.
- Source references and review status are preserved.

### Current limitations and pending decisions

These contracts are provisional and require cross-member review before production use.

- No survey file upload or private-storage implementation is included here.
- No OCR or AI extraction pipeline is implemented here.
- No coordinate reference system is inferred or transformed.
- Source measurements are not confirmed land geometry.
- Legal survey validity and boundary confirmation are outside these contracts.
- API routes and shared storage conventions remain subject to M4 coordination.
- Coordinate and geometry handoff requires M2/M6 agreement.

Tests: `python-services/tests/test_survey_contracts.py`
