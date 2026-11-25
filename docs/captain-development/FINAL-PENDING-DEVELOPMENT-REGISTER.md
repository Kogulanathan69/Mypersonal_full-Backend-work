# Final pending development register (discovery at HEAD `ce4cf1f`; outcomes at closure)

## Environment at start

| Item | Value |
|---|---|
| Docker | client/server 29.1.3; **no Compose plugin** |
| Host (WSL) | 3.9 GB RAM (about 2.2 GB available), 2 CPUs, 947 GB free disk |
| Python libraries | pyproj 3.8.0, shapely 2.1.2 present |
| Local LLM runtime | none |

## Reuse findings

- M2 `app/geometry/land/crs.py` already performs explicit-CRS normalisation (no ballpark transforms, projected-metre calculation CRS, WGS84 output).
- The M3 plan contract already models openings (`type door|window`, `wallId`, `offset`, `width`).
- The M3 editor already has `addOpening` / `moveOpening` / `removeOpening`.
- Gap: the Captain generation adapter emits no openings and independent per-room walls.

Order: code phases first, container build/verification last, so the images contain the final code and the memory-constrained host builds once.

| ID | Item | Phase | Requirement | Files (main) | Acceptance | Class |
|---|---|---|---|---|---|---|
| P-01 | Compose plugin, image builds, containerized stack + journeys | 1 | order §1 | `deploy/captain/*`, `.dockerignore` | Built images, running stack, journeys against containers | actionable |
| P-02 | Captain coordinate frame: source CRS + projected anchor + local frame, frame identity/version, provenance, digest; Sri Lanka CRS evaluation | 2 | FR04, order §2 | python `app/internal/land_frame.py`, C# land submission | Numerical reference tests, digest parity, live land + generation | actionable |
| P-03 | Stable physical-edge identity (rotation/reversal-invariant), edge listing | 2 | JC-01 (local) | same | Rotation/reversal tests | actionable |
| P-04 | Doors (entrance + internal) and windows (external walls) in generation; access graph; remove PHYSICAL_ACCESS_NOT_EVALUATED only when validated | 3 | FR07/FR08 | python `app/internal/openings.py`, `generation.py`, plan validation | Generated plans contain valid openings; reachability; journey | actionable |
| P-05 | Opening validation on save/edit (wall containment, corner clearance, overlap) | 3 | FR08 | python validation route | Negative tests | actionable |
| P-06 | Internal mTLS API → python-services (server + client certificates, separate keys, CA) + negative tests | 4 | G11 | `tools/captain/pki.sh`, `run-python.sh`, `PythonServiceClient` | Mutual auth proven; untrusted/missing/expired/wrong identity rejected | actionable (RAG mTLS = protected scope) |
| P-07 | RH09: isolated provider experiment MySql.Data vs MySqlConnector; containment | 5 | FR14 | `tools/captain/rh09/*` | Provider-level evidence | actionable (provider fix may be external) |
| P-08 | Real LLM adapter (OpenAI-compatible) + local model runtime (llama.cpp + small instruct model), strict JSON schema, validation pipeline | 6 | FR08, G20 | `OpenAiCompatibleEditProposalPort` | Real model responses produce validated candidates | actionable locally; external provider = input I-1 |
| P-09 | External SMTP readiness: enforce TLS for external delivery, credential file, timeouts | 7 | FR01 | `SmtpEmailSender` | Config tests; real send = input I-2/I-3 | actionable code / external activation |
| P-10 | Withdraw / report-ready / generation notifications | 7 | order §7.3 | `DeliverableAdapters`, `PlanningServices` | Journey | actionable |
| P-11 | Real RAG corpus activation | 8 | FR12 | protected | — | **BLOCKED_PROTECTED_SCOPE** (STEP 8B), input I-4 |
| P-12 | Python dependency audit (pip-audit), PDF-QA test enabled in the unit run | 9/10 | order §10.2, §9.8 | tooling | Audit output | actionable |
| P-13 | Intermittent `RequestBodyLimitTests` race investigation | 9 | order §9.6 | M4 test | Root cause + fix or documented | actionable |
| P-14 | Pass the specific AI rejection reason through | 9 | order §9.6 | V3 module (Captain side only) | Journey | actionable |
| P-15 | Final OpenAPI export, inventory, docs, regression | 9/10 | §7, §11 | docs | — | actionable |

Excluded: cost, BOQ, 3D, payments.

## Outcomes at closure (2026-11-25)

| ID | Outcome | Commit(s) | Verdict |
|---|---|---|---|
| P-01 | Compose v5.7.0; images built; containerized journeys 132/132 and 110/111; restart persistence | `a168cf1` | COMPLETE_AND_VERIFIED (local) |
| P-02 | `captain-frame-v1`: SLD99 auto-selection, anchor, provenance, digest parity, persisted frame | `2745372` | COMPLETE_AND_VERIFIED |
| P-03 | UUIDv5 edge identity (sorted endpoints) + split lineage | `2745372` | COMPLETE_AND_VERIFIED (roles UNASSIGNED) |
| P-04 | Doors/windows/circulation/access tree; `PHYSICAL_ACCESS_NOT_EVALUATED` removed from generated plans | `834b2ff`, `27e327d` | COMPLETE_AND_VERIFIED |
| P-05 | Opening validation; severity policy for edited plans | `834b2ff`, `27e327d` | COMPLETE_AND_VERIFIED |
| P-06 | API → python mTLS, 12/12 negatives, rotation | `2e1f167` | COMPLETE_AND_VERIFIED (RAG link protected) |
| P-07 | RH09 isolated comparison + server containment | `91c8c06` | BLOCKED_BY_EXTERNAL_DEPENDENCY |
| P-08 | Real LLM via llama.cpp + Qwen2.5-1.5B; 10/10 | `ba2c2be` | COMPLETE_AND_VERIFIED (local); external I-1 |
| P-09 | External SMTP requires STARTTLS; policy tests 7/7 | `add53f8` | IMPLEMENTED_AWAITING_EXTERNAL_ACTIVATION |
| P-10 | REPORT_READY + ARCHITECT_REQUEST_WITHDRAWN (transactional) | `add53f8` | COMPLETE_AND_VERIFIED. Generation-completion is intentionally not notified (synchronous response). |
| P-11 | Root cause diagnosed read-only; exact change request | `afb5dc2` | BLOCKED_BY_EXPLICIT_PROTECTED_SCOPE |
| P-12 | pip-audit 0/0; NuGet audit clean; PDF QA executed (Reports.Tests 164, 0 skipped) | — | COMPLETE_AND_VERIFIED |
| P-13 | Root cause demonstrated (early 413 + close vs in-flight upload); deterministic test | `8213f69` | COMPLETE_AND_VERIFIED |
| P-14 | Specific rejection reasons via a Captain decorator (M3 untouched) | `8070e9c` | COMPLETE_AND_VERIFIED |
| P-15 | OpenAPI re-exported (127 ops), TypeScript declarations (strict tsc), inventory, final docs, regression | `a69086a` + final docs commit | COMPLETE_AND_VERIFIED |

**Discovered during closure and fixed:**
- an edit-validation regression after the CRS change (`27e327d`);
- the MySQL rejection of `--idempotent` migration scripts (container init uses the full script).

**Discovered and documented:**
- H01 timing sensitivity;
- connection exhaustion when the persistence suite shares MySQL with a running stack (operational guidance: run persistence tests on a quiet server).
