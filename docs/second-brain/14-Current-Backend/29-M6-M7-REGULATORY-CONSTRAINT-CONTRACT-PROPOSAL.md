# M6–M7 regulatory constraint contract: PROPOSAL for team coordination

- **Status:** **PROPOSED, NOT APPROVED, NOT IMPLEMENTED** (2026-11-19).
- **Owners:** M7 (provider), M6 (consumer and authoritative validator). Decision: lead.
- No M6 code exists on any branch. Nothing here may be implemented until **M6, M7 and the lead** approve it.

## 1. Why a separate contract
- **Evidence contract v1** (implemented) returns *passages*: cited text plus an evidence status. It is advisory research evidence and must never decide a plan.
- **M6's plan** (task plan days 3–5) expects "M7 sourced rules / rule constraints / rule provenance" for its **deterministic** feasibility and geometry validation.
- **The gap:** turning a passage into an enforceable number (for example "rear space ≥ 3 m") is a **human-review** step. It can't come from BM25 or from any AI output.

## 2. Principles (non-negotiable)
1. **M6's deterministic validator is authoritative.** M7 never returns pass/fail for a plan.
2. **Only human-reviewed constraints can block.** Unreviewed constraints may only produce warnings.
3. **Every constraint carries full provenance** (document, page, character offsets, excerpt) that reconstructs from the approved corpus.
4. **A missing constraint is never treated as "no limit".** It yields `NEEDS_REVIEW` / `INSUFFICIENT_EVIDENCE`, never "feasible".
5. **Jurisdiction is exact.** There's no inference from parcel location; parcel applicability stays a human or M2 decision.
6. **Legal currency is never claimed:** `legal_status_verified=false` until G06 is resolved.

## 3. Proposed shape (v0, for discussion)
```json
{
  "contract_version": "constraints-0",
  "jurisdiction": "Colombo Municipal Council planning area",
  "constraints": [
    {
      "constraint_id": "sha256(...)",
      "kind": "REAR_SPACE_MIN",
      "comparator": ">=",
      "value": 3.0,
      "unit": "m",
      "conditions": [{"field": "building_height_m", "comparator": "<=", "value": 15.0}],
      "applies_to": "residential",
      "review_status": "HUMAN_REVIEWED",
      "reviewed_by_role": "domain_reviewer",
      "reviewed_at": "2026-11-20",
      "source": {"document_id": "<64 hex>", "page": 358, "start_char": 789, "end_char": 1269, "excerpt": "..."},
      "legal_status_verified": false
    }
  ],
  "coverage": {"requested_kinds": ["REAR_SPACE_MIN", "PLOT_COVERAGE_MAX"], "missing_kinds": ["PLOT_COVERAGE_MAX"]},
  "warnings": ["Legal currency and amendments are not verified."]
}
```

**Value sets:**
- **Kinds:** a closed enum agreed with M6, starting from what M6's validator can actually check: `REAR_SPACE_MIN`, `SIDE_SPACE_MIN`, `FRONT_SETBACK_MIN`, `PLOT_COVERAGE_MAX`, `FLOOR_AREA_RATIO_MAX`, `BUILDING_HEIGHT_MAX`, `PARKING_SPACES_MIN`. New kinds need a contract version change.
- **Review status:** `HUMAN_REVIEWED` (may block), `UNREVIEWED` (warning only), `REJECTED` (never returned).
- **M6 outcome per kind:**
  - reviewed and satisfied → `PASS`;
  - reviewed and violated → `FAIL` (blocking);
  - unreviewed → `WARNING`;
  - missing → `NEEDS_REVIEW`.

  The overall plan is never `FEASIBLE` while any requested kind is `NEEDS_REVIEW`.

## 4. Where the constraints come from (proposed)
- An **offline, version-controlled, reviewed constraint table** maintained by M7 with a domain reviewer. Each row references an approved passage by citation.
- The citation is re-verified against the hashed corpus when the service starts (as the STEP 6 assessor already does).
- Retrieval (contract v1) **helps reviewers find** candidate passages. It doesn't create constraints automatically.

