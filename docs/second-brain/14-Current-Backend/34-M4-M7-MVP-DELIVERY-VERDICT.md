# 34 — M4/M7 MVP delivery verdict (2026-11-19)

**Scope:** M4 (backend infrastructure, shared transport, CI) and M7 (regulation retrieval, structured regulatory constraints, QA) on `develop` @ `2b277f9`.
**Verdict:** **DELIVERED for the MVP development scope. NOT production-ready.** Every M4/M7-owned, unblocked item is implemented and verified with synthetic data. Production use is blocked by the items in §4, which other owners or the lead must close. This verdict is an engineering self-assessment, **not independent QA** (G19: PRs #30–#45 have 0 reviews).

## 1. Merge integrity
| PR | Merge | Verification |
|---|---|---|
| #40 constraint integrity | `542a137` | Tree byte-identical to head `3fa4c9c` |
| #41 cross-member integration | `0c61f49` | PR patch ID equals the merged change (#42 merged first; no shared files) |
| #44 Shapely pin and skip guard | `9597ddb` | Lead-approved dependency |
| #45 final acceptance | `2b277f9` | PR net patch ID `2a237c35…` equals the merged change (#43 merged first; no shared files) |

## 2. Verification evidence
**Post-merge CI #99 (`2b277f9`): every job succeeded** (step metadata).
- **database-compatibility:** per-run credentials; synthetic acceptance service start and stop; MySQL and live tests, where AC01–AC06 run; full-stack smoke; credential scan.
- **python-tests:** including the mandatory-test skip guard and the rag-service tests.
- **dotnet-build:** C# tests.
- **Artifacts published:** `dotnet-test-results`, `mysql-compatibility-results`, `python-test-results`. They were **not downloaded**, since GitHub sign-in is required. Hosted per-test counts can be read from them or from the job logs; the guard prints the python-services counts.

**Local regression on `2b277f9` (zero unexpected skips):**

| Suite | Passed | Failed | Skipped |
|---|---|---|---|
| python-services | 717 | 0 | 0 |
| of which M6 D02 geometry (area / feasibility / footprint / parcel) | 89 (11 / 30 / 19 / 29) | 0 | 0 |
| rag-service (synthetic corpus) | 238 | 0 | 0 |
| rag-service (approved real corpus, read-only) | 7 | 0 | 0 |
| Live hardening (auth, strict JSON, limits, 4 start-up refusals) | pass | — | — |
| Live acceptance (6 scenario digests from wire bytes, plus a no-token 401) | 7 | 0 | 0 |
| Live C10 on the real engine (16 concurrent at capacity 1) | 9×200, 7×503 `SERVICE_BUSY`; healthy afterwards | 0 | — |

Also:
- C# solution and MySQL test project build with 0 warnings and 0 errors;
- no vulnerable NuGet packages, and `pip check` is clean;
- the PDF-processor workspace is unchanged;
- the token never appears in server logs.

C# tests run only in CI, because Smart App Control blocks local execution.

**Cross-service acceptance (CI, synthetic evidence only):**

| Test | What it proves |
|---|---|
| AC01 ×6 | Digests are equal across Python and C# on live bytes. MISSING, CONFLICTING and UNVERIFIED evidence cannot pass M6's validator as FEASIBLE; only REVIEWED is accepted |
| AC02 | Synthetic evidence is rejected without the isolated opt-in |
| AC03 | A missing or wrong token gets 401 |
| AC04 | An Int64 revision is bound end to end |
| AC05 | The current and next tokens both work; a retired token does not |
| AC06 | Caller cancellation is not a dependency failure, and the service stays usable |

## 3. Delivered (M4/M7)
- **Regulation retrieval:**
  - private retrieval service, evidence contract v1;
  - verbatim engine copy with provenance hashes;
  - readiness check.
- **Structured regulatory constraints (`constraints-0`):**
  - sealed and pinned tables, and a reviewer registry with real **authorization** checks;
  - citations reconstructed from the hash-verified corpus;
  - overlap conflicts with no assumed precedence;
  - MISSING, UNVERIFIED and CONFLICTING are never treated as "no limit";
  - synthetic data isolated in Python and C#.
- **Integrity:**
  - BM-JCS-1 canonical digests with shared Python/C# vectors and independent C# recomputation;
  - exact revision binding with Int64 bounds.
- **Transport and security:**
  - strict raw JSON both ways (duplicate keys, exact case, strict numbers, no reflection of input);
  - internal token auth with rotation; the opt-out requires two settings;
  - https or loopback only in Production;
  - workload bounds (C10).
- **Gate:** `ConstraintEvidenceGate` maps evidence to M6's statuses, and M6 keeps feasibility authority.
- **CI:**
  - live Python, RAG and acceptance services with per-run masked tokens;
  - full-stack smoke;
  - distinct-port regression test;
  - mandatory-test skip guard;
  - credential scan.

## 4. Outstanding approvals and dependencies by member
| Owner | Item | Blocks |
|---|---|---|
| **M1** | `feature/m1-day02-day06-checkpoint` (`8b9380b`, 49 files, **no PR**). It adds the EF migration `AddProjectAuthorizationVersion` and model-snapshot changes (**M4 single-writer review for DbContext and migrations is required**) and two new packages, `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.15` and `System.IdentityModel.Tokens.Jwt 8.23.0` (**dependency approval and vulnerability review required**). It merges cleanly today. End-user authentication (A1/A2) and CORS (C6) depend on it | End-user auth, project ownership |
| **M2** | Confirmed metric parcel and edge-identity handoff (M6 D02) | Real parcels for D02/D03 |
| **M3** | Bind the verified regulatory `evidence_pack_id` into F02 validation evidence and fingerprints (`ValidationEvidenceId`) once M6 carries it | C08 end-to-end traceability |
| **M5** | Room-slot and required-area policy handoff (M6 D02); condition values (floors, building height) from requirements | D02 area, condition evaluation |
| **M6 + lead** | Doc 29 §11: (1) reviewer UUID vs pseudonymous string, (2) M6 owns the kind-to-edge mapping, (3) `regulatoryEvidencePackId` in feasibility results and jobs, (4) mandatory kinds. Then the M6 runtime consumer of `IRegulatoryConstraintSource` and `ConstraintEvidenceGate` | Cross-service activation |
| **python-services maintainer** | Inbound internal authentication; whether to constrain `numpy` (Shapely transitive, unpinned) | C09, reproducibility |
| **Lead / G06** | Real reviewer registry custody and appointments; real reviewed tables; clause identity, legal currency, Gazette/council precedence | Any real regulatory value |
| **Lead / G11** | Hosting, TLS, Staging plaintext policy, process-level limits (uvicorn connection cap, container CPU and memory) | Deployment |
| **Lead / G19** | Independent review of PRs #30–#45 | Release |
| **Lead / M4** | **R-1/R-3** (MySQL provider TLS race; Pomelo supports EF Core 9 only). Mitigated by the open gate, still open | Production database |

## 5. Explicitly not claimed
- No real regulations or reviewers.
- No legal or compliance certification.
- No production deployment.
- No independent QA.
- No hosted per-test counts read from CI artifacts.

## 6. Public regulation retrieval module (isolated; 2026-11-21)

`POST /api/v1/regulations/retrieve` is implemented as `RegulationRetrievalEndpointModule`
(`src/BuildMind.API/Endpoints/Regulations/`) but is **not registered** by the production composition root. Only
`AddIsolatedRegulationRetrievalFeature`, which refuses any environment except Testing, adds it.

| Aspect | Behaviour |
|---|---|
| Access | `RequireAuthorization()`; expensive operation (S4-B: 2 concurrent per user, 8 overall) |
| Request | `query` (1–500 after trim), optional `jurisdiction` (1–200), `documentIds` (1–3 distinct, 64 lowercase hex), `maxResults` (1–10, default 5). Unknown members and wrong JSON types → 400 `VALIDATION_FAILED`; non-JSON → 415. The rules match `RagServiceClient` exactly |
| Response | `contractVersion`, `status` (`EVIDENCE_FOUND`, `NO_MATCHING_EVIDENCE`, `INSUFFICIENT_JURISDICTION_EVIDENCE`, `INSUFFICIENT_SOURCE_EVIDENCE`, `AMBIGUOUS_EVIDENCE`), `passages` (document id, title, jurisdiction, 1-based PDF page, char span, excerpt, `lexicalScore`, evidence status, warnings), `warnings`, `legalStatusVerified: false`, `answerGenerated: false`, `disclaimer`; `Cache-Control: no-store` |
| Failures | rag-service failures → 503 `DEPENDENCY_UNAVAILABLE` / 504 `DEPENDENCY_TIMEOUT` (global handler); caller abort cancels the internal call |
| Privacy | The query and the caller identity are neither logged nor echoed in errors |
| Not included | Project scoping (`projectId`) is deferred until M1/M3 ownership checks exist; no generated answers, legal-currency claims or compliance decisions |

**Before public registration:**
1. lead approval of this contract;
2. M1 authentication merged, with `UseAuthentication` → `UseRateLimiter` → `UseAuthorization` in `Program.cs`;
3. one composition-root line in `Program.cs` (M4).

Tests: `RegulationRetrievalEndpointTests`.

**Registration path (2026-11-22, default off):**
- `AddRegulationRetrievalFeature(configuration, authenticationEnabled)` is called by `Program.cs` and registers the module **only** when `Regulations:Retrieval:Enabled=true`.
- When the flag is on, start-up is refused unless end-user authentication and `Services:Rag:Enabled` are both on.
- Authentication is still Development-only, so the endpoint cannot be exposed in any other environment.
- Turning the flag on is the lead's contract decision.
- Tests: `RegulationRetrievalRegistrationTests` (real composition root with M1 authentication):
  - 404 when off;
  - 401 anonymous, 200 signed-in (citations, `legalStatusVerified=false`);
  - 503 on RAG failure;
  - per-user expensive limit 429;
  - refusals without authentication or RAG.
- **Single registration (fail closed):** both registration paths go through one guard. A second registration (same path twice, or isolated + flag path) is refused at start-up rather than silently ignored, because a duplicate module would map the route twice.
- **Production/Staging with the flag on refuse start-up:** with authentication requested, the Development-only sign-in guard refuses; without it, the M7 guard refuses. Both are covered by tests.
