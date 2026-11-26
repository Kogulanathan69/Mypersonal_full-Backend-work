> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Change control, two-person file conflicts, handoff

## If two members need same file
1. Stop competing edits, identify primary writer in [[03-FILE-OWNERSHIP-REGISTER]].
2. Consumer opens issue with exact path, request, changed interface, tests, deadline.
3. Primary owner makes shared-file patch; consumer works in own folder or contract mock.
4. Owner PR merges; consumer fetches and updates adapter; M7 records handoff and evidence.
5. If owner unavailable, M7 proposes **one temporary exclusive writer**, technical owner/alternate approves, records start/end and freeze paths; no simultaneous edits.

### Specific conflict resolution
- **M4 vs M5** on `Program.cs`, `DbContext.cs`, migrations, `DependencyInjection.cs`: M4 writes; M5 requests integration and writes feature-local classes.
- **M6 vs M7** on FastAPI `main.py` and config: M6 writes; M7 writes `services/rag/**` and requests route registration.
- **M6 vs M5** canonical geometry schema: M6 writes schema, M5 writes C# consumer, contract approval required before breaking change.
- **M1 vs M2/M3** global Angular routes/theme: M1 merges central files; others edit own feature directories.

### Example: M5 needs a new DI registration
M5 writes `PythonPlanComputeClient.cs`; creates BM-### ticket specifying registration and tests; M4 alone modifies `DependencyInjection.cs`; PR reviewed by M5. M7 captures merge SHA.

### Example: M7 adds RAG route
M7 writes `api/rag_routes.py`, `schemas/rag.py`, `services/rag/**`, tests; M6 registers router inside `main.py` (no M7 edit there); M5 writes C# client after schema agreed.

### Temporary owner handoff record
```
Issue: BM-###; exact path: ...
Original owner: M4; temporary exclusive writer: M5
Approved by: M4 / lead; start: ...; expiry: ...
Competing edits frozen: ...; PR: ...; tests: ...; ownership returned: ...
```
