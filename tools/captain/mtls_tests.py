"""Live mutual-TLS negative tests against python-services on https://127.0.0.1:8091 (run-python.sh with the PKI).

Every case performs a real TLS handshake with the stdlib ssl module. Exit 0 only when all checks pass.
Usage: python3 mtls_tests.py
"""
import http.client
import json
import os
import socket
import ssl
import sys

P = os.path.expanduser("~/bm-captain/secrets/pki")
# MTLS_TARGET=rag runs the same suite against rag-service (8092); default python-services (8091).
TARGET = os.environ.get("MTLS_TARGET", "python")
TOKEN = open(os.path.expanduser(f"~/bm-captain/secrets/{'rag' if TARGET == 'rag' else 'python'}-internal.token")).read().strip()
HOST, PORT = "127.0.0.1", (8092 if TARGET == "rag" else 8091)
PROBE_PATH = "/internal/v1/regulations/search" if TARGET == "rag" else "/internal/v1/land/validate"
results = []


def check(name, ok, detail=""):
    results.append(ok)
    print(("PASS " if ok else "FAIL ") + name + (f"  [{detail}]" if detail and not ok else ""))


def context(ca="server-ca.crt", cert="api-client", hostname_check=True):
    ctx = ssl.create_default_context(ssl.Purpose.SERVER_AUTH, cafile=os.path.join(P, ca))
    ctx.check_hostname = hostname_check
    ctx.minimum_version = ssl.TLSVersion.TLSv1_2
    if cert:
        ctx.load_cert_chain(os.path.join(P, cert + ".crt"), os.path.join(P, cert + ".key"))
    return ctx


def request(ctx, path="/health", server_name=HOST, method="GET", body=None, token=None):
    conn = http.client.HTTPSConnection(HOST, PORT, context=ctx, timeout=10)
    if server_name != HOST:
        conn = http.client.HTTPSConnection(HOST, PORT, context=ctx, timeout=10)
        conn._create_connection = lambda address, *a, **k: socket.create_connection(address, *a, **k)  # noqa: SLF001
        conn.host = server_name  # SNI / hostname verification target
    headers = {"Content-Type": "application/json"}
    if token:
        headers["X-BuildMind-Internal-Token"] = token
    try:
        conn.request(method, path, body=json.dumps(body) if body is not None else None, headers=headers)
        response = conn.getresponse()
        return response.status, None
    except (ssl.SSLError, ConnectionResetError, BrokenPipeError, OSError) as error:
        return None, type(error).__name__
    finally:
        conn.close()


def handshake(ctx, server_name=HOST):
    """Raw handshake + one HTTP request; returns True when the server answered."""
    try:
        with socket.create_connection((HOST, PORT), timeout=10) as raw, ctx.wrap_socket(raw, server_hostname=server_name) as tls:
            tls.sendall(b"GET /health HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n")
            return tls.recv(64).startswith(b"HTTP/1.1 200")
    except (ssl.SSLError, ssl.CertificateError, ConnectionResetError, BrokenPipeError, OSError):
        return False


check("trusted server + trusted client certificate: handshake and /health 200", handshake(context()))
check("missing client certificate rejected", not handshake(context(cert=None)))
check("client certificate from an untrusted CA rejected", not handshake(context(cert="rogue-client")))
check("expired client certificate rejected", not handshake(context(cert="expired-client")))
check("server certificate used as a client identity rejected (issued by the server CA, not the client CA)",
      not handshake(context(cert="python-server")))
check("client refuses a server name that does not match the certificate", not handshake(context(), server_name="wrong.example"))
check("client trusting a different CA refuses the server", not handshake(context(ca="client-ca.crt")))
try:
    plain = http.client.HTTPConnection(HOST, PORT, timeout=10)
    plain.request("GET", "/health")
    plain_status = plain.getresponse().status
except (OSError, http.client.HTTPException):
    plain_status = None
check("plaintext HTTP to the TLS port gets no service response", plain_status in (None, 400), plain_status)

ctx = context()
with socket.create_connection((HOST, PORT), timeout=10) as raw, ctx.wrap_socket(raw, server_hostname=HOST) as tls:
    check("negotiated TLS 1.2+ with the expected server identity",
          tls.version() in ("TLSv1.2", "TLSv1.3") and ("DNS", ("rag-service" if TARGET == "rag" else "python-services")) in tls.getpeercert()["subjectAltName"], tls.version())


def internal(token):
    conn = http.client.HTTPSConnection(HOST, PORT, context=context(), timeout=10)
    headers = {"Content-Type": "application/json"}
    if token:
        headers["X-BuildMind-Internal-Token"] = token
    conn.request("POST", PROBE_PATH, body="{}", headers=headers)
    status = conn.getresponse().status
    conn.close()
    return status


check("valid mTLS but missing internal token rejected (401)", internal(None) == 401)
check("valid mTLS but wrong internal token rejected (401)", internal("x" * 40) == 401)
status = internal(TOKEN)
check(f"valid mTLS + valid token reaches request validation (4xx for an empty body, not 401): {status}", status in (400, 422), status)

print(f"\nSUMMARY: {sum(results)} passed, {len(results) - sum(results)} failed")
sys.exit(0 if all(results) else 1)
