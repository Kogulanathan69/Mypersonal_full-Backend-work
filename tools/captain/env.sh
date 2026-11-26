#!/usr/bin/env bash
# Source this file (". tools/captain/env.sh") to export Captain-local connection strings into the current shell.
# Values are read from ~/bm-captain/secrets and never echoed. Loopback + verified TLS: SslMode=VerifyFull (certificate
# chain against the Captain local CA AND host name; the server certificate carries SAN IP:127.0.0.1, DNS:localhost).
# BM_CAPTAIN_SSLMODE may only be raised/kept, never set to a non-verifying mode by these scripts.
_s="$HOME/bm-captain/secrets"
_p="${BM_CAPTAIN_MYSQL_PORT:-3417}"
# MySql.Data requires a client certificate (SslCert/SslKey) for VerifyFull with PEM files; issued by the same local CA.
_tls="SslMode=${BM_CAPTAIN_SSLMODE:-VerifyFull};SslCa=$_s/ca.pem;SslCert=$_s/tls/client-cert.pem;SslKey=$_s/tls/client-key.pem"
export ConnectionStrings__BuildMind="Server=127.0.0.1;Port=$_p;Database=buildmind_dev;User ID=bm_runtime;Password=$(cat "$_s/mysql-runtime.password");$_tls"
export BUILDMIND_MIGRATIONS_CONNECTION="Server=127.0.0.1;Port=$_p;Database=buildmind_dev;User ID=bm_migrator;Password=$(cat "$_s/mysql-migrator.password");$_tls"
export BUILDMIND_TEST_MYSQL="Server=127.0.0.1;Port=$_p;User ID=bm_test;Password=$(cat "$_s/mysql-test.password");$_tls"
export BUILDMIND_TEST_MYSQL_RUNTIME="Server=127.0.0.1;Port=$_p;Database=buildmind_dev;User ID=bm_runtime;Password=$(cat "$_s/mysql-runtime.password");$_tls"
unset _s _p _tls
