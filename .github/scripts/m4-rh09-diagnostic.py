"""Captain-owned, single-test diagnostic for the disposable GitHub runner."""

import json
import os
import re
import secrets
import shutil
import signal
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path


TEMP = Path(os.environ["RUNNER_TEMP"])
PRIVATE = TEMP / "rh09-private"
PUBLIC = TEMP / "rh09-public"
LABEL = f'{os.environ["GITHUB_RUN_ID"]}-{os.environ["GITHUB_RUN_ATTEMPT"]}'
TEST = "RH_MYSQL_HTTP_09_command_timeout_classification_maps_to_504"
FQN = f"BuildMind.Persistence.IntegrationTests.RevisionHistoryHttpMySqlTests.{TEST}"
SCHEMA = re.compile(r"buildmind_test_[0-9a-f]{32}\Z")
CONTAINER_ID = re.compile(r"[0-9a-f]{64}\Z")
USER = "bm_rh09"


def record(name, value):
    PUBLIC.mkdir(exist_ok=True)
    (PUBLIC / f"{name}.json").write_text(json.dumps(value, indent=2) + "\n")


def command(args, *, env=None, input_text=None, check=True, timeout=20):
    return subprocess.run(
        args, env=env, input=input_text, capture_output=True,
        text=True, timeout=timeout, check=check,
    )


def mysql(container, password, sql, host=None, check=True):
    args = ["docker", "exec", "-i", "-e", "MYSQL_PWD", container,
            "mysql", "--batch", "--skip-column-names", "--connect-timeout=5"]
    if host:
        args += ["--protocol=TCP", f"--host={host}", f"--user={USER}",
                 "--ssl-mode=VERIFY_IDENTITY", "--ssl-ca=/rh09-tls/ca.pem"]
    else:
        args += ["--protocol=SOCKET", "--user=root"]
    return command(args, env=dict(os.environ, MYSQL_PWD=password),
                   input_text=sql, check=check)


def start_mysql():
    PRIVATE.mkdir(mode=0o700)
    tls = PRIVATE / "tls"
    tls.mkdir(mode=0o755)
    ca_key = PRIVATE / "ca.key"
    request = PRIVATE / "server.csr"
    extensions = PRIVATE / "server.ext"
    extensions.write_text("subjectAltName=DNS:localhost\nextendedKeyUsage=serverAuth\n")
    command(["openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes",
             "-days", "1", "-subj", "/CN=RH09 ephemeral CA", "-keyout",
             str(ca_key), "-out", str(tls / "ca.pem")])
    command(["openssl", "req", "-newkey", "rsa:2048", "-nodes", "-subj",
             "/CN=localhost", "-keyout", str(tls / "server.key"), "-out", str(request)])
    command(["openssl", "x509", "-req", "-in", str(request), "-CA",
             str(tls / "ca.pem"), "-CAkey", str(ca_key), "-CAcreateserial",
             "-days", "1", "-extfile", str(extensions), "-out", str(tls / "server.pem")])
    # The image's mysql user must read the bind mount. Its host parent stays 0700.
    (tls / "server.key").chmod(0o644)
    root_password = secrets.token_hex(24)
    test_password = secrets.token_hex(24)
    (PRIVATE / "root-password").write_text(root_password)
    image = os.environ["MYSQL_IMAGE"]
    command(["docker", "pull", image], timeout=120)
    container = command([
        "docker", "run", "--detach", "--label", f"buildmind.rh09={LABEL}",
        "--tmpfs", "/var/lib/mysql:rw,nosuid,nodev", "--publish", "127.0.0.1::3306",
        "--mount", f"type=bind,src={tls},dst=/rh09-tls,readonly",
        "-e", "MYSQL_ROOT_PASSWORD", image,
        "--ssl-ca=/rh09-tls/ca.pem", "--ssl-cert=/rh09-tls/server.pem",
        "--ssl-key=/rh09-tls/server.key", "--require-secure-transport=ON",
    ], env=dict(os.environ, MYSQL_ROOT_PASSWORD=root_password)).stdout.strip()
    if not CONTAINER_ID.fullmatch(container):
        raise RuntimeError("invalid container ID")
    (PRIVATE / "container").write_text(container)
    for _ in range(30):
        # The image's bootstrap server accepts socket connections with TCP disabled.
        ping = command(["docker", "exec", container, "mysqladmin", "--protocol=TCP",
                        "--host=127.0.0.1", "--connect-timeout=2", "ping"],
                       check=False, timeout=5)
        if ping.returncode == 0:
            break
        time.sleep(2)
    else:
        raise RuntimeError("MySQL readiness deadline expired")
    if mysql(container, root_password, "SELECT @@GLOBAL.partial_revokes;").stdout.strip() != "0":
        raise RuntimeError("wildcard grants require partial_revokes=OFF")
    mysql(container, root_password, f"""
        CREATE USER '{USER}'@'%' IDENTIFIED BY '{test_password}' REQUIRE SSL;
        GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, INDEX, REFERENCES
          ON `buildmind\\_test\\_%`.* TO '{USER}'@'%';
    """)
    version = mysql(container, root_password, "SELECT VERSION();").stdout.strip()
    match = mysql(container, test_password, "SHOW SESSION STATUS LIKE 'Ssl_cipher';", "localhost")
    mismatch = mysql(container, test_password, "SELECT 1;", "127.0.0.1", check=False)
    outside = mysql(container, test_password, "SELECT User FROM mysql.user;", "localhost", check=False)
    probe = f"buildmind_test_{secrets.token_hex(16)}"
    mysql(container, test_password, f"CREATE DATABASE `{probe}`; DROP DATABASE `{probe}`;", "localhost")
    outside_name = f"rh09_outside_{secrets.token_hex(16)}"
    create_outside = mysql(container, test_password, f"CREATE DATABASE `{outside_name}`;",
                           "localhost", check=False)
    if create_outside.returncode == 0:
        mysql(container, root_password, f"DROP DATABASE `{outside_name}`;")
    tls_ok = bool(re.fullmatch(r"Ssl_cipher\s+\S+", match.stdout.strip()))
    mismatch_ok = mismatch.returncode != 0 and "ERROR 2026" in mismatch.stderr
    grants_ok = (outside.returncode != 0 and "ERROR 1142" in outside.stderr
                 and create_outside.returncode != 0 and "ERROR 1044" in create_outside.stderr)
    record("tls", {"mysql_8_4": version.startswith("8.4."),
                   "mysql_version": version if re.fullmatch(r"8\.4\.\d+", version) else None,
                   "verified_identity": tls_ok, "mismatch_rejected": mismatch_ok,
                   "out_of_scope_denied": grants_ok})
    if not (version.startswith("8.4.") and tls_ok and mismatch_ok and grants_ok):
        raise RuntimeError("MySQL security preflight failed")
    bound = command(["docker", "port", container, "3306/tcp"]).stdout.strip()
    port = re.fullmatch(r"127\.0\.0\.1:(\d+)", bound)
    if port is None:
        raise RuntimeError("MySQL port is not loopback-only")
    return (f"Server=localhost;Port={port[1]};User ID={USER};Password={test_password};"
            f"SslMode=VerifyFull;SslCa={tls / 'ca.pem'};Pooling=false")


