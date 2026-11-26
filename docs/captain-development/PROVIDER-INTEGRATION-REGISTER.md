# Provider integration register

| Provider | Boundary | Local substitute used for tests | Real activation | Status |
|---|---|---|---|---|
| Email (recovery, verification, invitations, notifications) | `ISystemEmailSender` / `IPasswordResetEmailSender` → `SmtpEmailSender` (SMTP, optional STARTTLS, credentials from a file) | Mailpit `axllent/mailpit:v1.21.8` on 127.0.0.1 (local capture; nothing relayed) | Needs an approved provider, sender domain/policy, credentials. `AllowExternalDelivery=true` is required for non-loopback hosts. | Local delivery **verified live** |
| AI plan editing | `IAiEditProposalPort` | `RuleBasedAiEditProposalPort`: a deterministic local grammar, **not an AI model**, labelled `RULE_BASED_LOCAL` | An LLM adapter needs an approved provider, data-processing approval (no private project data leaves without it), budget and limits (G11/G20). **Not implemented.** | Rule-based path verified live; default config remains 503 `AI_PROVIDER_NOT_CONFIGURED` |
| OCR | Local Tesseract + pdftoppm (sandboxed) | same (local) | No external OCR provider (forbidden for private documents) | Local only |
| Regulation RAG | Existing M7 `RagServiceClient` (unchanged, STEP 8B ON HOLD) | rag-service without a corpus (fail closed) | Approved corpus loading in a compatible environment (RAG-CORPUS-WSL) | Fail-closed paths verified only |
| Architect delivery | Outbox → email notification only; documents never attached | Mailpit | Needs an approved channel, recipient authority and retention (G12) | Notification delivery verified locally |
| Object storage | Private local directories (surveys, reports) | same | Production storage, backup and retention policy | Local |
| Payments, cost/BOQ, 3D | — | — | — | **EXCLUDED** (scope correction) |
