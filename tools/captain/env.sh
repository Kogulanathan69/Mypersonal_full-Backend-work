#!/usr/bin/env bash
# Source this file (". tools/captain/env.sh") to export Captain-local connection strings into the current shell.
# Values are read from ~/bm-captain/secrets and never echoed. Loopback + verified TLS (VerifyCA against the server CA).
_s="$HOME/bm-captain/secrets"
_p="${BM_CAPTAIN_MYSQL_PORT:-3417}"
_tls="SslMode=VerifyCA;SslCa=$_s/ca.pem"
export ConnectionStrings__BuildMind="Server=127.0.0.1;Port=$_p;Database=buildmind_dev;User ID=bm_runtime;Password=$(cat "$_s/mysql-runtime.password");$_tls"
export BUILDMIND_MIGRATIONS_CONNECTION="Server=127.0.0.1;Port=$_p;Database=buildmind_dev;User ID=bm_migrator;Password=$(cat "$_s/mysql-migrator.password");$_tls"
export BUILDMIND_TEST_MYSQL="Server=127.0.0.1;Port=$_p;User ID=bm_test;Password=$(cat "$_s/mysql-test.password");$_tls"
export BUILDMIND_TEST_MYSQL_RUNTIME="Server=127.0.0.1;Port=$_p;Database=buildmind_dev;User ID=bm_runtime;Password=$(cat "$_s/mysql-runtime.password");$_tls"
unset _s _p _tls
