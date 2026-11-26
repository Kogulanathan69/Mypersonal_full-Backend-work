# BE-M5-D06 Step 08-C — actual isolated PDF renderer

## Scope and preservation

Branch feature/m5-day06-pdf-reports; HEAD ab00376bba1ecb3006dc0c3904ecf556665def2b unchanged.
All seven pre-existing untracked files match the Step08B SHA256 manifest. No production registration, endpoints, database, other-member feature code, commits or pushes.
Backup: C:\Users\tharm\BuildMind-M5-Backups\Day06-Step08B-20261119-225137.

## NuGet recovery and security evidence

Read-only DNS succeeded outside the sandbox; WinHTTP direct access, no proxy. NuGet service index, vulnerability index and both advisory resources returned HTTP 200. Earlier timeout was not reproduced; a permanent root cause is not established. SDK 10.0.400. One combined cache-list command produced a formatting error; individual http-cache listing succeeded. No caches deleted or security settings changed.

Isolated evaluation directory: C:\Users\tharm\AppData\Local\Temp\BuildMind-Pdf624-Evaluation-195798c466494adc95ffbbbb1636c1ff.
PDFsharp-MigraDoc and PDFsharp pinned exactly [6.2.4], MIT. Default PKCS 8.0.1 and trial 10.0.12 were correctly rejected by audit: GHSA-846f-f764-76mx (high), GHSA-2fmv-rv2c-59x7 (moderate). Explicit [10.0.15] fixes both reported ranges. All evaluated packages' NuGet signatures verified; all evaluated package licenses MIT; isolated net10 build passed.
Actual Infrastructure graph and full solution audit reported no known vulnerabilities (10 projects), not a guarantee of absence. JSON evidence: %TEMP%\BuildMind-Step08C-solution-audit.json.
Two older project NU1900 warnings still appeared during solution build; not suppressed and their persistence is unresolved despite successful current audits.

## Implemented

Infrastructure-only PDF implementation of IPreliminaryReportRenderer, no registration. A4 navy/teal/white pages, project/revision/hash/confirmation/provenance, persisted requirements, actual vector floor geometry, thickness-scaled walls, wall-relative door/window marks, room schedule with polygon areas and labelled bounding extents, validation statuses and exact disclaimer.
Input revalidated through ReportDataBuilder; no second geometry parser or authorization claims. Operational caps: geometry JSON 1 MiB, 20 floors, 500 rooms, 10000 elements, 100 pages, output stream capped at 10 MiB. Cancellation checked between stages/lines and before return; synchronous PDF library calls are not interruptible mid-call.
Safe fixed renderer failure text, no inner exception disclosure; no rendering network/filesystem reads. Font embedded as resource, upstream Noto SIL OFL 1.1 license bundled. Global PDFsharp resolver is initialized once and incompatible existing resolver fails closed; application-wide font integration remains a production coordination issue.

## Unicode limitation

Only printable ASCII plus handled whitespace accepted. NotoSans embedding is not proof of Sinhala/Tamil shaping. Both scripts deliberately fail with safe error instead of silent corruption; mixed-language and third Unicode sample remain blocked. Other non-ASCII scripts likewise unsupported in this implementation. No invented transliteration or replacement glyphs.

## Tests and evidence

Reports final focused run: 81 passed, 0 failed, 0 skipped, preserving all 74 prior cases. Seven new cases: actual PDF 1/2 floors, Tamil/Sinhala safe rejection, cancellation, floor complexity limit, maximum M3 room-name length (200).
Two intermediate new-case failures came from synthetic duplicate IDs and 1500-character room name exceeding M3's 200 limit. Fixtures corrected; no assertion weakening or test removal.
Earlier full xUnit run: Reports79, Requirements400, Workflow145, Application128, Infrastructure728, all passed. Standalone C# contract runner348 passed. Final full rerun captured in execution report.

Synthetic evidence outside Git: C:\Users\tharm\BuildMind-M5-Backups\Day06-Step08C-PdfEvidence.
- synthetic-1-floor.pdf: 3 pages, A4, PDF1.7, 24596 bytes.
- synthetic-2-floor.pdf: 4 pages, A4, PDF1.7, 25446 bytes.
- single-floor-page2.png: independently rasterized with Poppler and visually inspected; 4x3 proportions, bottom door, schedule and footer readable.
Poppler pdfinfo parsed both. Text extraction utility unavailable; exact extracted-text assertions not completed. Only the single-floor diagram page received visual inspection, not all pages. Unicode PDF not generated. No real customer/authorization/confirmation evidence used.
Gitleaks external changed-file snapshot scan: no leaks. Whitespace checks passed before final documentation. Original files preserved.

## Remaining work / Step09

PARTIAL; Step09 not started. Pending: verified complex-script shaping/fonts, third sample, independent text extraction/revision assertions, all-page visual review, room names inside diagrams/collision strategy, concave/multiroom/window PDF-specific fixtures, direct output/page limit boundaries, fonts/global-resolver production coordination, full cancellation semantics, private storage and persisted authorization/confirmation. Resource caps are provisional safeguards, not production planning policies.
Do not claim this renderer production ready. Await approval before Step09.
