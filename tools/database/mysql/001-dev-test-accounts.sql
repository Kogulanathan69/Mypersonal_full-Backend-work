-- BuildMind — disposable development/test database accounts (BE-M4-D02a PROPOSAL)
--
-- REVIEW BEFORE USE. The lead runs this manually; nothing in BuildMind runs it automatically.
-- * Run it as a DBA account on a LOCAL development server only, never on a production server.
-- * Replace each <...PASSWORD...> placeholder with a newly generated strong secret, in your local client
--   session only. Never commit a filled-in copy, paste it into chat or keep it in shell history.
-- * The script creates only the empty `buildmind_dev` database and three accounts. It changes or deletes
--   no existing database, account or data. If an account already exists, CREATE USER fails and you
--   resolve that manually; the script never alters or drops existing accounts.
-- * Accounts are limited to localhost. root is never used by the application, migrations or tests.

-- PRE-CHECK (read-only). Both values must be as expected before you continue:
-- * partial_revokes must be 0 (OFF). When it is ON, MySQL treats % and _ in GRANT schema names
--   LITERALLY, so the bm_test grant below would cover no disposable database. Do not continue; ask M4.
-- * TLS must be available, because every account below REQUIREs it: tls_version must list TLSv1.2/1.3,
--   and in a TLS session, `SHOW SESSION STATUS LIKE 'Ssl_cipher'` must be non-empty.
SELECT @@GLOBAL.partial_revokes AS partial_revokes;
SHOW GLOBAL VARIABLES LIKE 'tls_version';

CREATE DATABASE IF NOT EXISTS `buildmind_dev` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;

-- Runtime identity: data access only. No DDL, GRANT, FILE or global privileges.
CREATE USER 'bm_runtime'@'localhost' IDENTIFIED BY '<RUNTIME_PASSWORD>' REQUIRE SSL PASSWORD EXPIRE NEVER;
GRANT SELECT, INSERT, UPDATE, DELETE ON `buildmind_dev`.* TO 'bm_runtime'@'localhost';

-- Migration identity: schema changes on buildmind_dev only. Data privileges cover __EFMigrationsHistory.
-- It has no GRANT OPTION and no access to any other schema.
CREATE USER 'bm_migrator'@'localhost' IDENTIFIED BY '<MIGRATOR_PASSWORD>' REQUIRE SSL PASSWORD EXPIRE NEVER;
GRANT CREATE, ALTER, DROP, INDEX, REFERENCES, SELECT, INSERT, UPDATE, DELETE, LOCK TABLES
    ON `buildmind_dev`.* TO 'bm_migrator'@'localhost';

-- Disposable-test identity: full control only of databases named buildmind_test_<...>; nothing else.
-- `\_` makes the underscore literal and `%` matches the rest (only while partial_revokes is OFF).
-- After the GRANT, run SHOW WARNINGS: if the server reports wildcard grants as deprecated, record that
-- for M4 (the grant still applies, but a future server version may need another approach).
CREATE USER 'bm_test'@'localhost' IDENTIFIED BY '<TEST_PASSWORD>' REQUIRE SSL PASSWORD EXPIRE NEVER;
GRANT ALL PRIVILEGES ON `buildmind\_test\_%`.* TO 'bm_test'@'localhost';
SHOW WARNINGS;

-- Verification (read-only): review the output before using the accounts.
SHOW GRANTS FOR 'bm_runtime'@'localhost';
SHOW GRANTS FOR 'bm_migrator'@'localhost';
SHOW GRANTS FOR 'bm_test'@'localhost';
