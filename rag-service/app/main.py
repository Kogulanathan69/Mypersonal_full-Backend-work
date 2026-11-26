"""BuildMind private regulation retrieval service (BE-M7, contract v1).

Internal only: bind to loopback or a private network. All /internal/* endpoints require the internal shared-secret
token (G11 MVP, see app/auth.py; fail closed). Interactive docs and the OpenAPI document are disabled. Errors never
include exception text, file paths, tokens or request input.
"""
import json
import logging
import os
from pathlib import Path
from typing import Any

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from pydantic import ValidationError

from .auth import AUTH_HEADER, REQUIRED_WITHOUT_TOKENS, InternalAuth
from .constraints import ConstraintProvider, ConstraintsRequest
from .contracts import CONTRACT_VERSION, SearchRequest, SourcesResponse
from .engine import EngineSettings, RetrievalEngine, Step6Engine
from .mapping import to_search_response, to_sources
from .strict_json import MAX_BODY_BYTES, RequestRejected, read_json_object
from .workload import WorkLimiter

SERVICE_NAME = "buildmind-rag"
CONSTRAINTS_VARIABLE = "BUILDMIND_RAG_CONSTRAINTS"
REVIEWERS_VARIABLE = "BUILDMIND_RAG_REVIEWERS"
logger = logging.getLogger(SERVICE_NAME)


def _problem(status: int, code: str, errors: list[dict[str, Any]] | None = None) -> JSONResponse:
    body: dict[str, Any] = {"code": code}
    if errors:
        body["errors"] = errors
    return JSONResponse(status_code=status, content=body)


def create_app(
    engine: RetrievalEngine | None,
    auth: InternalAuth = REQUIRED_WITHOUT_TOKENS,
    constraints: ConstraintProvider | None = None,
    limiter: WorkLimiter | None = None,
) -> FastAPI:
    # C10: bounded concurrent engine work with a per-request time budget (503 SERVICE_BUSY / 504 WORK_TIMEOUT).
    limiter = limiter or WorkLimiter.from_environment()
    app = FastAPI(title="BuildMind regulation retrieval", docs_url=None, redoc_url=None, openapi_url=None)

    @app.middleware("http")
    async def guard(request: Request, call_next):
        # Default-deny: everything except the identity/liveness probe requires the internal token. Authentication
        # runs first, so unauthenticated callers learn nothing about routes, validation or payload rules.
        if request.url.path != "/health":
            presented = request.headers.getlist(AUTH_HEADER)
            # A repeated token header is ambiguous (proxies and frameworks pick different values): always reject.
            failure = "UNAUTHENTICATED" if len(presented) > 1 else auth.check(presented[0] if presented else None)
            if failure == "AUTH_NOT_CONFIGURED":
                return _problem(503, failure)
            if failure is not None:
                return _problem(401, failure)
        length = request.headers.get("content-length")
        if length is not None and (not length.isdigit() or int(length) > MAX_BODY_BYTES):
            return _problem(413, "PAYLOAD_TOO_LARGE")
        return await call_next(request)

    @app.exception_handler(RequestValidationError)
    async def validation_failed(_: Request, exc: RequestValidationError) -> JSONResponse:
        return _problem(422, "VALIDATION_FAILED")  # fixed body: submitted names/values are never reflected

    @app.exception_handler(ValidationError)
    async def model_validation_failed(_: Request, exc: ValidationError) -> JSONResponse:
        return _problem(422, "VALIDATION_FAILED")

    @app.exception_handler(RequestRejected)
    async def rejected(_: Request, exc: RequestRejected) -> JSONResponse:
        return _problem(exc.status, exc.code)

    @app.exception_handler(Exception)
    async def unexpected(_: Request, exc: Exception) -> JSONResponse:
        logger.error("Unhandled %s in regulation retrieval", type(exc).__name__)
        return _problem(500, "INTERNAL_ERROR")

    @app.get("/health")
    def health() -> dict[str, Any]:
        return {"service": SERVICE_NAME, "status": "ok", "contract_version": CONTRACT_VERSION, "corpus_loaded": engine is not None}

    @app.post("/internal/v1/regulations/search")
    async def search(http: Request):
        # Strict raw JSON first (size, media type, duplicates, NaN, depth), then the contract model.
        request = SearchRequest.model_validate(await read_json_object(http))
        if engine is None:
            return _problem(503, "CORPUS_UNAVAILABLE")
        filters: dict[str, str] = {}
        if request.jurisdiction is not None:
            filters["jurisdiction"] = request.jurisdiction
        if request.document_ids is not None and len(request.document_ids) == 1:
            filters["document_id"] = request.document_ids[0]
        try:
            result = await limiter.run(engine.search, request.query, request.k, filters, request.document_ids)
        except ValueError:
            # Engine-side validation (for example an unsupported filter combination). Message deliberately dropped.
            return _problem(422, "VALIDATION_FAILED")
        return to_search_response(result).model_dump(mode="json")

    @app.get("/internal/v1/regulations/sources", response_model=SourcesResponse)
    def sources():
        if engine is None:
            return _problem(503, "CORPUS_UNAVAILABLE")
        return SourcesResponse(sources=to_sources(engine.registry()))

    @app.post("/internal/v1/regulations/constraints")
    async def regulatory_constraints(http: Request):
        """Human-reviewed constraint evidence pack (constraints contract v0; pending M6 + lead approval)."""
        request = ConstraintsRequest.model_validate(await read_json_object(http))
        if constraints is None:
            return _problem(503, "CONSTRAINTS_UNAVAILABLE")
        return await limiter.run(constraints.lookup, request)

    return app


def _load_engine() -> RetrievalEngine | None:
    settings = EngineSettings.from_environment()
    if settings is None:
        logger.warning("Regulation corpus not configured; search returns 503 CORPUS_UNAVAILABLE")
        return None
    return Step6Engine(settings)  # fails fast on any identity/hash mismatch


def _load_constraints(engine: RetrievalEngine | None) -> ConstraintProvider | None:
    path = os.environ.get(CONSTRAINTS_VARIABLE, "").strip()
    if not path:
        logger.warning("Constraint table not configured; constraints return 503 CONSTRAINTS_UNAVAILABLE")
        return None
    if engine is None:
        # Provenance cannot be verified without the hash-verified corpus: never serve unverifiable constraints.
        raise RuntimeError("A constraint table requires the regulation corpus for provenance verification")
    reviewers = os.environ.get(REVIEWERS_VARIABLE, "").strip()
    if not reviewers:
        # Reviewer authorization cannot be verified without the registry: never serve unauthorizable constraints.
        raise RuntimeError("A constraint table requires the reviewer registry (BUILDMIND_RAG_REVIEWERS)")
    # Synthetic tables only with an explicit isolated-testing opt-in; real tables require version and digest pins.
    return ConstraintProvider(
        json.loads(Path(path).read_text(encoding="utf-8")),
        engine.page_text,
        json.loads(Path(reviewers).read_text(encoding="utf-8")),
        expected_reviewers_sha256=os.environ.get("BUILDMIND_RAG_REVIEWERS_SHA256", "").strip() or None,
        allow_synthetic=os.environ.get("BUILDMIND_RAG_ALLOW_SYNTHETIC_CONSTRAINTS", "").strip() == "1",
        expected_version=os.environ.get("BUILDMIND_RAG_CONSTRAINTS_VERSION", "").strip() or None,
        expected_sha256=os.environ.get("BUILDMIND_RAG_CONSTRAINTS_SHA256", "").strip() or None,
    )


_engine = _load_engine()
app = create_app(_engine, InternalAuth.from_environment(), _load_constraints(_engine))
