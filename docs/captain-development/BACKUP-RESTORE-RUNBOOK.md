# Backup and restore runbook (local Captain environment; production procedures need approval)

| Step | Command (WSL, `~/bm-captain/src`) | Notes |
|---|---|---|
| Backup | `bash tools/captain/backup-db.sh` → prints `~/bm-captain/backups/<UTC stamp>/` | `mysqldump --single-transaction --routines --triggers` of `buildmind_dev` + `artifacts.tar.gz` (survey blobs, report PDFs) + `SHA256SUMS`; directory 0700, files 0600 |
| Restore drill (non-destructive) | `bash tools/captain/restore-verify.sh <backup-dir>` | Verifies checksums, restores into a **disposable** `buildmind_test_restore_*` database, compares per-table row counts with the source, always drops the disposable database, unpacks the artefacts to a temporary directory and deletes it |
| Real restore | **Not scripted on purpose.** Restoring over `buildmind_dev` is destructive and needs explicit authorization: stop the API; restore into a new database; switch the connection string; keep the old database until verified. | Never reset or drop the Captain database without authorization |

**Last drill:**
- backup `20261125T010348Z`;
- checksums OK;
- 37 tables compared, 0 mismatches;
- 0 leftover databases.

**Proposed objectives:** not approved business policy (G15); for discussion only.

| Objective | Proposal |
|---|---|
| RPO | 24 h (daily backup) plus binlog retention for point-in-time recovery |
| RTO | 4 h |
| Retention | 30 daily + 12 monthly |
| Encryption | Backups encrypted at rest with a key held outside the database host |
| Drills | Monthly restore drill |

**Consistency note:** the database dump and the artefact archive are taken a few seconds apart.
- An artefact written in between may be missing from the archive.
- The storage reconciler never deletes referenced blobs, and the extraction worker fails closed on a missing file.
- For production, take a filesystem/volume snapshot together with the database snapshot.
