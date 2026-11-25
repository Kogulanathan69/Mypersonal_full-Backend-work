# Captain Docker consent and Swagger verification

The first focused run at `~/bm-captain/results/docker-focused-a9AEZzGa` completed 293 checks with zero failures. The second run at `~/bm-captain/results/docker-hardened-tqSzJmXm` repeated the journey with file-specific mounts, UID-specific credential permissions and the secondary survey worker disabled. It completed 293 checks with zero failures and recorded 327 HTTP exchanges in `http.jsonl`. Both runs used the retained verification database and artifact volume. The repository was dirty at commit `0b87d24fd743a2be249b128636282d7e405f445c`.

The migration runner reported the existing database current through `20261125103924_AddLandEdgeRoles`. Both APIs reported healthy MySQL, Python and RAG dependencies. On port 5181 the architect could see fresh consent. After the expiry interval the request, report, response and feedback endpoints all returned 404 and the list excluded the request. The same request remained accessible through the primary API on port 8080. Phase 5 also exercised the secondary RAG policy and Phase 13 exercised additional customer and architect operations.

Before startup WSL had 2,963 MiB available. One sample during the first journey measured 144.3 MiB for the primary API and 107.4 MiB for the secondary. These are point samples. The primary and secondary share API authentication settings, verified database TLS and artifact storage. The secondary overrides the consent lifetime, generation policy and survey worker setting.

## Swagger verification

The development Swagger page returned HTTP 200 at `http://127.0.0.1:8080/swagger/index.html`. Its document at `http://127.0.0.1:8080/swagger/v1/swagger.json` contained 146 operations. The source contains 146 HTTP method registration calls. The browser access host was unavailable during inspection.

For visual verification in the running Development profile:

1. Open the Swagger page and confirm the document loads without a resolver error.
2. Compare the method and path in each expanded tag with the generated acceptance matrix. Check path and query parameters, required fields, nullable properties, response codes and schemas.
3. Expand upload and report operations. Verify the upload control accepts multipart input and the report download describes a binary response.
4. Execute the health operation. Then use a synthetic, role-appropriate session to execute a protected read. Clear authorization and repeat the protected read to verify the displayed authentication failure.
5. Record missing or inaccurate schemas and operation names against the exact method and path. Keep tokens out of screenshots.
