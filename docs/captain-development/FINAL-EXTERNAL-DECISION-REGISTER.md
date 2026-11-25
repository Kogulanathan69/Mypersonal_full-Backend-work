# Consolidated critical input request (operator / Captain)

Development continues on everything else while these are open. **Never paste secrets into chat or source files.** Each value goes into a file under `~/bm-captain/secrets/` (mode 0600), which the scripts read.

| # | Input | Why / what depends on it | Proceeds without it | Secure mechanism | Acceptance once supplied |
|---|---|---|---|---|---|
| I-1 | External LLM provider choice + authorization to send **minimised, non-identifying plan geometry** (no survey files, names or addresses) to it | Real external-model editing quality | OpenAI-compatible adapter, strict schema, validation pipeline; verified against a **local** model (llama.cpp) | `CaptainIntegration:AiEdit:Provider=openai-compatible`, `BaseUrl`, `Model`, `ApiKeyFile=~/bm-captain/secrets/llm.key` | Same journey checks against the provider |
| I-2 | Email provider (SMTP host/port, STARTTLS/TLS, sender address, credentials) | Real external delivery of verification, recovery, invitations and notifications | All code paths; Mailpit local capture | `CaptainIntegration:Email:*` env, `PasswordFile=~/bm-captain/secrets/smtp.password`, `AllowExternalDelivery=true` | Recovery + verification round trip to I-3 |
| I-3 | One authorized test recipient mailbox | Proof of receipt | — | operator-provided address | Code received and used successfully |
| I-4 | STEP 8B decision + approved corpus manifest valid on the target OS (or an approved Windows execution path, G17) + reviewed regulation data | Real retrieval, reviewed constraints, regulatory generation | Fail-closed paths; Captain-side evidence binding | M7-approved manifest | `RagLiveSmokeTests`, `RagConstraintAcceptanceTests`, journey phase 5 real-retrieval checks |
| I-5 | Authorized representative survey documents (English/Sinhala/Tamil) with ground truth | OCR accuracy measurement | Sandbox, pipeline, synthetic tests | private directory outside the repository | Accuracy report per language |
| I-6 | Ratification of the Captain role model, lifecycle stage names, coordinate-frame contract (JC-01 local), opening generation preferences | Shared-contract adoption | Implemented locally, versioned | review | Owner sign-off |
| I-7 | Production activation, independent review (G19), MySQL provider licence review (G03) | Production | — | — | Separate gates |