## 5. Interfaces (proposed, not created)
| Side | Interface | Notes |
|---|---|---|
| Python (rag-service) | `POST /internal/v1/regulations/constraints` with `{jurisdiction, kinds[]}` | Read-only; returns only `HUMAN_REVIEWED` and `UNREVIEWED` rows plus `missing_kinds` |
| C# | `IRegulatoryConstraintSource` (Application port, M7) | Consumed by M6 generation and feasibility orchestration |
| M6 Python | The validator receives constraints as **input data** | It never calls M7 directly (single orchestration path through C#) |

## 6. Open questions for the team
1. Which kinds can M6's validator check first (day-3/4 scope)?
2. Who is the domain reviewer, and what review evidence is needed (G06)?
3. How are the "conditions" (height, plot size, zone) supplied: from M2 land data or M5 requirements?
4. Is the Gazette (broad national scope) applicable alongside a council plan, and how are precedence and conflicts handled? Proposal: never merged automatically; returned separately as `NEEDS_REVIEW`.
5. Storage of the reviewed table (repo file vs database), and its versioning.

## 7. Approval checklist
- [ ] M6 agrees the kinds and outcome semantics.
- [ ] M7 agrees the review workflow and provenance.
- [ ] Lead approves the contract version, and where the reviewed table lives.
- [ ] Domain reviewer assigned (G06).

## 8. Coordination message (ready to send to M6; lead to send)
> **To M6, from M7 (cc lead).** The M7 regulation retrieval service is ready for review (PR #32). It returns cited passages and an evidence status only, never pass/fail.
>
> For your feasibility and validation work we propose a separate, reviewed **constraint** contract (doc 29): only human-reviewed constraints can block, a missing constraint means `NEEDS_REVIEW`, and your deterministic validator stays authoritative.
>
> Please reply with:
> 1. the constraint kinds your validator can check first;
> 2. where conditions such as height and plot size come from;
> 3. your preferred delivery path (constraints passed to your validator as input through C#).
>
> Nothing will be implemented until M6, M7 and the lead approve.

## 9. Revised technical submission to M6 (2026-11-19, after the M6 review: 2 PASS / 6 PARTIAL / 4 FAIL)
**Status:** constraints contract **v0 IMPLEMENTED on `feature/m7-regulatory-constraints` with synthetic fixtures only**. It still **needs M6 + lead approval** before any real values are loaded or M6 consumes it. No M6-owned code was changed.

### What M6 asked for → what now exists
| M6 review area | Implementation | Evidence |
|---|---|---|
| Versioned, human-reviewed provider | `rag-service/app/constraints.py`: table with `contract_version` `constraints-0`, `table_version`, `synthetic` flag; strict schema (`extra=forbid`, frozen) | `tests/test_constraints.py` |
| Verified numeric values and units | Closed kind enum; each kind fixes its comparator (`>=` MIN, `<=` MAX) and unit (`m`, `percent`, `ratio`, `count`); finite values in plausible ranges | Load-refusal tests (unit, comparator, range, NaN, unknown kind) |
| Source provenance | Every constraint cites document/page/offsets/excerpt; **the table is refused unless every excerpt reconstructs exactly from the hash-verified corpus** | Synthetic refusals; **real-corpus test: synthetic citations are refused against the real corpus** |
| Applicability | `applies_to` (`residential`/`any`) plus structured `conditions` (field/comparator/value/unit) returned as data; **M6 evaluates them** | Condition-scope test (different conditions ≠ conflict) |
| Missing / conflicting / unverified states | Per kind: `VERIFIED` (only HUMAN_REVIEWED), `UNVERIFIED` (unreviewed only), `CONFLICTING` (reviewed values disagree in the same scope), `MISSING` (never "no limit"); `REJECTED` never returned; overall `COMPLETE` only if all VERIFIED | State tests |
| Never auto-verify retrieved passages | The provider has no dependency on retrieval; there is no automatic review state; a search cannot change a pack | `test_retrieval_never_creates_constraints` |
| Binding to exact revisions | Request **requires** `binding{project_id, project_revision, land_revision_id, requirement_revision_id}`; echoed and covered by a deterministic `evidence_pack_id` (SHA-256 of the canonical pack) | Determinism and revision-sensitivity tests; the C# client rejects a pack bound to any other revision |
| Request validation and payload limits | Strict raw JSON before the models: streamed 16 KB limit (also chunked), `application/json` only, duplicate keys/NaN/trailing commas/depth > 32/non-object root → 400; schema → 422; no input echo | `tests/test_strict_json.py` |
| Deterministic validation authoritative | The contract carries data only; M7 never returns pass/fail for a plan | By design; C# port docs |

### Endpoint
`POST /internal/v1/regulations/constraints`. Internal token required; it returns 503 `CONSTRAINTS_UNAVAILABLE` when no table is configured, or refuses to start when the corpus is missing (provenance cannot be verified). Example: `rag-service/contract/constraints_response.v0.example.json` (generated from the real provider; validated by both Python and C# tests).

### C#
`IRegulatoryConstraintSource` → `RagServiceClient.GetConstraintsAsync`. It validates the pack on receipt (binding, kinds, state consistency, units, comparators, no REJECTED, legal flag) → otherwise `MalformedResponse`.

### Still needs M6 + lead decisions
1. **Kind set and units:** is the first set (7 kinds) right for M6's validator?
2. **Condition fields** (`building_height_m`, `plot_area_m2`, `floors`, `road_width_m`): where M6 gets their actual values (M2 land / M5 requirements).
3. **Real values:** who is the domain reviewer (G06), and where does the reviewed table live? **No real regulatory values are in the repository.**
4. **Gazette vs council precedence:** currently exact-jurisdiction only; overlapping scopes are not merged.

## 10. Revised technical evidence for M6 approval (post-merge, 2026-11-19)
**Merged:** PR #35 → `develop` `bf72343` (squash; tree byte-identical to the reviewed head `c84e0da`; **0 independent reviews**, G19).

| Evidence | Result |
|---|---|
| PR CI run #77 (`c84e0da`) | All jobs success, including the full-stack smoke and live C#→RAG steps (step metadata; artifacts not read) |
| Post-merge CI run #78 (`bf72343`) | All jobs success, same steps (step metadata) |
| Local regression on `develop` | Build 0 warnings; no vulnerable packages; python-services 54/54; rag-service 139/139; real corpus 7/7; engine copy byte-stable (0 CR) |
| **Live loopback service (real corpus, random token): 12/12** | Valid search 200 `EVIDENCE_FOUND`; no token 401; repeated token header 401; duplicate key 400; NaN 400; `text/plain` 415; unknown field 422; chunked 41 KB 413; constraints without table 503 `CONSTRAINTS_UNAVAILABLE`; missing binding 422; revision 0 422; token absent from server log |
| **Fabrication guard (live)** | Starting the service with the synthetic table against the real corpus is **refused** ("does not reconstruct"): no constraint can load unless its citation exists verbatim in an approved, hash-verified source |

**Not claimed:**
- no real regulatory values exist (G06; human review required);
- contract v0 is not approved by M6;
- legal currency is unverified;
- no M6 consumer exists (no M6 branch or code on the remote as of this check).

**Requested from M6:** review §9 and §10 and answer the four open questions in §9 (kinds/units, condition-field sources, domain reviewer/table location, Gazette/council precedence).

## 11. Constraint integrity: final technical evidence for M6 review (2026-11-19)
**Basis:** on 2026-11-19 M6 conditionally approved the provisional `constraints-0` design: the seven kinds, their units, revision binding and the C# orchestration direction. This section implements the conditions. **Status:** branch `feature/m7-constraint-integrity`, synthetic data only, **pending M6 + lead review**. No M6-owned code was changed; M6 contracts and validation are used read-only in tests.

### M4: integrity and transport
| Condition | Implementation | Evidence |
|---|---|---|
| Agreed digest serialization | **BM-JCS-1**: RFC 8785 (JCS), with numbers restricted to finite values, integers below 1e15, and decimals between 1e-4 and 1e15 without exponent. Anything outside that range is **rejected**, never approximated. Implemented as `rag-service/app/canonical_json.py` and `CanonicalJson.cs`. | Shared vectors `rag-service/contract/canonical_json_vectors.v1.json` (9 valid, 5 invalid), run by both suites |
| Independent digest recomputation | The C# client recomputes `evidence_pack_id` from the **received bytes**, excluding that field, and requires `digest_algorithm = SHA-256/BM-JCS-1`. | Unsealed edits are rejected (5 cases); a resealed harmless edit is accepted, which proves the check really recomputes |
| Ranges, kinds, comparators, condition units | Per-kind comparator, unit and range; condition field → unit and range; unsatisfiable conditions and duplicate ids rejected. Each rule is tested on **resealed** packs, so the rule itself (not the digest) causes the rejection. | `StrictContractTests` (40 tampered packs) |
| Synthetic evidence isolation | The client rejects synthetic packs unless `Services:Rag:AllowSyntheticConstraints` is set. That setting fails start-up outside **Development/Testing**, and Python may never set it. The gate treats synthetic evidence as UNVERIFIED unless explicitly allowed. rag-service loads a synthetic table only with `BUILDMIND_RAG_ALLOW_SYNTHETIC_CONSTRAINTS=1`. | C# start-up tests; live start-up refusal |
| Production transport without plaintext exceptions | In **Production**, every enabled internal service must use https or loopback, with or without a token, and `AllowPlaintextPrivateNetwork` itself fails start-up. Staging keeps the explicit, reviewable exception until G11. | `ConstraintIntegrityTests` (6 transport cases + host start-up) |
| Rotation, cancellation, timeout, safe errors | Unchanged and still covered: two-token rotation (Python); timeout → `Timeout`; caller cancellation is not a dependency failure; 503 → `Unavailable` | Existing tests |
| python-services authentication | **Coordination item** for the python-services maintainer (M6 bootstrap owner); not changed here. The C# transport rules already apply to Python. | — |

### M7: review, integrity and applicability
| Condition | Implementation | Evidence |
|---|---|---|
| Reviewer **authorization** (not only id format) | A separate, sealed **reviewer registry** (pseudonymous ids only; no names). Every reviewed or rejected constraint must be signed off by a reviewer who is registered, ACTIVE, holding the stated role, scoped to its jurisdiction **and** kind, and within the authorization period on `reviewed_at`. A revoked reviewer invalidates **all** of their sign-offs, so those constraints need re-review. The table cannot load without the registry, and the pack carries the registry version and digest. | 9 refusal cases, plus registry tampering and invalid registries; live: no-registry and wrong-pin start-ups refused |
| Table integrity and version | The table carries an `integrity` seal over BM-JCS-1. A real table needs **version, table digest and registry digest pins** and may not use synthetic versions or identities. A synthetic table needs `synthetic-` versions and `synthetic:`/`SYN-` identities. | Tamper, pin and namespace tests |
| Overlapping applicability and conflicts | Conditions form intervals per field (open and closed bounds). Two reviewed constraints **conflict when their applicability overlaps** and their requirements differ. The C# client independently rejects a VERIFIED result that hides such an overlap, and a CONFLICTING result without one. | 5 boundary cases (for example `<= 9` and `>= 9` overlap at exactly 9 m) plus a different-fields case |
| No unsupported precedence | No specificity, recency or Gazette/council precedence is ever applied: a conditional rule does **not** override a general one; they are CONFLICTING. **Behaviour change:** the earlier v0 test that treated "general + conditional" as non-conflicting now expects CONFLICTING, by M6's condition. | `test_a_general_rule_and_an_overlapping_conditional_rule_conflict_without_assumed_precedence` |
| Provenance and exact binding | Unchanged: citations must reconstruct from the hash-verified corpus; packs are bound to exact project, land and requirement revisions. | Existing tests; live refusal against the real corpus |

### Missing or conflicting evidence can never produce FEASIBLE
`ConstraintEvidenceGate`, in the Application layer, maps a verified pack and the **mandatory kinds** to M6's `RegulatoryEvidenceStatuses`, with precedence **CONFLICTING > MISSING > UNVERIFIED > REVIEWED**:
- a mandatory kind absent from the pack is MISSING;
- MISSING carries no rule-set version (an M6 rule).

The gate does not decide feasibility. M6's `GenerationContractValidation` remains authoritative and already refuses FEASIBLE unless the evidence status is REVIEWED. For every shared scenario, the tests run pack → client verification → gate → **M6's own validator**:

| Scenario | Rear | Side | Front | M6 status | FEASIBLE allowed? |
|---|---|---|---|---|---|
| north-baseline | VERIFIED | UNVERIFIED | MISSING | MISSING | No: M6 rejects it |
| all-setbacks-verified | VERIFIED | VERIFIED | VERIFIED | REVIEWED | Yes: M6 accepts it (positive control) |
| rear-conflict-overlapping-conditions | CONFLICTING | VERIFIED | VERIFIED | CONFLICTING | No |
| rear-disjoint-conditions | VERIFIED ×2 | VERIFIED | VERIFIED | REVIEWED | Yes (M6 evaluates the conditions) |
| side-unverified-only | VERIFIED | UNVERIFIED | VERIFIED | UNVERIFIED | No |
| unknown-jurisdiction | MISSING | MISSING | MISSING | MISSING | No |

Shared fixtures for M6: `rag-service/contract/constraints_scenarios.v0.json`. They are generated by `rag-service/tools/build_contract_fixtures.py`, and a test fails if any committed fixture drifts from the builder.

### Contract mismatches found against M6's merged contracts (#38): decisions needed
1. **Reviewer identity type:** M6 `ReviewedConstraintDto.reviewedBy` is a GUID; M7 uses a pseudonymous string id (`reviewer_id`). Choose one: a GUID registry id, or string ids on both sides.
2. **Parcel-edge mapping:** M6 consumes setbacks **per parcel edge** (`parcelEdgeIndex`, `distanceM`); M7 provides per-kind front/rear/side values. Which edge is front, rear or side is geometry. **Proposal:** M6 maps the edges; M7 never guesses them.
3. **Evidence binding in M6 results:** `FeasibilityResultDto` has no `evidence_pack_id`, `table_sha256` or `project_revision`. **Proposal:** add `regulatoryEvidencePackId` so a feasibility result is traceable to the exact verified pack.
4. **Mandatory kinds per check:** confirm that M6's initial feasibility treats FRONT/REAR/SIDE as mandatory.

**Not claimed:**
- no real regulatory values or real reviewers exist (G06);
- legal currency is unverified;
- C# tests compile locally but run only in CI (Smart App Control);
- no cross-service activation until M6 agrees items 1–4.

## 12. Cross-member integration status after PR #40 (2026-11-19)
**Merged:** PR #40 → `develop` `542a137`. The squash tree is byte-identical to the reviewed head `3fa4c9c`, with **0 independent reviews** (G19).
- **PR CI #87:** every job succeeded. This was the first execution of the C# integrity tests: digest recomputation, resealed tampering, gate → M6 validator, Production transport.
- **Post-merge CI #78 → #88:** every job succeeded, including the full-stack smoke (step metadata; artifacts not read).

**Branch:** `feature/m7-cross-member-integration` closes the M4/M7-owned items still open in M6's H4-R1 review (`docs/proposals/m6-ai-generation/BE-M6-D01-FINAL-DEVELOPMENT-REPORT.md`).

| H4-R1 | Open item named by M6 | Status now | Evidence |
|---|---|---|---|
| C08 | C# does not recompute the digest | **Closed in #40**: independent BM-JCS-1 recomputation | `StrictContractTests`; CI #87 |
| C08 | Int32 project revision vs M1's Int64 | **Closed here**: `ConstraintBinding.ProjectRevision` is `long`, bounded on both sides to 1…999,999,999,999,999 (the exact BM-JCS-1 integer domain) | Python 422 above the bound; C# `ArgumentException`; live 3,000,000,000 bound and digest-verified (AC04) |
| C05 | Conflicts only in equal scopes | **Closed in #40**: overlapping-interval conflicts, no precedence | Boundary tests |
| C11 | Web serializer defaults (case-insensitive names, numbers from strings) | **Closed here**: exact-case member names, `JsonNumberHandling.Strict` on every internal response | Lenient-typing and health tests |
| C11 | Field-to-unit rules and ranges on both sides | **Closed in #40** | Both suites |
| C09 | RAG formatter reflects submitted property names | **Closed here**: fixed 422 body `{"code":"VALIDATION_FAILED"}` | Python no-reflection test; live 422 |
| C09 | One environment setting can disable RAG auth | **Closed here**: `BUILDMIND_RAG_AUTH=disabled` also requires `BUILDMIND_RAG_ENVIRONMENT=development`, otherwise start-up fails | 5 refusal cases |
| C09 | Plaintext exception | **Closed for Production in #40**; Staging exception remains until G11 | Start-up tests |
| C09 | python-services authentication | **Open**: coordination with the python-services maintainer (M6 bootstrap owner) | — |
| C10 | Server-side bounded work | **Open**: client deadline, bulkhead and streamed 16 KB limit are verified; server CPU/time bounds per request are not | — |
| C12 | Reviewed-evidence M6 end-to-end coverage | **Partly closed here (synthetic):** live cross-service acceptance in CI. Real rules, persisted jobs and M6's runtime consumer remain open | AC01–AC04 |

### Cross-service acceptance (live, synthetic only)
`rag-service/tests/acceptance_app.py` is a **test-only** service, never deployed: the real rag-service FastAPI app, with real auth and strict parsing, mounted once per shared scenario. It refuses to start without `BUILDMIND_ACCEPTANCE_SYNTHETIC=1` and a token, and it cannot run with auth disabled. CI starts it on loopback with its own random, masked token, which is added to the credential scan. `RagConstraintAcceptanceTests` then sends each scenario's pack:

real Python HTTP bytes → production `RagServiceClient` (BM-JCS-1 recomputed in C#) → `ConstraintEvidenceGate` → **M6 `GenerationContractValidation`**.

| Test | Proves |
|---|---|
| AC01 ×6 | Cross-language digest equality on live bytes. MISSING, CONFLICTING and UNVERIFIED evidence is rejected by M6's validator for FEASIBLE; only REVIEWED is accepted |
| AC02 | Live synthetic evidence is rejected without the isolated opt-in; the gate never counts it as reviewed by default |
| AC03 | Missing or wrong token → 401; no evidence is returned |
| AC04 | An Int64 revision beyond Int32 is bound and digest-verified end to end |

**Unauthorized reviewers** cannot reach this path at all: a table signed by an unregistered, revoked, out-of-scope or out-of-period reviewer fails at load, so no pack is ever served (#40; live start-up refusals).

Local live run: 6/6 digests recomputed from wire bytes; no-token 401; token absent from the server log. **C# acceptance runs only in CI** (Smart App Control).

### Remaining dependencies (owner → item)
| Owner | Item | Blocks |
|---|---|---|
| **M6 + lead** | §11 items 1–4: reviewer id type (GUID vs pseudonymous string), M6 owns the parcel-edge mapping, `regulatoryEvidencePackId` in feasibility results and jobs, mandatory kinds | The M6 runtime adapter (pack → `RegulatoryEvidenceDto`) |
| **M6** | Runtime consumer that calls `IRegulatoryConstraintSource` + `ConstraintEvidenceGate` and keeps `NEEDS_REVIEW` for incomplete evidence | Cross-service activation |
| **M2 / M5 + M6** | Parcel facts, edge references and condition values (height, plot area, floors, road width) from the confirmed land and requirement revisions | Condition evaluation |
| **M4 + M6** | Propagate the evidence pack id through job identity and atomic publication | Traceable feasibility results |
| **Lead / G06** | Real reviewer registry custody, real reviewed tables, clause identity, Gazette/council precedence | Any real regulatory value |
| **python-services maintainer** | Inbound authentication rollout | C09 |
| **Lead / G11** | Hosting and TLS; Staging plaintext exception | Deployment |
| **Lead / G19** | Independent reviews of PRs #30–#40 | Release |

## 13. PR #41 CI corrections and server workload bounds (2026-11-19)
CI on candidate `58d793a` (merge `2b70062`), as reported by the lead from the actual logs: 2,338 tests, **1 failure**. MySQL/live integration was 75/75, cross-service acceptance 10/10, python-services 628/628 and rag-service 224/224. The full-stack smoke also failed.

| Defect | Root cause | Correction | Regression coverage |
|---|---|---|---|
| `PythonIntegrationApiTests` line 96 | `Assert.DoesNotContain("8000", RawBody)` matched a random trace id that happened to contain the digits 8000 | **Structured** leakage check: the traceId must match its format (32 lowercase hex, or the ASP.NET Core connection id) and is excluded from substring checks. Every other property name, string and number must not contain the host, `8000`, `://` or `Exception`. The raw body is still checked for the host, `://` and `Exception`, none of which can occur in a valid trace id | 3 trace ids containing 8000 accepted; 8 genuine leaks still detected (host, `ip:8000`, URL, exception name, numeric port, port as property name, leak disguised as traceId, malformed traceId) |
| Full-stack smoke | The acceptance service and the smoke API both bound `127.0.0.1:8097` | Acceptance service moved to **8094** (all references). The acceptance step and the smoke script now refuse to start if their port already answers | `CiWorkflowPortTests`: every loopback listener in the workflow has a distinct port, and every loopback URL points at a declared listener |

**C10, server-side work (closed for rag-service):** `app/workload.py`. Engine work (search and constraint lookup) runs on a dedicated pool bounded to `BUILDMIND_RAG_MAX_CONCURRENT_WORK` (1–64, default 4), with a per-request budget of `BUILDMIND_RAG_WORK_TIMEOUT_SECONDS` (1–60, default 10).
- **Over budget:** 504 `WORK_TIMEOUT`.
- **No free slot:** an immediate 503 `SERVICE_BUSY`, with no unbounded queue.
- **Slot release:** a slot is released only when its thread has really finished, so overrunning work can never pile up behind new requests.
- **C# mapping:** 503 → `Unavailable`, 504 → `Timeout`, which is the existing safe mapping.
- **Tests:** 11, covering timeout, busy refusal, recovery, constraint lookups, normal work and configuration bounds.
- **Out of scope:** process-level limits (uvicorn `--limit-concurrency`, container CPU and memory) remain deployment items (G11).

## 14. Final cross-member acceptance evidence (2026-11-19, after PRs #41 and #42)
**Merges:**
- **PR #42 (M6 D02, footprint and feasibility)** → `9bbed47`.
- **PR #41 (M4/M7)** → `0c61f49`. Its tree differs from the head `2beacad` only because #42 merged first. The PR's patch ID equals the merged change (`88d90d67…`), so #41 landed byte-identically, and no file was touched by both PRs. **0 independent reviews** on either PR (G19).
- **CI:** #90 (PR #42), #92 (PR #41 head) and **post-merge #93**: all jobs succeeded, including the acceptance-service start and stop, C# tests, rag-service tests and the full-stack smoke (step metadata).

### M4/M7 items closed on `feature/m7-final-acceptance`
| Condition (M6 D02 reassessment) | Item | Correction | Evidence |
|---|---|---|---|
| C11 | "An isolated synthetic registry with a non-prefixed version was accepted by Python, while the C# receipt policy requires the `synthetic-` prefix" | Python now refuses any registry whose version prefix disagrees with its `synthetic` flag, in both directions | M6's exact case refused; parity test: **every pack Python can serve** (all 6 scenarios plus an in-memory real-shaped table and registry) satisfies the C# receipt prefix rules for table version, registry version, reviewer ids and references |
| C09/C10 | Credential operations unverified | Live rotation window in CI: the acceptance service runs with a **current and a next** random token. Both are masked and both are in the credential scan | AC05: both tokens return the same verified pack; a retired token gets 401 |
| C10 | Production resource and cancellation behaviour | Live caller cancellation through the production client, and a **real-engine** overload run | AC06: cancellation is not a dependency failure and the next call succeeds. Local real corpus at capacity 1 with 16 concurrent searches: 11×200 and 5×503 `SERVICE_BUSY`, no other status, no unhandled error, healthy afterwards, token absent from the log |
| C08 | Int32 vs Int64 revision | **Closed in #41** (M6 D02 assessed #40) | AC04 + bounds tests |

### Acceptance status (M4/M7 scope)
| Condition | M4/M7 status | Remaining owner and item |
|---|---|---|
| C01, C05, C06, C07 | Met (synthetic scope) | M6 + lead: final mandatory kinds |
| C02 | Met for provenance mechanics | Lead/G06: clause identity, legal currency, domain sign-off |
| C03 | Met for overlap without precedence | M2/M5/M6: parcel facts, edge assignment; lead: authority precedence |
| C04 | Met for authorization mechanics | Lead/G06: real reviewer appointments, table custody, real values |
| C08 | Met for C# binding, digest and Int64 | **M6 + M4:** propagate the verified `evidence_pack_id` and revision through D02 results and jobs (needs §11 item 3) |
| C09 | Met for client and RAG controls | python-services maintainer: inbound auth; lead/G11: hosting, TLS, Staging policy |
| C10 | Met for client deadline, server bounds, rotation and cancellation | G11: process-level limits (uvicorn connection cap, container CPU and memory) |
| C11 | Met for canonical, scenario and prefix parity | **M6 + lead:** reviewer UUID vs string; kind-to-edge mapping (§11 items 1–2) |
| C12 | Synthetic live end to end met (AC01–AC06) | Real rules, approved corpus in an approved environment, persisted jobs |

### New cross-member finding: M6 D02 tests are skipped without `shapely`
`python-services/tests/m6_contract/test_d02_{feasibility,footprint,parcel}.py` use `importorskip("shapely")`. `shapely` is in **no** python-services requirements file, which is what CI installs. Locally the three modules are reported as SKIPPED (639 passed, 3 skipped), and CI most likely skips them too, while the job shows green. M6's D02 report states that "Shapely 2.1.2 was already available in the approved local environment".

**Decision needed (dependency change, owned by the python-services maintainer and M6):** either pin `shapely` in the python-services requirements, so the D02 engine is tested in CI and available at runtime, or make the D02 tests fail rather than skip in CI. Not changed here: dependency changes require approval.
