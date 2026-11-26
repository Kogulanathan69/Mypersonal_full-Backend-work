# Remaining development register (end of the extended execution)

The classification follows section 30 of the order.

| Feature | Current implementation | Missing | Class | Required decision / dependency | Next action |
|---|---|---|---|---|---|
| Real email delivery | SMTP adapter, Mailpit verified | Approved provider and sender | External provider required | Provider, domain, credentials | Configure `CaptainIntegration:Email:*` + `AllowExternalDelivery` after approval |
| Email verification enforcement | Recorded, not enforced | Login gate | Business policy approval required | Whether unverified accounts may log in or act | One configurable check in the login path |
| LLM plan editing | Rule-based local adapter (not AI) | LLM adapter, budgets, privacy controls | External provider required | Provider, data processing, cost limits (G11/G20) | Implement a second `IAiEditProposalPort` behind the same switch |
| Specific AI rejection reasons | M3 returns `AI_PROPOSAL_INVALID` | Pass-through of the proposal diagnostic | Code implemented, owner decision (M3) | M3 owner | Extend the M3 orchestration result |
| Regulation corpus activation | Fail closed | Real retrieval and reviewed constraints | Protected scope authorization required | STEP 8B / M7, G06, EDGE-ROLE | Approved manifest for the target OS |
| Reviewed-constraint admin | Module scope exists | Applier | Authoritative data required | Constraint contract (doc 29) ratification | Add an applier once ratified |
| Generation openings/circulation/diversity scoring | D02/D04 rectangles, 3 distinct alternatives | Doors/windows, access evaluation, quality ranking | Code still missing | M6 geometry contract (G07) | M6 extension |
| JC-01 CRS / edge identity | Local metric frame | Source CRS preservation, projection, edge UUIDs | Code still missing (unratified shared contract) | JC-01 ratification, CRS policy | Local versioned schema after the decision |
| Project archive/transfer/delete | Derived lifecycle only | Writers | Business policy approval required | Retention, ownership transfer policy | M1 lifecycle writer |
| Report retention/regeneration/sharing history | Reports, list, download | Retention jobs, access history | Business policy approval required | G12 | Configurable cleanup (disabled by default) |
| Architect company accounts | Individual profiles | Company model | Business policy approval required | G09 | — |
| Withdrawal / report-ready notifications | Request-received, response, feedback, survey-processed | Withdraw and report-ready hooks | Code still missing (small) | — | Add `Notify` calls in `WithdrawAsync` / `PublishAsync` |
| Internal mTLS | Token auth only | mTLS, cert rotation | Code still missing + hosting decision | G11 | Design with the hosting choice |
| RH09 | Unresolved | Provider fix or containment proof | Integration implemented but not verified (blocker) | M4 / G03 | Isolated diagnostic (RH09 report) |
| Containers / CI | Definitions written | Build, validate, run CI | Integration implemented but not verified | Push authorization | Build locally when resources allow; CI after push approval |
| OCR accuracy | Pipeline + sandbox | Measured accuracy (eng/sin/tam) | Authoritative data required | Authorised sample surveys + ground truth | Evaluation suite |
| Independent review | Package prepared | Review | Independent review required | G19 reviewer | — |
| Cost, BOQ, 3D, payments | — | — | **EXCLUDED** by the Captain scope correction | — | None |
