# Database compatibility CI job — PROPOSAL (BE-M4-D02a, revised 2026-11-18 in STEP 05-C)

> **Superseded by the implementation (BE-M4-D02b, 2026-11-18, commit `947d8f8`).** Lead authorization: internal compatibility testing only. The live job in `.github/workflows/backend-ci.yml` differs from the diff below: the image is pinned by digest, the data directory is on `tmpfs`, `have_ssl` is replaced by test-level TLS evidence (T22/T23), there is a CLI migration generation step, a leftover-database check and a credential scan gating the artifact upload, and the test count is now 26. The text below is kept as history.

Status: **PROPOSED, not applied.** `.github/workflows/backend-ci.yml` is unchanged. Applying it needs explicit lead approval. G03 has conditional approval for internal development and compatibility testing only; the commercial licence review is still pending.

**G03 note:** the job restores the provider from NuGet onto an ephemeral GitHub runner for internal testing. It publishes no provider binaries: the only artifact is a TRX test result. The repository is **public** (verified 2026-11-18). That publishes BuildMind source with a package reference, not the provider itself. The lead should still confirm that this fits the conditional authorization.

## Design

- **Separate job** `database-compatibility`. The existing `dotnet-build` and `python-tests` jobs and their check names stay unchanged. It is not a required check until G13 is decided.
- **Disposable MySQL 8.4:**
  - Started with `docker run` rather than `services:`, because service containers start before any step and can't use passwords generated during the run.
  - Bound to `127.0.0.1` only; health check; removed in an `always()` step.
- **Pre-check:** the job requires `partial_revokes = 0`, otherwise wildcard schema grants are literal, and `have_ssl = YES`.
- **Temporary credentials:**
  - Three random passwords are generated per run with `openssl rand` and masked with `::add-mask::`. No repository secrets, nothing committed.
  - The container's root account is used only to create accounts inside the throwaway container; tests never use root.
  - Every account `REQUIRE SSL`, and every connection string uses `SslMode=Required`. The application's validation rejects anything weaker.
  - Accounts use host `%` because the Docker bridge address isn't `localhost`.
- **Least privilege:**
  - `bm_runtime`: DML on `buildmind_dev` only. The spike's least-privilege test checks this without selecting that database.
  - `bm_test`: `ALL` only on `buildmind\_test\_%`.
- **Tooling smoke check:** `dotnet-ef migrations list --no-connect` against `BuildMindDbContext`, through the design-time factory. It proves the local tool, design package and provider load together. It does **not** prove a generated feature migration.
- **Explicit test run:** the integration project is targeted by path. It stays outside `BuildMind.sln`.
  - Inventory: **22 tests**, T01–T20 plus the least-privilege (LP) and index (SQL) checks.
- **Results:** a TRX artifact kept 14 days. Test output holds only test names, observed column types and generated SQL; connection strings are never written and passwords are masked.
- **Permissions and limits:** `contents: read`; 20-minute timeout.

Open points:
- Pin `mysql:8.4` by digest once approved.
- The actions still use the Node 20 runtime, the same as the existing jobs.

## Exact proposed diff (against `develop` `fef5814`)

