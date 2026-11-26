"""BuildMind private Python services.

`/health` is the unauthenticated liveness probe. Every `/internal/*` route requires the internal shared-secret token
(app/internal/auth.py; fail closed). Interactive docs and the OpenAPI document are disabled. Error bodies carry fixed
codes only: never exception text, request values, paths or tokens.
"""
import json
import logging

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
from starlette.concurrency import run_in_threadpool
from pydantic import ValidationError

from app.internal.auth import AUTH_HEADER, REQUIRED_WITHOUT_TOKENS, InternalAuth
from app.internal.generation import GenerationRunRequest, run_generation
from app.internal.land import DependencyUnavailable as LandDependencyUnavailable
from app.internal.land import LandValidationRequest, validate_land
from app.internal.plan_validation import DependencyUnavailable as PlanDependencyUnavailable
from app.internal.plan_edit import parse_request as parse_edit_preview
from app.internal.plan_edit import preview as edit_preview
from app.internal.plan_validation import PlanValidationRequest, validate_plan
from app.internal.exact_plan_validation import ExactPlanValidationRequest, GeometryBindingError, validate_exact_plan
from app.internal.land_frame import LandFrameRequest, frame_from_request
from app.geometry.land.validation import LandGeometryError
from app.internal.survey_extract import IntegrityRejected, SurveyExtractRequest
from app.internal.survey_extract import extract as extract_survey
from app.validation.boundary import ContractJsonError, parse_contract_json
from app.validation.boundary import _unique_members as _unique_json_members

MAX_BODY_BYTES = 2 * 1024 * 1024
# Survey extraction carries a base64 file of at most 6 MiB (about 8 MiB encoded): a larger limit for that route only.
SURVEY_EXTRACT_PATH = "/internal/v1/survey/extract"
SURVEY_MAX_BODY_BYTES = 9 * 1024 * 1024


# Exact geometry is limited to 1 MiB of UTF-8.
# Outer JSON escaping can expand it by up to six times.
# Reserve an additional 2 MiB for request metadata.
EXACT_PLAN_VALIDATE_PATH = "/internal/v2/plans/validate"
EXACT_PLAN_MAX_BODY_BYTES = 8 * 1024 * 1024


def _limit(path: str) -> int:
    if path == SURVEY_EXTRACT_PATH:
        return SURVEY_MAX_BODY_BYTES
    if path == EXACT_PLAN_VALIDATE_PATH:
        return EXACT_PLAN_MAX_BODY_BYTES
    return MAX_BODY_BYTES


def _json_preflight(raw: bytes) -> None:
    """Reject duplicate members and non-finite constants before model validation (same rules as M6 ingress)."""
    def _bad_constant(_token):
        raise ContractJsonError("Invalid JSON number.")

    try:
        json.loads(raw, object_pairs_hook=_unique_json_members, parse_constant=_bad_constant)
    except ContractJsonError:
        raise
    except (ValueError, RecursionError):
        raise ContractJsonError("Invalid contract JSON.") from None
logger = logging.getLogger("buildmind-python")


def _problem(status: int, code: str) -> JSONResponse:
    return JSONResponse(status_code=status, content={"code": code})