def live_group(group):
    rows = command(["ps", "-eo", "pgid=,stat="]).stdout.splitlines()
    return any(pgid == str(group) and not state.startswith("Z")
               for pgid, state in (row.split() for row in rows))


def stop_group(group, grace):
    if live_group(group):
        os.killpg(group, signal.SIGTERM)
        end = time.monotonic() + grace
        while time.monotonic() < end and live_group(group):
            time.sleep(0.1)
        if live_group(group):
            os.killpg(group, signal.SIGKILL)
    end = time.monotonic() + 3
    while time.monotonic() < end and live_group(group):
        time.sleep(0.1)
    return not live_group(group)


def sanitize_trx():
    raw = PRIVATE / "rh09.trx"
    if not raw.is_file() or raw.stat().st_size > 1024 * 1024:
        return "NO_RESULT"
    try:
        root = ET.parse(raw).getroot()
    except ET.ParseError:
        return "NO_RESULT"
    rows = root.findall(".//{*}UnitTestResult")
    counters = root.find(".//{*}Counters")
    if len(rows) != 1 or counters is None or counters.get("executed") != "1":
        return "NO_RESULT"
    row = rows[0]
    outcome = row.get("outcome")
    duration = row.get("duration", "")
    if (row.get("testName") not in {TEST, FQN} or outcome not in {"Passed", "Failed"}
            or not re.fullmatch(r"\d{2}:\d{2}:\d{2}(\.\d+)?", duration)):
        return "NO_RESULT"
    ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
    ET.register_namespace("", ns)
    clean = ET.Element(f"{{{ns}}}TestRun", {"name": "RH-09 sanitized result"})
    results = ET.SubElement(clean, f"{{{ns}}}Results")
    ET.SubElement(results, f"{{{ns}}}UnitTestResult",
                  {"testName": FQN, "outcome": outcome, "duration": duration})
    ET.ElementTree(clean).write(PUBLIC / "rh09.sanitized.trx", encoding="utf-8", xml_declaration=True)
    return outcome.upper()


