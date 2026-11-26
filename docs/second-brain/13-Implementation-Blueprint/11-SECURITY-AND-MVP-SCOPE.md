> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Security and business invariants

- Main API authenticates every project access; Python private internal endpoints do not trust browser sessions.
- Uploaded surveys remain confidential; upload size/type/content checks and safe object-storage keys; no private PDFs or credentials in Git.
- User edits: geometry fully validated and rejected atomically on invalid results; accept creates immutable revision; edited revision requires new homeowner confirmation.
- Three **distinct validated** 2D alternatives when feasible, otherwise honest infeasibility response; manual editor supported.
- Regulatory RAG always returns sources and versions, authority/jurisdiction when known, and `NOT_VERIFIED` for uncertain applicability; retrieval similarity is not compliance evidence.
- Vastu backend-only optional heuristic, not a customer question or edit blocker.
- Staff proposals in every assigned admin module become live only after Main Admin approval.
- No official government approval claim, no 3D, no cost estimate. Payments from older notes are NOT assumed part of current seven-day minimum target without a new approval.
- Report links/architect sharing require explicit recipient, documents, customer consent and access checks; no automatic public sharing.
- Protect secrets in environment/secret manager, not source; review CORS, service auth, JWT/cookie security and role authorization.
