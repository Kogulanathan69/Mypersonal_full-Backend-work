# Production readiness matrix: verdict **NO-GO**

| Gate | State | Blocking? | What is needed |
|---|---|---|---|
| RH09 provider timeout hang | Unresolved; no new experiment this execution | **Yes** | See RH09-RESOLUTION-REPORT acceptance scenarios |
| G03 MySQL provider licence (GPL-2.0 + FOSS exception) | Not reviewed | **Yes** | Licence review |
| G11 internal transport (shared token only; no mTLS between services) | Partial | **Yes** | Internal TLS/mTLS design and certificates; token rotation procedure. Token files are now supported in python-services |
| G19 independent review | Not obtained | **Yes** | See INDEPENDENT-REVIEW-PACKAGE |
| Captain profile is Development-only | By design | **Yes** | Owner ratification of the contracts, then an approved production composition |
| Email provider | Local Mailpit only | **Yes** for recovery/invitations | Approved provider, sender policy, credentials |
| AI provider | Rule-based local parser only | No (feature degrades honestly) | Approved provider and data policy (G20) |
| Regulation corpus | Blocked in WSL; fail closed | No for preliminary plans (labelled synthetic) | Corpus activation (STEP 8B / M7) |
| OCR sandbox | Process-level limits; no container read-only root verified | Medium | Container hardening as in `deploy/captain` (not built here) |
| Backups | Scripts + verified local drill | Medium | Approved RPO/RTO, encryption, off-host storage |
| Containers / compose | Written, **not built or validated** here | Medium | Build plus `docker compose config` in CI |
| Observability | Structured console logs, correlation via `traceId`; no metrics endpoint | Medium | Metrics/alerting stack decision |
| CI | Workflows exist; nothing pushed, so not run | **Yes** | Push only when authorized |
| Retention / disclosure policies | Not approved | **Yes** for production data | Product/legal decisions (G12) |
