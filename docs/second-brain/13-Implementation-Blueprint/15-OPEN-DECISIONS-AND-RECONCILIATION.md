> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Older documentation vs current working blueprint

The original folder 00–12 files remain unchanged except new navigation links in three entry pages. They are historical source snapshots; some have **MSSQL confirmed**, **FastAPI recommended**, **payment-gated download** and other earlier choices.

| Topic | Old BE vault | Current proposed direction |
|---|---|---|
| Database | MSSQL | MySQL 8.x, verify real repository and approve ADR |
| Python | recommended / not approved | FastAPI compute services + RAG, verify existing repo |
| C# structure | modular API proposed | Clean Architecture: Domain, Application, Infrastructure, API |
| Payment | subscription gated export previously proposed | Do not assume in 7-day MVP without new product decision |
| 3D/cost | out of scope | remains out of scope |
| Vastu | not consistently described | backend heuristic only, hidden from UI, nonblocking |
| Staff approval | limited detail | all staff-proposed writes await Main Admin approval |
| confirmation | initial final approval | current validated immutable revision + explicit homeowner action |

**Unresolved approval:** mono-repo/multi-repo, SDK/dependency versions, exact database schema, geometry schema, source/jurisdiction expert validation, signed contracts, queues, storage/vector DB, production hardening and actual GitHub usernames. Compare with actual repos before calling source tree verified. Seven-day schedule is aggressive demo goal, not guaranteed production completion.
