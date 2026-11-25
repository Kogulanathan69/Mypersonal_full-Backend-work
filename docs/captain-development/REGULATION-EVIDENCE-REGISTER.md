# Regulation evidence register

| Item | State | Evidence / reason |
|---|---|---|
| STEP 8B scope (rag-service, `RagServiceClient`, M7 registration, corpus identity controls) | **ON HOLD, untouched** | No file in the protected scope changed in this execution |
| Approved corpus activation | **BLOCKED (environment)** | Manifest pins Windows-separator identities; the loader correctly refuses normalisation; G17 prevents running the API on Windows. Not bypassed. |
| Minimal change needed to activate | Requires authorization | Either an M7-approved manifest regenerated on the target OS (new identity pins, reviewed), or an approved Windows execution path for the API (G17). Both are outside the Captain's authority. |
| Retrieval API (`POST /regulations/retrieve`) | Fail closed, verified | Anonymous → 401; no corpus → 503 with no passages |
| Generation with `rag-constraints` evidence | Fail closed, verified | 503, no job published; evidence is never REVIEWED (EDGE-ROLE open) |
| Synthetic development evidence | Labelled | Generated plans carry `SYNTHETIC_FIXTURE` plus warnings; never described as regulatory compliance |
| Reviewed-constraint administration | Not implemented | Module `REGULATORY_CONSTRAINTS` exists in the staff scope model; an applier requires the M6/M7 constraint contract (doc 29) to be ratified |
| Invented regulations | None | No numeric building rule was created anywhere |