def create_app(auth: InternalAuth = REQUIRED_WITHOUT_TOKENS) -> FastAPI:
    application = FastAPI(title="BuildMind Python Services", version="0.2.0",
                          docs_url=None, redoc_url=None, openapi_url=None)

    @application.middleware("http")
    async def guard(request: Request, call_next):
        if request.url.path != "/health":
            presented = request.headers.getlist(AUTH_HEADER)
            failure = "UNAUTHENTICATED" if len(presented) > 1 else auth.check(presented[0] if presented else None)
            if failure == "AUTH_NOT_CONFIGURED":
                return _problem(503, failure)
            if failure is not None:
                return _problem(401, failure)
        length = request.headers.get("content-length")
        if length is not None and (not length.isdigit() or int(length) > _limit(request.url.path)):
            return _problem(413, "PAYLOAD_TOO_LARGE")
        return await call_next(request)

    async def raw_json(request: Request) -> bytes:
        limit = _limit(request.url.path)
        buffer = bytearray()

        async for chunk in request.stream():
            if len(chunk) > limit - len(buffer):
                raise _Rejected(413, "PAYLOAD_TOO_LARGE")
            buffer.extend(chunk)

        raw = bytes(buffer)
        if request.headers.get("content-type", "").split(";")[0].strip().lower() != "application/json":
            raise _Rejected(415, "UNSUPPORTED_MEDIA_TYPE")
        return raw

    async def body(request: Request, model):
        raw = await raw_json(request)
        try:
            return parse_contract_json(model, raw)
        except (ContractJsonError, ValidationError, ValueError):
            raise _Rejected(422, "VALIDATION_FAILED") from None

    @application.exception_handler(_Rejected)
    async def rejected(_: Request, exc: _Rejected) -> JSONResponse:
        return _problem(exc.status, exc.code)

    async def dependency_unavailable(_: Request, __: Exception) -> JSONResponse:
        return _problem(503, "DEPENDENCY_UNAVAILABLE")

    for dependency_error in (LandDependencyUnavailable, PlanDependencyUnavailable, ImportError):
        application.add_exception_handler(dependency_error, dependency_unavailable)

    @application.exception_handler(Exception)
    async def unexpected(_: Request, exc: Exception) -> JSONResponse:
        logger.error("Unhandled %s in python-services", type(exc).__name__)
        return _problem(500, "INTERNAL_ERROR")

    @application.get("/health")
    def health() -> dict[str, str]:
        return {"service": "buildmind-python", "status": "ok"}

    @application.post("/internal/v1/land/validate")
    async def land_validate(request: Request):
        return validate_land(await body(request, LandValidationRequest))

    @application.post("/internal/v1/land/frame")
    async def land_frame(request: Request):
        parsed = await body(request, LandFrameRequest)
        try:
            return {"valid": True, "code": "VALID", **await run_in_threadpool(frame_from_request, parsed)}
        except LandGeometryError as error:
            # Invalid geometry or CRS is a validation result (like land/validate), not a dependency failure.
            return {"schemaVersion": "captain-land-frame/1", "valid": False, "code": str(error.args[0]) if error.args else "LAND_FRAME_INVALID"}

    @application.post("/internal/v1/generation/run")
    async def generation_run(request: Request):
        return run_generation(await body(request, GenerationRunRequest))

    @application.post("/internal/v1/plans/validate")
    async def plan_validate(request: Request):
        return validate_plan(await body(request, PlanValidationRequest))

    @application.post(EXACT_PLAN_VALIDATE_PATH)
    async def exact_plan_validate(request: Request):
        parsed = await body(request, ExactPlanValidationRequest)
        try:
            return await run_in_threadpool(validate_exact_plan, parsed)
        except GeometryBindingError:
            raise _Rejected(422, "VALIDATION_FAILED") from None

    @application.post(SURVEY_EXTRACT_PATH)
    async def survey_extract_handler(request: Request):
        parsed = await body(request, SurveyExtractRequest)
        try:
            # PDF parsing and OCR block: run in the threadpool, bounded by the subprocess timeouts in ocr.py.
            return await run_in_threadpool(extract_survey, parsed)
        except IntegrityRejected as error:
            raise _Rejected(422, str(error)) from None

    @application.post("/internal/v1/plans/edit-preview")
    async def plan_edit_preview(request: Request):
        raw = await raw_json(request)
        try:
            _json_preflight(raw)
            parsed = parse_edit_preview(raw)
        except (ContractJsonError, ValidationError, ValueError):
            raise _Rejected(422, "VALIDATION_FAILED") from None
        return edit_preview(parsed)

    return application


class _Rejected(Exception):
    def __init__(self, status: int, code: str):
        super().__init__(code)
        self.status = status
        self.code = code


app = create_app(InternalAuth.from_environment())
