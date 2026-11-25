# RH09 resolution report: status **UNRESOLVED (DEFERRED)**

## Defect (previously reproduced, see the M4 RH-09 diagnostic workflow and earlier Captain evidence)

- **Symptom:** MySql.Data 26.7.0 and 26.10.0 can hang during command-timeout recovery and reader cleanup.
- **Reproduced without BuildMind or EF:** `SELECT SLEEP(5)` with a 1 s `CommandTimeout` hangs instead of raising a timeout.

## Work in this execution

**No new RH09 experiment was run in this execution.** The reasons:
- the order requires an isolated diagnostic environment;
- the host was memory-constrained (a stack restart was stopped by the host for low memory during this session).

I am not claiming any resolution, containment proof or provider fix. No timeout wrapper was added: one that hides a still-running database operation is explicitly forbidden.

## Independently actionable next steps (not executed)

1. In an isolated container, re-run the minimal repro against MySql.Data 26.10.0 with the exact `SslMode=VerifyFull` configuration, capturing a thread dump of the hang.
2. Evaluate `MySqlConnector` (MIT) as an alternative ADO.NET provider.
   - It is **not** an EF Core 10 provider by itself: Pomelo has no EF Core 10 release (G03 study).
   - It can only be adopted for EF if a compatible EF provider exists. This is an M4 decision with licence review (G03).
3. Server-side bound: `max_execution_time` (MySQL, SELECT only) as defence in depth. It is not a client-side fix.
4. Acceptance scenarios that must pass before RH09 is reported resolved:
   - provider-level timeout raised within bound;
   - the connection is disposed and the pool recovers;
   - a following query succeeds;
   - the HTTP request returns 504/503 within bound;
   - no orphaned server-side query (`SHOW PROCESSLIST`).

**Production impact:** RH09 remains a production blocker (NO-GO).
