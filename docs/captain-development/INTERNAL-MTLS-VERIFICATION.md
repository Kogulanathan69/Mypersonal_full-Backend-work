# Internal mTLS verification (API ↔ python-services)

**Verdict:** COMPLETE_AND_VERIFIED for API → python-services (local).
- API → rag-service remains token-only. Its client and registration are STEP 8B-protected, so they were not changed.
- MySQL keeps VerifyFull with its own CA and client certificate.

## Design

| Element | Implementation |
|---|---|
| PKI (`tools/captain/pki.sh`) | Two dedicated local CAs: `server-ca` signs the python-services server certificate (SAN localhost, 127.0.0.1, python-services; EKU serverAuth); `client-ca` signs the API client certificate (EKU clientAuth). Separate EC P-256 key per identity; 90-day leaves. Private keys live only in `~/bm-captain/secrets/pki` (0700/0600), never in the repository. |
| Server (`run-python.sh`) | uvicorn HTTPS with `--ssl-ca-certs client-ca.crt --ssl-cert-reqs 2` (CERT_REQUIRED). No plaintext listener. The internal token is still required. |
| Client (`InternalMtls.cs`) | Presents the client certificate; validates the server with custom root trust (server CA only), the serverAuth EKU and the host name. No accept-any path; a handshake failure is a dependency failure (no fallback). Registered for `PythonServiceClient` only, from the Captain composition. |
| Client identity | Possession of a certificate issued by the dedicated client CA. That CA issues only the API identity, so the server certificate or any other CA's certificate is refused. There is no per-CN allowlist at the TLS layer; authorization stays with the internal token. |
| Rotation | `pki.sh rotate` re-issues both leaves with new keys. python-services needs a restart to load them; the API reloads certificate files when `IHttpClientFactory` recycles the handler (default 2 minutes). |

## Evidence (2026-11-25)

| Test | Result |
|---|---|
| `tools/captain/mtls_tests.py` (real handshakes): trusted pair; missing client cert; rogue-CA client; expired client; server cert used as client; wrong server name; wrong client trust CA; plaintext to TLS port; TLS ≥ 1.2 + expected SAN; missing token 401; wrong token 401; valid token reaches the route | **12/12** |
| `InternalMtlsTests` (.NET): pinned CA accepted; name mismatch, rogue CA, wrong EKU, expired, missing certificate rejected | **2/2** (7 assertions) |
| API readiness over mTLS | `python: Healthy` |
| Customer journey phases 2, 3, 4, 8 over mTLS (generation, validation, edit preview, survey worker) | **99/99** |
| Rotation: new leaves issued (server serial …A4CA → …A4CC), python restarted, new certificate served, API stayed Healthy without a restart, suite re-run | **12/12** |

**Not verified:** that the API has already switched to its *rotated client* certificate. It may still present the previous client certificate, which remains valid until expiry, until its handler recycles. Revocation lists are not used (private offline CA, short-lived leaves).
