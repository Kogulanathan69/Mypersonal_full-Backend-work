#!/usr/bin/env python3
"""Verify that the Development API actually registered its public routes.

Run after tools/portable/start.sh. No authentication tokens or secrets needed.
"""
import json
import os
import sys
import time
import urllib.error
import urllib.request

port = os.environ.get("BM_API_PORT", "5182")
url = f"http://127.0.0.1:{port}/swagger/v1/swagger.json"
required = {
    ("POST", "/api/v1/auth/register"),
    ("POST", "/api/v1/auth/login"),
    ("GET", "/api/v1/projects"),
    ("POST", "/api/v1/projects"),
    ("GET", "/api/v1/admin/dashboard"),
    ("GET", "/api/v1/staff/dashboard"),
}
required_path_prefixes = (
    "/api/v1/projects/{id}/surveys",
    "/api/v1/projects/{projectId}/land",
    "/api/v1/projects/{projectId}/generation-jobs",
)
document = None
last_error = None
# Compose service health is not guaranteed to include Swagger readiness.
for attempt in range(20):
    try:
        with urllib.request.urlopen(url, timeout=5) as response:
            document = json.load(response)
        break
    except (OSError, ValueError, urllib.error.URLError) as exc:
        last_error = exc
        if attempt < 19:
            time.sleep(1)
if document is None:
    print(f"API_DOCUMENT_UNAVAILABLE: {url}: {last_error}", file=sys.stderr)
    sys.exit(1)

paths = document.get("paths", {})
routes = {(method.upper(), path) for path, ops in paths.items()
          for method in ops if method.lower() in
          {"get", "post", "put", "patch", "delete", "options", "head"}}
missing = sorted(required - routes)
for prefix in required_path_prefixes:
    if not any(path.startswith(prefix) for path in paths):
        missing.append(f"PATH_PREFIX {prefix}")
print(f"SWAGGER_URL: {url}")
print(f"REGISTERED_ROUTES: {len(routes)}")
if missing:
    print("INCOMPLETE_BACKEND_ROUTE_REGISTRATION", file=sys.stderr)
    for item in missing:
        print(f"MISSING: {item}", file=sys.stderr)
    print("Start the configured Development portable stack (port 5182), "
          "not a standalone dotnet run with default disabled features.",
          file=sys.stderr)
    sys.exit(2)
print("BUILDMIND_DEVELOPMENT_ROUTES_REGISTERED")
print("Route registration only: authenticated operations still require live workflow tests.")
