# BE-M5-D06 Step 08 — library-independent rendering preparation

## Verified baseline and preservation

- Branch: feature/m5-day06-pdf-reports; HEAD: ab00376bba1ecb3006dc0c3904ecf556665def2b.
- Exactly four authorized untracked files at entry; no tracked/staged changes.
- External backup: C:\Users\tharm\BuildMind-M5-Backups\Day06-Step08-20261119-224151.
- Four copied files verified against SHA256.txt; all four original hashes unchanged after work.

## Implemented

- ReportDrawingViewport: library-independent uniform, centered metre-to-page transform with Y inversion.
- Closed rings remain closed when each vertex is transformed; no geometry replacement or normalization.
- Opening endpoints use wall-relative metre offset/width, including diagonal walls.
- Finite positive extents/scale, finite points and valid opening bounds enforced.
- Viewport enumeration bounded at 100001 items with 100000 accepted maximum (presentation resource cap, not planning policy).
- Seven new synthetic tests; no dependencies, DI, endpoints, storage or database changes.

## Actual verification

- Reports: 74 passed, 0 failed, 0 skipped (67 existing plus 7 new).
- Release solution build: success, 0 errors, 2 NU1900 warnings (Requirements and Application.UnitTests projects).
- NuGet vulnerability feed unavailable; vulnerability verification NOT successful.
- New source/test whitespace checks passed; source audit found no file/network/process access or logging in the new utility.
- No actual PDF generated; signature, independent parsing, pagination, fonts and visual PDF checks not performed.

## Library evaluation and decision

No PDF package reference or written package approval found in inspected source/docs. Do not install one yet.

- PDFsharp/MigraDoc: MIT licensing; Core build suitable candidate for portable vector drawing plus document layout. Explicit embedded font resolver needed. Higher-.NET compatibility is documented, but an exact approved version still needs net10 restore/build/deployment and vulnerability verification. Sinhala/Tamil glyph coverage and shaping must be tested, not inferred from Unicode support.
- QuestPDF: attractive layout/vector option; current Community license is source-available commercial licensing, with eligibility conditions including revenue and entity exclusions. BuildMind eligibility is unverified. Native deployment assets and complex-script fonts need target-platform tests.
- Recommendation: evaluate an explicitly approved, pinned PDFsharp/MigraDoc Core package first, subject to M4/lead dependency and font approval; QuestPDF remains an alternative after legal eligibility confirmation.

References checked:
- https://docs.pdfsharp.net/LICENSE.html
- https://docs.pdfsharp.net/PDFsharp/Overview/Specifications.html
- https://docs.pdfsharp.net/PDFsharp/Topics/Fonts/Font-Embedding.html
- https://www.questpdf.com/license/community.html
- https://www.questpdf.com/api-reference/text/font-management.html
- https://www.nuget.org/packages/QuestPDF/

## Pending and Step 09 gate

PARTIAL. Only renderer preparation complete. IPreliminaryReportRenderer has no concrete PDF implementation.
Pending approved library/version and font licensing; dependency audit; bounded input/output/cancellation/error wrapper; headings/evidence/disclaimer rendering; floor pages/labels/dimensions; independent parser and sample visual QA.
All actual renderer negative cases (failure/output limits/cancellation/Unicode) remain unexecuted. Existing extractor/builder tests are not PDF rendering evidence.
Structural confirmation consistency does not prove persisted confirmation or authorization. Preserve M3/M4 integration boundaries; do not expose raw diagnostics or hidden Vastu heuristics.
No Step 09 work, commits, pushes or merges performed. Await approval before proceeding.
