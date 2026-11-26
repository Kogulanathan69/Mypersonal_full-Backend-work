# OCR sandbox and accuracy report

## Sandbox (python-services `app/survey/ocr.py`, commit `efa3244`)

| Control | Implementation | Verified by |
|---|---|---|
| Non-root | Refuses to run when euid = 0 | `test_refuses_to_run_as_root` |
| Process isolation | One process per tool, new session, `stdin=/dev/null`, scrubbed env (`PATH`, locale, `HOME`/`TMPDIR` = private dir; no proxies, tokens or service secrets) | `test_children_run_with_resource_limits_scrubbed_env_and_new_session` |
| Resource limits | `RLIMIT_AS` 1.5 GiB, `RLIMIT_CPU` timeout + 5 s, `RLIMIT_FSIZE` 256 MiB, `RLIMIT_NOFILE` 64, `RLIMIT_CORE` 0 | same test |
| Wall-clock limit | 60 s per page, 120 s for rasterisation | `test_timeouts_are_enforced` |
| Decompression bombs | PNG IHDR / JPEG SOF dimensions are checked **before** decoding: ≤ 12 000 px per side, ≤ 50 MP | 4 parametrised rejection tests + header tests |
| PDF limits | ≤ 10 pages, `-scale-to 3508`, total rendered PNG ≤ 200 MiB | code; malformed-PDF local test |
| Temporary files | Private 0700 `TemporaryDirectory`, always removed | code |
| Network | Optional `BUILDMIND_OCR_NETWORK_ISOLATION=1` runs each tool in an empty network namespace (`unshare --net --map-root-user`); fails closed if unavailable | **not enabled or verified in WSL** (user namespaces not tested here) |
| Read-only root FS / container | **Not implemented locally.** Production should run python-services as a non-root container with a read-only root and a tmpfs work dir. | — |

## Languages

- **Installed:** `eng`, `sin`, `tam` (Tesseract 5.3.4).
- **Defect corrected:** OCR previously ran English only (`LANGUAGES = "eng"`). It now uses the installed subset of `eng+sin+tam` and reports it in `ocrLanguages`.

## Accuracy: NOT MEASURED

- **What exists:** a synthetic text-layer PDF extraction (`PDF_TEXT`) and a real-Tesseract test on a synthetic English image ("North boundary length: 12 m" → 12) pass.
- **What does not exist:** an authorised set of representative Sri Lankan survey documents in English, Sinhala or Tamil is not in the repository. **No accuracy figures are claimed.** Sinhala/Tamil numeric extraction quality is unknown.
- **Mitigation:** every OCR result is `NEEDS_REVIEW` with values `REVIEW_REQUIRED`. The customer review workflow (CONFIRM / CORRECT / REJECT, m/ft normalisation, versioned) records the customer's decisions. **Land is never confirmed from OCR.**
- **Required next step:** authorised sample documents plus ground-truth labels to build an evaluation suite.

## Tests

- 11 sandbox unit tests;
- 5 real-OCR local tests (`tests_local`, not collected by CI);
- python-services suite 992 passed.