def run():
    connection = start_mysql()
    args = ["dotnet", "test",
            "tests/BuildMind.Persistence.IntegrationTests/BuildMind.Persistence.IntegrationTests.csproj",
            "--configuration", "Release", "--no-build", "--no-restore",
            "--filter", f"FullyQualifiedName={FQN}", "--logger", "trx;LogFileName=rh09.trx",
            "--results-directory", str(PRIVATE)]
    started = time.monotonic()
    interrupted = False

    def request_stop(_signum, _frame):
        nonlocal interrupted
        interrupted = True

    signal.signal(signal.SIGTERM, request_stop)
    signal.signal(signal.SIGINT, request_stop)
    with (PRIVATE / "test.log").open("wb") as log:
        test = subprocess.Popen(args, cwd="source", start_new_session=True,
                                env=dict(os.environ, BUILDMIND_TEST_MYSQL=connection),
                                stdout=log, stderr=subprocess.STDOUT)
        (PRIVATE / "group").write_text(str(test.pid))
        while test.poll() is None and not interrupted and time.monotonic() - started < 120:
            time.sleep(0.05)
        elapsed = time.monotonic() - started
        timed_out = test.poll() is None and not interrupted
        processes_gone = stop_group(test.pid, 15)
        code = test.wait(timeout=3)
        if processes_gone:
            (PRIVATE / "group").unlink()
    outcome = sanitize_trx()
    status = ("CANCELLED" if interrupted else "TIMEOUT" if timed_out else
              "PASS" if code == 0 and outcome == "PASSED" and processes_gone else "FAIL")
    record("test", {"status": status, "test_seconds": round(elapsed, 3), "exit_code": code,
                    "trx_outcome": outcome, "group_stopped": processes_gone})
    return 0 if status == "PASS" else 1


def cleanup():
    report = {"processes_stopped": None, "sessions_before": None, "sessions_remaining": None,
              "schemas_before": None, "schemas_remaining": None, "container_removed": None}
    ok = True
    group_file = PRIVATE / "group"
    test_file = PUBLIC / "test.json"
    if test_file.exists():
        report["processes_stopped"] = json.loads(test_file.read_text())["group_stopped"]
    if group_file.exists():
        try:
            report["processes_stopped"] = stop_group(int(group_file.read_text()), 3)
            ok &= report["processes_stopped"]
        except (OSError, subprocess.SubprocessError, ValueError):
            ok = False
    try:
        containers = command(["docker", "ps", "-aq", "--no-trunc", "--filter",
                              f"label=buildmind.rh09={LABEL}"]).stdout.split()
        if len(containers) > 1 or any(not CONTAINER_ID.fullmatch(c) for c in containers):
            raise RuntimeError("unexpected container ownership")
        for container in containers:
            try:
                root_password = (PRIVATE / "root-password").read_text()

                def sessions():
                    return mysql(container, root_password,
                                 f"SELECT ID FROM information_schema.PROCESSLIST WHERE USER='{USER}';").stdout.split()

                def schemas():
                    return mysql(container, root_password,
                                 "SELECT SCHEMA_NAME FROM information_schema.SCHEMATA "
                                 "WHERE REGEXP_LIKE(SCHEMA_NAME,'^buildmind_test_[0-9a-f]{32}$','c');").stdout.split()

                active = sessions()
                report["sessions_before"] = len(active)
                for session in active:
                    mysql(container, root_password, f"KILL CONNECTION {int(session)};", check=False)
                names = schemas()
                report["schemas_before"] = len(names)
                for name in names:
                    if not SCHEMA.fullmatch(name):
                        raise RuntimeError("unexpected schema")
                    mysql(container, root_password, f"DROP DATABASE `{name}`;")
                report["sessions_remaining"] = len(sessions())
                report["schemas_remaining"] = len(schemas())
                ok &= report["sessions_remaining"] == report["schemas_remaining"] == 0
            finally:
                command(["docker", "rm", "-fv", container])
                remaining = command(["docker", "ps", "-aq", "--no-trunc", "--filter",
                                     f"label=buildmind.rh09={LABEL}"]).stdout.strip()
                report["container_removed"] = not remaining
                ok &= report["container_removed"]
        if (PRIVATE / "container").exists() and not containers:
            ok = False
    except (OSError, subprocess.SubprocessError, ValueError, RuntimeError):
        ok = False
    finally:
        record("cleanup", dict(report, ok=ok))
        if PRIVATE.exists():
            shutil.rmtree(PRIVATE)
    return 0 if ok else 1


if __name__ == "__main__":
    try:
        sys.exit({"run": run, "cleanup": cleanup}[sys.argv[1]]())
    except (OSError, subprocess.SubprocessError, ValueError, RuntimeError) as error:
        # Some subprocess errors contain their command or stderr. Emit only the type.
        print(f"RH-09 diagnostic infrastructure error: {type(error).__name__}", file=sys.stderr)
        sys.exit(1)