```diff
--- a/.github/workflows/backend-ci.yml
+++ b/.github/workflows/backend-ci.yml
@@ jobs:
   python-tests:
     runs-on: ubuntu-latest
     steps:
       - uses: actions/checkout@v4
       - uses: actions/setup-python@v5
         with:
           python-version: '3.13'
       - run: python -m pip install -r python-services/requirements-dev.txt
       - run: cd python-services && python -m pytest -q
+  database-compatibility:
+    # BE-M4-D02 provider compatibility spike on a disposable MySQL 8.4 (G03: internal testing only).
+    runs-on: ubuntu-latest
+    timeout-minutes: 20
+    permissions:
+      contents: read
+    steps:
+      - uses: actions/checkout@v4
+      - uses: actions/setup-dotnet@v4
+        with:
+          dotnet-version: '10.0.x'
+      - name: Generate per-run database credentials
+        run: |
+          for name in ROOT TEST RUNTIME; do
+            value="$(openssl rand -hex 24)"
+            echo "::add-mask::${value}"
+            echo "BM_${name}_PASSWORD=${value}" >> "$GITHUB_ENV"
+          done
+      - name: Start disposable MySQL 8.4
+        run: |
+          docker run -d --name buildmind-mysql \
+            -e MYSQL_ROOT_PASSWORD="$BM_ROOT_PASSWORD" \
+            -p 127.0.0.1:3306:3306 \
+            --health-cmd "mysqladmin ping -h 127.0.0.1 --silent" \
+            --health-interval 5s --health-timeout 5s --health-retries 30 \
+            mysql:8.4
+          for attempt in $(seq 1 60); do
+            if [ "$(docker inspect --format '{{.State.Health.Status}}' buildmind-mysql)" = "healthy" ]; then
+              exit 0
+            fi
+            sleep 2
+          done
+          echo "MySQL did not become healthy" >&2
+          exit 1
+      - name: Create disposable least-privilege accounts
+        run: |
+          checks="$(docker exec -e MYSQL_PWD="$BM_ROOT_PASSWORD" buildmind-mysql \
+            mysql -uroot -N -e 'SELECT @@GLOBAL.partial_revokes, @@GLOBAL.have_ssl;')"
+          if [ "$checks" != "$(printf '0\tYES')" ]; then
+            echo "Unexpected server settings (partial_revokes/have_ssl): $checks" >&2
+            exit 1
+          fi
+          docker exec -i -e MYSQL_PWD="$BM_ROOT_PASSWORD" buildmind-mysql mysql -uroot <<SQL
+          CREATE DATABASE buildmind_dev CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
+          CREATE USER 'bm_runtime'@'%' IDENTIFIED BY '${BM_RUNTIME_PASSWORD}' REQUIRE SSL;
+          GRANT SELECT, INSERT, UPDATE, DELETE ON buildmind_dev.* TO 'bm_runtime'@'%';
+          CREATE USER 'bm_test'@'%' IDENTIFIED BY '${BM_TEST_PASSWORD}' REQUIRE SSL;
+          GRANT ALL PRIVILEGES ON \`buildmind\\_test\\_%\`.* TO 'bm_test'@'%';
+          SQL
+      - name: Verify dotnet-ef design-time tooling (no database connection)
+        env:
+          BUILDMIND_MIGRATIONS_CONNECTION: Server=127.0.0.1;Port=3306;Database=buildmind_test_tooling;User ID=bm_test;SslMode=Required;Password=${{ env.BM_TEST_PASSWORD }}
+        run: |
+          dotnet tool restore
+          dotnet tool run dotnet-ef migrations list --no-connect --configuration Release \
+            --project src/BuildMind.Infrastructure --startup-project src/BuildMind.API
+      - name: Run provider compatibility spike
+        env:
+          BUILDMIND_TEST_MYSQL: Server=127.0.0.1;Port=3306;User ID=bm_test;SslMode=Required;Password=${{ env.BM_TEST_PASSWORD }}
+          BUILDMIND_TEST_MYSQL_RUNTIME: Server=127.0.0.1;Port=3306;Database=buildmind_dev;User ID=bm_runtime;SslMode=Required;Password=${{ env.BM_RUNTIME_PASSWORD }}
+        run: >-
+          dotnet test tests/BuildMind.Persistence.IntegrationTests/BuildMind.Persistence.IntegrationTests.csproj
+          --configuration Release
+          --logger "trx;LogFileName=persistence-spike.trx"
+          --logger "console;verbosity=normal"
+          --results-directory TestResults
+      - name: Upload spike results
+        if: always()
+        uses: actions/upload-artifact@v4
+        with:
+          name: persistence-spike-results
+          path: TestResults/*.trx
+          retention-days: 14
+      - name: Remove disposable MySQL
+        if: always()
+        run: docker rm -f buildmind-mysql || true
```

In the heredoc, the backquotes are escaped (`` \` ``) and `\\_` yields the literal `\_`, so the `bm_test` grant applies only to `buildmind_test_*` databases (with `partial_revokes = 0`, which the step checks first).

## Acceptance (when approved and run)

- The tooling smoke check passes.
- All **22** tests pass.
- The observed column types are recorded in the evidence register.
- The existing jobs are unaffected.
- No credentials appear in logs or artifacts.

G03 then gets updated evidence. The commercial licence decision stays a separate gate.
